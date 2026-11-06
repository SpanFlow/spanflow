using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class DistinctAggregator : IAggregator
{
    public string Name => "distinct";

    public JsonNode? Aggregate(IEnumerable<JsonNode?> values)
    {
        var result = new JsonArray();
        foreach (var value in values.Where(static value => value is not null).Distinct(JsonNodeStructuralComparer.Instance))
        {
            result.Add(value!.DeepClone());
        }

        return result;
    }
}
