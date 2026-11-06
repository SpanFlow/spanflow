using System.Text.Json;
using System.Text.Json.Nodes;
using SpanFlow.Mapping.Core.Models;

namespace SpanFlow.Mapping.Engine;

internal sealed class SortExecutor : INodeExecutor
{
    public string Key => "sort";

    public JsonNode? ExecuteInline(JsonObject host, EvaluationContext context, IEvaluator evaluator)
        => Execute(host["sort"]!.Deserialize<SortDefinition>(JsonSerializerOptionsProvider.Options)!, context, evaluator);

    public JsonNode Execute(SortDefinition definition, EvaluationContext context, IEvaluator evaluator)
    {
        // Resolve the collection. A single resolved array is flattened to its
        // items so both `input.orders` and `input.orders[]` work.
        List<JsonNode?> resolved = definition.Input is not null
            ? [evaluator.Evaluate(definition.Input, context)]
            : JsonPathResolver.ResolveMany(context, definition.From ?? "$").ToList();

        IEnumerable<JsonNode?> items = resolved is [JsonArray single]
            ? single
            : resolved;

        IOrderedEnumerable<JsonNode?>? ordered = null;
        foreach (var key in definition.Keys)
        {
            var keyPath = string.IsNullOrWhiteSpace(key.By) ? "$" : key.By;
            JsonNode? KeySelector(JsonNode? item) =>
                JsonPathResolver.ResolveSingle(context.WithCurrent(item), keyPath);

            var descending = string.Equals(key.Order, "desc", StringComparison.OrdinalIgnoreCase);
            ordered = ordered is null
                ? (descending
                    ? items.OrderByDescending(KeySelector, NodeValueComparer.Instance)
                    : items.OrderBy(KeySelector, NodeValueComparer.Instance))
                : (descending
                    ? ordered.ThenByDescending(KeySelector, NodeValueComparer.Instance)
                    : ordered.ThenBy(KeySelector, NodeValueComparer.Instance));
        }

        var result = new JsonArray();
        foreach (var item in ordered ?? items)
        {
            result.Add(item?.DeepClone());
        }

        return result;
    }
}
