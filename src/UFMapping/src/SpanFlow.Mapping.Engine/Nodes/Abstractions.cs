using System.Text.Json.Nodes;
using SpanFlow.Mapping.Core.Models;

namespace SpanFlow.Mapping.Engine;

/// <summary>
/// Capabilities a node executor needs from the engine: recursive evaluation of
/// nested expressions (composition), string/path evaluation, condition testing,
/// and access to the function registries.
/// </summary>
internal interface IEvaluator
{
    JsonNode? Evaluate(JsonNode? expression, EvaluationContext context);

    JsonNode? EvaluateString(string expression, EvaluationContext context);

    bool EvaluateWhere(WhereDefinition definition, EvaluationContext context);

    /// <summary>Compare an already-evaluated value against a literal with an operator.</summary>
    bool Compare(JsonNode? left, string op, JsonNode? value);

    AggregatorRegistry Aggregators { get; }

    TransformRegistry Transforms { get; }
}

/// <summary>
/// A node whose DSL can appear inline (nested) inside another expression.
/// <see cref="Key"/> is the JSON property that identifies it.
/// </summary>
internal interface INodeExecutor
{
    string Key { get; }

    JsonNode? ExecuteInline(JsonObject host, EvaluationContext context, IEvaluator evaluator);
}
