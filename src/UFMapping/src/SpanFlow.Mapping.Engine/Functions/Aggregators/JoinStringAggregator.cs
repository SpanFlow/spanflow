using System.Text.Json.Nodes;
using SpanFlow.Mapping.Core.Models;

namespace SpanFlow.Mapping.Engine;

public sealed class JoinStringAggregator : IAggregator, IParameterizedAggregator
{
    private const string DefaultSeparator = ", ";

    public string Name => "joinString";

    public JsonNode? Aggregate(IEnumerable<JsonNode?> values) => Join(values, DefaultSeparator);

    public JsonNode? Aggregate(IEnumerable<JsonNode?> values, AggregateDefinition definition)
        => Join(values, definition.Separator ?? DefaultSeparator);

    private static JsonNode? Join(IEnumerable<JsonNode?> values, string separator)
        => JsonValue.Create(string.Join(separator, values.Select(v => JsonScalarConverter.ToText(v) ?? string.Empty)));
}
