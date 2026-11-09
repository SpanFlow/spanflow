using System.Data.Common;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;

namespace SpanFlow.Connectors.Database;

/// <summary>PostgreSQL adapter (the historical default provider), backed by Npgsql.</summary>
public sealed class PostgresAdapter : IDbProviderAdapter
{
    public string Provider => "postgres";

    public string ParameterPrefix => "@";

    public DbConnection CreateConnection(string connectionString) => new NpgsqlConnection(connectionString);

    public string QuoteIdentifier(string identifier) =>
        string.Join('.', identifier.Split('.').Select(part => $"\"{part.Replace("\"", "\"\"")}\""));

    public void AddPayloadParameter(DbCommand command, string name, string payloadJson) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb) { Value = payloadJson });

    public void AddTypedParameter(DbCommand command, string name, JsonNode? value, string? dbType)
    {
        if (DbValues.IsJson(dbType))
        {
            command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
            {
                Value = value?.ToJsonString() ?? (object)DBNull.Value,
            });
            return;
        }
        command.Parameters.Add(new NpgsqlParameter(name, DbValues.ToClr(value)));
    }

    public string BuildUpsert(
        string quotedTable, IReadOnlyList<string> columns,
        IReadOnlyList<string> keyColumns, IReadOnlyList<string> valueGroups) =>
        DbUpsert.OnConflictExcluded(this, quotedTable, columns, keyColumns, valueGroups);

    public string LimitOffsetClause(int limit, int offset) => $"LIMIT {limit} OFFSET {offset}";

    public async Task<IReadOnlyList<DbTableDto>> ListTablesAsync(DbConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT table_schema, table_name FROM information_schema.tables
            WHERE table_type = 'BASE TABLE' AND table_schema NOT IN ('pg_catalog', 'information_schema')
            ORDER BY table_schema, table_name
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);

        var tables = new List<DbTableDto>();
        while (await reader.ReadAsync(ct))
        {
            tables.Add(new DbTableDto { Schema = reader.GetString(0), Name = reader.GetString(1) });
        }
        return tables;
    }

    public async Task<IReadOnlyList<DbColumnDto>> ListColumnsAsync(DbConnection connection, string table, CancellationToken ct)
    {
        var (schema, name) = DbIntrospection.SplitTable(table, defaultSchema: "public");

        var primaryKeys = new HashSet<string>();
        await using (var pkCommand = connection.CreateCommand())
        {
            pkCommand.CommandText =
                """
                SELECT kcu.column_name
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu
                  ON kcu.constraint_name = tc.constraint_name AND kcu.table_schema = tc.table_schema
                WHERE tc.constraint_type = 'PRIMARY KEY' AND tc.table_schema = @s AND tc.table_name = @t
                """;
            DbIntrospection.AddString(pkCommand, "@s", schema);
            DbIntrospection.AddString(pkCommand, "@t", name);
            await using var pkReader = await pkCommand.ExecuteReaderAsync(ct);
            while (await pkReader.ReadAsync(ct)) primaryKeys.Add(pkReader.GetString(0));
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT column_name, data_type, is_nullable
            FROM information_schema.columns
            WHERE table_schema = @s AND table_name = @t
            ORDER BY ordinal_position
            """;
        DbIntrospection.AddString(command, "@s", schema);
        DbIntrospection.AddString(command, "@t", name);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var columns = new List<DbColumnDto>();
        while (await reader.ReadAsync(ct))
        {
            var columnName = reader.GetString(0);
            columns.Add(new DbColumnDto
            {
                Name = columnName,
                DataType = reader.GetString(1),
                Nullable = string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase),
                IsPrimaryKey = primaryKeys.Contains(columnName),
            });
        }
        return columns;
    }

    public async Task<IReadOnlyList<string>> ListForeignKeyColumnsAsync(DbConnection connection, string table, CancellationToken ct)
    {
        var (schema, name) = DbIntrospection.SplitTable(table, defaultSchema: "public");
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT kcu.column_name
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON kcu.constraint_name = tc.constraint_name AND kcu.constraint_schema = tc.constraint_schema
             AND kcu.table_name = tc.table_name
            WHERE tc.constraint_type = 'FOREIGN KEY' AND tc.table_schema = @s AND tc.table_name = @t
            """;
        DbIntrospection.AddString(command, "@s", schema);
        DbIntrospection.AddString(command, "@t", name);
        return await DbIntrospection.ReadStringsAsync(command, ct);
    }

    // Unknown-typed text: the server reads it as the type of whatever it is compared with or assigned to,
    // so a uuid, timestamp or numeric column takes a JSON string without "operator does not exist".
    public void AddInferredParameter(DbCommand command, string name, JsonNode? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Unknown) { Value = DbIntrospection.LiteralText(value) });

    public string LockRowsSuffix => " FOR UPDATE";

    // Npgsql reports a column's table only under KeyInfo (it looks the table oid up when asked).
    public System.Data.CommandBehavior WorkbenchReadBehavior => System.Data.CommandBehavior.KeyInfo;
}
