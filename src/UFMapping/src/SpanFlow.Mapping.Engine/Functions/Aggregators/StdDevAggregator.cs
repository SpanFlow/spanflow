using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class StdDevAggregator : IAggregator
{
    public string Name => "stddev";

    // Population standard deviation: the square root of the population variance.
    public JsonNode? Aggregate(IEnumerable<JsonNode?> values)
    {
        var numeric = values.Select(JsonScalarConverter.ToDecimal).ToArray();
        if (numeric.Length == 0)
        {
            return JsonValue.Create(0d);
        }

        var mean = numeric.Average();
        var variance = numeric.Sum(v => (v - mean) * (v - mean)) / numeric.Length;
        return JsonValue.Create(Math.Sqrt((double)variance));
    }
}
