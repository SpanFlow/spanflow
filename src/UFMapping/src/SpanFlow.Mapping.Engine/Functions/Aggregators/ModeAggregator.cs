using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class ModeAggregator : IAggregator
{
    public string Name => "mode";

    public JsonNode? Aggregate(IEnumerable<JsonNode?> values)
    {
        var present = values.Where(static value => value is not null).ToList();
        if (present.Count == 0)
        {
            return null;
        }

        // Most frequent value; ties resolve to the first-seen value.
        var best = present
            .GroupBy(value => value!.ToJsonString())
            .OrderByDescending(group => group.Count())
            .First();

        return best.First()!.DeepClone();
    }
}
