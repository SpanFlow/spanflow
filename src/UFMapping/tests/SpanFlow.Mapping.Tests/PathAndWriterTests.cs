using Xunit;

namespace SpanFlow.Mapping.Tests;

public class PathAndWriterTests
{
    private const string Orders = """{ "orders": [ { "id": 1, "amount": 100 }, { "id": 2, "amount": 250 } ] }""";

    [Fact]
    public void ArrayProperty_ImplicitFanOut_Tolerated()
    {
        // input.orders.amount reads `amount` off the array (no explicit []).
        var mapping = TestSupport.Rule(
            """{ "target": "r", "aggregate": { "function": "sum", "from": "input.orders.amount" } }""");
        Assert.Equal(350m, TestSupport.Run(mapping, Orders)["r"]!.GetValue<decimal>());
    }

    [Fact]
    public void ExplicitCollection_WithSelect_Equivalent()
    {
        var mapping = TestSupport.Rule(
            """{ "target": "r", "aggregate": { "function": "sum", "from": "input.orders[]", "select": "amount" } }""");
        Assert.Equal(350m, TestSupport.Run(mapping, Orders)["r"]!.GetValue<decimal>());
    }

    [Fact]
    public void NestedTargetPath_CreatesObjectTree()
    {
        var mapping = TestSupport.Rule(
            """{ "target": "summary.totals.amount", "aggregate": { "function": "sum", "from": "input.orders[]", "select": "amount" } }""");
        var result = TestSupport.Run(mapping, Orders);
        Assert.Equal(350m, result["summary"]!["totals"]!["amount"]!.GetValue<decimal>());
    }

    [Fact]
    public void LiteralString_PassesThroughViaFrom()
    {
        var mapping = TestSupport.Rule("""{ "target": "r", "from": "'hello'" }""");
        Assert.Equal("hello", TestSupport.Run(mapping, "{}")["r"]!.GetValue<string>());
    }

    [Fact]
    public void MissingPath_YieldsNull()
    {
        var mapping = TestSupport.Rule("""{ "target": "r", "from": "input.does.not.exist" }""");
        Assert.Null(TestSupport.Run(mapping, "{}")["r"]);
    }
}
