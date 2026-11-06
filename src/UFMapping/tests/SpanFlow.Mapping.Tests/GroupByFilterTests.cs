using Xunit;

namespace SpanFlow.Mapping.Tests;

/// <summary>
/// groupBy's two optional filters. `where` drops rows BEFORE grouping (SQL WHERE);
/// `having` drops whole groups AFTER the map is computed (SQL HAVING). Both are
/// optional and independent, and both take nested all/any (AND/OR) clauses.
/// </summary>
public class GroupByFilterTests
{
    private const string Orders = """
        { "orders": [
          { "customerId": "C1", "status": "Completed", "amount": 100 },
          { "customerId": "C1", "status": "Cancelled", "amount": 999 },
          { "customerId": "C1", "status": "Completed", "amount": 20 },
          { "customerId": "C2", "status": "Completed", "amount": 50 },
          { "customerId": "C3", "status": "Cancelled", "amount": 70 }
        ] }
        """;

    [Fact]
    public void Where_FiltersRowsBeforeGrouping()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "customers", "groupBy": {
              "from": "input.orders[]", "key": "customerId",
              "where": { "field": "status", "op": "eq", "value": "Completed" },
              "map": {
                "customerId": "key",
                "total": { "aggregate": { "function": "sum", "select": "amount" } },
                "orders": { "aggregate": { "function": "count" } }
              } } }
            """);

        var customers = TestSupport.Run(mapping, Orders)["customers"]!.AsArray();

        // C3 had only a cancelled order, so it produces no group at all.
        Assert.Equal(["C1", "C2"], customers.Select(c => c!["customerId"]!.GetValue<string>()));

        // C1's cancelled 999 is excluded from both the sum and the count.
        var c1 = customers[0]!;
        Assert.Equal(120m, c1["total"]!.GetValue<decimal>());
        Assert.Equal(2, c1["orders"]!.GetValue<int>());
    }

    [Fact]
    public void Having_DropsWholeGroups_ByTheirComputedValues()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "customers", "groupBy": {
              "from": "input.orders[]", "key": "customerId",
              "having": { "field": "orderCount", "op": "gte", "value": 2 },
              "map": {
                "customerId": "key",
                "orderCount": { "aggregate": { "function": "count" } }
              } } }
            """);

        // Only C1 has 2+ rows; C2 and C3 have one each and are dropped.
        var only = Assert.Single(TestSupport.Run(mapping, Orders)["customers"]!.AsArray());
        Assert.Equal("C1", only!["customerId"]!.GetValue<string>());
        Assert.Equal(3, only["orderCount"]!.GetValue<int>());
    }

    [Fact]
    public void WhereAndHaving_ComposeInThatOrder()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "customers", "groupBy": {
              "from": "input.orders[]", "key": "customerId",
              "where": { "field": "status", "op": "eq", "value": "Completed" },
              "having": { "field": "total", "op": "gt", "value": 100 },
              "map": {
                "customerId": "key",
                "total": { "aggregate": { "function": "sum", "select": "amount" } }
              } } }
            """);

        // C1 keeps 100 + 20 = 120 (the 999 cancelled row was filtered out first,
        // so `having` sees 120, not 1119) and clears the > 100 bar.
        // C2's completed total is 50, so it fails having. C3 is gone at `where`.
        var only = Assert.Single(TestSupport.Run(mapping, Orders)["customers"]!.AsArray());
        Assert.Equal("C1", only!["customerId"]!.GetValue<string>());
        Assert.Equal(120m, only["total"]!.GetValue<decimal>());
    }

    [Fact]
    public void Where_CombinesClausesWithAnd()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "customers", "groupBy": {
              "from": "input.orders[]", "key": "customerId",
              "where": { "all": [
                { "field": "status", "op": "eq", "value": "Completed" },
                { "field": "amount", "op": "gte", "value": 100 } ] },
              "map": {
                "customerId": "key",
                "total": { "aggregate": { "function": "sum", "select": "amount" } }
              } } }
            """);

        // Completed AND >= 100: only C1's 100 row survives.
        var only = Assert.Single(TestSupport.Run(mapping, Orders)["customers"]!.AsArray());
        Assert.Equal("C1", only!["customerId"]!.GetValue<string>());
        Assert.Equal(100m, only["total"]!.GetValue<decimal>());
    }

    [Fact]
    public void Where_CombinesClausesWithOr()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "customers", "groupBy": {
              "from": "input.orders[]", "key": "customerId",
              "where": { "any": [
                { "field": "status", "op": "eq", "value": "Cancelled" },
                { "field": "amount", "op": "gte", "value": 100 } ] },
              "map": {
                "customerId": "key",
                "orders": { "aggregate": { "function": "count" } }
              } } }
            """);

        var customers = TestSupport.Run(mapping, Orders)["customers"]!.AsArray();

        // Cancelled OR >= 100 keeps: C1's 100 + C1's cancelled 999, C3's cancelled.
        // C2's completed 50 matches neither, so C2 disappears.
        Assert.Equal(["C1", "C3"], customers.Select(c => c!["customerId"]!.GetValue<string>()));
        Assert.Equal(2, customers[0]!["orders"]!.GetValue<int>());
    }

    [Fact]
    public void NeitherFilter_BehavesExactlyAsBefore()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "customers", "groupBy": {
              "from": "input.orders[]", "key": "customerId",
              "map": { "customerId": "key", "orders": { "aggregate": { "function": "count" } } } } }
            """);

        var customers = TestSupport.Run(mapping, Orders)["customers"]!.AsArray();
        Assert.Equal(["C1", "C2", "C3"], customers.Select(c => c!["customerId"]!.GetValue<string>()));
        Assert.Equal(3, customers[0]!["orders"]!.GetValue<int>());
    }
}
