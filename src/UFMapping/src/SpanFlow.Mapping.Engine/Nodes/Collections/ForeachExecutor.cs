using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

/// <summary>Projects each item of a collection through a map of expressions.</summary>
internal sealed class ForeachExecutor
{
    public JsonNode Execute(
        string foreachPath,
        IReadOnlyDictionary<string, JsonNode?> map,
        EvaluationContext context,
        IEvaluator evaluator)
        => Project(JsonPathResolver.ResolveMany(context, foreachPath), map, context, evaluator);

    /// <summary>Iterate a nested collection expression (e.g. a wired input / parameter).</summary>
    public JsonNode Execute(
        JsonNode? input,
        IReadOnlyDictionary<string, JsonNode?> map,
        EvaluationContext context,
        IEvaluator evaluator)
    {
        var node = evaluator.Evaluate(input, context);
        var items = node is JsonArray array ? array.AsEnumerable() : node is null ? [] : [node];
        return Project(items, map, context, evaluator);
    }

    private static JsonNode Project(
        IEnumerable<JsonNode?> source,
        IReadOnlyDictionary<string, JsonNode?> map,
        EvaluationContext context,
        IEvaluator evaluator)
    {
        var items = source.ToArray();
        var result = new JsonArray();

        foreach (var item in items)
        {
            var itemContext = context.WithCurrent(item);
            var targetObject = new JsonObject();

            foreach (var pair in map)
            {
                JsonPathWriter.SetValue(targetObject, pair.Key, evaluator.Evaluate(pair.Value, itemContext));
            }

            result.Add(targetObject);
        }

        return result;
    }
}
