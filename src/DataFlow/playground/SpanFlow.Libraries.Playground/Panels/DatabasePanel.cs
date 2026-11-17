using System.Data.Common;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using SpanFlow.Connectors.Database;
using SpanFlow.Expressions;
using SQLitePCL;

namespace SpanFlow.Libraries.Playground.Panels;

/// <summary>One connection as the page sees it: never a password, and the whole string only for the hosted demo.</summary>
public sealed record ConnectionView(string Id, string Name, string Provider, string Display, string Source, bool AllowWrites);

/// <summary>
/// The connections a query may use (D71.11). <c>sample</c> is the in-memory SQLite sample (always there). Hosted:
/// only the configured ones (the demo database), never a string from a request. Self-hosted: configured ones plus
/// the ones the owner saves from the page.
/// </summary>
public sealed class Connections(IOptions<PlaygroundOptions> options, IServiceProvider services)
{
    public const string SampleId = "sample";

    private readonly PlaygroundOptions _options = options.Value;

    public static readonly DbProviderRegistry Registry = new([new PostgresAdapter(), new SqlServerAdapter(), new MySqlAdapter(), new SqliteAdapter()]);

    private SavedData? Saved => _options.IsHosted ? null : services.GetService<SavedData>();

    public IReadOnlyList<ConnectionView> List()
    {
        var list = new List<ConnectionView> { new(SampleId, "Sample (SQLite, in memory)", "sqlite", "A small sample made for each query and gone after it", "built in", false) };
        list.AddRange(_options.Connections.Select(c => new ConnectionView(c.Id, c.Name.Length > 0 ? c.Name : c.Id, c.Provider,
            c.ShowConnectionString ? c.ConnectionString : Mask(c.ConnectionString), "config", c.AllowWrites)));
        if (Saved is { } saved)
            list.AddRange(saved.Read(s => s.Connections.Select(c => new ConnectionView(c.Id, c.Name, c.Provider, Mask(c.ConnectionString), "saved", c.AllowWrites)).ToList()));
        return list;
    }

    public (string Provider, string ConnectionString, bool AllowWrites, bool Hosted) Resolve(string id)
    {
        if (_options.Connections.FirstOrDefault(c => c.Id == id) is { } configured)
            return (configured.Provider, configured.ConnectionString, configured.AllowWrites && !_options.IsHosted, _options.IsHosted);
        if (Saved?.Read(s => s.Connections.FirstOrDefault(c => c.Id == id)) is { } saved)
            return (saved.Provider, saved.ConnectionString, saved.AllowWrites, false);
        throw PanelRefusal.NotFound($"No connection '{id}'.");
    }

    private static readonly Regex Password = new(@"(?i)\b(password|pwd)\s*=\s*[^;]*", RegexOptions.Compiled);

    public static string Mask(string connectionString) => Password.Replace(connectionString, "$1=******");
}

/// <summary>
/// <c>POST /api/database/inspect</c>: <c>{ sql, provider?, params? }</c>: <see cref="QueryAdvisor.Inspect"/> for that
/// provider, the <see cref="SqlFingerprint"/>, and the <c>{{ }}</c> placeholders bound as parameters the way the
/// database connector binds them (<see cref="TemplateRenderer.RenderWithParameters"/>), so a value can never become SQL.
/// <para>
/// <c>POST /api/database/query</c>: <c>{ connectionId, sql, params? }</c>: the same, then the statement runs on that
/// connection: one statement, under the time cap, at most <c>MaxRows</c> rows back. Read only unless the connection
/// allows writes: a read-only run happens inside a transaction that is always rolled back (and is a read-only
/// transaction on PostgreSQL), and must start like a read.
/// </para>
/// </summary>
public static partial class DatabasePanel
{
    public static IReadOnlyCollection<string> Providers => Connections.Registry.Providers;

    public static Task<JsonNode> Inspect(PanelCall call)
    {
        var (sql, provider, context) = Read(call.Body);
        return Task.FromResult<JsonNode>(Inspection(sql, provider, context, call.Limits));
    }

    public static async Task<JsonNode> Query(PanelCall call)
    {
        var connectionId = PlaygroundEndpoint.OptionalString(call.Body, "connectionId") ?? Connections.SampleId;
        if (connectionId == Connections.SampleId) return await QuerySample(call);

        var connections = call.Services.GetRequiredService<Connections>();
        var (provider, connectionString, allowWrites, _) = connections.Resolve(connectionId);
        var (sql, _, context) = Read(call.Body);
        var answer = Inspection(sql, provider, context, call.Limits);
        answer["connectionId"] = connectionId;
        var ct = call.Cancellation;
        var adapter = Connections.Registry.Get(provider);
        if (!allowWrites && !StartsLikeARead(sql))
            throw PanelRefusal.Invalid("This connection is read only: send a SELECT (or WITH ... SELECT, SHOW, EXPLAIN).");

        var (text, parameters) = Bind(sql, context, adapter.ParameterPrefix + "p");
        var clock = Stopwatch.StartNew();
        try
        {
            await using var connection = adapter.CreateConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            if (!allowWrites && provider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
            {
                await using var readOnly = connection.CreateCommand();
                readOnly.Transaction = transaction;
                readOnly.CommandText = "SET TRANSACTION READ ONLY";
                await readOnly.ExecuteNonQueryAsync(ct);
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = text;
            command.CommandTimeout = Math.Max(1, (int)Math.Ceiling(call.Limits.TimeoutMs / 1000.0));
            foreach (var parameter in parameters) adapter.AddTypedParameter(command, parameter.Name, parameter.Value, null);

            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                await ReadRows(reader, answer, call.Limits.MaxRows, ct);
                answer["rowsAffected"] = reader.RecordsAffected;
            }

            if (allowWrites) await transaction.CommitAsync(ct);
            else await transaction.RollbackAsync(ct);
            answer["committed"] = allowWrites;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DbException ex)
        {
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ex.Message, ex, ct);
            throw PanelRefusal.Failed(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw PanelRefusal.Failed(ex.Message);
        }

        answer["elapsedMs"] = Math.Round(clock.Elapsed.TotalMilliseconds, 1);
        return answer;
    }

    /// <summary>The in-memory SQLite sample, locked down (D71.4).</summary>
    private static async Task<JsonNode> QuerySample(PanelCall call)
    {
        var (sql, _, context) = Read(call.Body);
        var answer = Inspection(sql, "sqlite", context, call.Limits);
        answer["connectionId"] = Connections.SampleId;
        var ct = call.Cancellation;

        var (text, parameters) = Bind(sql, context, "@p");
        var clock = Stopwatch.StartNew();
        await using var connection = await SampleDatabase.OpenLockedAsync(clock, call.Limits.TimeoutMs, ct);
        RequireOneStatement(connection, text);

        await using var command = connection.CreateCommand();
        command.CommandText = text;
        var adapter = new SqliteAdapter();
        foreach (var parameter in parameters) adapter.AddTypedParameter(command, parameter.Name, parameter.Value, null);

        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            await ReadRows(reader, answer, call.Limits.MaxRows, ct);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == raw.SQLITE_INTERRUPT)
        {
            throw new OperationCanceledException("The query ran past the time limit.", ex, ct.IsCancellationRequested ? ct : new CancellationToken(true));
        }
        catch (SqliteException ex)
        {
            throw PanelRefusal.Failed(Explain(ex.Message));
        }

        answer["elapsedMs"] = Math.Round(clock.Elapsed.TotalMilliseconds, 1);
        return answer;
    }

    private static async Task ReadRows(DbDataReader reader, JsonObject answer, int maxRows, CancellationToken ct)
    {
        var rows = new JsonArray();
        var columns = new JsonArray();
        var truncated = false;
        for (var i = 0; i < reader.FieldCount; i++) columns.Add(reader.GetName(i));
        while (await reader.ReadAsync(ct))
        {
            if (rows.Count == maxRows) { truncated = true; break; }
            var row = new JsonObject();
            for (var i = 0; i < reader.FieldCount; i++) row[UniqueName(row, reader.GetName(i))] = Value(reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i));
            rows.Add(row);
        }
        answer["columns"] = columns;
        answer["rows"] = rows;
        answer["rowCount"] = rows.Count;
        answer["truncated"] = truncated;
        answer["maxRows"] = maxRows;
    }

    private static (string Sql, string Provider, ExpressionContext Context) Read(JsonObject body)
    {
        var sql = PlaygroundEndpoint.RequireString(body, "sql", SampleDatabase.MaxSqlBytes);
        if (string.IsNullOrWhiteSpace(sql)) throw PanelRefusal.Invalid("Send some SQL.");
        ExpressionsPanel.CheckTemplate(sql);

        var provider = (PlaygroundEndpoint.OptionalString(body, "provider") ?? "sqlite").ToLowerInvariant();
        if (!Providers.Contains(provider)) throw PanelRefusal.Invalid($"\"provider\" is one of {string.Join(", ", Providers)}.");

        var parameters = body["params"] switch
        {
            null => null,
            JsonObject p => p.DeepClone(),
            _ => throw PanelRefusal.Invalid("\"params\" is an object; {{ params.name }} in the SQL reads it."),
        };
        return (sql, provider, new ExpressionContext { Params = parameters });
    }

    private static JsonObject Inspection(string sql, string provider, ExpressionContext context, PanelLimits limits)
    {
        var advice = QueryAdvisor.Inspect(sql, new QueryAdvisorOptions(provider, MaxRows: limits.MaxRows));
        var (text, parameters) = Bind(sql, context, "@p");
        return new JsonObject
        {
            ["provider"] = provider,
            ["advice"] = new JsonArray(advice.Select(a => (JsonNode?)new JsonObject
            {
                ["code"] = a.Code,
                ["severity"] = a.Severity,
                ["message"] = a.Message,
                ["suggestion"] = a.Suggestion,
            }).ToArray()),
            ["blocked"] = QueryAdvisor.IsBlocked(advice),
            ["fingerprint"] = SqlFingerprint.Canonicalize(sql),
            ["boundSql"] = text,
            ["parameters"] = new JsonArray(parameters.Select(p => (JsonNode?)new JsonObject
            {
                ["name"] = p.Name,
                ["value"] = p.Value?.DeepClone(),
            }).ToArray()),
        };
    }

    private static (string Text, IReadOnlyList<TemplateParameter> Parameters) Bind(string sql, ExpressionContext context, string prefix)
    {
        try
        {
            return TemplateRenderer.RenderWithParameters(sql, context, prefix);
        }
        catch (ExpressionException ex)
        {
            throw PanelRefusal.Failed(ex.Message);
        }
    }

    [GeneratedRegex(@"^\s*(?:(?:--[^\n]*\n)|(?:/\*.*?\*/)|\s)*(?<word>[A-Za-z]+)", RegexOptions.Singleline)]
    private static partial Regex FirstWord();

    /// <summary>The first keyword, past comments, is one a read starts with.</summary>
    internal static bool StartsLikeARead(string sql) =>
        FirstWord().Match(sql) is { Success: true } m
        && m.Groups["word"].Value.ToLowerInvariant() is "select" or "with" or "show" or "explain" or "values" or "table" or "describe" or "desc";

    /// <summary>
    /// Sample only: one statement, and a read. Microsoft.Data.Sqlite would otherwise run every statement in the text in
    /// turn. The authorizer refuses anything but a read anyway; this gives the clearer message first.
    /// </summary>
    private static void RequireOneStatement(SqliteConnection connection, string sql)
    {
        var db = connection.Handle!;
        var rc = raw.sqlite3_prepare_v2(db, sql, out var statement, out var tail);
        using (statement)
        {
            if (rc != raw.SQLITE_OK) throw PanelRefusal.Failed(Explain(raw.sqlite3_errmsg(db).utf8_to_string()));
            if (statement is null || statement.IsInvalid) throw PanelRefusal.Invalid("Send one SELECT statement.");
            if (raw.sqlite3_stmt_readonly(statement) == 0) throw PanelRefusal.Invalid("The sample database is read-only: send a SELECT.");
        }

        if (string.IsNullOrWhiteSpace(tail)) return;
        rc = raw.sqlite3_prepare_v2(db, tail, out var next, out _);
        using (next)
        {
            if (rc != raw.SQLITE_OK || (next is not null && !next.IsInvalid))
                throw PanelRefusal.Invalid("Send one statement at a time.");
        }
    }

    private static string Explain(string? message) =>
        message is not null && message.Contains("not authorized", StringComparison.OrdinalIgnoreCase)
            ? "Not allowed here: the sample database answers reads only (no ATTACH, PRAGMA, VACUUM or writes)."
            : message ?? "SQLite refused the statement.";

    private static string UniqueName(JsonObject row, string name)
    {
        var unique = name.Length == 0 ? "column" : name;
        for (var n = 2; row.ContainsKey(unique); n++) unique = $"{name}_{n}";
        return unique;
    }

    private static JsonNode? Value(object value) => value switch
    {
        DBNull => null,
        bool b => JsonValue.Create(b),
        byte or short or int or long or sbyte or ushort or uint => JsonValue.Create(Convert.ToInt64(value)),
        ulong u => JsonValue.Create(u),
        float f => double.IsFinite(f) ? JsonValue.Create((double)f) : JsonValue.Create(f.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        double d => double.IsFinite(d) ? JsonValue.Create(d) : JsonValue.Create(d.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        decimal m => JsonValue.Create(m),
        string s => JsonValue.Create(s),
        DateTime dt => JsonValue.Create(dt.ToString("o", System.Globalization.CultureInfo.InvariantCulture)),
        DateTimeOffset dto => JsonValue.Create(dto.ToString("o", System.Globalization.CultureInfo.InvariantCulture)),
        DateOnly d => JsonValue.Create(d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
        TimeSpan t => JsonValue.Create(t.ToString()),
        Guid g => JsonValue.Create(g.ToString()),
        byte[] bytes => new JsonObject { ["base64"] = Convert.ToBase64String(bytes) },
        _ => JsonValue.Create(value.ToString()),
    };
}
