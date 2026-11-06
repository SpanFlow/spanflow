using Xunit;

namespace SpanFlow.Mapping.Tests;

public class TransformTests
{
    private const string User = """{ "user": { "firstName": "Areeb", "lastName": "Khan", "age": 23 } }""";

    private static string Transform(string fn, string args) =>
        TestSupport.Rule($$"""{ "target": "r", "transform": { "function": "{{fn}}", "args": {{args}} } }""");

    [Fact]
    public void Concat_JoinsLiteralsAndPaths()
    {
        var result = TestSupport.Run(
            Transform("concat", """[ "input.user.firstName", "' '", "input.user.lastName" ]"""), User);
        Assert.Equal("Areeb Khan", result["r"]!.GetValue<string>());
    }

    [Fact]
    public void Upper_And_Lower()
    {
        Assert.Equal("AREEB", TestSupport.Run(Transform("upper", """[ "input.user.firstName" ]"""), User)["r"]!.GetValue<string>());
        Assert.Equal("areeb", TestSupport.Run(Transform("lower", """[ "input.user.firstName" ]"""), User)["r"]!.GetValue<string>());
    }

    [Fact]
    public void Add_SumsNumbers()
    {
        var result = TestSupport.Run(Transform("add", "[ 2, 3, 5 ]"), "{}");
        Assert.Equal(10m, result["r"]!.GetValue<decimal>());
    }

    [Fact]
    public void Multiply_MultipliesNumbers()
    {
        var result = TestSupport.Run(Transform("multiply", "[ 2, 3, 4 ]"), "{}");
        Assert.Equal(24m, result["r"]!.GetValue<decimal>());
    }

    [Fact]
    public void Coalesce_ReturnsFirstNonNull()
    {
        var result = TestSupport.Run(Transform("coalesce", """[ "input.missing", "'fallback'" ]"""), "{}");
        Assert.Equal("fallback", result["r"]!.GetValue<string>());
    }

    [Fact]
    public void UnknownTransform_Throws()
    {
        Assert.ThrowsAny<Exception>(() =>
            TestSupport.Run(Transform("nope", "[ 1 ]"), "{}"));
    }

    [Fact]
    public void Trim_RemovesWhitespace()
        => Assert.Equal("hi", TestSupport.Run(Transform("trim", """[ "'  hi  '" ]"""), "{}")["r"]!.GetValue<string>());

    [Fact]
    public void Replace_SubstitutesText()
        => Assert.Equal("a+b", TestSupport.Run(Transform("replace", """[ "'a-b'", "'-'", "'+'" ]"""), "{}")["r"]!.GetValue<string>());

    [Fact]
    public void Substring_ExtractsRange()
        => Assert.Equal("ell", TestSupport.Run(Transform("substring", """[ "'hello'", 1, 3 ]"""), "{}")["r"]!.GetValue<string>());

    [Fact]
    public void Length_CountsCharacters()
        => Assert.Equal(5, TestSupport.Run(Transform("length", """[ "'hello'" ]"""), "{}")["r"]!.GetValue<int>());

    [Theory]
    [InlineData("subtract", "[ 10, 3 ]", 7)]
    [InlineData("divide", "[ 12, 3 ]", 4)]
    [InlineData("divide", "[ 1, 0 ]", 0)]   // divide-by-zero guard
    [InlineData("mod", "[ 7, 3 ]", 1)]
    [InlineData("floor", "[ 3.9 ]", 3)]
    [InlineData("ceil", "[ 3.1 ]", 4)]
    [InlineData("abs", "[ -5 ]", 5)]
    public void NumericTransforms(string fn, string args, decimal expected)
        => Assert.Equal(expected, TestSupport.Run(Transform(fn, args), "{}")["r"]!.GetValue<decimal>());

    [Fact]
    public void WiredTransform_InDesignerWrapper_IsEvaluated()
    {
        // What the designer emits when one Transform node feeds another's argument.
        var result = TestSupport.Run(
            Transform("upper", """[ { "transform": { "function": "concat", "args": [ "input.user.firstName", "'!'" ] } } ]"""), User);
        Assert.Equal("AREEB!", result["r"]!.GetValue<string>());
    }

    [Fact]
    public void WiredCondition_InDesignerWrapper_IsEvaluated()
    {
        var result = TestSupport.Run(
            Transform("upper", """[ { "condition": { "if": { "field": "input.user.age", "op": "gte", "value": 18 }, "then": "'adult'", "else": "'minor'" } } ]"""), User);
        Assert.Equal("ADULT", result["r"]!.GetValue<string>());
    }

    [Fact]
    public void Round_ToDecimals()
        => Assert.Equal(3.15m, TestSupport.Run(Transform("round", "[ 3.146, 2 ]"), "{}")["r"]!.GetValue<decimal>());
}
