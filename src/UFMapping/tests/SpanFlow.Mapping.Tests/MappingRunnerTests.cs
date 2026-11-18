using System.Text;
using System.Text.Json.Nodes;
using SpanFlow.Mapping.Core.Models;
using SpanFlow.Mapping.Formats;
using Xunit;

namespace SpanFlow.Mapping.Tests;

/// <summary>E73-T1: the runner (decode, unchanged engine, encode) and the settings checks around it.</summary>
public class MappingRunnerTests
{
    private const string Rules = """
        "target": { "type": "object", "mappings": [
          { "target": "orderId", "from": "input.id" },
          { "target": "who", "from": "input.customer.name" } ] }
        """;

    private static MappingDefinition Definition(string? formats = null) =>
        TestSupport.Mapping(formats is null ? $"{{ {Rules} }}" : $"{{ \"formats\": {formats}, {Rules} }}");

    [Fact]
    public void NoFormats_IsJsonInJsonOut_AndMatchesTheEngine()
    {
        const string input = """{"id":7,"customer":{"name":"Zoë"}}""";
        var definition = Definition();

        var result = new MappingRunner().Run(definition, input);

        Assert.Equal("json", result.InputFormat);
        Assert.Equal("json", result.OutputFormat);
        Assert.Equal("application/json", result.ContentType);
        Assert.Equal("""{"orderId":7,"who":"Zoë"}""", result.OutputText);
        Assert.Equal(TestSupport.Run($"{{ {Rules} }}", input).ToJsonString(), result.Mapped.ToJsonString());
        FormatSamplesTests.AssertSameJson(JsonNode.Parse(input), result.Decoded, "decoded");
    }

    [Fact]
    public void FormatsBlock_IsReadFromTheDefinitionJson_CaseInsensitive()
    {
        var definition = Definition("""{ "Input": { "Type": "json", "Options": { "ndjson": true } }, "output": { "type": "JSON", "options": { "indent": true } } }""");

        Assert.Equal("json", definition.Formats!.Input!.Type);
        Assert.True(definition.Formats.Input.Options!["ndjson"]!.GetValue<bool>());
        Assert.Equal("JSON", definition.Formats.Output!.Type);
    }

    [Fact]
    public void OutputOptions_Apply_AndTheDecodedInputStaysIntact()
    {
        var definition = Definition("""{ "output": { "type": "json", "options": { "indent": true } } }""");

        var result = new MappingRunner().Run(definition, """{"id":1,"customer":{"name":"A"}}""");

        Assert.Equal("{\n  \"orderId\": 1,\n  \"who\": \"A\"\n}", result.OutputText.Replace("\r\n", "\n"));
        Assert.Equal("A", result.Decoded!["customer"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void RunDecoded_SkipsTheInputFormat_ButEncodes()
    {
        var definition = TestSupport.Mapping("""
            { "formats": { "input": { "type": "nope" }, "output": { "type": "json", "options": { "ndjson": true } } },
              "target": { "type": "array", "from": "input.items[]", "map": { "n": "n" } } }
            """);

        var result = new MappingRunner().RunDecoded(definition, JsonNode.Parse("""{"items":[{"n":1},{"n":2}]}"""));

        Assert.Equal("{\"n\":1}\n{\"n\":2}", result.OutputText);
    }

    [Fact]
    public void OtherSources_AreGivenBesideTheInput()
    {
        var definition = TestSupport.Mapping("""
            { "target": { "type": "object", "mappings": [
                { "target": "id", "from": "input.id" }, { "target": "rate", "from": "rates.usd" } ] } }
            """);
        var others = new Dictionary<string, JsonNode?> { ["rates"] = JsonNode.Parse("""{"usd":278}""") };

        var result = new MappingRunner().Run(definition, """{"id":3}""", others);

        Assert.Equal("""{"id":3,"rate":278}""", result.OutputText);
        Assert.Throws<ArgumentException>(() => new MappingRunner().Run(definition, "{}", new Dictionary<string, JsonNode?> { ["Input"] = null }));
    }

    [Fact]
    public void UnknownFormat_NamesTheKnownOnes()
    {
        var ex = Assert.Throws<MappingFormatException>(() => new MappingRunner().Run(Definition("""{ "output": { "type": "yaml" } }"""), "{}"));

        Assert.Equal(FormatDirection.Encode, ex.Direction);
        Assert.Contains("Unknown format 'yaml'", ex.Message);
        Assert.Contains("json", ex.Message);
    }

    [Theory]
    [InlineData("""{ "input": { "type": "json", "options": { "indnet": true } } }""", "Unknown option 'indnet'")]
    [InlineData("""{ "input": { "type": "json", "options": { "indent": true } } }""", "only applies when writing json")]
    [InlineData("""{ "output": { "type": "json", "options": { "ndjson": "yes" } } }""", "must be true or false")]
    [InlineData("""{ "input": { "type": "json", "options": { "encoding": "klingon" } } }""", "Unknown encoding 'klingon'")]
    public void BadOptions_FailBeforeAnyDataIsRead(string formats, string message)
    {
        // The input is not valid JSON: the settings error must win, whatever the data is.
        var ex = Assert.Throws<MappingFormatException>(() => new MappingRunner().Run(Definition(formats), "not json"));

        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void InvalidJson_SaysWhere()
    {
        var ex = Assert.Throws<MappingFormatException>(() => new MappingRunner().Run(Definition(), "{\n  \"id\": 7,\n  \"x\": }"));

        Assert.Equal("json", ex.Format);
        Assert.Equal(FormatDirection.Decode, ex.Direction);
        Assert.Equal(3, ex.Location!.Line);
        Assert.StartsWith("Reading json: ", ex.Message);
        Assert.Contains("line 3", ex.Message);
        Assert.DoesNotContain("LineNumber", ex.Message);
    }

    [Fact]
    public void InvalidNdjsonLine_SaysWhichLine()
    {
        var definition = Definition("""{ "input": { "type": "json", "options": { "ndjson": true } } }""");

        var ex = Assert.Throws<MappingFormatException>(() => new MappingRunner().Run(definition, "{\"a\":1}\n\n{\"a\":}"));

        Assert.Equal(3, ex.Location!.Line);
    }

    [Fact]
    public void NdjsonOutput_NeedsAList()
    {
        var ex = Assert.Throws<MappingFormatException>(() => new MappingRunner().Run(Definition("""{ "output": { "type": "json", "options": { "ndjson": true } } }"""), """{"id":1}"""));

        Assert.Contains("needs the mapping to produce a list", ex.Message);
    }

    [Fact]
    public void Limits_InputSize_AndDepth()
    {
        var engine = new Engine.MappingEngine();
        var small = new MappingRunner(engine, FormatRegistry.CreateDefault(), new FormatLimits { MaxInputBytes = 10, MaxDepth = 3 });

        var tooBig = Assert.Throws<MappingFormatException>(() => small.Run(Definition(), """{"id":1234567890}"""));
        Assert.Contains("more than the limit", tooBig.Message);

        var tooDeep = Assert.Throws<MappingFormatException>(() => small.Run(Definition(), "[[[[1]]]]"));
        Assert.Equal("json", tooDeep.Format);
    }

    [Fact]
    public void BytesInput_UsesTheEncodingOption_AndOutputBytesFollowTheOutputEncoding()
    {
        var latin1 = Encoding.GetEncoding("iso-8859-1");
        var definition = Definition("""
            { "input": { "type": "json", "options": { "encoding": "iso-8859-1" } },
              "output": { "type": "json", "options": { "encoding": "utf-16" } } }
            """);

        var result = new MappingRunner().Run(definition, FormatPayload.FromBytes(latin1.GetBytes("""{"id":1,"customer":{"name":"Müller"}}""")));

        Assert.Equal("Müller", result.Mapped["who"]!.GetValue<string>());
        Assert.Equal(Encoding.Unicode.GetBytes("""{"orderId":1,"who":"Müller"}"""), result.OutputBytes);
    }

    [Fact]
    public void Registry_RefusesADuplicate_ButReplaces()
    {
        var registry = FormatRegistry.CreateDefault();

        Assert.Throws<InvalidOperationException>(() => registry.Register(new Formats.Codecs.JsonCodec()));
        registry.Replace(new Formats.Codecs.JsonCodec());
        Assert.True(registry.TryGet(null, out var codec));
        Assert.Equal("json", codec.Type);
    }
}
