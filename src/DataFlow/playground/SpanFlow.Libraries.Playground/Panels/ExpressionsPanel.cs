using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SpanFlow.Expressions;

namespace SpanFlow.Libraries.Playground.Panels;

/// <summary>
/// <c>POST /api/expressions/evaluate</c>: <c>{ mode, expression | template, data }</c>.
/// <list type="bullet">
/// <item><c>expression</c>: <c>ExpressionEvaluator.Evaluate</c>, with its truthiness (what a condition would decide).</item>
/// <item><c>template</c>: <c>TemplateRenderer.RenderString</c> on one string with <c>{{ }}</c> placeholders.</item>
/// <item><c>json</c>: <c>TemplateRenderer.Render</c> on a JSON template, every string in it rendered.</item>
/// </list>
/// <c>data</c> holds the roots an expression may start with: <c>input</c>, <c>params</c>, <c>vars</c>, <c>state</c>,
/// <c>trigger</c>, <c>headers</c>, <c>body</c>, <c>user</c>. Pure: the library reads JSON and runs no code.
/// </summary>
public static partial class ExpressionsPanel
{
    public const int MaxExpressionLength = 4000;
    public const int MaxTemplateLength = 32_000;

    /// <summary>Brackets open at once, and <c>!</c> in one expression, at most (D71.7): the parser recurses on both.</summary>
    public const int MaxNesting = 64;

    [GeneratedRegex(@"\{\{(?<expr>[^{}]+)\}\}")]
    private static partial Regex Placeholder();

    public static Task<JsonNode> Run(PanelCall call)
    {
        var body = call.Body;
        var mode = PlaygroundEndpoint.OptionalString(body, "mode") ?? "expression";
        var context = Context(body["data"]);

        JsonObject answer;
        switch (mode)
        {
            case "expression":
            {
                var expression = PlaygroundEndpoint.RequireString(body, "expression", MaxExpressionLength);
                CheckNesting(expression);
                var value = Evaluate(() => ExpressionEvaluator.Evaluate(expression, context));
                answer = new JsonObject
                {
                    ["result"] = value?.DeepClone(),
                    ["type"] = TypeOf(value),
                    ["truthy"] = Evaluate(() => JsonValue.Create(ExpressionEvaluator.EvaluateAsBool(expression, context))),
                };
                break;
            }
            case "template":
            {
                var template = PlaygroundEndpoint.RequireString(body, "template", MaxTemplateLength);
                CheckTemplate(template);
                CheckRenderedSize([template], context, call.Limits.MaxOutputBytes);
                var value = Evaluate(() => TemplateRenderer.RenderString(template, context));
                answer = new JsonObject { ["result"] = value?.DeepClone(), ["type"] = TypeOf(value) };
                break;
            }
            case "json":
            {
                if (!body.ContainsKey("template")) throw PanelRefusal.Invalid("Send \"template\": any JSON whose strings may hold {{ }} placeholders.");
                var template = body["template"];
                foreach (var text in Strings(template)) CheckTemplate(text);
                CheckRenderedSize(Strings(template), context, call.Limits.MaxOutputBytes);
                var value = Evaluate(() => TemplateRenderer.Render(template, context));
                answer = new JsonObject { ["result"] = value?.DeepClone(), ["type"] = TypeOf(value) };
                break;
            }
            case "batch":
            {
                // One expression over every row: each row is `input`, the other roots stay as given. The way to test
                // a filter or a condition on real data before using it.
                var expression = PlaygroundEndpoint.RequireString(body, "expression", MaxExpressionLength);
                CheckNesting(expression);
                if ((body["rows"] ?? body["data"]?["input"]) is not JsonArray rows)
                    throw PanelRefusal.Invalid("Batch reads an array: send \"rows\": [ ... ] (or make data.input an array).");
                var results = new JsonArray();
                var matched = 0;
                long size = 0;
                foreach (var row in rows.Take(call.Limits.MaxRows))
                {
                    call.Cancellation.ThrowIfCancellationRequested();
                    var rowContext = context with { Input = row?.DeepClone() };
                    var value = Evaluate(() => ExpressionEvaluator.Evaluate(expression, rowContext));
                    var truthy = ExpressionEvaluator.EvaluateAsBool(expression, rowContext);
                    if (truthy) matched++;
                    // Rows may each repeat a large root (vars.big): stop at the output cap, not after building it all.
                    size += SizeOf(value);
                    if (size > call.Limits.MaxOutputBytes)
                        throw PanelRefusal.TooLarge($"The results pass {call.Limits.MaxOutputBytes:N0} bytes, the limit for an answer, by row {results.Count + 1:N0}.");
                    results.Add(new JsonObject { ["index"] = results.Count, ["result"] = value?.DeepClone(), ["truthy"] = truthy });
                }
                answer = new JsonObject
                {
                    ["results"] = results,
                    ["rowCount"] = results.Count,
                    ["matched"] = matched,
                    ["truncated"] = rows.Count > call.Limits.MaxRows,
                    ["maxRows"] = call.Limits.MaxRows,
                };
                break;
            }
            default:
                throw PanelRefusal.Invalid("\"mode\" is expression, template, json or batch.");
        }

        call.Cancellation.ThrowIfCancellationRequested();
        return Task.FromResult<JsonNode>(answer);
    }

    private static ExpressionContext Context(JsonNode? data)
    {
        if (data is null) return new ExpressionContext();
        if (data is not JsonObject roots) throw PanelRefusal.Invalid("\"data\" is an object of roots: { \"input\": ..., \"params\": ..., ... }.");
        return new ExpressionContext
        {
            Input = roots["input"]?.DeepClone(),
            Params = roots["params"]?.DeepClone(),
            Vars = roots["vars"]?.DeepClone(),
            State = roots["state"]?.DeepClone(),
            Trigger = roots["trigger"]?.DeepClone(),
            Headers = roots["headers"]?.DeepClone(),
            Body = roots["body"]?.DeepClone(),
            User = roots["user"]?.DeepClone(),
        };
    }

    private static JsonNode? Evaluate(Func<JsonNode?> evaluate)
    {
        try
        {
            return evaluate();
        }
        catch (ExpressionException ex)
        {
            throw PanelRefusal.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Refuses a template whose result would pass the output cap, counted BEFORE anything is built. A short template
    /// can repeat a large value (<c>{{ vars.s }}</c> 3,000 times over 30 KB is 90 MB), and the renderer builds the whole
    /// string first: eight such calls took the process past 2 GB. Each distinct placeholder is evaluated once.
    /// </summary>
    internal static void CheckRenderedSize(IEnumerable<string> templates, ExpressionContext context, int maxBytes)
    {
        long total = 0;
        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var template in templates)
        {
            total += template.Length;
            foreach (Match match in Placeholder().Matches(template))
            {
                var expression = match.Groups["expr"].Value.Trim();
                if (!sizes.TryGetValue(expression, out var size))
                    sizes[expression] = size = SizeOf(Evaluate(() => ExpressionEvaluator.Evaluate(expression, context)));
                total += size;
                if (total > maxBytes)
                    throw PanelRefusal.TooLarge($"This template would make more than {maxBytes:N0} bytes, the limit for an answer.");
            }
        }
    }

    /// <summary>Characters a value takes in an answer: a string's own length, anything else as JSON.</summary>
    private static long SizeOf(JsonNode? value) =>
        value is JsonValue v && v.TryGetValue<string>(out var text) ? text.Length : value?.ToJsonString().Length ?? 4;

    internal static void CheckTemplate(string template)
    {
        if (template.Length > MaxTemplateLength)
            throw PanelRefusal.TooLarge($"A template string is {template.Length:N0} characters; the limit is {MaxTemplateLength:N0}.");
        foreach (Match match in Placeholder().Matches(template)) CheckNesting(match.Groups["expr"].Value);
    }

    /// <summary>Refuses an expression nested deeper than <see cref="MaxNesting"/>, before the recursive parser sees it.</summary>
    internal static void CheckNesting(string expression)
    {
        int depth = 0, deepest = 0, bangs = 0;
        foreach (var c in expression)
        {
            switch (c)
            {
                case '(' or '[': deepest = Math.Max(deepest, ++depth); break;
                case ')' or ']': depth = Math.Max(0, depth - 1); break;
                case '!': bangs++; break;
            }
        }

        if (deepest > MaxNesting || bangs > MaxNesting)
            throw PanelRefusal.TooLarge($"The expression nests deeper than {MaxNesting} levels (brackets or '!').");
    }

    private static IEnumerable<string> Strings(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (_, child) in obj)
                foreach (var text in Strings(child)) yield return text;
                break;
            case JsonArray array:
                foreach (var child in array)
                foreach (var text in Strings(child)) yield return text;
                break;
            case JsonValue value when value.TryGetValue<string>(out var s):
                yield return s;
                break;
        }
    }

    private static string TypeOf(JsonNode? value) => value switch
    {
        null => "null",
        JsonObject => "object",
        JsonArray => "array",
        JsonValue v => v.GetValueKind() switch
        {
            System.Text.Json.JsonValueKind.String => "string",
            System.Text.Json.JsonValueKind.Number => "number",
            System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False => "boolean",
            _ => "value",
        },
        _ => "value",
    };
}
