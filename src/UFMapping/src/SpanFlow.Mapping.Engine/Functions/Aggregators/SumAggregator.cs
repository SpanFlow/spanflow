using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class SumAggregator : IAggregator
{
    public string Name => "sum";

    public JsonNode? Aggregate(IEnumerable<JsonNode?> values)
        => JsonValue.Create(values.Sum(JsonScalarConverter.ToDecimal));
}
