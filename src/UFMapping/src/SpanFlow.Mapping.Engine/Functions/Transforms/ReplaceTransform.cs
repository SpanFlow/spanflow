using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class ReplaceTransform : ITransform
{
    // replace(text, search, replacement)
    public string Name => "replace";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
    {
        var text = JsonScalarConverter.ToText(args.ElementAtOrDefault(0)) ?? string.Empty;
        var search = JsonScalarConverter.ToText(args.ElementAtOrDefault(1)) ?? string.Empty;
        var replacement = JsonScalarConverter.ToText(args.ElementAtOrDefault(2)) ?? string.Empty;

        return JsonValue.Create(search.Length == 0 ? text : text.Replace(search, replacement));
    }
}
