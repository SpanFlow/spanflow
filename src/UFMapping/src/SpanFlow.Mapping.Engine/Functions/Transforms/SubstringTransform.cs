using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class SubstringTransform : ITransform
{
    // substring(text, start, [length]); indices are clamped to the string.
    public string Name => "substring";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
    {
        var text = JsonScalarConverter.ToText(args.ElementAtOrDefault(0)) ?? string.Empty;

        var start = (int)JsonScalarConverter.ToDecimal(args.ElementAtOrDefault(1));
        start = Math.Clamp(start, 0, text.Length);

        var remaining = text.Length - start;
        var length = args.Count > 2 ? (int)JsonScalarConverter.ToDecimal(args.ElementAtOrDefault(2)) : remaining;
        length = Math.Clamp(length, 0, remaining);

        return JsonValue.Create(text.Substring(start, length));
    }
}
