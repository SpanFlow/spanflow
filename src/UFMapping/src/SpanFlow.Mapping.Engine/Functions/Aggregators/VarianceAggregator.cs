using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class VarianceAggregator : IAggregator
{
    public string Name => "variance";

    // Population variance: mean of squared deviations from the mean.
    public JsonNode? Aggregate(IEnumerable<JsonNode?> values)
    {
        var numeric = values.Select(JsonScalarConverter.ToDecimal).ToArray();
        if (numeric.Length == 0)
        {
            return JsonValue.Create(0m);
        }

        var mean = numeric.Average();
        var variance = numeric.Sum(v => (v - mean) * (v - mean)) / numeric.Length;
        return JsonValue.Create(variance);
    }
}
