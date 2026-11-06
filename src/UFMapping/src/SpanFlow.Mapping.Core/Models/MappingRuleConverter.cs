using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SpanFlow.Mapping.Core.Models;

/// <summary>
/// Reads a <see cref="MappingRule"/> in either form:
/// <list type="bullet">
/// <item>Typed (preferred): <c>{ "target": "x", "type": "aggregate", "function": "sum", ... }</c></item>
/// <item>Legacy (implicit): <c>{ "target": "x", "aggregate": { "function": "sum", ... } }</c></item>
/// </list>
/// In the typed form the operation's fields are flattened onto the rule, which
/// maps directly onto each definition type, so binding is just a deserialize.
/// </summary>
public sealed class MappingRuleConverter : JsonConverter<MappingRule>
{
    public override MappingRule Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var obj = JsonSerializer.Deserialize<JsonObject>(ref reader, options)
            ?? throw new JsonException("Expected a mapping rule object.");

        var target = obj["target"]?.GetValue<string>()
            ?? throw new JsonException("A mapping rule must define a target.");

        var type = obj["type"]?.GetValue<string>();
        return type is null
            ? ReadLegacy(obj, target, options)
            : ReadTyped(obj, target, type, options);
    }

    private static MappingRule ReadTyped(JsonObject obj, string target, string type, JsonSerializerOptions options)
    {
        // The flattened fields are exactly the definition's fields, so the whole
        // object deserializes straight into the definition (extra keys ignored).
        return type.ToLowerInvariant() switch
        {
            "fieldcopy" or "field" => new MappingRule { Target = target, From = (obj["path"] ?? obj["from"])?.GetValue<string>() },
            "aggregate" => new MappingRule { Target = target, Aggregate = obj.Deserialize<AggregateDefinition>(options) },
            "transform" => new MappingRule { Target = target, Transform = obj.Deserialize<TransformDefinition>(options) },
            "valuemap" => new MappingRule { Target = target, ValueMap = obj.Deserialize<ValueMapDefinition>(options) },
            "sort" => new MappingRule { Target = target, Sort = obj.Deserialize<SortDefinition>(options) },
            "filter" => new MappingRule { Target = target, Filter = obj.Deserialize<FilterDefinition>(options) },
            "join" => new MappingRule { Target = target, Join = obj.Deserialize<JoinDefinition>(options) },
            "exception" => new MappingRule { Target = target, Exception = obj.Deserialize<ExceptionDefinition>(options) },
            "condition" or "ifelse" => new MappingRule { Target = target, Condition = obj.Deserialize<ConditionDefinition>(options) },
            "groupby" => new MappingRule { Target = target, GroupBy = obj.Deserialize<GroupByDefinition>(options) },
            "foreach" => new MappingRule
            {
                Target = target,
                Foreach = (obj["from"] ?? obj["foreach"])?.GetValue<string>(),
                ForeachInput = obj["input"]?.DeepClone(),
                Map = obj["map"]?.Deserialize<Dictionary<string, JsonNode?>>(options),
            },
            _ => throw new JsonException($"Unknown rule type '{type}'."),
        };
    }

    private static MappingRule ReadLegacy(JsonObject obj, string target, JsonSerializerOptions options)
    {
        return new MappingRule
        {
            Target = target,
            From = obj["from"]?.GetValue<string>(),
            Foreach = obj["foreach"]?.GetValue<string>(),
            Map = obj["map"]?.Deserialize<Dictionary<string, JsonNode?>>(options),
            Aggregate = obj["aggregate"]?.Deserialize<AggregateDefinition>(options),
            GroupBy = obj["groupBy"]?.Deserialize<GroupByDefinition>(options),
            Transform = obj["transform"]?.Deserialize<TransformDefinition>(options),
            Condition = obj["condition"]?.Deserialize<ConditionDefinition>(options),
            ValueMap = obj["valueMap"]?.Deserialize<ValueMapDefinition>(options),
            Sort = obj["sort"]?.Deserialize<SortDefinition>(options),
            Filter = obj["filter"]?.Deserialize<FilterDefinition>(options),
            Join = obj["join"]?.Deserialize<JoinDefinition>(options),
            Exception = obj["exception"]?.Deserialize<ExceptionDefinition>(options),
        };
    }

    public override void Write(Utf8JsonWriter writer, MappingRule value, JsonSerializerOptions options)
    {
        // Emit the legacy nested form (round-trip only; not used by the runtime).
        writer.WriteStartObject();
        writer.WriteString("target", value.Target);

        void Mode(string name, object? definition)
        {
            if (definition is null) return;
            writer.WritePropertyName(name);
            JsonSerializer.Serialize(writer, definition, definition.GetType(), options);
        }

        if (value.From is not null) writer.WriteString("from", value.From);
        if (value.Foreach is not null)
        {
            writer.WriteString("foreach", value.Foreach);
            Mode("map", value.Map);
        }
        Mode("aggregate", value.Aggregate);
        Mode("groupBy", value.GroupBy);
        Mode("transform", value.Transform);
        Mode("condition", value.Condition);
        Mode("valueMap", value.ValueMap);
        Mode("sort", value.Sort);
        Mode("filter", value.Filter);
        Mode("join", value.Join);
        Mode("exception", value.Exception);

        writer.WriteEndObject();
    }
}
