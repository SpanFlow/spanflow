using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class MinAggregator : IAggregator
{
    public string Name => "min";

    public JsonNode? Aggregate(IEnumerable<JsonNode?> values)
    {
        var numeric = values.Select(JsonScalarConverter.ToDecimal).ToArray();
        return JsonValue.Create(numeric.Length == 0 ? 0m : numeric.Min());
    }
}
