using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SpanFlow.Mapping.Core.Models;

public sealed class MappingDefinition
{
    public Dictionary<string, string> Sources { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public TargetDefinition Target { get; init; } = new();
}

public sealed class TargetDefinition
{
    public string Type { get; init; } = "object";

    public List<MappingRule> Mappings { get; init; } = [];

    // For an array-root target (Type == "array"): iterate this source collection
    // and project each item through Map.
    public string? From { get; init; }

    public Dictionary<string, JsonNode?>? Map { get; init; }
}

[JsonConverter(typeof(MappingRuleConverter))]
public sealed class MappingRule
{
    public required string Target { get; init; }

    public string? From { get; init; }

    public string? Foreach { get; init; }

    /// <summary>A nested collection expression to iterate (e.g. a wired input /
    /// parameter), as an alternative to the <see cref="Foreach"/> path.</summary>
    public JsonNode? ForeachInput { get; init; }

    public Dictionary<string, JsonNode?>? Map { get; init; }

    public AggregateDefinition? Aggregate { get; init; }

    public GroupByDefinition? GroupBy { get; init; }

    public TransformDefinition? Transform { get; init; }

    public ConditionDefinition? Condition { get; init; }

    public ValueMapDefinition? ValueMap { get; init; }

    public SortDefinition? Sort { get; init; }

    public FilterDefinition? Filter { get; init; }

    public JoinDefinition? Join { get; init; }

    public ExceptionDefinition? Exception { get; init; }
}

/// <summary>
/// Raises a <see cref="SpanFlow.Mapping.Core.MappingException"/> with <see cref="Message"/>
/// when the input satisfies the condition; otherwise passes the input through.
/// </summary>
public sealed class ExceptionDefinition
{
    /// <summary>Nested expression to test. Takes precedence over <see cref="From"/>.</summary>
    public JsonNode? Input { get; init; }

    /// <summary>Path to the value to test. Defaults to the current item ($).</summary>
    public string? From { get; init; }

    /// <summary>Comparison operator (eq, neq, gt, gte, lt, lte, contains).</summary>
    public required string Op { get; init; }

    /// <summary>Value compared against the input.</summary>
    public JsonNode? Value { get; init; }

    /// <summary>Human-readable message raised when the condition holds.</summary>
    public required string Message { get; init; }
}

public sealed class JoinDefinition
{
    /// <summary>Nested expression for the left collection. Takes precedence over <see cref="Left"/>.</summary>
    public JsonNode? LeftInput { get; init; }

    /// <summary>Path to the left collection.</summary>
    public string? Left { get; init; }

    /// <summary>Nested expression for the right collection. Takes precedence over <see cref="Right"/>.</summary>
    public JsonNode? RightInput { get; init; }

    /// <summary>Path to the right collection.</summary>
    public string? Right { get; init; }

    /// <summary>Key field within each left item.</summary>
    public required string LeftKey { get; init; }

    /// <summary>Key field within each right item.</summary>
    public required string RightKey { get; init; }
}

public sealed class FilterDefinition
{
    /// <summary>Nested expression producing the array to filter. Takes precedence over <see cref="From"/>.</summary>
    public JsonNode? Input { get; init; }

    /// <summary>Collection path to filter. Optional when <see cref="Input"/> is set.</summary>
    public string? From { get; init; }

    /// <summary>Condition each item must satisfy to be kept.</summary>
    public required WhereDefinition Where { get; init; }
}

public sealed class SortDefinition
{
    /// <summary>
    /// Nested expression producing the array to sort (another node's output).
    /// Takes precedence over <see cref="From"/> when present.
    /// </summary>
    public JsonNode? Input { get; init; }

    /// <summary>Collection path to sort. Optional when <see cref="Input"/> is set.</summary>
    public string? From { get; init; }

    public List<SortKey> Keys { get; init; } = [];
}

public sealed class SortKey
{
    /// <summary>Path to the sort value within each item. Empty/null sorts by the item itself.</summary>
    public string? By { get; init; }

    /// <summary>"asc" (default) or "desc".</summary>
    public string Order { get; init; } = "asc";
}

public sealed class ValueMapDefinition
{
    /// <summary>
    /// Nested expression producing the value to look up (e.g. another aggregate/transform).
    /// Takes precedence over <see cref="From"/> when present, enabling node chaining.
    /// </summary>
    public JsonNode? Input { get; init; }

    /// <summary>
    /// Path to the input value to look up. When omitted, the current context node ($) is used.
    /// </summary>
    public string? From { get; init; }

    /// <summary>
    /// Lookup table keyed by the textual form of the input value.
    /// </summary>
    public Dictionary<string, JsonNode?> Table { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Value returned when the input has no matching key. Null when not specified.
    /// </summary>
    public JsonNode? Default { get; init; }
}

public sealed class AggregateDefinition
{
    public required string Function { get; init; }

    public string? From { get; init; }

    /// <summary>A nested collection expression (e.g. a wired input / function parameter)
    /// to fold, as an alternative to <see cref="From"/>.</summary>
    public JsonNode? Input { get; init; }

    public string? Select { get; init; }

    public WhereDefinition? Where { get; init; }

    /// <summary>Separator for the <c>joinString</c> aggregate. Defaults to ", ".</summary>
    public string? Separator { get; init; }
}

public sealed class GroupByDefinition
{
    /// <summary>A nested collection expression to group (e.g. a wired input / parameter).
    /// Takes precedence over <see cref="From"/>.</summary>
    public JsonNode? Input { get; init; }

    public string? From { get; init; }

    public required string Key { get; init; }

    /// <summary>
    /// Optional row filter applied BEFORE grouping (SQL <c>WHERE</c>): only rows
    /// that match take part in the grouping, so the key, the aggregates and
    /// <c>items</c> all see just the survivors. A group left with no rows never
    /// appears. Clauses nest with <c>all</c>/<c>any</c> for AND/OR.
    /// </summary>
    public WhereDefinition? Where { get; init; }

    /// <summary>
    /// Optional group filter applied AFTER the map is computed (SQL <c>HAVING</c>):
    /// the clause is evaluated against the produced group object, so it can test
    /// the aggregates by their output names (e.g. <c>orderCount gte 5</c>).
    /// Groups that fail are dropped from the result.
    /// </summary>
    public WhereDefinition? Having { get; init; }

    public Dictionary<string, JsonNode?> Map { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TransformDefinition
{
    public required string Function { get; init; }

    public List<JsonNode?> Args { get; init; } = [];
}

public sealed class ConditionDefinition
{
    public required WhereDefinition If { get; init; }

    public JsonNode? Then { get; init; }

    public JsonNode? Else { get; init; }
}

public sealed class WhereDefinition
{
    /// <summary>
    /// Child clauses combined with logical AND. When set, this is a group node
    /// (leaf operands are ignored).
    /// </summary>
    public List<WhereDefinition>? All { get; init; }

    /// <summary>
    /// Child clauses combined with logical OR. When set, this is a group node.
    /// </summary>
    public List<WhereDefinition>? Any { get; init; }

    /// <summary>
    /// Nested expression for the left-hand operand (another node's output).
    /// Takes precedence over <see cref="Field"/> when present, enabling chaining.
    /// </summary>
    public JsonNode? Input { get; init; }

    /// <summary>
    /// Path to the left-hand operand. Optional when <see cref="Input"/> is set.
    /// </summary>
    public string? Field { get; init; }

    /// <summary>
    /// Comparison operator for a leaf clause. Not used by group nodes.
    /// </summary>
    public string? Op { get; init; }

    public JsonNode? Value { get; init; }
}
