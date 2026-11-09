using System.Text.Json.Nodes;

namespace SpanFlow.Libraries.Tests;

/// <summary>
/// SQL-text tests for the provider dialects that can't be executed here (no live SQL Server /
/// MySQL). Verifies quoting and the upsert statement each adapter generates.
/// </summary>
public class SqlDialectTests
{
    private static DbTableWriter.TableRequest Request(string table, string[] columns, string[] keys, string operation = "upsert")
    {
        var cols = new JsonArray();
        foreach (var c in columns) cols.Add(new JsonObject { ["column"] = c });
        return DbTableWriter.Parse(new JsonObject
        {
            ["table"] = table,
            ["operation"] = operation,
            ["keyColumns"] = new JsonArray(keys.Select(k => (JsonNode)k!).ToArray()),
            ["columns"] = cols,
        });
    }

    private static string Upsert(IDbProviderAdapter adapter, string table, string[] columns, string[] keys)
    {
        var writer = new DbTableWriter(adapter);
        var spec = Request(table, columns, keys);
        var row = new JsonObject();
        foreach (var c in columns) row[c] = 1;
        var (sql, _) = writer.BuildInsert(spec, [row], upsert: true);
        return sql;
    }

    // ---- identifier quoting ----

    [Theory]
    [InlineData("orders", "`orders`")]
    [InlineData("shop.orders", "`shop`.`orders`")]
    [InlineData("we`ird", "`we``ird`")]
    public void MySql_quotes_with_backticks(string identifier, string expected) =>
        Assert.Equal(expected, new MySqlAdapter().QuoteIdentifier(identifier));

    [Theory]
    [InlineData("orders", "[orders]")]
    [InlineData("dbo.orders", "[dbo].[orders]")]
    [InlineData("we]ird", "[we]]ird]")]
    public void SqlServer_quotes_with_brackets(string identifier, string expected) =>
        Assert.Equal(expected, new SqlServerAdapter().QuoteIdentifier(identifier));

    // ---- upsert dialects ----

    [Fact]
    public void MySql_upsert_uses_on_duplicate_key_update()
    {
        var sql = Upsert(new MySqlAdapter(), "orders", ["id", "total", "note"], ["id"]);
        Assert.Equal(
            "INSERT INTO `orders` (`id`, `total`, `note`) VALUES (@p0, @p1, @p2) " +
            "ON DUPLICATE KEY UPDATE `total` = VALUES(`total`), `note` = VALUES(`note`)", sql);
    }

    [Fact]
    public void SqlServer_upsert_uses_merge()
    {
        var sql = Upsert(new SqlServerAdapter(), "dbo.orders", ["id", "total"], ["id"]);
        Assert.Equal(
            "MERGE INTO [dbo].[orders] AS target USING (VALUES (@p0, @p1)) AS source ([id], [total]) " +
            "ON target.[id] = source.[id] " +
            "WHEN MATCHED THEN UPDATE SET target.[total] = source.[total] " +
            "WHEN NOT MATCHED THEN INSERT ([id], [total]) VALUES (source.[id], source.[total]);", sql);
    }

    [Fact]
    public void Postgres_upsert_still_uses_on_conflict_after_the_refactor()
    {
        var sql = Upsert(new PostgresAdapter(), "public.orders", ["id", "total"], ["id"]);
        Assert.Equal(
            "INSERT INTO \"public\".\"orders\" (\"id\", \"total\") VALUES (@p0, @p1) " +
            "ON CONFLICT (\"id\") DO UPDATE SET \"total\" = excluded.\"total\"", sql);
    }

    [Fact]
    public void Upsert_with_only_key_columns_degrades_to_a_no_op_per_dialect()
    {
        Assert.EndsWith("ON CONFLICT (\"id\") DO NOTHING", Upsert(new PostgresAdapter(), "t", ["id"], ["id"]));
        Assert.EndsWith("ON DUPLICATE KEY UPDATE `id` = `id`", Upsert(new MySqlAdapter(), "t", ["id"], ["id"]));
        // SQL Server MERGE with no non-key columns has no WHEN MATCHED branch.
        Assert.DoesNotContain("WHEN MATCHED", Upsert(new SqlServerAdapter(), "t", ["id"], ["id"]));
    }

    // ---- registry wiring ----

    [Fact]
    public void Registry_resolves_all_four_providers()
    {
        var registry = new DbProviderRegistry(
            [new PostgresAdapter(), new SqliteAdapter(), new SqlServerAdapter(), new MySqlAdapter()]);
        Assert.Equal("postgres", registry.Get("postgres").Provider);
        Assert.Equal("sqlite", registry.Get("sqlite").Provider);
        Assert.Equal("sqlserver", registry.Get("sqlserver").Provider);
        Assert.Equal("mysql", registry.Get("MySQL").Provider); // case-insensitive
        Assert.All(new[] { "postgres", "sqlite", "sqlserver", "mysql" }, p => Assert.Equal("@", registry.Get(p).ParameterPrefix));
    }
}
