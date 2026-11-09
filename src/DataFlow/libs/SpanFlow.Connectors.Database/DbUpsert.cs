namespace SpanFlow.Connectors.Database;

/// <summary>Full upsert-statement builders, one per SQL dialect family.</summary>
internal static class DbUpsert
{
    private static string InsertHead(
        IDbProviderAdapter adapter, string quotedTable, IReadOnlyList<string> columns, IReadOnlyList<string> valueGroups)
    {
        var columnList = string.Join(", ", columns.Select(adapter.QuoteIdentifier));
        return $"INSERT INTO {quotedTable} ({columnList}) VALUES {string.Join(", ", valueGroups)}";
    }

    private static List<string> NonKey(IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns) =>
        columns.Where(c => !keyColumns.Contains(c)).ToList();

    /// <summary>postgres / sqlite: <c>INSERT ... ON CONFLICT (keys) DO UPDATE SET x = excluded.x</c>.</summary>
    public static string OnConflictExcluded(
        IDbProviderAdapter adapter, string quotedTable, IReadOnlyList<string> columns,
        IReadOnlyList<string> keyColumns, IReadOnlyList<string> valueGroups)
    {
        var insert = InsertHead(adapter, quotedTable, columns, valueGroups);
        var keys = string.Join(", ", keyColumns.Select(adapter.QuoteIdentifier));
        var updateColumns = NonKey(columns, keyColumns);
        if (updateColumns.Count == 0)
        {
            return $"{insert} ON CONFLICT ({keys}) DO NOTHING";
        }
        var assignments = string.Join(", ",
            updateColumns.Select(c => $"{adapter.QuoteIdentifier(c)} = excluded.{adapter.QuoteIdentifier(c)}"));
        return $"{insert} ON CONFLICT ({keys}) DO UPDATE SET {assignments}";
    }

    /// <summary>mysql: <c>INSERT ... ON DUPLICATE KEY UPDATE x = VALUES(x)</c> (keyed by the table's unique/PK index).</summary>
    public static string OnDuplicateKey(
        IDbProviderAdapter adapter, string quotedTable, IReadOnlyList<string> columns,
        IReadOnlyList<string> keyColumns, IReadOnlyList<string> valueGroups)
    {
        var insert = InsertHead(adapter, quotedTable, columns, valueGroups);
        var updateColumns = NonKey(columns, keyColumns);
        if (updateColumns.Count == 0)
        {
            // Nothing to change on conflict — a no-op update on a key column keeps it idempotent.
            var key = adapter.QuoteIdentifier(keyColumns[0]);
            return $"{insert} ON DUPLICATE KEY UPDATE {key} = {key}";
        }
        var assignments = string.Join(", ",
            updateColumns.Select(c => $"{adapter.QuoteIdentifier(c)} = VALUES({adapter.QuoteIdentifier(c)})"));
        return $"{insert} ON DUPLICATE KEY UPDATE {assignments}";
    }

    /// <summary>sqlserver: a <c>MERGE</c> keyed on <paramref name="keyColumns"/>.</summary>
    public static string Merge(
        IDbProviderAdapter adapter, string quotedTable, IReadOnlyList<string> columns,
        IReadOnlyList<string> keyColumns, IReadOnlyList<string> valueGroups)
    {
        var sourceColumns = string.Join(", ", columns.Select(adapter.QuoteIdentifier));
        var on = string.Join(" AND ",
            keyColumns.Select(k => $"target.{adapter.QuoteIdentifier(k)} = source.{adapter.QuoteIdentifier(k)}"));
        var insertColumns = string.Join(", ", columns.Select(adapter.QuoteIdentifier));
        var insertValues = string.Join(", ", columns.Select(c => $"source.{adapter.QuoteIdentifier(c)}"));

        var matched = "";
        var updateColumns = NonKey(columns, keyColumns);
        if (updateColumns.Count > 0)
        {
            var set = string.Join(", ",
                updateColumns.Select(c => $"target.{adapter.QuoteIdentifier(c)} = source.{adapter.QuoteIdentifier(c)}"));
            matched = $"WHEN MATCHED THEN UPDATE SET {set} ";
        }

        return $"MERGE INTO {quotedTable} AS target USING (VALUES {string.Join(", ", valueGroups)}) " +
               $"AS source ({sourceColumns}) ON {on} {matched}" +
               $"WHEN NOT MATCHED THEN INSERT ({insertColumns}) VALUES ({insertValues});";
    }
}
