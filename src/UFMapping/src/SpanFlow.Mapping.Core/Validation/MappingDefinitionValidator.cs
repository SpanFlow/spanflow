using SpanFlow.Mapping.Core.Models;

namespace SpanFlow.Mapping.Core.Validation;

public static class MappingDefinitionValidator
{
    public static void Validate(MappingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definition.Target);

        var type = definition.Target.Type;

        if (string.Equals(type, "array", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(definition.Target.From) || definition.Target.Map is null || definition.Target.Map.Count == 0)
            {
                throw new InvalidOperationException("An array target must define 'from' and a non-empty 'map'.");
            }
            return;
        }

        if (!string.Equals(type, "object", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only object and array targets are supported.");
        }

        foreach (var rule in definition.Target.Mappings)
        {
            ValidateRule(rule);
        }
    }

    private static void ValidateRule(MappingRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Target))
        {
            throw new InvalidOperationException("Each mapping rule must define a target path.");
        }

        var configuredModes = 0;
        configuredModes += !string.IsNullOrWhiteSpace(rule.From) ? 1 : 0;
        configuredModes += rule.Foreach is not null || rule.ForeachInput is not null ? 1 : 0;
        configuredModes += rule.Aggregate is not null ? 1 : 0;
        configuredModes += rule.GroupBy is not null ? 1 : 0;
        configuredModes += rule.Transform is not null ? 1 : 0;
        configuredModes += rule.Condition is not null ? 1 : 0;
        configuredModes += rule.ValueMap is not null ? 1 : 0;
        configuredModes += rule.Sort is not null ? 1 : 0;
        configuredModes += rule.Filter is not null ? 1 : 0;
        configuredModes += rule.Join is not null ? 1 : 0;
        configuredModes += rule.Exception is not null ? 1 : 0;

        if (configuredModes != 1)
        {
            throw new InvalidOperationException(
                $"Rule '{rule.Target}' must define exactly one execution mode.");
        }

        if (rule.Sort is not null && rule.Sort.Keys.Count == 0)
        {
            throw new InvalidOperationException($"Sort rule '{rule.Target}' must define at least one key.");
        }

        if (rule.ValueMap is not null && rule.ValueMap.Table.Count == 0 && rule.ValueMap.Default is null)
        {
            throw new InvalidOperationException($"ValueMap rule '{rule.Target}' must define a table entry or a default.");
        }

        if ((rule.Foreach is not null || rule.ForeachInput is not null) && (rule.Map is null || rule.Map.Count == 0))
        {
            throw new InvalidOperationException($"Foreach rule '{rule.Target}' must define a map block.");
        }

        if (rule.GroupBy is not null && rule.GroupBy.Map.Count == 0)
        {
            throw new InvalidOperationException($"GroupBy rule '{rule.Target}' must define a map block.");
        }
    }
}
