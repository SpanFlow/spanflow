using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpanFlow.Mapping.Core.Models;
using SpanFlow.Mapping.Core.Validation;

namespace SpanFlow.Mapping.Engine;

public sealed class MappingEngine : IEvaluator
{
    private readonly AggregateExecutor _aggregate = new();
    private readonly ValueMapExecutor _valueMap = new();
    private readonly SortExecutor _sort = new();
    private readonly FilterExecutor _filter = new();
    private readonly JoinExecutor _join = new();
    private readonly TransformExecutor _transform = new();
    private readonly IfElseExecutor _ifElse = new();
    private readonly ForeachExecutor _foreach = new();
    private readonly GroupByExecutor _groupBy = new();
    private readonly ExceptionExecutor _exception = new();

    /// <summary>Inline (nested) executors, checked in order against an expression object.</summary>
    private readonly IReadOnlyList<INodeExecutor> _inlineExecutors;

    public MappingEngine()
        : this(AggregatorRegistry.CreateDefault(), TransformRegistry.CreateDefault())
    {
    }

    public MappingEngine(AggregatorRegistry aggregators, TransformRegistry transforms)
    {
        Aggregators = aggregators;
        Transforms = transforms;
        _inlineExecutors = [_aggregate, _transform, _ifElse, _valueMap, _sort, _filter, _join, _exception];
    }

    public AggregatorRegistry Aggregators { get; }

    public TransformRegistry Transforms { get; }

    public JsonNode Execute(MappingDefinition definition, JsonNode? input)
    {
        return Execute(definition, new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["input"] = input
        });
    }

    public JsonNode Execute(MappingDefinition definition, IReadOnlyDictionary<string, JsonNode?> sources)
    {
        MappingDefinitionValidator.Validate(definition);

        if (sources.Keys.Any(name => string.Equals(name, JsonPathResolver.GroupSourceName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new SpanFlow.Mapping.Core.MappingException(
                $"'{JsonPathResolver.GroupSourceName}' is a reserved source name: inside a groupBy map it names the group's own members. Rename the source.");
        }

        var context = new EvaluationContext(sources, null, null, null);

        // Array-root target: the whole output is a list projected from a source list.
        if (string.Equals(definition.Target.Type, "array", StringComparison.OrdinalIgnoreCase))
        {
            return _foreach.Execute(definition.Target.From!, definition.Target.Map!, context, this);
        }

        var output = new JsonObject();

        foreach (var rule in definition.Target.Mappings)
        {
            JsonNode? value;
            try
            {
                value = ExecuteRule(rule, context);
            }
            catch (SpanFlow.Mapping.Core.MappingException)
            {
                throw; // already a clean, user-facing message
            }
            catch (Exception ex)
            {
                throw new SpanFlow.Mapping.Core.MappingException($"Error mapping '{rule.Target}': {ex.Message}", ex);
            }

            JsonPathWriter.SetValue(output, rule.Target, value);
        }

        return output;
    }

    private JsonNode? ExecuteRule(MappingRule rule, EvaluationContext context)
    {
        if (rule.From is not null) return EvaluateString(rule.From, context);
        if (rule.Transform is not null) return _transform.Execute(rule.Transform, context, this);
        if (rule.Condition is not null) return _ifElse.Execute(rule.Condition, context, this);
        if (rule.Aggregate is not null) return _aggregate.Execute(rule.Aggregate, context, this);
        if (rule.ValueMap is not null) return _valueMap.Execute(rule.ValueMap, context, this);
        if (rule.Sort is not null) return _sort.Execute(rule.Sort, context, this);
        if (rule.Filter is not null) return _filter.Execute(rule.Filter, context, this);
        if (rule.Join is not null) return _join.Execute(rule.Join, context, this);
        if (rule.ForeachInput is not null) return _foreach.Execute(rule.ForeachInput, rule.Map!, context, this);
        if (rule.Foreach is not null) return _foreach.Execute(rule.Foreach, rule.Map!, context, this);
        if (rule.GroupBy is not null) return _groupBy.Execute(rule.GroupBy, context, this);
        if (rule.Exception is not null) return _exception.Execute(rule.Exception, context, this);

        throw new InvalidOperationException($"Unsupported rule '{rule.Target}'.");
    }

    // ---- IEvaluator: the composition surface shared by all executors --------
    // Explicit interface implementations delegate to these internal methods so
    // the public engine type can expose helpers that take the internal context.

    JsonNode? IEvaluator.Evaluate(JsonNode? expression, EvaluationContext context) => Evaluate(expression, context);

    JsonNode? IEvaluator.EvaluateString(string expression, EvaluationContext context) => EvaluateString(expression, context);

    bool IEvaluator.EvaluateWhere(WhereDefinition definition, EvaluationContext context) => EvaluateWhere(definition, context);

    bool IEvaluator.Compare(JsonNode? left, string op, JsonNode? value) => Compare(left, op, value);

    internal JsonNode? Evaluate(JsonNode? expression, EvaluationContext context)
    {
        switch (expression)
        {
            case null:
                return null;

            case JsonObject jsonObject:
                // The designer wires one node into another as its rule-level
                // wrapper ({ "transform": { "function": ... } }, { "condition": { "if": ... } }).
                // Unwrap it to the inline form, or the nested node would be
                // copied through as data instead of evaluated.
                if (jsonObject.Count == 1
                    && (jsonObject["transform"] ?? jsonObject["condition"]) is JsonObject wrapped)
                {
                    return Evaluate(wrapped, context);
                }

                foreach (var executor in _inlineExecutors)
                {
                    if (jsonObject.ContainsKey(executor.Key))
                    {
                        return executor.ExecuteInline(jsonObject, context, this);
                    }
                }

                // Nested list/object producers (e.g. a function returning these).
                if (jsonObject.ContainsKey("groupBy"))
                {
                    return _groupBy.Execute(jsonObject["groupBy"].Deserialize<GroupByDefinition>(JsonSerializerOptionsProvider.Options)!, context, this);
                }

                if (jsonObject.ContainsKey("foreachInput") && jsonObject.ContainsKey("map"))
                {
                    return _foreach.Execute(jsonObject["foreachInput"], DeserializeMap(jsonObject["map"]), context, this);
                }

                if (jsonObject.ContainsKey("foreach") && jsonObject.ContainsKey("map"))
                {
                    return _foreach.Execute(jsonObject["foreach"]!.GetValue<string>(), DeserializeMap(jsonObject["map"]), context, this);
                }

                // Object construction: evaluate each field.
                if (jsonObject["object"] is JsonObject shape)
                {
                    var built = new JsonObject();
                    foreach (var field in shape)
                    {
                        built[field.Key] = Evaluate(field.Value, context);
                    }
                    return built;
                }

                if (jsonObject.ContainsKey("from"))
                {
                    return EvaluateString(jsonObject["from"]!.GetValue<string>(), context);
                }

                return jsonObject.DeepClone();

            case JsonArray array:
                return new JsonArray(array.Select(item => Evaluate(item, context)).ToArray());

            case JsonValue value when value.TryGetValue<string>(out var text):
                return EvaluateString(text, context);

            default:
                return expression.DeepClone();
        }
    }

    private static IReadOnlyDictionary<string, JsonNode?> DeserializeMap(JsonNode? map)
        => map.Deserialize<Dictionary<string, JsonNode?>>(JsonSerializerOptionsProvider.Options) ?? new();

    internal JsonNode? EvaluateString(string expression, EvaluationContext context)
    {
        if (expression.Length >= 2 && expression[0] == '\'' && expression[^1] == '\'')
        {
            return JsonValue.Create(expression[1..^1]);
        }

        if (string.Equals(expression, "key", StringComparison.OrdinalIgnoreCase) && context.GroupKey is not null)
        {
            return JsonValue.Create(context.GroupKey);
        }

        // A path that asks for a collection evaluates to the whole collection.
        // The "[]" marker means "fan out across this array" wherever it appears:
        // `orders[]` is every order, `orders[].amount` is every amount. Without
        // this, wiring a list into a node as an expression (e.g.
        // `"input": { "from": "orders[]" }`) would silently keep only the first
        // row: the node would then filter/sort/aggregate a one-element list and
        // return a plausible but wrong result. Executors that take `input`
        // already flatten a single resolved array back into its items.
        if (expression.Contains("[]", StringComparison.Ordinal))
        {
            return new JsonArray(JsonPathResolver.ResolveMany(context, expression)
                .Select(node => node?.DeepClone())
                .ToArray());
        }

        return JsonPathResolver.ResolveSingle(context, expression)?.DeepClone();
    }

    internal bool EvaluateWhere(WhereDefinition definition, EvaluationContext context)
    {
        // Group clauses combine children with AND / OR, recursively.
        if (definition.All is { Count: > 0 } all)
        {
            return all.All(clause => EvaluateWhere(clause, context));
        }

        if (definition.Any is { Count: > 0 } any)
        {
            return any.Any(clause => EvaluateWhere(clause, context));
        }

        if (string.IsNullOrWhiteSpace(definition.Op))
        {
            throw new InvalidOperationException("A leaf condition must define an operator.");
        }

        // A nested input expression (another node's output) takes precedence over
        // a plain field path, so conditions can be chained after other nodes.
        var leftNode = definition.Input is not null
            ? Evaluate(definition.Input, context)
            : JsonPathResolver.ResolveSingle(context, definition.Field ?? "$");

        return Compare(leftNode, definition.Op, definition.Value);
    }

    internal bool Compare(JsonNode? left, string op, JsonNode? value)
    {
        var rightValue = JsonScalarConverter.ToObject(value);

        return (op ?? string.Empty).ToLowerInvariant() switch
        {
            "eq" => Equals(JsonScalarConverter.ToObject(left), rightValue),
            "neq" => !Equals(JsonScalarConverter.ToObject(left), rightValue),
            "gt" => CompareNumbers(left, rightValue) > 0,
            "gte" => CompareNumbers(left, rightValue) >= 0,
            "lt" => CompareNumbers(left, rightValue) < 0,
            "lte" => CompareNumbers(left, rightValue) <= 0,
            "contains" => (JsonScalarConverter.ToText(left) ?? string.Empty)
                .Contains(Convert.ToString(rightValue, CultureInfo.InvariantCulture) ?? string.Empty, StringComparison.OrdinalIgnoreCase),
            _ => throw new InvalidOperationException($"Unsupported operator '{op}'.")
        };
    }

    private static int CompareNumbers(JsonNode? leftNode, object? rightValue)
    {
        var left = JsonScalarConverter.ToDecimal(leftNode);
        var right = Convert.ToDecimal(rightValue, CultureInfo.InvariantCulture);
        return left.CompareTo(right);
    }
}
