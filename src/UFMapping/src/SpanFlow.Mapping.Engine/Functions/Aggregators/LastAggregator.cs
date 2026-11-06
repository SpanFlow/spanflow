using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class LastAggregator : IAggregator
{
    public string Name => "last";

    public JsonNode? Aggregate(IEnumerable<JsonNode?> values)
        => values.LastOrDefault()?.DeepClone();
}
