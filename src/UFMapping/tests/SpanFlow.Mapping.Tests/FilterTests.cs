using System.Linq;
using Xunit;

namespace SpanFlow.Mapping.Tests;

public class FilterTests
{
    private const string Orders = """
        {
          "orders": [
            { "id": 1, "status": "Completed", "amount": 100 },
            { "id": 2, "status": "Pending",   "amount": 50 },
            { "id": 3, "status": "Completed", "amount": 150 }
          ]
        }
        """;

    [Fact]
    public void KeepsMatchingItems()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "r", "filter": {
              "from": "input.orders[]",
              "where": { "field": "status", "op": "eq", "value": "Completed" } } }
            """);
        var ids = TestSupport.Run(mapping, Orders)["r"]!.AsArray().Select(n => n!["id"]!.GetValue<int>()).ToArray();
        Assert.Equal(new[] { 1, 3 }, ids);
    }

    [Fact]
    public void ChainsIntoSort()
    {
        // Completed orders, sorted by amount desc -> ids 3 (150), 1 (100)
        var mapping = TestSupport.Rule(
            """
            { "target": "r", "sort": {
              "input": { "filter": { "from": "input.orders[]", "where": { "field": "status", "op": "eq", "value": "Completed" } } },
              "keys": [ { "by": "amount", "order": "desc" } ] } }
            """);
        var ids = TestSupport.Run(mapping, Orders)["r"]!.AsArray().Select(n => n!["id"]!.GetValue<int>()).ToArray();
        Assert.Equal(new[] { 3, 1 }, ids);
    }
}
