using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

internal sealed class NodeValueComparer : IComparer<JsonNode?>
{
    public static NodeValueComparer Instance { get; } = new();

    public int Compare(JsonNode? x, JsonNode? y)
    {
        // Nulls sort first.
        var xObj = JsonScalarConverter.ToObject(x);
        var yObj = JsonScalarConverter.ToObject(y);
        if (xObj is null && yObj is null) return 0;
        if (xObj is null) return -1;
        if (yObj is null) return 1;

        // Numeric comparison when both sides are numbers; otherwise ordinal text.
        if (JsonScalarConverter.TryToDecimal(x, out var dx) && JsonScalarConverter.TryToDecimal(y, out var dy))
        {
            return dx.CompareTo(dy);
        }

        return string.Compare(JsonScalarConverter.ToText(x), JsonScalarConverter.ToText(y), StringComparison.Ordinal);
    }
}

internal sealed class JsonNodeStructuralComparer : IEqualityComparer<JsonNode?>
{
    public static JsonNodeStructuralComparer Instance { get; } = new();

    public bool Equals(JsonNode? x, JsonNode? y)
    {
        return string.Equals(x?.ToJsonString(), y?.ToJsonString(), StringComparison.Ordinal);
    }

    public int GetHashCode(JsonNode? obj)
    {
        return StringComparer.Ordinal.GetHashCode(obj?.ToJsonString() ?? string.Empty);
    }
}
