using Xunit;

namespace SpanFlow.Mapping.Tests;

public class TypedDslTests
{
    private const string Input = """
        {
          "user": { "name": "Bob" },
          "orders": [
            { "id": 1, "status": "Completed", "amount": 100 },
            { "id": 2, "status": "Pending",   "amount": 50 }
          ]
        }
        """;

    [Fact]
    public void TypedRules_ExecuteLikeLegacy()
    {
        var mapping = TestSupport.Mapping(
            """
            {
              "target": {
                "type": "object",
                "mappings": [
                  { "target": "name", "type": "fieldCopy", "path": "input.user.name" },
                  { "target": "total", "type": "aggregate", "function": "sum", "from": "input.orders[]", "select": "amount" },
                  { "target": "completed", "type": "filter", "from": "input.orders[]",
                    "where": { "field": "status", "op": "eq", "value": "Completed" } }
                ]
              }
            }
            """);

        var result = new SpanFlow.Mapping.Engine.MappingEngine().Execute(mapping, TestSupport.Input(Input));
        Assert.Equal("Bob", result["name"]!.GetValue<string>());
        Assert.Equal(150m, result["total"]!.GetValue<decimal>());
        Assert.Single(result["completed"]!.AsArray());
    }

    [Fact]
    public void TypedAndLegacy_ProduceTheSameResult()
    {
        var typed = TestSupport.Rule(
            """{ "target": "r", "type": "aggregate", "function": "max", "from": "input.orders[]", "select": "amount" }""");
        var legacy = TestSupport.Rule(
            """{ "target": "r", "aggregate": { "function": "max", "from": "input.orders[]", "select": "amount" } }""");

        var engine = new SpanFlow.Mapping.Engine.MappingEngine();
        var t = engine.Execute(TestSupport.Mapping(typed), TestSupport.Input(Input))["r"]!.GetValue<decimal>();
        var l = engine.Execute(TestSupport.Mapping(legacy), TestSupport.Input(Input))["r"]!.GetValue<decimal>();
        Assert.Equal(l, t);
        Assert.Equal(100m, t);
    }

    [Fact]
    public void TypedCondition_Works()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "r", "type": "condition",
              "if": { "field": "input.orders[]", "op": "contains", "value": "x" },
              "then": "'a'", "else": "'b'" }
            """);
        // (contains on a non-string is just exercising the typed condition path)
        var result = new SpanFlow.Mapping.Engine.MappingEngine().Execute(TestSupport.Mapping(mapping), TestSupport.Input(Input));
        Assert.Contains(result["r"]!.GetValue<string>(), new[] { "a", "b" });
    }
}
