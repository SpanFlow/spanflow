using System.Text.Json;
using System.Text.Json.Nodes;
using SpanFlow.Mapping.Core.Models;

namespace SpanFlow.Mapping.Engine;

/// <summary>Inner-joins two collections on matching keys, merging item fields.</summary>
internal sealed class JoinExecutor : INodeExecutor
{
    public string Key => "join";

    public JsonNode? ExecuteInline(JsonObject host, EvaluationContext context, IEvaluator evaluator)
        => Execute(host["join"]!.Deserialize<JoinDefinition>(JsonSerializerOptionsProvider.Options)!, context, evaluator);

    public JsonNode Execute(JoinDefinition definition, EvaluationContext context, IEvaluator evaluator)
    {
        var left = Resolve(definition.LeftInput, definition.Left, context, evaluator);
        var right = Resolve(definition.RightInput, definition.Right, context, evaluator).ToList();

        var result = new JsonArray();
        foreach (var leftItem in left)
        {
            var leftKey = JsonScalarConverter.ToText(
                JsonPathResolver.ResolveSingle(context.WithCurrent(leftItem), definition.LeftKey));

            foreach (var rightItem in right)
            {
                var rightKey = JsonScalarConverter.ToText(
                    JsonPathResolver.ResolveSingle(context.WithCurrent(rightItem), definition.RightKey));

                if (string.Equals(leftKey, rightKey, StringComparison.Ordinal))
                {
                    result.Add(Merge(leftItem, rightItem));
                }
            }
        }

        return result;
    }

    private static IEnumerable<JsonNode?> Resolve(JsonNode? input, string? path, EvaluationContext context, IEvaluator evaluator)
    {
        List<JsonNode?> resolved = input is not null
            ? [evaluator.Evaluate(input, context)]
            : JsonPathResolver.ResolveMany(context, path ?? "$").ToList();

        return resolved is [JsonArray single] ? single : resolved;
    }

    private static JsonObject Merge(JsonNode? left, JsonNode? right)
    {
        var merged = new JsonObject();
        if (left is JsonObject leftObject)
        {
            foreach (var property in leftObject)
            {
                merged[property.Key] = property.Value?.DeepClone();
            }
        }

        // Right fields overwrite left on key conflicts.
        if (right is JsonObject rightObject)
        {
            foreach (var property in rightObject)
            {
                merged[property.Key] = property.Value?.DeepClone();
            }
        }

        return merged;
    }
}
