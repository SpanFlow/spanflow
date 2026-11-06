using Xunit;

namespace SpanFlow.Mapping.Tests;

public class ValueMapTests
{
    [Fact]
    public void Match_ReturnsMappedValue()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "r", "valueMap": { "from": "input.month", "table": { "1": "Jan", "2": "Feb" }, "default": "Unknown" } }
            """);
        Assert.Equal("Feb", TestSupport.Run(mapping, """{ "month": 2 }""")["r"]!.GetValue<string>());
    }

    [Fact]
    public void NoMatch_ReturnsDefault()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "r", "valueMap": { "from": "input.month", "table": { "1": "Jan" }, "default": "N/A" } }
            """);
        Assert.Equal("N/A", TestSupport.Run(mapping, """{ "month": 9 }""")["r"]!.GetValue<string>());
    }

    [Fact]
    public void NoMatch_NoDefault_IsNull()
    {
        var mapping = TestSupport.Rule(
            """{ "target": "r", "valueMap": { "from": "input.month", "table": { "1": "Jan" } } }""");
        Assert.Null(TestSupport.Run(mapping, """{ "month": 9 }""")["r"]);
    }

    [Fact]
    public void NestedInput_LooksUpAggregateResult()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "r", "valueMap": {
              "input": { "aggregate": { "function": "min", "from": "input.orders[]", "select": "id" } },
              "table": { "1": "Jan", "2": "Feb" }, "default": "Unknown" } }
            """);
        Assert.Equal("Jan", TestSupport.Run(mapping, """{ "orders": [ { "id": 2 }, { "id": 1 } ] }""")["r"]!.GetValue<string>());
    }

    [Fact]
    public void InlineInsideForeach_UsesCurrentItem()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "items", "foreach": "input.codes[]", "map": {
              "label": { "valueMap": { "table": { "A": "Active", "I": "Inactive" }, "default": "Other" } } } }
            """);
        var items = TestSupport.Run(mapping, """{ "codes": [ "A", "I", "Z" ] }""")["items"]!.AsArray();
        Assert.Equal("Active", items[0]!["label"]!.GetValue<string>());
        Assert.Equal("Inactive", items[1]!["label"]!.GetValue<string>());
        Assert.Equal("Other", items[2]!["label"]!.GetValue<string>());
    }
}
