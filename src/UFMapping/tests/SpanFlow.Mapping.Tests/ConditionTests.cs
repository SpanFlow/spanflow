using Xunit;

namespace SpanFlow.Mapping.Tests;

public class ConditionTests
{
    private static string Cond(string op, string value) =>
        TestSupport.Rule(
            $$"""
            { "target": "r", "condition": {
              "if": { "field": "input.n", "op": "{{op}}", "value": {{value}} },
              "then": "'yes'", "else": "'no'" } }
            """);

    [Theory]
    [InlineData("eq", "5", 5, "yes")]
    [InlineData("eq", "5", 6, "no")]
    [InlineData("neq", "5", 6, "yes")]
    [InlineData("gt", "5", 6, "yes")]
    [InlineData("gt", "5", 5, "no")]
    [InlineData("gte", "5", 5, "yes")]
    [InlineData("lt", "5", 4, "yes")]
    [InlineData("lte", "5", 5, "yes")]
    public void Operators(string op, string value, int n, string expected)
    {
        var result = TestSupport.Run(Cond(op, value), $$"""{ "n": {{n}} }""");
        Assert.Equal(expected, result["r"]!.GetValue<string>());
    }

    [Fact]
    public void Contains_MatchesSubstringCaseInsensitive()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "r", "condition": {
              "if": { "field": "input.text", "op": "contains", "value": "world" },
              "then": "'hit'", "else": "'miss'" } }
            """);
        Assert.Equal("hit", TestSupport.Run(mapping, """{ "text": "Hello WORLD" }""")["r"]!.GetValue<string>());
    }

    [Fact]
    public void NestedInput_ComparesAggregateResult()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "r", "condition": {
              "if": { "input": { "aggregate": { "function": "min", "from": "input.orders[]", "select": "id" } }, "op": "eq", "value": 1 },
              "then": "'Win'", "else": "'Lose'" } }
            """);
        Assert.Equal("Win", TestSupport.Run(mapping, """{ "orders": [ { "id": 1 }, { "id": 5 } ] }""")["r"]!.GetValue<string>());
        Assert.Equal("Lose", TestSupport.Run(mapping, """{ "orders": [ { "id": 3 }, { "id": 5 } ] }""")["r"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(1, 0, "Win")]   // both clauses true
    [InlineData(1, 9, "Lose")]  // second false
    [InlineData(2, 0, "Lose")]  // first false
    public void All_RequiresEveryClause(int a, int b, string expected)
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "r", "condition": {
              "if": { "all": [
                { "field": "input.a", "op": "eq", "value": 1 },
                { "field": "input.b", "op": "eq", "value": 0 }
              ] },
              "then": "'Win'", "else": "'Lose'" } }
            """);
        Assert.Equal(expected, TestSupport.Run(mapping, $$"""{ "a": {{a}}, "b": {{b}} }""")["r"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(1, 9, "yes")]   // first true
    [InlineData(9, 0, "yes")]   // second true
    [InlineData(9, 9, "no")]    // neither
    public void Any_RequiresOneClause(int a, int b, string expected)
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "r", "condition": {
              "if": { "any": [
                { "field": "input.a", "op": "eq", "value": 1 },
                { "field": "input.b", "op": "eq", "value": 0 }
              ] },
              "then": "'yes'", "else": "'no'" } }
            """);
        Assert.Equal(expected, TestSupport.Run(mapping, $$"""{ "a": {{a}}, "b": {{b}} }""")["r"]!.GetValue<string>());
    }

    [Fact]
    public void NestedThenBranch_EvaluatedWhenTrue()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "r", "condition": {
              "if": { "input": { "aggregate": { "function": "count", "from": "input.orders[]" } }, "op": "gte", "value": 2 },
              "then": { "aggregate": { "function": "sum", "from": "input.orders[]", "select": "amount" } },
              "else": "'none'" } }
            """);
        Assert.Equal(350m, TestSupport.Run(mapping, """{ "orders": [ { "amount": 100 }, { "amount": 250 } ] }""")["r"]!.GetValue<decimal>());
    }
}
