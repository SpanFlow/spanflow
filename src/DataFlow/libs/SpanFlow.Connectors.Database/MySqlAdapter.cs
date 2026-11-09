using System.Data.Common;
using System.Text.Json.Nodes;
using MySqlConnector;

namespace SpanFlow.Connectors.Database;

/// <summary>
/// MySQL / MariaDB adapter (MySqlConnector). Upserts via <c>ON DUPLICATE KEY UPDATE</c>.
/// The connection is scoped to a single database, so introspection reports no schema.
/// </summary>
public sealed class MySqlAdapter : IDbProviderAdapter
{
    public string Provider => "mysql";

    public string ParameterPrefix => "@";

    public DbConnection CreateConnection(string connectionString) => new MySqlConnection(connectionString);

    public string QuoteIdentifier(string identifier) =>
        string.Join('.', identifier.Split('.').Select(part => $"`{part.Replace("`", "``")}`"));

    public void AddPayloadParameter(DbCommand command, string name, string payloadJson) =>
        AddValue(command, name, payloadJson);

    public void AddTypedParameter(DbCommand command, string name, JsonNode? value, string? dbType) =>
        AddValue(command, name, DbValues.ToClr(value));

    public string BuildUpsert(
        string quotedTable, IReadOnlyList<string> columns,
        IReadOnlyList<string> keyColumns, IReadOnlyList<string> valueGroups) =>
        DbUpsert.OnDuplicateKey(this, quotedTable, columns, keyColumns, valueGroups);

    public string LimitOffsetClause(int limit, int offset) => $"LIMIT {limit} OFFSET {offset}";

    public async Task<IReadOnlyList<DbTableDto>> ListTablesAsync(DbConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT TABLE_NAME FROM information_schema.tables
            WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_SCHEMA = DATABASE()
            ORDER BY TABLE_NAME
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);

        var tables = new List<DbTableDto>();
        while (await reader.ReadAsync(ct))
        {
            tables.Add(new DbTableDto { Schema = null, Name = reader.GetString(0) });
        }
        return tables;
    }

    public async Task<IReadOnlyList<DbColumnDto>> ListColumnsAsync(DbConnection connection, string table, CancellationToken ct)
    {
        // MySQL's schema is the database; the connection already selects it. Use the bare table name.
        var (_, name) = DbIntrospection.SplitTable(table, defaultSchema: "");

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_KEY
            FROM information_schema.columns
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t
            ORDER BY ORDINAL_POSITION
            """;
        DbIntrospection.AddString(command, "@t", name);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var columns = new List<DbColumnDto>();
        while (await reader.ReadAsync(ct))
        {
            columns.Add(new DbColumnDto
            {
                Name = reader.GetString(0),
                DataType = reader.GetString(1),
                Nullable = string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase),
                IsPrimaryKey = string.Equals(reader.GetString(3), "PRI", StringComparison.OrdinalIgnoreCase),
            });
        }
        return columns;
    }

    private static void AddValue(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    public async Task<IReadOnlyList<string>> ListForeignKeyColumnsAsync(DbConnection connection, string table, CancellationToken ct)
    {
        var (_, name) = DbIntrospection.SplitTable(table, defaultSchema: "");
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COLUMN_NAME FROM information_schema.KEY_COLUMN_USAGE
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND REFERENCED_TABLE_NAME IS NOT NULL
            """;
        DbIntrospection.AddString(command, "@t", name);
        return await DbIntrospection.ReadStringsAsync(command, ct);
    }

    public string LockRowsSuffix => " FOR UPDATE";
}
