# SpanFlow.Connectors.Database

One adapter interface over PostgreSQL, SQL Server, MySQL/MariaDB and SQLite. It comes from
[SpanFlow](https://github.com/SpanFlow/spanflow), where it runs every database step of a pipeline.

- `IDbProviderAdapter`, one per database: opens a connection, quotes identifiers, lists tables, columns and
  foreign keys, and builds upserts and paging clauses in that database's own SQL.
- `DbProviderRegistry` picks an adapter by name (`postgres`, `sqlserver`, `mysql`, `sqlite`). It can also wrap
  every adapter it hands out, for example to refuse servers your host doesn't allow.
- `QueryAdvisor.Inspect(sql)` reviews a query before it runs: it flags `SELECT *` and a `SELECT` with no row
  limit, and suggests the fix in the right dialect (`LIMIT`, `TOP` or `FETCH FIRST`).
- `SqlFingerprint` gives a query a canonical form, so two saved queries written differently are recognised as
  the same one.

```csharp
using SpanFlow.Connectors.Database;

var registry = new DbProviderRegistry([new PostgresAdapter(), new SqlServerAdapter(), new MySqlAdapter(), new SqliteAdapter()]);
var sqlite = registry.Get("sqlite");

await using var connection = sqlite.CreateConnection("Data Source=app.db");
await connection.OpenAsync();
foreach (var table in await sqlite.ListTablesAsync(connection, CancellationToken.None))
{
    var columns = await sqlite.ListColumnsAsync(connection, table.QualifiedName, CancellationToken.None);
    Console.WriteLine($"{table.QualifiedName}: {string.Join(", ", columns.Select(c => c.Name))}");
}

foreach (var advice in QueryAdvisor.Inspect("SELECT * FROM orders"))
{
    Console.WriteLine($"{advice.Severity}: {advice.Message} {advice.Suggestion}");
}
```

Targets .NET 8, 9 and 10. MIT licensed.
