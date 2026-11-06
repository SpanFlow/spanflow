using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

internal static class JsonScalarConverter
{
    public static decimal ToDecimal(JsonNode? node)
    {
        var value = ToObject(node);
        return value switch
        {
            null => 0m,
            decimal dec => dec,
            int intValue => intValue,
            long longValue => longValue,
            double doubleValue => Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture),
            float floatValue => Convert.ToDecimal(floatValue, CultureInfo.InvariantCulture),
            string text when decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture)
        };
    }

    public static bool TryToDecimal(JsonNode? node, out decimal value)
    {
        switch (ToObject(node))
        {
            case decimal dec: value = dec; return true;
            case int i: value = i; return true;
            case long l: value = l; return true;
            case double d: value = Convert.ToDecimal(d, CultureInfo.InvariantCulture); return true;
            case float f: value = Convert.ToDecimal(f, CultureInfo.InvariantCulture); return true;
            case string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed):
                value = parsed; return true;
            default:
                value = 0m; return false;
        }
    }

    public static string? ToText(JsonNode? node)
    {
        return ToObject(node)?.ToString();
    }

    /// <summary>
    /// JSON truthiness: null/false/0/"" are falsy; non-empty strings, non-zero
    /// numbers, true, and any object/array are truthy.
    /// </summary>
    public static bool IsTruthy(JsonNode? node)
    {
        return ToObject(node) switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0,
            decimal d => d != 0m,
            int i => i != 0,
            long l => l != 0,
            double db => db != 0,
            float f => f != 0,
            _ => true
        };
    }

    public static object? ToObject(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonObject or JsonArray)
        {
            return node.DeepClone();
        }

        var element = JsonSerializer.Deserialize<JsonElement>(node.ToJsonString());

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => node.ToJsonString()
        };
    }
}
