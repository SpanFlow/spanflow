using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class MedianAggregator : IAggregator
{
    public string Name => "median";

    public JsonNode? Aggregate(IEnumerable<JsonNode?> values)
    {
        var numeric = values.Select(JsonScalarConverter.ToDecimal).OrderBy(v => v).ToArray();
        if (numeric.Length == 0)
        {
            return JsonValue.Create(0m);
        }

        var mid = numeric.Length / 2;
        var median = numeric.Length % 2 == 1
            ? numeric[mid]
            : (numeric[mid - 1] + numeric[mid]) / 2m;

        return JsonValue.Create(median);
    }
}
