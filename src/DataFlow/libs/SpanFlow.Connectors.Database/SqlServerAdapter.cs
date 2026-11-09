using System.Data.Common;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;

namespace SpanFlow.Connectors.Database;

/// <summary>SQL Server adapter (Microsoft.Data.SqlClient). Upserts via <c>MERGE</c>.</summary>
public sealed class SqlServerAdapter : IDbProviderAdapter
{
    public string Provider => "sqlserver";

    public string ParameterPrefix => "@";

    public DbConnection CreateConnection(string connectionString) => new SqlConnection(connectionString);

    public string QuoteIdentifier(string identifier) =>
        string.Join('.', identifier.Split('.').Select(part => $"[{part.Replace("]", "]]")}]"));

    public void AddPayloadParameter(DbCommand command, string name, string payloadJson) =>
        AddValue(command, name, payloadJson);

    public void AddTypedParameter(DbCommand command, string name, JsonNode? value, string? dbType) =>
        AddValue(command, name, DbValues.ToClr(value));

    public string BuildUpsert(
        string quotedTable, IReadOnlyList<string> columns,
        IReadOnlyList<string> keyColumns, IReadOnlyList<string> valueGroups) =>
        DbUpsert.Merge(this, quotedTable, columns, keyColumns, valueGroups);

    public string LimitOffsetClause(int limit, int offset) =>
        $"OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY";

    public async Task<IReadOnlyList<DbTableDto>> ListTablesAsync(DbConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_SCHEMA, TABLE_NAME
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
        var (schema, name) = DbIntrospection.SplitTable(table, defaultSchema: "dbo");

        var primaryKeys = new HashSet<string>();
        await using (var pkCommand = connection.CreateCommand())
        {
            pkCommand.CommandText =
                """
                SELECT kcu.COLUMN_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                  ON kcu.CONSTRAINT_NAME = tc.CONSTRAINT_NAME AND kcu.TABLE_SCHEMA = tc.TABLE_SCHEMA
                WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY' AND tc.TABLE_SCHEMA = @s AND tc.TABLE_NAME = @t
                """;
            DbIntrospection.AddString(pkCommand, "@s", schema);
            DbIntrospection.AddString(pkCommand, "@t", name);
            await using var pkReader = await pkCommand.ExecuteReaderAsync(ct);
            while (await pkReader.ReadAsync(ct)) primaryKeys.Add(pkReader.GetString(0));
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @s AND TABLE_NAME = @t
            ORDER BY ORDINAL_POSITION
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

    private static void AddValue(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    public async Task<IReadOnlyList<string>> ListForeignKeyColumnsAsync(DbConnection connection, string table, CancellationToken ct)
    {
        var (schema, name) = DbIntrospection.SplitTable(table, defaultSchema: "dbo");
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT kcu.COLUMN_NAME
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
            JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
              ON kcu.CONSTRAINT_NAME = tc.CONSTRAINT_NAME AND kcu.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA
            WHERE tc.CONSTRAINT_TYPE = 'FOREIGN KEY' AND tc.TABLE_SCHEMA = @s AND tc.TABLE_NAME = @t
            """;
        DbIntrospection.AddString(command, "@s", schema);
        DbIntrospection.AddString(command, "@t", name);
        return await DbIntrospection.ReadStringsAsync(command, ct);
    }

    // Without KeyInfo, SqlClient leaves BaseTableName empty and the workbench could never offer an edit.
    public System.Data.CommandBehavior WorkbenchReadBehavior => System.Data.CommandBehavior.KeyInfo;

    public string LockRowsTableHint => " WITH (UPDLOCK, ROWLOCK)";
}
