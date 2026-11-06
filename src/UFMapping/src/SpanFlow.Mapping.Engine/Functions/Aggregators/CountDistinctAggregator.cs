using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class CountDistinctAggregator : IAggregator
{
    public string Name => "countDistinct";

    public JsonNode? Aggregate(IEnumerable<JsonNode?> values)
        => JsonValue.Create(values
            .Where(static value => value is not null)
            .Distinct(JsonNodeStructuralComparer.Instance)
            .Count());
}
