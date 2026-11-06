using Xunit;

namespace SpanFlow.Mapping.Tests;

public class GroupByForeachTests
{
    [Fact]
    public void GroupBy_AggregatesPerGroup()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "customers", "groupBy": {
              "from": "input.orders[]", "key": "customerId",
              "map": {
                "customerId": "key",
                "totalAmount": { "aggregate": { "function": "sum", "select": "amount" } },
                "orderCount": { "aggregate": { "function": "count" } },
                "lastStatus": { "aggregate": { "function": "last", "select": "status" } }
              } } }
            """);

        var input = """
            { "orders": [
              { "customerId": "C1", "status": "Completed", "amount": 100 },
              { "customerId": "C2", "status": "Pending",   "amount": 50 },
              { "customerId": "C1", "status": "Refunded",  "amount": 20 }
            ] }
            """;

        var customers = TestSupport.Run(mapping, input)["customers"]!.AsArray();
        Assert.Equal(2, customers.Count);
        var c1 = customers[0]!;
        Assert.Equal("C1", c1["customerId"]!.GetValue<string>());
        Assert.Equal(120m, c1["totalAmount"]!.GetValue<decimal>());
        Assert.Equal(2, c1["orderCount"]!.GetValue<int>());
        Assert.Equal("Refunded", c1["lastStatus"]!.GetValue<string>());
    }

    [Fact]
    public void GroupBy_ItemsAggregate_CollectsGroupMembers()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "customers", "groupBy": {
              "from": "input.orders[]", "key": "customerId",
              "map": {
                "customerId": "key",
                "orders": { "aggregate": { "function": "items" } },
                "amounts": { "aggregate": { "function": "items", "select": "amount" } }
              } } }
            """);

        var customers = TestSupport.Run(mapping, Orders)["customers"]!.AsArray();
        var c1 = customers[0]!;

        var orders = c1["orders"]!.AsArray();
        Assert.Equal(2, orders.Count);
        Assert.Equal("Completed", orders[0]!["status"]!.GetValue<string>());
        Assert.Equal("Refunded", orders[1]!["status"]!.GetValue<string>());

        Assert.Equal([100m, 20m], c1["amounts"]!.AsArray().Select(v => v!.GetValue<decimal>()));
    }

    [Fact]
    public void GroupBy_GroupSource_FeedsNestedNodes()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "customers", "groupBy": {
              "from": "input.orders[]", "key": "customerId",
              "map": {
                "customerId": "key",
                "lines": { "foreach": "group[]", "map": {
                  "status": "status",
                  "label": { "function": "concat", "args": [ "'Order-'", "amount" ] } } },
                "kept": { "filter": {
                  "from": "group[]",
                  "where": { "field": "amount", "op": "gte", "value": 20 } } },
                "bigOrders": { "filter": {
                  "from": "group[]",
                  "where": { "field": "amount", "op": "gte", "value": 100 } } }
              } } }
            """);

        var customers = TestSupport.Run(mapping, Orders)["customers"]!.AsArray();
        var c1 = customers[0]!;

        var lines = c1["lines"]!.AsArray();
        Assert.Equal(2, lines.Count);
        Assert.Equal("Completed", lines[0]!["status"]!.GetValue<string>());
        Assert.Equal("Order-100", lines[0]!["label"]!.GetValue<string>());

        // Both rows of the group survive: the filter sees the whole group, not just its first row.
        Assert.Equal([100m, 20m], c1["kept"]!.AsArray().Select(row => row!["amount"]!.GetValue<decimal>()));

        var big = Assert.Single(c1["bigOrders"]!.AsArray());
        Assert.Equal(100m, big!["amount"]!.GetValue<decimal>());
    }

    [Fact]
    public void GroupSource_OutsideAGroup_ResolvesEmpty()
    {
        var mapping = TestSupport.Rule("""{ "target": "rows", "aggregate": { "function": "items", "from": "group[]" } }""");

        Assert.Empty(TestSupport.Run(mapping, Orders)["rows"]!.AsArray());
    }

    private const string Orders = """
        { "orders": [
          { "customerId": "C1", "status": "Completed", "amount": 100 },
          { "customerId": "C2", "status": "Pending",   "amount": 50 },
          { "customerId": "C1", "status": "Refunded",  "amount": 20 }
        ] }
        """;

    [Fact]
    public void Foreach_ProjectsEachItem()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "orders", "foreach": "input.orders[]", "map": {
              "id": "id",
              "label": { "function": "concat", "args": [ "'Order-'", "id" ] } } }
            """);

        var orders = TestSupport.Run(mapping, """{ "orders": [ { "id": 10 }, { "id": 11 } ] }""")["orders"]!.AsArray();
        Assert.Equal(2, orders.Count);
        Assert.Equal(10, orders[0]!["id"]!.GetValue<int>());
        Assert.Equal("Order-10", orders[0]!["label"]!.GetValue<string>());
    }
}
