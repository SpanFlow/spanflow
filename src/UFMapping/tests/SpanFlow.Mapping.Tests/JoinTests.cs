using System.Linq;
using Xunit;

namespace SpanFlow.Mapping.Tests;

public class JoinTests
{
    private const string Input = """
        {
          "orders": [
            { "id": 1, "customerId": "C1", "amount": 100 },
            { "id": 2, "customerId": "C2", "amount": 50 },
            { "id": 3, "customerId": "C9", "amount": 20 }
          ],
          "customers": [
            { "custId": "C1", "name": "Acme" },
            { "custId": "C2", "name": "Globex" }
          ]
        }
        """;

    [Fact]
    public void InnerJoin_MergesMatchedRows()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "r", "join": {
              "left": "input.orders[]", "right": "input.customers[]",
              "leftKey": "customerId", "rightKey": "custId" } }
            """);

        var rows = TestSupport.Run(mapping, Input)["r"]!.AsArray();
        // C9 has no matching customer, so only 2 rows.
        Assert.Equal(2, rows.Count);
        Assert.Equal("Acme", rows[0]!["name"]!.GetValue<string>());
        Assert.Equal(100, rows[0]!["amount"]!.GetValue<int>());
        Assert.Equal("Globex", rows[1]!["name"]!.GetValue<string>());
    }
}
