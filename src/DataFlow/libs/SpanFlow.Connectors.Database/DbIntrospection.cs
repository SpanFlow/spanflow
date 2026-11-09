using System.Data.Common;
using System.Text.Json.Nodes;

namespace SpanFlow.Connectors.Database;

/// <summary>Small helpers shared by the adapters' schema-introspection queries.</summary>
internal static class DbIntrospection
{
    /// <summary>Splits <c>schema.table</c> into its parts, using <paramref name="defaultSchema"/> when unqualified.</summary>
    public static (string Schema, string Name) SplitTable(string table, string defaultSchema)
    {
        var dot = table.LastIndexOf('.');
        return dot < 0 ? (defaultSchema, table) : (table[..dot], table[(dot + 1)..]);
    }

    public static void AddString(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    /// <summary>Runs <paramref name="command"/> and returns its first column, distinct, in order.</summary>
    public static async Task<IReadOnlyList<string>> ReadStringsAsync(DbCommand command, CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(ct);
        var values = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            if (!reader.IsDBNull(0) && !values.Contains(reader.GetString(0))) values.Add(reader.GetString(0));
        }
        return values;
    }

    /// <summary>
    /// A JSON value as the literal text a server can read into whatever type the column has (E56-T8):
    /// a string as itself, a number or boolean as its JSON text, an object or array as JSON.
    /// </summary>
    public static object LiteralText(JsonNode? value) => value switch
    {
        null => DBNull.Value,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        _ => value.ToJsonString(),
    };
}
