using System.Text.Json.Nodes;

namespace SpanFlow.Connectors.Database;

/// <summary>Shared JSON→ADO value coercion used by provider adapters for table-mode binding.</summary>
internal static class DbValues
{
    /// <summary>
    /// Converts a JSON value to a CLR value ADO providers understand: null → DBNull, bool/number/
    /// string as-is, objects/arrays serialized to their JSON text (for json/text columns).
    /// </summary>
    public static object ToClr(JsonNode? node)
    {
        if (node is null) return DBNull.Value;
        if (node is JsonObject or JsonArray) return node.ToJsonString();
        if (node is JsonValue value)
        {
            if (value.TryGetValue<bool>(out var b)) return b;
            if (value.TryGetValue<long>(out var l)) return l;
            if (value.TryGetValue<double>(out var d)) return d;
            if (value.TryGetValue<string>(out var s)) return s;
        }
        return node.ToJsonString();
    }

    /// <summary>True when the dbType hint names a JSON column type.</summary>
    public static bool IsJson(string? dbType) =>
        dbType is not null && (dbType.Equals("json", StringComparison.OrdinalIgnoreCase)
                               || dbType.Equals("jsonb", StringComparison.OrdinalIgnoreCase));
}
