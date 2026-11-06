using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class LengthTransform : ITransform
{
    public string Name => "length";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
        => JsonValue.Create((JsonScalarConverter.ToText(args.FirstOrDefault()) ?? string.Empty).Length);
}
