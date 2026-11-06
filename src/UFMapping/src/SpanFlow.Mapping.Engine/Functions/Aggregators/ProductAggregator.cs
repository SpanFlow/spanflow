using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class ProductAggregator : IAggregator
{
    public string Name => "product";

    public JsonNode? Aggregate(IEnumerable<JsonNode?> values)
    {
        // Empty product is the multiplicative identity (1), mirroring sum's 0.
        decimal total = 1m;
        foreach (var value in values)
        {
            total *= JsonScalarConverter.ToDecimal(value);
        }

        return JsonValue.Create(total);
    }
}
