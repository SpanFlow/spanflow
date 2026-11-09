using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SpanFlow.Expressions;

/// <summary>
/// Resolves <c>{{ expr }}</c> placeholders in request JSON against an <see cref="ExpressionContext"/>.
/// Two modes:
/// <list type="bullet">
/// <item><see cref="Render"/> — inline substitution (HTTP paths/bodies, file paths, ...).
/// A string that is exactly one placeholder becomes the expression's typed value; mixed text
/// interpolates as strings.</item>
/// <item><see cref="RenderWithParameters"/> — for command text (SQL, LDAP filters): each
/// placeholder becomes a positional parameter (<c>@p0</c>, <c>@p1</c>, ...) and its value goes
/// into the returned bag. Values are NEVER spliced into command text.</item>
/// </list>
/// </summary>
public static partial class TemplateRenderer
{
    [GeneratedRegex(@"\{\{(?<expr>[^{}]+)\}\}")]
    private static partial Regex Placeholder();

    /// <summary>
    /// Recursively renders every string in a JSON tree. Non-strings pass through. Values under a
    /// key in <paramref name="rawKeys"/> are copied verbatim (not rendered) — used to leave SQL
    /// command text with its <c>{{ }}</c> intact so the db connector parameter-binds it instead of
    /// splicing values in (injection safety).
    /// </summary>
    public static JsonNode? Render(JsonNode? template, ExpressionContext context, IReadOnlySet<string>? rawKeys = null)
    {
        switch (template)
        {
            case null:
                return null;
            case JsonObject obj:
            {
                var result = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    result[key] = rawKeys?.Contains(key) == true ? value?.DeepClone() : Render(value, context, rawKeys);
                }
                return result;
            }
            case JsonArray array:
            {
                var result = new JsonArray();
                foreach (var item in array) result.Add(Render(item, context, rawKeys));
                return result;
            }
            case JsonValue value when value.TryGetValue<string>(out var s):
                return RenderString(s, context);
            default:
                return template.DeepClone();
        }
    }

    /// <summary>
    /// Renders one string. Exactly one placeholder spanning the whole string keeps the
    /// expression's type ("{{input.count}}" → 42, not "42"); anything else interpolates.
    /// </summary>
    public static JsonNode? RenderString(string template, ExpressionContext context)
    {
        var matches = Placeholder().Matches(template);
        if (matches.Count == 0) return JsonValue.Create(template);

        if (matches.Count == 1 && matches[0].Index == 0 && matches[0].Length == template.Length)
        {
            return ExpressionEvaluator.Evaluate(matches[0].Groups["expr"].Value.Trim(), context)?.DeepClone();
        }

        var result = new StringBuilder();
        var position = 0;
        foreach (Match match in matches)
        {
            result.Append(template, position, match.Index - position);
            var value = ExpressionEvaluator.Evaluate(match.Groups["expr"].Value.Trim(), context);
            result.Append(ToStringValue(value));
            position = match.Index + match.Length;
        }
        result.Append(template, position, template.Length - position);
        return JsonValue.Create(result.ToString());
    }

    /// <summary>
    /// Replaces each placeholder in command text with <paramref name="parameterPrefix"/>0, 1, ...
    /// and returns the evaluated values alongside. Repeated identical expressions share a parameter.
    /// </summary>
    public static (string Text, IReadOnlyList<TemplateParameter> Parameters) RenderWithParameters(
        string commandTemplate, ExpressionContext context, string parameterPrefix = "@p")
    {
        var parameters = new List<TemplateParameter>();
        var byExpression = new Dictionary<string, string>();

        var text = Placeholder().Replace(commandTemplate, match =>
        {
            var expression = match.Groups["expr"].Value.Trim();
            if (byExpression.TryGetValue(expression, out var existing)) return existing;

            var name = $"{parameterPrefix}{parameters.Count}";
            parameters.Add(new TemplateParameter(name, ExpressionEvaluator.Evaluate(expression, context)));
            byExpression[expression] = name;
            return name;
        });

        return (text, parameters);
    }

    /// <summary>
    /// Renders command text where values can't be positionally bound (LDAP filters, shell-like
    /// surfaces): each placeholder's value is passed through <paramref name="escape"/> before it is
    /// spliced in, so a value can never break out into the surrounding syntax. Literal text is left
    /// exactly as authored. Callers supply the syntax-specific escaper (e.g. RFC 4515 for LDAP).
    /// </summary>
    public static string RenderWithEscaping(string template, ExpressionContext context, Func<string, string> escape) =>
        Placeholder().Replace(template, match =>
            escape(ToStringValue(ExpressionEvaluator.Evaluate(match.Groups["expr"].Value.Trim(), context))));

    private static string ToStringValue(JsonNode? value) => value switch
    {
        null => "",
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        _ => value.ToJsonString(),
    };
}

/// <summary>One bound parameter produced by <see cref="TemplateRenderer.RenderWithParameters"/>.</summary>
public record TemplateParameter(string Name, JsonNode? Value);
