using System.Text.Json.Nodes;
using SpanFlow.Mapping.Core.Models;
using SpanFlow.Mapping.Formats;
using Xunit;

namespace SpanFlow.Mapping.Tests;

/// <summary>
/// E73-T2 to T5: what the sample corpus cannot show (refusals, their messages and places) and whole runs through
/// MappingRunner with a different format on each side. The happy paths per codec are in FormatSamples/.
/// </summary>
public class FormatCodecTests
{
    private static FormatSpec Spec(string type, string? options) =>
        new() { Type = type, Options = options is null ? null : JsonNode.Parse(options)!.AsObject() };

    private static JsonNode? Read(string type, string raw, string? options = null) =>
        new MappingRunner().Decode(Spec(type, options), FormatPayload.FromText(raw));

    private static string Write(string type, string json, string? options = null) =>
        new MappingRunner().Encode(Spec(type, options), JsonNode.Parse(json)).ToString();

    private static MappingFormatException Fails(Action action) => Assert.Throws<MappingFormatException>(action);

    // ---------------------------------------------------------------- positional

    private const string PkLayout = """
        { "layout": { "fields": [ { "name": "country", "length": 2, "type": "alpha" }, { "name": "account", "length": 13, "type": "numeric" } ] } }
        """;

    [Fact]
    public void Positional_LayoutTypo_IsNamed()
    {
        var ex = Fails(() => Read("positional", "ab", """{ "layout": { "fields": [ { "name": "a", "lenght": 2 } ] } }"""));
        Assert.Contains("Unknown key 'lenght'", ex.Message);
        Assert.Contains("layout.fields[0]", ex.Message);
    }

    [Fact]
    public void Positional_ShortField_SaysWhichFieldAndWhere()
    {
        var ex = Fails(() => Read("positional", "PK00011", PkLayout));
        Assert.Contains("Field 'account' needs 13 characters", ex.Message);
        Assert.Equal(2, ex.Location!.Offset);
        Assert.Equal("account", ex.Location.Field);
    }

    [Fact]
    public void Positional_NumericWithALetter_IsRefused()
    {
        var ex = Fails(() => Read("positional", "PK0001112A23344", PkLayout));
        Assert.Contains("must be digits only", ex.Message);
    }

    [Fact]
    public void Positional_LengthPrefix_AboveMax_IsRefused()
    {
        var ex = Fails(() => Read("positional", "010abcdefghij", """{ "layout": { "fields": [ { "name": "memo", "prefix": 3, "max": 5 } ] } }"""));
        Assert.Contains("more than its max of 5", ex.Message);
    }

    [Fact]
    public void Positional_LengthPrefix_WritesTheRealLength()
    {
        var text = Write("positional", """{"code":"AB123","memo":"x"}""",
            """{ "layout": { "fields": [ { "name": "code", "prefix": 2 }, { "name": "memo", "prefix": 3 } ] } }""");
        Assert.Equal("05AB123001x", text);
    }

    [Fact]
    public void Positional_CountField_IsWrittenFromTheList_NotFromTheJson()
    {
        const string layout = """{ "layout": { "fields": [ { "name": "n", "length": 1, "type": "integer" }, { "name": "items", "repeat": "n", "fields": [ { "name": "v", "length": 2 } ] } ] } }""";
        Assert.Equal("2a b ", Write("positional", """{"n":9,"items":[{"v":"a"},{"v":"b"}]}""", layout));
    }

    [Fact]
    public void Positional_FixedRepeat_NeedsExactlyThatMany()
    {
        var ex = Fails(() => Write("positional", """{"items":[{"v":"a"},{"v":"b"}]}""",
            """{ "layout": { "fields": [ { "name": "items", "repeat": 3, "fields": [ { "name": "v", "length": 1 } ] } ] } }"""));
        Assert.Contains("holds exactly 3 items", ex.Message);
    }

    [Fact]
    public void Positional_DataAfterTheLastField_FailsUnlessNotStrict()
    {
        const string layout = """{ "fields": [ { "name": "a", "length": 2 } ] }""";
        Assert.Contains("left after the last field", Fails(() => Read("positional", "abc", $$"""{ "layout": {{layout}} }""")).Message);
        Assert.Equal("ab", Read("positional", "abc", $$"""{ "layout": {{layout}}, "strict": false }""")!["a"]!.GetValue<string>());
    }

    [Fact]
    public void Positional_ValueTooLong_OnWrite()
    {
        var ex = Fails(() => Write("positional", """{"a":"abc"}""", """{ "layout": { "fields": [ { "name": "a", "length": 2 } ] } }"""));
        Assert.Contains("is 3 characters ('abc'), the field holds 2", ex.Message);
    }

    [Fact]
    public void Positional_NegativeAmount_NeedsASign()
    {
        var ex = Fails(() => Write("positional", """{"a":-1.5}""", """{ "layout": { "fields": [ { "name": "a", "length": 6, "type": "decimal", "scale": 2 } ] } }"""));
        Assert.Contains("has no 'sign'", ex.Message);
    }

    [Fact]
    public void Positional_Records_UnknownLine_SaysWhichLine()
    {
        var ex = Fails(() => Read("positional", "H1\nX2\n",
            """{ "layout": { "records": [ { "name": "h", "when": { "equals": "H" }, "fields": [ { "name": "v", "length": 2 } ] } ] } }"""));
        Assert.Equal(2, ex.Location!.Line);
        Assert.Contains("No record in the layout matches", ex.Message);
    }

    [Fact]
    public void Positional_RepeatByAFieldThatComesLater_IsALayoutError()
    {
        var ex = Fails(() => Read("positional", "1a", """{ "layout": { "fields": [ { "name": "items", "repeat": "n", "fields": [ { "name": "v", "length": 1 } ] }, { "name": "n", "length": 1 } ] } }"""));
        Assert.Contains("must be a field before it", ex.Message);
    }

    [Fact]
    public void Positional_Unit_IsCheckedBeforeData()
    {
        var ex = Fails(() => Read("positional", "ab", """{ "unit": "words", "layout": { "fields": [ { "name": "a", "length": 2 } ] } }"""));
        Assert.Contains("chars or bytes", ex.Message);
    }

    // ---------------------------------------------------------------- text

    [Fact]
    public void Text_UnclosedQuote_SaysTheLine()
    {
        var ex = Fails(() => Read("text", "a,b\n1,\"open\n2,3", """{ "mode": "delimited" }"""));
        Assert.Contains("never closed", ex.Message);
        Assert.Equal(2, ex.Location!.Line);
    }

    [Fact]
    public void Text_Delimited_MissingCellsAreEmpty_ExtraCellsGetNumberedColumns()
    {
        var rows = Read("text", "a,b\n1\n1,2,3", """{ "mode": "delimited" }""")!["rows"]!;
        Assert.Equal("""[{"a":"1","b":""},{"a":"1","b":"2","column3":"3"}]""", rows.ToJsonString());
    }

    [Fact]
    public void Text_Delimited_WithoutQuote_RefusesAValueHoldingTheDelimiter()
    {
        var ex = Fails(() => Write("text", """{"rows":[{"a":"x,y"}]}""", """{ "mode": "delimited", "quote": "" }"""));
        Assert.Contains("no quote character", ex.Message);
    }

    [Fact]
    public void Text_Pattern_ListsLinesThatDoNotMatch()
    {
        var result = Read("text", "Ali owes 5\nhello\n", """{ "mode": "pattern", "pattern": "^(?<name>\\w+) owes (?<amount>\\d+)$" }""")!;
        Assert.Single(result["rows"]!.AsArray());
        Assert.Equal(2, result["unmatched"]![0]!["line"]!.GetValue<int>());
    }

    [Fact]
    public void Text_Template_BadBrace_IsRefused()
    {
        Assert.Contains("never closed", Fails(() => Write("text", """{"rows":[{}]}""", """{ "mode": "pattern", "template": "{name" }""")).Message);
        Assert.Equal("{x} = 1", Write("text", """[{"v":1}]""", """{ "mode": "pattern", "template": "{{x}} = {v}" }"""));
    }

    [Fact]
    public void Text_UnknownMode_IsRefused() =>
        Assert.Contains("must be whole, lines, delimited, pattern or fixed", Fails(() => Read("text", "x", """{ "mode": "csv" }""")).Message);

    // ---------------------------------------------------------------- xml

    [Fact]
    public void Xml_Dtd_IsRefused()
    {
        var ex = Fails(() => Read("xml", """<!DOCTYPE a [<!ENTITY x "y">]><a>&x;</a>"""));
        Assert.Contains("DTD", ex.Message);
    }

    [Fact]
    public void Xml_Malformed_SaysWhere()
    {
        var ex = Fails(() => Read("xml", "<a>\n  <b>\n</a>"));
        Assert.Equal(3, ex.Location!.Line);
    }

    [Fact]
    public void Xml_UndeclaredPrefix_IsRefused_UnlessTheOptionDeclaresIt()
    {
        Assert.Contains("prefix 'p' is not declared", Fails(() => Write("xml", """{"a":{"p:b":"1"}}""")).Message);
        Assert.Equal("""<a xmlns:p="urn:p"><p:b>1</p:b></a>""",
            Write("xml", """{"a":{"p:b":"1"}}""", """{ "declaration": false, "namespaces": { "p": "urn:p" } }"""));
    }

    [Fact]
    public void Xml_NeedsOneRoot()
    {
        var ex = Fails(() => Write("xml", """{"a":1,"b":2}"""));
        Assert.Contains("top-level keys: a, b", ex.Message);
    }

    [Fact]
    public void Xml_RootOption_MustMatchOnRead()
    {
        var ex = Fails(() => Read("xml", "<Order/>", """{ "root": "Invoice" }"""));
        Assert.Contains("The root element is 'Order', not 'Invoice'", ex.Message);
    }

    [Fact]
    public void Xml_OneOrTwoItems_SameShape_WithArrays()
    {
        const string options = """{ "arrays": ["Line"] }""";
        Assert.IsType<JsonArray>(Read("xml", "<o><Line>1</Line></o>", options)!["o"]!["Line"]);
        Assert.IsType<JsonArray>(Read("xml", "<o><Line>1</Line><Line>2</Line></o>", options)!["o"]!["Line"]);
        Assert.IsAssignableFrom<JsonValue>(Read("xml", "<o><Line>1</Line></o>")!["o"]!["Line"]);
    }

    // ---------------------------------------------------------------- edi

    [Fact]
    public void Edi_NotEdi_IsSaidPlainly() =>
        Assert.Contains("does not look like EDI", Fails(() => Read("edi", "hello")).Message);

    [Fact]
    public void Edi_X12_TransactionWithoutSe_IsRefused()
    {
        const string isa = "ISA*00*          *00*          *ZZ*A              *ZZ*B              *261117*1200*U*00401*000000001*0*P*:~";
        var ex = Fails(() => Read("edi", isa + "GS*PO*A*B*20261117*1200*1*X*004010~ST*850*1~BEG*00~GE*1*1~IEA*1*000000001~"));
        Assert.Contains("not closed by SE", ex.Message);
        Assert.Equal(5, ex.Location!.Segment);
    }

    [Fact]
    public void Edi_X12_ValueHoldingASeparator_IsRefused()
    {
        var json = """
            {"interchange":{"ISA":["00","","00","","ZZ","A","ZZ","B","261117","1200","U","00401","1","0","P",":"],
             "groups":[{"GS":["PO","A","B","20261117","1200","1","X","004010"],"transactions":[{"ST":["850","1"],"segments":[{"id":"N1","e":["a*b"]}]}]}]}}
            """;
        Assert.Contains("no way to escape", Fails(() => Write("edi", json)).Message);
    }

    [Fact]
    public void Edi_X12_PadsIsa_AndComputesTrailers()
    {
        var json = """
            {"interchange":{"ISA":["00","","00","","ZZ","A","ZZ","B","261117","1200","U","00401","42","0","P","?"],
             "groups":[{"GS":["PO","A","B","20261117","1200","7","X","004010"],"transactions":[{"ST":["850","0001"],"segments":[{"id":"BEG","e":["00"]},{"id":"N1","e":["ST",["x","y"]]}]}]}]}}
            """;
        var text = Write("edi", json);
        Assert.StartsWith("ISA*00*          *00*          *ZZ*A              *ZZ*B              *261117*1200*U*00401*000000042*0*P*:~", text);
        Assert.Contains("N1*ST*x:y~SE*4*0001~GE*1*7~IEA*1*000000042~", text);
        Assert.Equal(106, text.IndexOf("GS", StringComparison.Ordinal));
    }

    [Fact]
    public void Edi_Edifact_ComputesCounts_AndEscapes()
    {
        var json = """
            {"interchange":{"UNB":[["UNOC","3"],"S","R",["261117","1200"],"R1"],
             "messages":[{"UNH":["M1",["INVOIC","D","96A","UN"]],"segments":[{"id":"BGM","e":["380","INV+1"]},{"id":"MOA","e":[["77","10.5"]]}]}]}}
            """;
        Assert.Equal("UNB+UNOC:3+S+R+261117:1200+R1'UNH+M1+INVOIC:D:96A:UN'BGM+380+INV?+1'MOA+77:10.5'UNT+4+M1'UNZ+1+R1'", Write("edi", json));
    }

    [Fact]
    public void Edi_Edifact_Groups_RoundTrip()
    {
        const string raw = "UNB+UNOA:3+S+R+261117:1200+R1'UNG+INVOIC+S+R+261117:1200+G1+UN+D:96A'UNH+1+INVOIC:D:96A:UN'BGM+380'UNT+3+1'UNE+1+G1'UNZ+1+R1'";
        var json = Read("edi", raw)!;
        Assert.Equal("G1", json["interchange"]!["groups"]![0]!["UNG"]![4]!.GetValue<string>());
        Assert.Equal(raw, Write("edi", json.ToJsonString()));
    }

    // ---------------------------------------------------------------- whole runs: a different format on each side

    [Fact]
    public void Run_XmlIn_CsvOut()
    {
        var definition = TestSupport.Mapping("""
            { "formats": { "input": { "type": "xml", "options": { "arrays": ["Line"] } }, "output": { "type": "text", "options": { "mode": "delimited" } } },
              "target": { "type": "array", "from": "input.Order.Line[]", "map": { "sku": "@sku", "qty": "#text" } } }
            """);

        var result = new MappingRunner().Run(definition, """<Order><Line sku="A">2</Line><Line sku="B">1</Line></Order>""");

        Assert.Equal("sku,qty\nA,2\nB,1", result.OutputText);
        Assert.Equal("text/plain", result.ContentType);
    }

    [Fact]
    public void Run_JsonIn_XmlOut()
    {
        var definition = TestSupport.Mapping("""
            { "formats": { "output": { "type": "xml", "options": { "root": "Invoice", "declaration": false } } },
              "target": { "type": "object", "mappings": [ { "target": "@number", "from": "input.id" }, { "target": "Customer", "from": "input.customer.name" } ] } }
            """);

        var result = new MappingRunner().Run(definition, """{"id":7,"customer":{"name":"Zoë"}}""");

        Assert.Equal("""<Invoice number="7"><Customer>Zoë</Customer></Invoice>""", result.OutputText);
        Assert.Equal("application/xml", result.ContentType);
    }

    [Fact]
    public void Run_CsvIn_XmlOut()
    {
        var definition = TestSupport.Mapping("""
            { "formats": { "input": { "type": "text", "options": { "mode": "delimited" } }, "output": { "type": "xml", "options": { "root": "People", "declaration": false } } },
              "target": { "type": "object", "mappings": [ { "target": "Person", "foreach": "input.rows[]", "map": { "@id": "id", "Name": "name" } } ] } }
            """);

        var result = new MappingRunner().Run(definition, "id,name\n1,Ali\n2,Sara\n");

        Assert.Equal("""<People><Person id="1"><Name>Ali</Name></Person><Person id="2"><Name>Sara</Name></Person></People>""", result.OutputText);
    }

    [Fact]
    public void Run_PositionalIn_JsonOut()
    {
        var definition = TestSupport.Mapping($$"""
            { "formats": { "input": { "type": "positional", "options": {{PkLayout}} } },
              "target": { "type": "object", "mappings": [ { "target": "iban.country", "from": "input.country" }, { "target": "iban.account", "from": "input.account" } ] } }
            """);

        var result = new MappingRunner().Run(definition, "PK0001111223344");

        Assert.Equal("""{"iban":{"country":"PK","account":"0001111223344"}}""", result.OutputText);
    }
}
