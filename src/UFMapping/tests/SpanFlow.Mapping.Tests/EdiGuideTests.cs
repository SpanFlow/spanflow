using System.Text.Json.Nodes;
using SpanFlow.Mapping.Core.Models;
using SpanFlow.Mapping.Formats;
using Xunit;

namespace SpanFlow.Mapping.Tests;

/// <summary>
/// E73-T11: EDI level 2. The named happy paths are in FormatSamples/edi (*-guided) and examples/edi-997-ack; this
/// covers the problem report, validate, guides of your own, and what writing refuses.
/// </summary>
public class EdiGuideTests
{
    private static string X12(params string[] body) =>
        "ISA*00*          *00*          *ZZ*SENDERID       *ZZ*RECEIVERID     *261117*1200*U*00401*000000001*0*P*:~\n" +
        "GS*PO*SENDERID*RECEIVERID*20261117*1200*1*X*004010~\n" +
        string.Concat(body.Select(b => b + "\n")) +
        "GE*1*1~\nIEA*1*000000001~\n";

    private static string Tx(params string[] segments) =>
        $"ST*850*0001~\n{string.Concat(segments.Select(s => s + "\n"))}SE*{segments.Length + 2}*0001~";

    private static FormatSpec Edi(JsonNode? guides, bool validate = false, string? newline = null)
    {
        var options = new JsonObject { ["guides"] = guides };
        if (validate) options["validate"] = true;
        if (newline is not null) options["newline"] = newline;
        return new FormatSpec { Type = "edi", Options = options };
    }

    private static JsonNode Read(string raw, FormatSpec? spec = null) => new MappingRunner().Decode(spec ?? Edi("bundled"), FormatPayload.FromText(raw))!;

    private static JsonObject FirstTx(JsonNode decoded) => decoded["interchange"]!["groups"]![0]!["transactions"]![0]!.AsObject();

    private static MappingFormatException Fails(Action action) => Assert.Throws<MappingFormatException>(action);

    [Fact]
    public void AllSevenGuides_AreBundled_AndParse()
    {
        Assert.Equal(
            ["edifact-desadv", "edifact-invoic", "edifact-orders", "x12-810", "x12-850", "x12-856", "x12-997"],
            EdiGuides.Bundled.Select(g => g.Name).ToArray());
        Assert.Equal("Purchase order", EdiGuides.Bundled.Single(g => g.Name == "x12-850").Title);
    }

    [Fact]
    public void Problems_AreListedBesideTheMessage_NotThrown()
    {
        var raw = X12(Tx(
            "BEG*99*SA*PO-1-THAT-IS-FAR-TOO-LONG-FOR-BEG03**2026111~",
            "XYZ*1~",
            "PO1*1*2*EA*nine~"));

        var tx = FirstTx(Read(raw));

        var problems = tx["problems"]!.AsArray().Select(p => $"{p!["segment"]} {p["id"]} {p["element"]} | {p["problem"]}").ToList();
        Assert.Contains("4 BEG BEG01 | BEG01 (purposeCode) '99' is not one of the guide's codes (00, 01, 05, 06, 07).", problems);
        Assert.Contains("4 BEG BEG03 | BEG03 (poNumber) is 35 characters, more than 22.", problems);
        Assert.Contains("4 BEG BEG05 | BEG05 (date) is 7 characters, fewer than 8.", problems);
        Assert.Contains(problems, p => p.StartsWith("5 XYZ  | This segment is not where the guide expects it", StringComparison.Ordinal));
        Assert.True(problems.Contains("6 PO1 PO104 | PO104 (unitPrice) 'nine' is not a number."), string.Join(" // ", problems));
        // Still mapped: the values are there, and the stray segment is kept.
        Assert.Equal("nine", tx["body"]!["PO1Loop"]![0]!["PO1"]!["unitPrice"]!.GetValue<string>());
        Assert.Equal("XYZ", tx["body"]!["unexpected"]![0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public void AMissingMandatorySegment_IsReportedWhereItShouldHaveBeen()
    {
        var tx = FirstTx(Read(X12(Tx("PO1*1*2*EA*9.5~"))));

        var problem = tx["problems"]!.AsArray().Single()!;
        Assert.Equal("BEG", problem["id"]!.GetValue<string>());
        Assert.Equal(4, problem["segment"]!.GetValue<int>());
        Assert.Equal("Missing BEG: the guide needs it, found 0.", problem["problem"]!.GetValue<string>());
    }

    [Fact]
    public void Validate_StopsAtTheFirstProblem_WithItsSegment()
    {
        var ex = Fails(() => Read(X12(Tx("BEG*99*SA*PO1**20261117~", "PO1*1~")), Edi("bundled", validate: true)));

        Assert.Contains("Segment 4 (BEG) BEG01: BEG01 (purposeCode) '99' is not one of the guide's codes", ex.Message);
        Assert.Equal(4, ex.Location!.Segment);
        Assert.Equal("BEG01", ex.Location.Field);
    }

    [Fact]
    public void AMessageWithNoGuide_StaysNumbered_BesideGuidedOnes()
    {
        var raw = X12(Tx("BEG*00*SA*PO1**20261117~", "PO1*1~"), "ST*860*0002~\nBCH*01*SA*PO1~\nSE*3*0002~").Replace("GE*1*1", "GE*2*1");
        var transactions = Read(raw)["interchange"]!["groups"]![0]!["transactions"]!.AsArray();

        Assert.Equal("x12-850", transactions[0]!["guide"]!.GetValue<string>());
        Assert.Null(transactions[1]!["guide"]);
        Assert.Equal("BCH", transactions[1]!["segments"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("860", transactions[1]!["ST"]!["code"]!.GetValue<string>());

        var written = new MappingRunner().Encode(Edi("bundled", newline: "\n"), Read(raw)).ToString();
        Assert.Equal(raw, written);
    }

    [Fact]
    public void AGuideOfYourOwn_ComesBeforeTheBundledOne()
    {
        var own = JsonNode.Parse("""
            { "name": "acme-850", "standard": "x12", "message": "850",
              "segments": [ { "id": "BEG", "min": 1, "elements": [ { "name": "purpose" }, { "name": "kind" }, { "name": "order" } ] },
                            { "loop": "Lines", "max": 999, "segments": [ { "id": "PO1", "elements": [ { "name": "line" }, { "name": "qty" } ] } ] } ] }
            """);
        var tx = FirstTx(Read(X12(Tx("BEG*00*SA*PO1~", "PO1*1*5~")), Edi(new JsonArray(own, "bundled"))));

        Assert.Equal("acme-850", tx["guide"]!.GetValue<string>());
        Assert.Equal("PO1", tx["body"]!["BEG"]!["order"]!.GetValue<string>());
        Assert.Equal("5", tx["body"]!["Lines"]![0]!["PO1"]!["qty"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("\"x12-999\"", "There is no bundled guide 'x12-999'")]
    [InlineData("""{ "name": "g", "standard": "x12", "message": "850", "segments": [ { "id": "BEG", "elemnts": [] } ] }""", "Unknown key 'elemnts'")]
    [InlineData("""{ "name": "g", "standard": "x12", "message": "850", "segments": [ { "loop": "L", "segments": [ { "loop": "M", "segments": [ { "id": "A" } ] } ] } ] }""", "Loop 'L' must start with a segment")]
    [InlineData("""{ "name": "g", "standard": "x12", "message": "850", "segments": [ { "id": "A" }, { "id": "A" } ] }""", "has 'A' twice at one level")]
    [InlineData("42", "Option 'guides' takes")]
    public void GuideMistakes_AreNamed(string guides, string message)
    {
        var ex = Fails(() => Read(X12(Tx("BEG*00~")), Edi(JsonNode.Parse(guides))));
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void Writing_ByPlace_ForElementsTheGuideDoesNotName()
    {
        var json = Read(X12(Tx("BEG*00*SA*PO1**20261117~", "PO1*1~")));
        var beg = FirstTx(json)["body"]!["BEG"]!.AsObject();
        beg["BEG07"] = "X";
        beg["BEG08-2"] = "B";

        var written = new MappingRunner().Encode(Edi("bundled", newline: "\n"), json).ToString();

        Assert.Contains("BEG*00*SA*PO1**20261117**X*:B~", written);
    }

    [Fact]
    public void Writing_AnUnknownName_SaysWhatIsThere()
    {
        var json = Read(X12(Tx("BEG*00*SA*PO1**20261117~", "PO1*1~")));
        FirstTx(json)["body"]!["BEG"]!["purposCode"] = "00";
        var ex = Fails(() => new MappingRunner().Encode(Edi("bundled"), json));
        Assert.Contains("BEG has no element 'purposCode'. Elements: purposeCode, typeCode, poNumber", ex.Message);

        json = Read(X12(Tx("BEG*00*SA*PO1**20261117~", "PO1*1~")));
        FirstTx(json)["body"]!["PO1Lop"] = new JsonArray();
        ex = Fails(() => new MappingRunner().Encode(Edi("bundled"), json));
        Assert.Contains("x12-850 has no 'PO1Loop' at this level".Replace("PO1Loop", "PO1Lop"), ex.Message);
        Assert.Contains("BEG, CUR, REF, PER, DTM, TD5, N1Loop, PO1Loop, CTTLoop", ex.Message);
    }

    [Fact]
    public void Writing_WithValidate_RefusesWhatTheGuideRequires()
    {
        var json = Read(X12(Tx("BEG*00*SA*PO1**20261117~", "PO1*1~")));
        FirstTx(json)["body"]!.AsObject().Remove("PO1Loop");

        // Without validate it is written as given; with it, the missing loop is refused.
        new MappingRunner().Encode(Edi("bundled"), json.DeepClone());
        var ex = Fails(() => new MappingRunner().Encode(Edi("bundled", validate: true), json));
        Assert.Contains("Missing PO1Loop: the guide needs it, the mapping gave 0.", ex.Message);
    }

    [Fact]
    public void NamedEnvelopes_NeedTheGuidesOption_ToBeWritten()
    {
        var json = Read(X12(Tx("BEG*00*SA*PO1**20261117~", "PO1*1~")));
        var ex = Fails(() => new MappingRunner().Encode(new FormatSpec { Type = "edi" }, json));
        Assert.Contains("set the guides option", ex.Message);
    }

    [Fact]
    public void AnEdifactComposite_IsAnObject_AndWritesBack()
    {
        var raw = "UNB+UNOA:3+S:14+R:14+261117:1200+R1'\nUNH+1+ORDERS:D:96A:UN'\nBGM+220+PO9+9'\nDTM+137:20261117:102'\nQTY+21:5:PCE'\nUNS+S'\nUNT+7+1'\nUNZ+1+R1'\n";
        var json = Read(raw);
        var body = json["interchange"]!["messages"]![0]!["body"]!;

        // QTY before any LIN is out of place in ORDERS: reported, kept, still written.
        Assert.Equal("QTY", json["interchange"]!["messages"]![0]!["problems"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("ORDERS", json["interchange"]!["messages"]![0]!["UNH"]!["type"]!["type"]!.GetValue<string>());
        Assert.Equal("137", body["DTM"]![0]!["period"]!["qualifier"]!.GetValue<string>());
        Assert.Equal("S", body["UNS"]!["sectionId"]!.GetValue<string>());
        Assert.Contains("QTY+21:5:PCE'", new MappingRunner().Encode(Edi("bundled", newline: "\n"), json).ToString());
    }
}
