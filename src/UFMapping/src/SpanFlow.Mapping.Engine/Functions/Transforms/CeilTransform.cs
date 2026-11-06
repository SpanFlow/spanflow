using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class CeilTransform : ITransform
{
    public string Name => "ceil";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
        => JsonValue.Create(Math.Ceiling(JsonScalarConverter.ToDecimal(args.FirstOrDefault())));
}
