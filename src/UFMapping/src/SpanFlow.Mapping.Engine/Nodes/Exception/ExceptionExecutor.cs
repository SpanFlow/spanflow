using System.Text.Json;
using System.Text.Json.Nodes;
using SpanFlow.Mapping.Core;
using SpanFlow.Mapping.Core.Models;

namespace SpanFlow.Mapping.Engine;

/// <summary>
/// Raises a <see cref="MappingException"/> with a human-readable message when
/// the input meets the condition; otherwise passes the input through unchanged.
/// </summary>
internal sealed class ExceptionExecutor : INodeExecutor
{
    public string Key => "exception";

    public JsonNode? ExecuteInline(JsonObject host, EvaluationContext context, IEvaluator evaluator)
        => Execute(host["exception"]!.Deserialize<ExceptionDefinition>(JsonSerializerOptionsProvider.Options)!, context, evaluator);

    public JsonNode? Execute(ExceptionDefinition definition, EvaluationContext context, IEvaluator evaluator)
    {
        var value = definition.Input is not null
            ? evaluator.Evaluate(definition.Input, context)
            : evaluator.EvaluateString(definition.From ?? "$", context);

        if (evaluator.Compare(value, definition.Op, definition.Value))
        {
            throw new MappingException(definition.Message);
        }

        return value?.DeepClone();
    }
}
