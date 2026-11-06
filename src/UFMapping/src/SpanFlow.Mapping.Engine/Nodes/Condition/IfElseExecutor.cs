using System.Text.Json;
using System.Text.Json.Nodes;
using SpanFlow.Mapping.Core.Models;

namespace SpanFlow.Mapping.Engine;

internal sealed class IfElseExecutor : INodeExecutor
{
    // Inline conditions are identified by the object carrying an "if" key.
    public string Key => "if";

    public JsonNode? ExecuteInline(JsonObject host, EvaluationContext context, IEvaluator evaluator)
        => Execute(host.Deserialize<ConditionDefinition>(JsonSerializerOptionsProvider.Options)!, context, evaluator);

    public JsonNode? Execute(ConditionDefinition definition, EvaluationContext context, IEvaluator evaluator)
    {
        var isMatch = evaluator.EvaluateWhere(definition.If, context);
        return evaluator.Evaluate(isMatch ? definition.Then : definition.Else, context);
    }
}
