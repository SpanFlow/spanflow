using Xunit;

namespace SpanFlow.Mapping.Tests;

public class AggregateTests
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

    private static string Agg(string fn, string? select = "amount") =>
        TestSupport.Rule(
            select is null
                ? $$"""{ "target": "r", "aggregate": { "function": "{{fn}}", "from": "input.orders[]" } }"""
                : $$"""{ "target": "r", "aggregate": { "function": "{{fn}}", "from": "input.orders[]", "select": "{{select}}" } }""");

    [Theory]
    [InlineData("sum", 300)]
    [InlineData("avg", 100)]
    [InlineData("min", 50)]
    [InlineData("max", 150)]
    [InlineData("product", 750000)] // 100 * 50 * 150
    [InlineData("range", 100)]      // 150 - 50
    public void NumericAggregates(string fn, decimal expected)
    {
        var result = TestSupport.Run(Agg(fn), Orders);
        Assert.Equal(expected, result["r"]!.GetValue<decimal>());
    }

    [Fact]
    public void Count_IgnoresSelect_CountsItems()
    {
        var result = TestSupport.Run(Agg("count", select: null), Orders);
        Assert.Equal(3, result["r"]!.GetValue<int>());
    }

    [Fact]
    public void First_And_Last()
    {
        Assert.Equal(100m, TestSupport.Run(Agg("first"), Orders)["r"]!.GetValue<decimal>());
        Assert.Equal(150m, TestSupport.Run(Agg("last"), Orders)["r"]!.GetValue<decimal>());
    }

    [Fact]
    public void Distinct_ReturnsUniqueValuesInOrder()
    {
        var result = TestSupport.Run(Agg("distinct", "status"), Orders);
        var values = result["r"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        Assert.Equal(new[] { "Completed", "Pending" }, values);
    }

    [Fact]
    public void CountDistinct_CountsUniqueValues()
    {
        // statuses: Completed, Pending, Completed -> 2 unique
        var result = TestSupport.Run(Agg("countDistinct", "status"), Orders);
        Assert.Equal(2, result["r"]!.GetValue<int>());
    }

    [Fact]
    public void Where_FiltersBeforeAggregating()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "r", "aggregate": {
              "function": "sum", "from": "input.orders[]",
              "where": { "field": "status", "op": "eq", "value": "Completed" },
              "select": "amount" } }
            """);
        Assert.Equal(250m, TestSupport.Run(mapping, Orders)["r"]!.GetValue<decimal>());
    }

    [Fact]
    public void Median_OddCount_ReturnsMiddle()
    {
        var input = """{ "nums": [ 6, 1, 2 ] }"""; // sorted 1,2,6 -> 2
        var mapping = TestSupport.Rule(
            """{ "target": "r", "aggregate": { "function": "median", "from": "input.nums[]" } }""");
        Assert.Equal(2m, TestSupport.Run(mapping, input)["r"]!.GetValue<decimal>());
    }

    [Fact]
    public void Median_EvenCount_AveragesTwoMiddle()
    {
        var input = """{ "nums": [ 1, 2, 3, 6 ] }"""; // (2+3)/2 = 2.5
        var mapping = TestSupport.Rule(
            """{ "target": "r", "aggregate": { "function": "median", "from": "input.nums[]" } }""");
        Assert.Equal(2.5m, TestSupport.Run(mapping, input)["r"]!.GetValue<decimal>());
    }

    [Fact]
    public void Mode_ReturnsMostFrequentValue()
    {
        var input = """{ "nums": [ 1, 2, 2, 3 ] }""";
        var mapping = TestSupport.Rule(
            """{ "target": "r", "aggregate": { "function": "mode", "from": "input.nums[]" } }""");
        Assert.Equal(2, TestSupport.Run(mapping, input)["r"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("[ false, false, true ]", true)]
    [InlineData("[ false, false ]", false)]
    public void Any_TrueWhenAnyValueIsTruthy(string flags, bool expected)
    {
        var mapping = TestSupport.Rule(
            """{ "target": "r", "aggregate": { "function": "any", "from": "input.flags[]" } }""");
        Assert.Equal(expected, TestSupport.Run(mapping, $$"""{ "flags": {{flags}} }""")["r"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("[ true, true ]", true)]
    [InlineData("[ true, false ]", false)]
    public void All_TrueWhenEveryValueIsTruthy(string flags, bool expected)
    {
        var mapping = TestSupport.Rule(
            """{ "target": "r", "aggregate": { "function": "all", "from": "input.flags[]" } }""");
        Assert.Equal(expected, TestSupport.Run(mapping, $$"""{ "flags": {{flags}} }""")["r"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("[ false, false ]", true)]
    [InlineData("[ false, true ]", false)]
    public void None_TrueWhenNoValueIsTruthy(string flags, bool expected)
    {
        var mapping = TestSupport.Rule(
            """{ "target": "r", "aggregate": { "function": "none", "from": "input.flags[]" } }""");
        Assert.Equal(expected, TestSupport.Run(mapping, $$"""{ "flags": {{flags}} }""")["r"]!.GetValue<bool>());
    }

    [Fact]
    public void Variance_PopulationVariance()
    {
        // [2,4,4,4,5,5,7,9] -> mean 5, variance 4
        var input = """{ "nums": [ 2, 4, 4, 4, 5, 5, 7, 9 ] }""";
        var mapping = TestSupport.Rule(
            """{ "target": "r", "aggregate": { "function": "variance", "from": "input.nums[]" } }""");
        Assert.Equal(4m, TestSupport.Run(mapping, input)["r"]!.GetValue<decimal>());
    }

    [Fact]
    public void StdDev_PopulationStandardDeviation()
    {
        // [2,4,4,4,5,5,7,9] -> variance 4, stddev 2
        var input = """{ "nums": [ 2, 4, 4, 4, 5, 5, 7, 9 ] }""";
        var mapping = TestSupport.Rule(
            """{ "target": "r", "aggregate": { "function": "stddev", "from": "input.nums[]" } }""");
        Assert.Equal(2d, TestSupport.Run(mapping, input)["r"]!.GetValue<double>());
    }

    [Fact]
    public void JoinString_UsesSeparator()
    {
        var input = """{ "tags": [ "a", "b", "c" ] }""";
        var mapping = TestSupport.Rule(
            """{ "target": "r", "aggregate": { "function": "joinString", "from": "input.tags[]", "separator": "-" } }""");
        Assert.Equal("a-b-c", TestSupport.Run(mapping, input)["r"]!.GetValue<string>());
    }

    [Fact]
    public void JoinString_DefaultsToCommaSpace()
    {
        var input = """{ "tags": [ "a", "b" ] }""";
        var mapping = TestSupport.Rule(
            """{ "target": "r", "aggregate": { "function": "joinString", "from": "input.tags[]" } }""");
        Assert.Equal("a, b", TestSupport.Run(mapping, input)["r"]!.GetValue<string>());
    }

    [Fact]
    public void Sum_OfEmptyCollection_IsZero()
    {
        var result = TestSupport.Run(Agg("sum"), """{ "orders": [] }""");
        Assert.Equal(0m, result["r"]!.GetValue<decimal>());
    }
}
