using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class NoneAggregator : IAggregator
{
    public string Name => "none";

    public JsonNode? Aggregate(IEnumerable<JsonNode?> values)
        => JsonValue.Create(!values.Any(JsonScalarConverter.IsTruthy));
}
