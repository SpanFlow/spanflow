using SpanFlow.Mapping.Core.Validation;
using Xunit;

namespace SpanFlow.Mapping.Tests;

public class ValidationTests
{
    [Fact]
    public void NonObjectTarget_Rejected()
    {
        var mapping = TestSupport.Mapping("""{ "target": { "type": "array", "mappings": [] } }""");
        Assert.Throws<InvalidOperationException>(() => MappingDefinitionValidator.Validate(mapping));
    }

    [Fact]
    public void Rule_WithNoMode_Rejected()
    {
        var mapping = TestSupport.Mapping("""{ "target": { "type": "object", "mappings": [ { "target": "r" } ] } }""");
        Assert.Throws<InvalidOperationException>(() => MappingDefinitionValidator.Validate(mapping));
    }

    [Fact]
    public void Rule_WithTwoModes_Rejected()
    {
        var mapping = TestSupport.Mapping(
            """{ "target": { "type": "object", "mappings": [ { "target": "r", "from": "input.x", "aggregate": { "function": "sum" } } ] } }""");
        Assert.Throws<InvalidOperationException>(() => MappingDefinitionValidator.Validate(mapping));
    }

    [Fact]
    public void ValueMap_WithoutTableOrDefault_Rejected()
    {
        var mapping = TestSupport.Mapping(
            """{ "target": { "type": "object", "mappings": [ { "target": "r", "valueMap": { "from": "input.x", "table": {} } } ] } }""");
        Assert.Throws<InvalidOperationException>(() => MappingDefinitionValidator.Validate(mapping));
    }

    [Fact]
    public void ValueMap_DefaultOnly_Allowed()
    {
        var mapping = TestSupport.Mapping(
            """{ "target": { "type": "object", "mappings": [ { "target": "r", "valueMap": { "table": {}, "default": "x" } } ] } }""");
        MappingDefinitionValidator.Validate(mapping); // should not throw
    }

    [Fact]
    public void Foreach_WithoutMap_Rejected()
    {
        var mapping = TestSupport.Mapping(
            """{ "target": { "type": "object", "mappings": [ { "target": "r", "foreach": "input.x[]" } ] } }""");
        Assert.Throws<InvalidOperationException>(() => MappingDefinitionValidator.Validate(mapping));
    }
}
