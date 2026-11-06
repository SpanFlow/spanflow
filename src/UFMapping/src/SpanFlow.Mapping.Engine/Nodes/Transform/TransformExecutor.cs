using System.Text.Json;
using System.Text.Json.Nodes;
using SpanFlow.Mapping.Core.Models;

namespace SpanFlow.Mapping.Engine;

internal sealed class TransformExecutor : INodeExecutor
{
    // Inline transforms are identified by the object carrying a "function" key.
    public string Key => "function";

    public JsonNode? ExecuteInline(JsonObject host, EvaluationContext context, IEvaluator evaluator)
        => Execute(host.Deserialize<TransformDefinition>(JsonSerializerOptionsProvider.Options)!, context, evaluator);

    public JsonNode? Execute(TransformDefinition definition, EvaluationContext context, IEvaluator evaluator)
    {
        var function = evaluator.Transforms.Get(definition.Function);
        var args = definition.Args.Select(arg => evaluator.Evaluate(arg, context)).ToArray();
        return function.Apply(args);
    }
}
