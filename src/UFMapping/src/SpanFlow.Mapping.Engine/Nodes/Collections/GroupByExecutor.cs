using System.Text.Json.Nodes;
using SpanFlow.Mapping.Core.Models;

namespace SpanFlow.Mapping.Engine;

/// <summary>Groups a collection by a key and projects each group through a map.</summary>
internal sealed class GroupByExecutor
{
    public JsonNode Execute(GroupByDefinition groupBy, EvaluationContext context, IEvaluator evaluator)
    {
        JsonNode?[] items;
        if (groupBy.Input is not null)
        {
            var node = evaluator.Evaluate(groupBy.Input, context);
            items = node is JsonArray array ? array.ToArray() : node is null ? [] : [node];
        }
        else
        {
            items = JsonPathResolver.ResolveMany(context, groupBy.From ?? "$").ToArray();
        }
        // WHERE: drop rows before they are grouped, so the key, the aggregates
        // and `items` all see only the rows that matched.
        if (groupBy.Where is not null)
        {
            items = items
                .Where(item => evaluator.EvaluateWhere(groupBy.Where, context.WithCurrent(item)))
                .ToArray();
        }

        var groups = items.GroupBy(item =>
        {
            var keyNode = JsonPathResolver.ResolveSingle(context.WithCurrent(item), groupBy.Key);
            return JsonScalarConverter.ToText(keyNode) ?? string.Empty;
        }, StringComparer.Ordinal);

        var result = new JsonArray();

        foreach (var group in groups)
        {
            var groupItems = group.ToArray();
            var groupContext = context.WithGroup(groupItems, group.Key).WithCurrent(groupItems.FirstOrDefault());
            var targetObject = new JsonObject();

            foreach (var pair in groupBy.Map)
            {
                JsonNode? value;
                if (pair.Value is JsonValue jsonValue &&
                    jsonValue.TryGetValue<string>(out var text) &&
                    string.Equals(text, "key", StringComparison.OrdinalIgnoreCase))
                {
                    value = JsonValue.Create(group.Key);
                }
                else
                {
                    value = evaluator.Evaluate(pair.Value, groupContext);
                }

                JsonPathWriter.SetValue(targetObject, pair.Key, value);
            }

            // HAVING: test the finished group object, so a clause can name an
            // aggregate by its output field (e.g. "orderCount" gte 5). The group
            // context is kept, so a clause may also nest its own aggregate.
            if (groupBy.Having is not null &&
                !evaluator.EvaluateWhere(groupBy.Having, groupContext.WithCurrent(targetObject)))
            {
                continue;
            }

            result.Add(targetObject);
        }

        return result;
    }
}
