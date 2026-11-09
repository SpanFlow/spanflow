using System.Text.Json.Nodes;
using SpanFlow.Expressions;

namespace SpanFlow.Connectors.Database;

/// <summary>
/// Builds parameterized table-mode SQL (insert / update / upsert) from a deliver request and the
/// payload rows. Column values are extracted by JSON path and bound as parameters — never spliced
/// into the SQL text. Pure/synchronous so it is unit-testable without a database.
/// </summary>
public sealed class DbTableWriter(IDbProviderAdapter adapter)
{
    /// <summary>One value to bind: its placeholder name, the JSON value, and an optional dbType hint.</summary>
    public readonly record struct Bind(string Name, JsonNode? Value, string? DbType);

    public sealed record ColumnSpec(string Column, string? Path, JsonNode? Literal, bool Now, string? DbType);

    public sealed record TableRequest(
        string Table, string Operation, IReadOnlyList<ColumnSpec> Columns,
        IReadOnlyList<string> KeyColumns, int BatchSize, bool Transaction);

    public static TableRequest Parse(JsonObject request)
    {
        var table = request["table"]?.GetValue<string>()
            ?? throw new InvalidOperationException("table-mode deliver requires a 'table'.");

        var operation = (request["operation"]?.GetValue<string>() ?? "insert").ToLowerInvariant();
        if (operation is not ("insert" or "update" or "upsert"))
        {
            throw new InvalidOperationException($"table-mode 'operation' must be insert, update, or upsert (got '{operation}').");
        }

        if (request["columns"] is not JsonArray columnsJson || columnsJson.Count == 0)
        {
            throw new InvalidOperationException("table-mode deliver requires a non-empty 'columns' array.");
        }

        var columns = new List<ColumnSpec>();
        foreach (var entry in columnsJson)
        {
            var column = entry?["column"]?.GetValue<string>()
                ?? throw new InvalidOperationException("each table-mode column requires a 'column' name.");
            columns.Add(new ColumnSpec(
                Column: column,
                Path: entry?["path"]?.GetValue<string>(),
                Literal: entry?["value"]?.DeepClone(),
                Now: entry?["now"]?.GetValue<bool>() == true,
                DbType: entry?["dbType"]?.GetValue<string>()));
        }

        var keyColumns = (request["keyColumns"] as JsonArray)?.Select(k => k!.GetValue<string>()).ToList() ?? [];
        if (operation is "update" or "upsert" && keyColumns.Count == 0)
        {
            throw new InvalidOperationException($"table-mode '{operation}' requires 'keyColumns'.");
        }

        var batchSize = request["batchSize"] is JsonNode b && int.TryParse(b.ToString(), out var n) && n > 0 ? n : 500;
        var transaction = request["transaction"]?.GetValue<bool>() ?? true;

        return new TableRequest(table, operation, columns, keyColumns, batchSize, transaction);
    }

    /// <summary>A single multi-row INSERT for the batch (with an upsert clause when requested).</summary>
    public (string Sql, IReadOnlyList<Bind> Binds) BuildInsert(TableRequest request, IReadOnlyList<JsonNode> rows, bool upsert)
    {
        var binds = new List<Bind>();
        var valueGroups = new List<string>();
        foreach (var row in rows)
        {
            var placeholders = new List<string>();
            foreach (var column in request.Columns)
            {
                var name = $"{adapter.ParameterPrefix}p{binds.Count}";
                placeholders.Add(name);
                binds.Add(new Bind(name, ValueFor(column, row), column.DbType));
            }
            valueGroups.Add($"({string.Join(", ", placeholders)})");
        }

        var quotedTable = adapter.QuoteIdentifier(request.Table);
        var columnNames = request.Columns.Select(c => c.Column).ToList();

        if (upsert)
        {
            return (adapter.BuildUpsert(quotedTable, columnNames, request.KeyColumns, valueGroups), binds);
        }

        var columnList = string.Join(", ", request.Columns.Select(c => adapter.QuoteIdentifier(c.Column)));
        return ($"INSERT INTO {quotedTable} ({columnList}) VALUES {string.Join(", ", valueGroups)}", binds);
    }

    /// <summary>An UPDATE ... WHERE keyColumns for one row.</summary>
    public (string Sql, IReadOnlyList<Bind> Binds) BuildUpdate(TableRequest request, JsonNode row)
    {
        var binds = new List<Bind>();
        var byColumn = request.Columns.ToDictionary(c => c.Column);

        var setColumns = request.Columns.Where(c => !request.KeyColumns.Contains(c.Column));
        var setClause = string.Join(", ", setColumns.Select(column =>
        {
            var name = $"{adapter.ParameterPrefix}p{binds.Count}";
            binds.Add(new Bind(name, ValueFor(column, row), column.DbType));
            return $"{adapter.QuoteIdentifier(column.Column)} = {name}";
        }));

        var whereClause = string.Join(" AND ", request.KeyColumns.Select(key =>
        {
            var column = byColumn.TryGetValue(key, out var spec) ? spec : new ColumnSpec(key, null, null, false, null);
            var name = $"{adapter.ParameterPrefix}p{binds.Count}";
            binds.Add(new Bind(name, ValueFor(column, row), column.DbType));
            return $"{adapter.QuoteIdentifier(key)} = {name}";
        }));

        var sql = $"UPDATE {adapter.QuoteIdentifier(request.Table)} SET {setClause} WHERE {whereClause}";
        return (sql, binds);
    }

    /// <summary>The column's value for a row: now() token, a literal, or the JSON path (defaults to the column name).</summary>
    public JsonNode? ValueFor(ColumnSpec column, JsonNode row)
    {
        if (column.Now) return JsonValue.Create(DateTimeOffset.UtcNow.ToString("o"));
        if (column.Literal is not null) return column.Literal.DeepClone();

        var path = string.IsNullOrWhiteSpace(column.Path) ? column.Column : column.Path.TrimStart('$', '.');
        return ExpressionEvaluator.Evaluate($"input.{path}", new ExpressionContext { Input = row });
    }
}
