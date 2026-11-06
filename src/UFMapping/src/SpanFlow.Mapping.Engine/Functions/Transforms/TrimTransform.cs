using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class TrimTransform : ITransform
{
    public string Name => "trim";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
        => JsonValue.Create((JsonScalarConverter.ToText(args.FirstOrDefault()) ?? string.Empty).Trim());
}
