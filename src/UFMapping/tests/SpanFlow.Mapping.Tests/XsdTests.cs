using System.Text.Json.Nodes;
using SpanFlow.Mapping.Core.Models;
using SpanFlow.Mapping.Formats;
using Xunit;

namespace SpanFlow.Mapping.Tests;

/// <summary>
/// E73-T10: the xml codec's xsd and validate options. The ISO 20022 happy paths (camt.053, pacs.008) are in
/// FormatSamples/xml with their schemas; this covers the shapes a schema can say things in, and the refusals.
/// </summary>
public class XsdTests
{
    private const string OrderXsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:o" xmlns="urn:o" elementFormDefault="qualified">
          <xs:element name="Order">
            <xs:complexType>
              <xs:sequence>
                <xs:element name="Id" type="xs:int"/>
                <xs:element name="Rush" type="xs:boolean" minOccurs="0"/>
                <xs:element name="Total" type="xs:decimal" minOccurs="0"/>
                <xs:element name="Code" type="xs:string" minOccurs="0"/>
                <xs:choice maxOccurs="unbounded">
                  <xs:element name="Line" type="Line"/>
                  <xs:element name="Note" type="xs:string"/>
                </xs:choice>
                <xs:element name="Part" type="Part" minOccurs="0"/>
              </xs:sequence>
              <xs:attribute name="version" type="xs:positiveInteger"/>
            </xs:complexType>
          </xs:element>
          <xs:complexType name="Line">
            <xs:sequence><xs:element name="Qty" type="xs:integer"/></xs:sequence>
            <xs:attribute name="sku" type="xs:string"/>
          </xs:complexType>
          <xs:complexType name="Part">
            <xs:sequence><xs:element name="Part" type="Part" minOccurs="0" maxOccurs="unbounded"/></xs:sequence>
            <xs:attribute name="n" type="xs:int"/>
          </xs:complexType>
        </xs:schema>
        """;

    private static FormatSpec Spec(bool validate = false, string xsd = OrderXsd)
    {
        var options = new JsonObject { ["xsd"] = xsd };
        if (validate) options["validate"] = true;
        return new FormatSpec { Type = "xml", Options = options };
    }

    private static JsonNode Read(string xml, FormatSpec? spec = null) => new MappingRunner().Decode(spec ?? Spec(), FormatPayload.FromText(xml))!;

    private static MappingFormatException Fails(Action action) => Assert.Throws<MappingFormatException>(action);

    [Fact]
    public void Repeats_InsideARepeatedChoice_AreLists_EvenWithOneItem()
    {
        var order = Read("""<Order xmlns="urn:o"><Id>7</Id><Line sku="A"><Qty>2</Qty></Line></Order>""")["Order"]!;

        var lines = Assert.IsType<JsonArray>(order["Line"]);
        Assert.Single(lines);
        Assert.Equal(2, lines[0]!["Qty"]!.GetValue<int>());
        Assert.Equal("A", lines[0]!["@sku"]!.GetValue<string>());
    }

    [Fact]
    public void Types_FromTheSchema_NumbersBooleansAndAttributes_TextStaysText()
    {
        var order = Read("""<Order xmlns="urn:o" version="3"><Id>007</Id><Rush>1</Rush><Total>12.50</Total><Code>0042</Code><Note>hi</Note></Order>""")["Order"]!;

        Assert.Equal("3", order["@version"]!.ToJsonString());
        Assert.Equal("7", order["Id"]!.ToJsonString());
        Assert.True(order["Rush"]!.GetValue<bool>());
        Assert.Equal("12.50", order["Total"]!.ToJsonString());
        Assert.Equal("0042", order["Code"]!.GetValue<string>());
        Assert.IsType<JsonArray>(order["Note"]);
    }

    [Fact]
    public void Prefixes_InTheDocument_DoNotMatter()
    {
        var order = Read("""<o:Order xmlns:o="urn:o"><o:Id>1</o:Id><o:Line><o:Qty>5</o:Qty></o:Line></o:Order>""")["o:Order"]!;

        Assert.Equal(1, order["o:Id"]!.GetValue<int>());
        Assert.Equal(5, order["o:Line"]![0]!["o:Qty"]!.GetValue<int>());
    }

    [Fact]
    public void ASelfContainingType_IsFollowedOncePerBranch_AndStillRead()
    {
        var order = Read("""<Order xmlns="urn:o"><Id>1</Id><Note>n</Note><Part n="1"><Part n="2"><Part n="3"/></Part></Part></Order>""")["Order"]!;

        Assert.Equal(1, order["Part"]!["@n"]!.GetValue<int>());
        Assert.IsType<JsonArray>(order["Part"]!["Part"]);
        // Below the first repeat the schema is not followed again: the deepest part is read as plain XML.
        Assert.Equal("3", order["Part"]!["Part"]![0]!["Part"]!["@n"]!.GetValue<string>());
    }

    [Fact]
    public void AValueThatDoesNotFitItsType_StaysText_WithoutValidate()
    {
        var order = Read("""<Order xmlns="urn:o"><Id>seven</Id><Note>n</Note></Order>""")["Order"]!;
        Assert.Equal("seven", order["Id"]!.GetValue<string>());
    }

    [Fact]
    public void Validate_StopsAtTheFirstBreak_WithItsPlace()
    {
        var ex = Fails(() => Read("<Order xmlns=\"urn:o\">\n  <Id>seven</Id>\n  <Note>n</Note>\n</Order>", Spec(validate: true)));

        Assert.StartsWith("Reading xml: The XML does not follow the xsd:", ex.Message);
        Assert.Contains("Id", ex.Message);
        Assert.Equal(2, ex.Location!.Line);
    }

    [Fact]
    public void Validate_AValidDocument_Reads()
    {
        var order = Read("""<Order xmlns="urn:o"><Id>1</Id><Note>n</Note></Order>""", Spec(validate: true));
        Assert.Equal(1, order["Order"]!["Id"]!.GetValue<int>());
    }

    [Fact]
    public void Validate_WithoutAnXsd_IsRefused()
    {
        var spec = new FormatSpec { Type = "xml", Options = new JsonObject { ["validate"] = true } };
        var ex = Fails(() => Read("<a/>", spec));
        Assert.Contains("needs the schema in the xsd option", ex.Message);
    }

    [Theory]
    [InlineData("<not-a-schema/>")]
    [InlineData("""<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="A" type="Missing"/></xs:schema>""")]
    [InlineData("""<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="A"></xs:schema>""")]
    public void ASchemaThatDoesNotCompile_IsNamed(string xsd)
    {
        var ex = Fails(() => Read("<A/>", Spec(xsd: xsd)));
        Assert.Contains("The xsd option is not a schema that compiles", ex.Message);
        Assert.Equal("xsd", ex.Location!.Field);
    }

    [Fact]
    public void Imports_AreNotFetched()
    {
        // With no resolver the imported schema is never loaded, so the type it would give is simply unknown.
        const string xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:o="urn:other">
              <xs:import namespace="urn:other" schemaLocation="http://example.invalid/other.xsd"/>
              <xs:element name="A" type="o:T"/>
            </xs:schema>
            """;
        var ex = Fails(() => Read("<A/>", Spec(xsd: xsd)));
        Assert.Contains("not a schema that compiles", ex.Message);
    }

    [Fact]
    public void Xsd_IsForReading_Only()
    {
        var ex = Fails(() => new MappingRunner().Encode(Spec(), JsonNode.Parse("""{ "a": 1 }""")));
        Assert.Contains("only applies when reading xml", ex.Message);
    }

    [Fact]
    public void ARun_XmlWithXsd_ToCsv_GivesOneRowPerEntry_EvenForOne()
    {
        var formats = new JsonObject
        {
            ["input"] = new JsonObject { ["type"] = "xml", ["options"] = new JsonObject { ["xsd"] = OrderXsd } },
            ["output"] = new JsonObject { ["type"] = "text", ["options"] = new JsonObject { ["mode"] = "delimited", ["header"] = true } },
        };
        var definition = TestSupport.Mapping($$"""
            { "formats": {{formats.ToJsonString()}},
              "target": { "type": "array", "from": "input.Order.Line[]", "map": { "sku": "@sku", "qty": "Qty" } } }
            """);

        var result = new MappingRunner().Run(definition, """<Order xmlns="urn:o"><Id>1</Id><Line sku="A"><Qty>2</Qty></Line></Order>""");

        Assert.Equal("sku,qty\nA,2", result.OutputText.Replace("\r\n", "\n").TrimEnd('\n'));
    }
}
