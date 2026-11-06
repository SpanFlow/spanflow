using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class AbsTransform : ITransform
{
    public string Name => "abs";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
        => JsonValue.Create(Math.Abs(JsonScalarConverter.ToDecimal(args.FirstOrDefault())));
}
