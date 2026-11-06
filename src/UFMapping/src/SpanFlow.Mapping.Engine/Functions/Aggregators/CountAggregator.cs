using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class CountAggregator : IAggregator
{
    public string Name => "count";

    public JsonNode? Aggregate(IEnumerable<JsonNode?> values)
        => JsonValue.Create(values.Count());
}
