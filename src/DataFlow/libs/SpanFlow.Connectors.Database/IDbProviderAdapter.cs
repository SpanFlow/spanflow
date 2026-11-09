using System.Data.Common;
using System.Text.Json.Nodes;

namespace SpanFlow.Connectors.Database;

/// <summary>
/// Provider-specific ADO/SQL details for the database connector. One implementation per
/// RDBMS (postgres today; sqlserver/mysql/sqlite in E02-T3) so the connector stays
/// provider-agnostic. Table-mode upsert dialects (E02-T2) and schema introspection (E02-T4)
/// extend this interface as those tasks land.
/// </summary>
public interface IDbProviderAdapter
{
    /// <summary>Key matched (case-insensitively) against a connection config's "provider".</summary>
    string Provider { get; }

    /// <summary>Prefix for a bound parameter placeholder in SQL text (e.g. "@").</summary>
    string ParameterPrefix { get; }

    /// <summary>Creates a closed connection for the given connection string.</summary>
    DbConnection CreateConnection(string connectionString);

    /// <summary>
    /// Quotes a possibly schema-qualified identifier, escaping embedded quote characters
    /// (e.g. <c>public.orders</c> → <c>"public"."orders"</c> for postgres).
    /// </summary>
    string QuoteIdentifier(string identifier);

    /// <summary>
    /// Binds the whole pipeline payload as one parameter for raw jsonb-style delivery. Each
    /// provider chooses the right column type (postgres: jsonb; others: string).
    /// </summary>
    void AddPayloadParameter(DbCommand command, string name, string payloadJson);

    /// <summary>
    /// Binds one table-mode column value, coercing the JSON value to the provider's type
    /// (honoring the optional <paramref name="dbType"/> hint, e.g. "jsonb", "numeric").
    /// </summary>
    void AddTypedParameter(DbCommand command, string name, JsonNode? value, string? dbType);

    /// <summary>
    /// Builds a complete upsert statement keyed on <paramref name="keyColumns"/> for the given
    /// rows — <c>INSERT ... ON CONFLICT DO UPDATE</c> (postgres/sqlite),
    /// <c>INSERT ... ON DUPLICATE KEY UPDATE</c> (mysql), or <c>MERGE</c> (sqlserver).
    /// <paramref name="columns"/> are the (unquoted) column names in row order;
    /// <paramref name="valueGroups"/> are the pre-built <c>"(@p0, @p1)"</c> placeholder tuples.
    /// </summary>
    string BuildUpsert(
        string quotedTable, IReadOnlyList<string> columns,
        IReadOnlyList<string> keyColumns, IReadOnlyList<string> valueGroups);

    /// <summary>
    /// The dialect's row-window clause for a paged read (E24-T3 <c>dbTable</c> list). Postgres,
    /// MySQL and SQLite spell it <c>LIMIT n OFFSET m</c>; SQL Server uses
    /// <c>OFFSET m ROWS FETCH NEXT n ROWS ONLY</c>, which requires an <c>ORDER BY</c> — the caller
    /// always supplies one. <paramref name="limit"/> and <paramref name="offset"/> are integers, so
    /// they are safe to inline; no client string ever reaches this clause.
    /// </summary>
    string LimitOffsetClause(int limit, int offset);

    /// <summary>
    /// Checked before an *introspection* connection is opened, so browsing can refuse instead of
    /// quietly inventing a database. Only file-backed providers have anything to say: SQLite creates
    /// the file named by "Data Source" the moment it is opened, so browsing a connection whose file
    /// does not exist yet succeeded and reported zero tables — indistinguishable, on screen, from a
    /// database whose tables had been dropped. Throws <see cref="InvalidOperationException"/> to
    /// refuse; the default is to allow (a server-backed provider has no file to miss).
    /// </summary>
    void ValidateIntrospectable(string connectionString) { }

    /// <summary>
    /// Creates the empty database a <see cref="DatabaseNotInitializedException"/> named, and returns
    /// what was created (for SQLite, the resolved file path). Only file-backed providers implement
    /// it: a server-backed provider's database is created by whoever administers that server, never
    /// by us, so the default refuses. Creating an <i>empty</i> database is the whole job — the tables
    /// are the caller's next step, in the workbench or in the pipeline that owns them.
    /// </summary>
    string Initialize(string connectionString) =>
        throw new NotSupportedException(
            $"A '{Provider}' database has to be created on its server; only file-backed databases can be created from here.");

    /// <summary>Lists the user tables of the database on an open connection.</summary>
    Task<IReadOnlyList<DbTableDto>> ListTablesAsync(DbConnection connection, CancellationToken ct);

    /// <summary>Lists the columns (name, type, nullability, PK membership) of one table.</summary>
    Task<IReadOnlyList<DbColumnDto>> ListColumnsAsync(DbConnection connection, string table, CancellationToken ct);

    /// <summary>
    /// The columns of one table that are part of a foreign key (E56-T8). Every adapter answers it on
    /// purpose, with no default: an empty answer would make every column editable in the workbench.
    /// </summary>
    Task<IReadOnlyList<string>> ListForeignKeyColumnsAsync(DbConnection connection, string table, CancellationToken ct);

    /// <summary>
    /// How the workbench executes a read (E56-T8). SQL Server and Npgsql only report the table a column came
    /// from under <see cref="System.Data.CommandBehavior.KeyInfo"/>; SQLite and MySqlConnector report it by default.
    /// </summary>
    System.Data.CommandBehavior WorkbenchReadBehavior => System.Data.CommandBehavior.Default;

    /// <summary>
    /// Binds a value whose column type the server should decide (E56-T8: a workbench row edit, where the
    /// value arrives as JSON and the column could be a uuid, a timestamp or a number). Postgres sends it as
    /// an untyped literal so <c>col = $1</c> takes the column's type; the others convert on their own.
    /// </summary>
    void AddInferredParameter(DbCommand command, string name, JsonNode? value) => AddTypedParameter(command, name, value, null);

    /// <summary>A table hint that locks the rows a <c>SELECT</c> reads until the transaction ends (SQL Server).</summary>
    string LockRowsTableHint => "";

    /// <summary>The clause that locks the rows a <c>SELECT</c> reads until the transaction ends (postgres, mysql).</summary>
    string LockRowsSuffix => "";
}
