using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

/// <summary>
/// Folds a sequence of values into a single result. Add a new aggregate by
/// implementing this and registering it in <see cref="AggregatorRegistry.CreateDefault"/>.
/// </summary>
public interface IAggregator
{
    /// <summary>The DSL function name (e.g. "sum"). Matched case-insensitively.</summary>
    string Name { get; }

    JsonNode? Aggregate(IEnumerable<JsonNode?> values);
}

/// <summary>
/// An aggregator that also reads configuration from the rule (e.g. a separator).
/// The executor prefers this overload when the aggregator implements it.
/// </summary>
internal interface IParameterizedAggregator
{
    JsonNode? Aggregate(IEnumerable<JsonNode?> values, Core.Models.AggregateDefinition definition);
}
