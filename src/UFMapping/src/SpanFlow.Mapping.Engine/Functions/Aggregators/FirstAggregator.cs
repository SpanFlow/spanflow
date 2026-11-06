using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class FirstAggregator : IAggregator
{
    public string Name => "first";

    public JsonNode? Aggregate(IEnumerable<JsonNode?> values)
        => values.FirstOrDefault()?.DeepClone();
}
