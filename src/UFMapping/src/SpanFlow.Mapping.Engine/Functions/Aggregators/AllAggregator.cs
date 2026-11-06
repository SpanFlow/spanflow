using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class AllAggregator : IAggregator
{
    public string Name => "all";

    // Vacuously true for an empty sequence.
    public JsonNode? Aggregate(IEnumerable<JsonNode?> values)
        => JsonValue.Create(values.All(JsonScalarConverter.IsTruthy));
}
