using System.Text;
using System.Text.Json.Nodes;
using SpanFlow.Mapping.Core.Models;
using SpanFlow.Mapping.Formats;
using Xunit;

namespace SpanFlow.Mapping.Tests;

/// <summary>
/// E73-T9: bitmaps (ISO 8583), the bundled starter layouts and EBCDIC. The byte-exact happy paths are in
/// FormatSamples/positional (iso8583-*, ebcdic-record); this covers the bundled field table and the refusals.
/// </summary>
public class Iso8583Tests
{
    private const string Auth = "0200" + "7020000000808000" + "16" + "4111111111111111" + "000000" + "000000010000" + "123456" + "TERM0001" + "840";

    private static FormatSpec Spec(JsonObject options) => new() { Type = "positional", Options = options };

    private static FormatSpec Spec(string options) => Spec(JsonNode.Parse(options)!.AsObject());

    private static FormatSpec Starter(string name) => Spec(StarterLayouts.Find(name)!.Options);

    private static MappingFormatException Fails(Action action) => Assert.Throws<MappingFormatException>(action);

    [Fact]
    public void Starters_AreBundled_AndHandOutCopies()
    {
        Assert.Equal(["iso8583-1987", "iso8583-1987-binary", "pk-account"], StarterLayouts.All.Select(s => s.Name).ToArray());
        var starter = StarterLayouts.Find("ISO8583-1987")!;
        Assert.Equal("positional", starter.Format);
        starter.Options["layout"] = null;
        Assert.NotNull(starter.Options["layout"]);

        // The 1987 table: every data element from 2 to 128 but 65 (the tertiary bitmap marker), each with its bit.
        var members = starter.Options["layout"]!["fields"]![1]!["fields"]!.AsArray();
        Assert.Equal(126, members.Count);
        Assert.Equal(Enumerable.Range(2, 127).Where(b => b != 65), members.Select(m => m!["bit"]!.GetValue<int>()));
    }

    [Fact]
    public void Starter_ReadsAnAuthorisationRequest_AndWritesItBackExactly()
    {
        var runner = new MappingRunner();
        var decoded = runner.Decode(Starter("iso8583-1987"), FormatPayload.FromText(Auth))!;

        Assert.Equal("0200", decoded["mti"]!.GetValue<string>());
        Assert.Equal("4111111111111111", decoded["pan"]!.GetValue<string>());
        Assert.Equal("000000010000", decoded["amountTransaction"]!.GetValue<string>());
        Assert.Equal("TERM0001", decoded["cardAcceptorTerminalId"]!.GetValue<string>());
        Assert.Equal(7, decoded.AsObject().Count);
        Assert.Equal(Auth, runner.Encode(Starter("iso8583-1987"), decoded).ToString());
    }

    [Fact]
    public void Starter_Binary_InEbcdic_RoundTripsByteForByte()
    {
        var options = StarterLayouts.Find("iso8583-1987-binary")!.Options;
        options["encoding"] = "ebcdic";
        var spec = Spec(options);
        var ebcdic = new MappingRunner().Encode(Spec("""{ "encoding": "ebcdic", "layout": { "fields": [ { "name": "x", "rest": true } ] } }"""), new JsonObject { ["x"] = "x" });
        var codePage = Encoding.GetEncoding(37);
        Assert.Equal(new byte[] { 0xA7 }, ebcdic.AsBytes(codePage)); // 'x' in IBM037: the alias resolved

        var json = new JsonObject { ["mti"] = "0800", ["stan"] = "000001", ["networkManagementCode"] = "301" };
        var written = new MappingRunner().Encode(spec, json).AsBytes(codePage);

        // MTI, primary bitmap (bit 1 for the secondary, bit 11), secondary bitmap (bit 70), then the elements.
        var expected = codePage.GetBytes("0800")
            .Concat(new byte[] { 0x80, 0x20, 0, 0, 0, 0, 0, 0 })
            .Concat(new byte[] { 0x04, 0, 0, 0, 0, 0, 0, 0 })
            .Concat(codePage.GetBytes("000001301"))
            .ToArray();
        Assert.Equal(expected, written);
        Assert.True(JsonNode.DeepEquals(json, new MappingRunner().Decode(spec, FormatPayload.FromBytes(written))));
    }

    [Fact]
    public void Run_8583In_JsonOut_ThroughAMapping()
    {
        var formats = new JsonObject { ["input"] = new JsonObject { ["type"] = "positional", ["options"] = StarterLayouts.Find("iso8583-1987")!.Options } };
        var definition = TestSupport.Mapping($$"""
            { "formats": {{formats.ToJsonString()}},
              "target": { "type": "object", "mappings": [
                { "target": "card", "from": "input.pan" },
                { "target": "terminal", "from": "input.cardAcceptorTerminalId" } ] } }
            """);
        var result = new MappingRunner().Run(definition, Auth);
        Assert.Equal("""{"card":"4111111111111111","terminal":"TERM0001"}""", result.OutputText);
    }

    [Fact]
    public void Bitmap_BitSetWithNoField_SaysWhichBit()
    {
        var ex = Fails(() => new MappingRunner().Decode(
            Spec("""{ "layout": { "fields": [ { "name": "mti", "length": 4 }, { "bitmap": "hex", "fields": [ { "bit": 2, "name": "pan", "prefix": 2 } ] } ] } }"""),
            FormatPayload.FromText("0200" + "6000000000000000" + "0212" + "34")));
        Assert.Contains("has bit 3 set but no field for it", ex.Message);
        Assert.Equal(24, ex.Location!.Offset); // after MTI 4, bitmap 16, pan "02" + "12"
    }

    [Fact]
    public void Bitmap_NotHex_IsRefused()
    {
        var ex = Fails(() => new MappingRunner().Decode(
            Spec("""{ "layout": { "fields": [ { "bitmap": "hex", "fields": [ { "bit": 2, "name": "a", "length": 1 } ] } ] } }"""),
            FormatPayload.FromText("40000000000000ZZ1")));
        Assert.Contains("must be 16 hex characters", ex.Message);
    }

    [Fact]
    public void Bitmap_Binary_InUtf8_IsRefused()
    {
        var ex = Fails(() => new MappingRunner().Encode(
            Spec("""{ "layout": { "fields": [ { "bitmap": "binary", "fields": [ { "bit": 2, "name": "a", "length": 1 } ] } ] } }"""),
            new JsonObject { ["a"] = "x" }));
        Assert.Contains("needs a single-byte encoding", ex.Message);
    }

    [Theory]
    [InlineData("""[ { "name": "a", "length": 1, "bit": 2 } ]""", "has a 'bit' but is not inside a bitmap")]
    [InlineData("""[ { "bitmap": "hex", "fields": [ { "name": "a", "length": 1 } ] } ]""", "needs a 'bit' from 2 to 128")]
    [InlineData("""[ { "bitmap": "hex", "fields": [ { "bit": 1, "name": "a", "length": 1 } ] } ]""", "needs a 'bit' from 2 to 128")]
    [InlineData("""[ { "bitmap": "hex", "fields": [ { "bit": 2, "name": "a", "length": 1 }, { "bit": 2, "name": "b", "length": 1 } ] } ]""", "use bit 2")]
    [InlineData("""[ { "bitmap": "hex", "fields": [ { "bit": 2, "const": "A", "length": 1 } ] } ]""", "needs a 'name'")]
    [InlineData("""[ { "bitmap": "octal", "fields": [ { "bit": 2, "name": "a", "length": 1 } ] } ]""", "must be hex")]
    [InlineData("""[ { "bitmap": "hex", "length": 16, "fields": [ { "bit": 2, "name": "a", "length": 1 } ] } ]""", "not length")]
    [InlineData("""[ { "name": "a", "length": 1 }, { "bitmap": "hex", "fields": [ { "bit": 2, "name": "a", "length": 1 } ] } ]""", "are named 'a'")]
    public void Bitmap_LayoutMistakes_AreNamed(string fields, string message)
    {
        var ex = Fails(() => new MappingRunner().Decode(Spec($$"""{ "layout": { "fields": {{fields}} } }"""), FormatPayload.FromText("x")));
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void Bitmap_Named_NestsItsMembers_AndAbsentMembersAreLeftOut()
    {
        var spec = Spec("""
            { "layout": { "fields": [
              { "name": "mti", "length": 4 },
              { "name": "elements", "bitmap": "hex", "fields": [
                { "bit": 3, "name": "code", "length": 2 },
                { "bit": 2, "name": "pan", "prefix": 2, "type": "numeric" } ] } ] } }
            """);
        var json = JsonNode.Parse("""{ "mti": "0100", "elements": { "pan": "1234", "code": null } }""");
        var text = new MappingRunner().Encode(spec, json).ToString();
        Assert.Equal("0100" + "4000000000000000" + "041234", text);
        Assert.Equal("""{"mti":"0100","elements":{"pan":"1234"}}""", new MappingRunner().Decode(spec, FormatPayload.FromText(text))!.ToJsonString());
    }

    [Fact]
    public void Encoding_Unknown_ListsEbcdic()
    {
        var ex = Fails(() => new MappingRunner().Decode(Spec("""{ "encoding": "klingon", "layout": { "fields": [ { "name": "a", "rest": true } ] } }"""), FormatPayload.FromText("a")));
        Assert.Contains("ebcdic (IBM037)", ex.Message);
    }

    [Fact]
    public void Encoding_OtherEbcdicCodePages_Resolve()
    {
        var decoded = new MappingRunner().Decode(
            Spec("""{ "encoding": "IBM500", "layout": { "fields": [ { "name": "a", "length": 2 } ] } }"""),
            FormatPayload.FromBytes([0xC1, 0xC2]));
        Assert.Equal("AB", decoded!["a"]!.GetValue<string>());
    }
}
