using SpanFlow.Mapping.Core;
using SpanFlow.Mapping.Engine;
using Xunit;

namespace SpanFlow.Mapping.Tests;

public class ExceptionTests
{
    [Fact]
    public void Raises_TheMessage_WhenConditionHolds()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "checked", "type": "exception",
              "from": "input.amount", "op": "lt", "value": 0,
              "message": "Amount cannot be negative" }
            """);

        var ex = Assert.Throws<MappingException>(() =>
            new MappingEngine().Execute(TestSupport.Mapping(mapping), TestSupport.Input("""{ "amount": -5 }""")));
        Assert.Equal("Amount cannot be negative", ex.Message);
    }

    [Fact]
    public void PassesValueThrough_WhenConditionIsFalse()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "amount", "type": "exception",
              "from": "input.amount", "op": "lt", "value": 0,
              "message": "Amount cannot be negative" }
            """);

        var result = new MappingEngine().Execute(TestSupport.Mapping(mapping), TestSupport.Input("""{ "amount": 42 }"""));
        Assert.Equal(42, result["amount"]!.GetValue<int>());
    }

    [Fact]
    public void EngineErrors_AreWrappedWithRuleContext()
    {
        // An unknown aggregate would otherwise surface a cryptic message.
        var mapping = TestSupport.Rule(
            """{ "target": "total", "type": "aggregate", "function": "nope", "from": "input.orders[]", "select": "amount" }""");

        var ex = Assert.Throws<MappingException>(() =>
            new MappingEngine().Execute(TestSupport.Mapping(mapping), TestSupport.Input("""{ "orders": [] }""")));
        Assert.Contains("total", ex.Message);
    }
}
