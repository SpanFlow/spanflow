using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

/// <summary>Collects the values as-is, preserving order and duplicates.</summary>
public sealed class ItemsAggregator : IAggregator
{
    public string Name => "items";

    public JsonNode? Aggregate(IEnumerable<JsonNode?> values)
    {
        var result = new JsonArray();
        foreach (var value in values.Where(static value => value is not null))
        {
            result.Add(value!.DeepClone());
        }

        return result;
    }
}
