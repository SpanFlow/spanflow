using System.Linq;
using Xunit;

namespace SpanFlow.Mapping.Tests;

public class SortTests
{
    private const string Orders = """
        {
          "orders": [
            { "id": 1, "status": "B", "amount": 100 },
            { "id": 2, "status": "A", "amount": 250 },
            { "id": 3, "status": "A", "amount": 50 }
          ]
        }
        """;

    private static int[] Ids(System.Text.Json.Nodes.JsonNode result) =>
        result["r"]!.AsArray().Select(n => n!["id"]!.GetValue<int>()).ToArray();

    [Fact]
    public void SortsAscendingByDefault()
    {
        var mapping = TestSupport.Rule(
            """{ "target": "r", "sort": { "from": "input.orders[]", "keys": [ { "by": "amount" } ] } }""");
        Assert.Equal(new[] { 3, 1, 2 }, Ids(TestSupport.Run(mapping, Orders))); // amounts 50,100,250
    }

    [Fact]
    public void SortsDescending()
    {
        var mapping = TestSupport.Rule(
            """{ "target": "r", "sort": { "from": "input.orders[]", "keys": [ { "by": "amount", "order": "desc" } ] } }""");
        Assert.Equal(new[] { 2, 1, 3 }, Ids(TestSupport.Run(mapping, Orders))); // amounts 250,100,50
    }

    [Fact]
    public void SortsByMultipleKeys()
    {
        // status asc, then amount desc
        var mapping = TestSupport.Rule(
            """
            { "target": "r", "sort": { "from": "input.orders[]", "keys": [
              { "by": "status", "order": "asc" },
              { "by": "amount", "order": "desc" } ] } }
            """);
        // A:250(id2), A:50(id3), B:100(id1)
        Assert.Equal(new[] { 2, 3, 1 }, Ids(TestSupport.Run(mapping, Orders)));
    }

    [Fact]
    public void SortsStringsLexicographically()
    {
        var mapping = TestSupport.Rule(
            """{ "target": "r", "sort": { "from": "input.names[]", "keys": [ { "by": "" } ] } }""");
        var sorted = TestSupport.Run(mapping, """{ "names": [ "Charlie", "alice", "Bob" ] }""")
            ["r"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        Assert.Equal(new[] { "Bob", "Charlie", "alice" }, sorted); // ordinal: uppercase before lowercase
    }

    [Fact]
    public void NonCollectionPath_IsFlattened()
    {
        // `input.orders` (no []) still sorts the array's items.
        var mapping = TestSupport.Rule(
            """{ "target": "r", "sort": { "from": "input.orders", "keys": [ { "by": "amount" } ] } }""");
        Assert.Equal(new[] { 3, 1, 2 }, Ids(TestSupport.Run(mapping, Orders)));
    }
}
