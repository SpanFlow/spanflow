using System.Text.Json;
using System.Text.Json.Nodes;
using SpanFlow.Mapping.Core.Models;

namespace SpanFlow.Mapping.Engine;

internal sealed class FilterExecutor : INodeExecutor
{
    public string Key => "filter";

    public JsonNode? ExecuteInline(JsonObject host, EvaluationContext context, IEvaluator evaluator)
        => Execute(host["filter"]!.Deserialize<FilterDefinition>(JsonSerializerOptionsProvider.Options)!, context, evaluator);

    public JsonNode Execute(FilterDefinition definition, EvaluationContext context, IEvaluator evaluator)
    {
        // Resolve the collection; a single resolved array is flattened to its items.
        List<JsonNode?> resolved = definition.Input is not null
            ? [evaluator.Evaluate(definition.Input, context)]
            : JsonPathResolver.ResolveMany(context, definition.From ?? "$").ToList();

        IEnumerable<JsonNode?> items = resolved is [JsonArray single]
            ? single
            : resolved;

        var result = new JsonArray();
        foreach (var item in items)
        {
            if (evaluator.EvaluateWhere(definition.Where, context.WithCurrent(item)))
            {
                result.Add(item?.DeepClone());
            }
        }

        return result;
    }
}
