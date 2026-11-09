using System.Data.Common;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace SpanFlow.Connectors.Database;

/// <summary>
/// SQLite adapter (Microsoft.Data.Sqlite). Brought in with E02-T2 so table-mode delivery can be
/// exercised against a real in-memory/file database; SQL Server + MySQL adapters follow in E02-T3.
/// </summary>
public sealed class SqliteAdapter : IDbProviderAdapter
{
    public string Provider => "sqlite";

    public string ParameterPrefix => "@";

    /// <summary>
    /// Opens the way every SQLite caller does — a pipeline run, the workbench, an MCP table — so the
    /// missing-folder rule lives here once. SQLite creates a missing database <i>file</i> on open but not
    /// its folder, so "Data Source=data/x.db" (the form the AI authoring guidelines recommend) failed a
    /// fresh deployment's first run with "SQLite Error 14: unable to open database file", while the
    /// workbench's <see cref="Initialize"/> created the folder and worked. Browsing is unaffected: it is
    /// refused by <see cref="ValidateIntrospectable"/> before a connection is ever made.
    /// </summary>
    public DbConnection CreateConnection(string connectionString)
    {
        EnsureParentDirectory(connectionString);
        return new SqliteConnection(connectionString);
    }

    public string QuoteIdentifier(string identifier) =>
        string.Join('.', identifier.Split('.').Select(part => $"\"{part.Replace("\"", "\"\"")}\""));

    public void AddPayloadParameter(DbCommand command, string name, string payloadJson) =>
        AddValue(command, name, payloadJson);

    // SQLite is dynamically typed, so the dbType hint isn't needed — nested JSON stores as text.
    public void AddTypedParameter(DbCommand command, string name, JsonNode? value, string? dbType) =>
        AddValue(command, name, DbValues.ToClr(value));

    public string BuildUpsert(
        string quotedTable, IReadOnlyList<string> columns,
        IReadOnlyList<string> keyColumns, IReadOnlyList<string> valueGroups) =>
        DbUpsert.OnConflictExcluded(this, quotedTable, columns, keyColumns, valueGroups);

    public string LimitOffsetClause(int limit, int offset) => $"LIMIT {limit} OFFSET {offset}";

    /// <summary>
    /// Refuses to browse a database file that is not there yet. Opening one would create it, and an
    /// empty new file lists no tables — so a mistyped path, or a demo whose setup pipeline has not
    /// been run, read as "this database has no tables" and left a 0-byte file behind. In-memory and
    /// shared-cache databases have no file and are always browsable.
    /// </summary>
    public void ValidateIntrospectable(string connectionString)
    {
        var full = ResolveFilePath(connectionString);
        if (full is null) return;

        // A zero-byte file counts as "not there": SQLite writes a 100-byte header the moment it
        // creates anything, so an empty one is the leftover of an earlier browse that opened a path
        // nothing had written to — the very state this refusal exists to explain.
        var file = new FileInfo(full);
        if (!file.Exists || file.Length == 0)
        {
            throw new DatabaseNotInitializedException(
                Provider, full,
                $"The SQLite database file '{full}' does not exist yet, so there are no tables to browse. "
                + "A relative 'Data Source' is resolved against the API's working directory. Create the "
                + "empty database here and add tables in the workbench, or run the pipeline that creates "
                + "them, or point the connection at an existing file.");
        }
    }

    /// <summary>
    /// Creates the file the refusal above named: an empty database with a real header and no tables,
    /// so browsing it stops being a refusal and starts being an honest "no tables yet". SQLite only
    /// writes the header once something is stored, so opening the file is not enough — a table is
    /// created and dropped, which leaves a valid, empty database behind. A missing parent directory
    /// is created too, because "Data Source=data/app.db" against a fresh working directory would
    /// otherwise fail with an error about a path the caller never typed.
    /// </summary>
    public string Initialize(string connectionString)
    {
        var full = ResolveFilePath(connectionString)
            ?? throw new InvalidOperationException(
                "This SQLite connection has no database file to create — in-memory and 'file:' URI databases already exist for as long as they are open.");

        EnsureParentDirectory(connectionString);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder(connectionString)
        {
            DataSource = full,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS _dataflow_init (x); DROP TABLE _dataflow_init;";
        command.ExecuteNonQuery();

        return full;
    }

    /// <summary>
    /// Creates the database file's folder when an open could create the file itself. Not for a read-only
    /// open — that cannot create anything, and a missing file there is an honest error, not a folder to
    /// make — nor for databases with no file at all.
    /// </summary>
    private static void EnsureParentDirectory(string connectionString)
    {
        var mode = new SqliteConnectionStringBuilder(connectionString).Mode;
        if (mode is SqliteOpenMode.ReadOnly or SqliteOpenMode.Memory) return;

        var directory = ResolveFilePath(connectionString) is { } full ? Path.GetDirectoryName(full) : null;
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
    }

    /// <summary>
    /// The full path of the connection's database file, or null when there is no file to speak of —
    /// an in-memory database, a shared-cache "file:" URI, or a connection string with no data source.
    /// Those are always browsable and can never be "missing", so both callers above skip them.
    /// </summary>
    private static string? ResolveFilePath(string connectionString)
    {
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        return string.IsNullOrWhiteSpace(dataSource)
            || dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
            || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            ? null
            : Path.GetFullPath(dataSource);
    }

    public async Task<IReadOnlyList<DbTableDto>> ListTablesAsync(DbConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
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
        // SQLite has no schemas; use the table name as-is (strip any qualifier).
        var (_, name) = DbIntrospection.SplitTable(table, defaultSchema: "");

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, type, \"notnull\", pk FROM pragma_table_info(@t) ORDER BY cid";
        DbIntrospection.AddString(command, "@t", name);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var columns = new List<DbColumnDto>();
        while (await reader.ReadAsync(ct))
        {
            columns.Add(new DbColumnDto
            {
                Name = reader.GetString(0),
                DataType = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Nullable = reader.GetInt64(2) == 0,   // notnull flag inverted
                IsPrimaryKey = reader.GetInt64(3) > 0, // pk position (0 = not part of PK)
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
        command.CommandText = "SELECT \"from\" FROM pragma_foreign_key_list(@t)";
        DbIntrospection.AddString(command, "@t", name);
        return await DbIntrospection.ReadStringsAsync(command, ct);
    }
}
