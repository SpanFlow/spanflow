using System.Text.Json;
using System.Text.Json.Nodes;
using SpanFlow.Mapping.Core.Models;

namespace SpanFlow.Mapping.Engine;

internal sealed class ValueMapExecutor : INodeExecutor
{
    public string Key => "valueMap";

    public JsonNode? ExecuteInline(JsonObject host, EvaluationContext context, IEvaluator evaluator)
        => Execute(host["valueMap"]!.Deserialize<ValueMapDefinition>(JsonSerializerOptionsProvider.Options)!, context, evaluator);

    public JsonNode? Execute(ValueMapDefinition definition, EvaluationContext context, IEvaluator evaluator)
    {
        // A nested input expression (another node's output) takes precedence over a
        // plain path, so value-maps can be chained after aggregates, transforms, etc.
        var lookup = definition.Input is not null
            ? evaluator.Evaluate(definition.Input, context)
            : evaluator.EvaluateString(definition.From ?? "$", context);
        var key = JsonScalarConverter.ToText(lookup);

        if (key is not null && definition.Table.TryGetValue(key, out var mapped))
        {
            return mapped?.DeepClone();
        }

        return definition.Default?.DeepClone();
    }
}
