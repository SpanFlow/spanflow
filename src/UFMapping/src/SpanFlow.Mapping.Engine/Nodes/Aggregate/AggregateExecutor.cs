using System.Text.Json;
using System.Text.Json.Nodes;
using SpanFlow.Mapping.Core.Models;

namespace SpanFlow.Mapping.Engine;

internal sealed class AggregateExecutor : INodeExecutor
{
    public string Key => "aggregate";

    public JsonNode? ExecuteInline(JsonObject host, EvaluationContext context, IEvaluator evaluator)
        => Execute(host["aggregate"]!.Deserialize<AggregateDefinition>(JsonSerializerOptionsProvider.Options)!, context, evaluator);

    public JsonNode? Execute(AggregateDefinition definition, EvaluationContext context, IEvaluator evaluator)
    {
        var aggregator = evaluator.Aggregators.Get(definition.Function);

        IEnumerable<JsonNode?> candidates;
        if (definition.Input is not null)
        {
            // A wired collection (e.g. a function's list parameter): evaluate it,
            // and fold its elements if it resolves to an array.
            var node = evaluator.Evaluate(definition.Input, context);
            candidates = node is JsonArray array ? array : node is null ? [] : [node];
        }
        else if (!string.IsNullOrWhiteSpace(definition.From))
        {
            candidates = JsonPathResolver.ResolveMany(context, definition.From!);
        }
        else if (context.GroupItems is not null)
        {
            candidates = context.GroupItems;
        }
        else if (context.Current is not null)
        {
            candidates = [context.Current];
        }
        else
        {
            candidates = [];
        }

        if (definition.Where is not null)
        {
            candidates = candidates.Where(item => evaluator.EvaluateWhere(definition.Where, context.WithCurrent(item)));
        }

        if (!string.IsNullOrWhiteSpace(definition.Select))
        {
            candidates = candidates.SelectMany(item =>
                JsonPathResolver.ResolveMany(context.WithCurrent(item), definition.Select!));
        }

        return aggregator is IParameterizedAggregator parameterized
            ? parameterized.Aggregate(candidates, definition)
            : aggregator.Aggregate(candidates);
    }
}
