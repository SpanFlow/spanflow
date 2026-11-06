using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class AnyAggregator : IAggregator
{
    public string Name => "any";

    public JsonNode? Aggregate(IEnumerable<JsonNode?> values)
        => JsonValue.Create(values.Any(JsonScalarConverter.IsTruthy));
}
