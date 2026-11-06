using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class FloorTransform : ITransform
{
    public string Name => "floor";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
        => JsonValue.Create(Math.Floor(JsonScalarConverter.ToDecimal(args.FirstOrDefault())));
}
