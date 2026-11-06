using System.Text.Json.Nodes;
using SpanFlow.Mapping.Core;
using Xunit;

namespace SpanFlow.Mapping.Tests;

public class ConversionTests
{
    // RFC 9562 Appendix A test vectors — all made at 2022-02-22T19:22:22Z.
    private const string V1 = "c232ab00-9414-11ec-b3c8-9f6bdeced846";
    private const string V4 = "919108f7-52d1-4320-9bac-f847db4148a8";
    private const string V6 = "1ec9414c-232a-6b00-b3c8-9f6bdeced846";
    private const string V7 = "017f22e2-79b0-7cc3-98c4-dc0c0c07398f";
    private const string Nil = "00000000-0000-0000-0000-000000000000";

    private static JsonNode? Eval(string fn, string args, string input = "{}") =>
        TestSupport.Run(
            TestSupport.Rule($$"""{ "target": "r", "transform": { "function": "{{fn}}", "args": {{args}} } }"""),
            input)["r"];

    /// <summary>The result as JSON text, so strings, numbers and null compare in one theory.</summary>
    private static string Json(string fn, string args, string input = "{}") => Eval(fn, args, input)?.ToJsonString() ?? "null";

    // ---- numbers -----------------------------------------------------------

    [Theory]
    [InlineData("convert", """[ "'12'", "'int'" ]""", "12")]
    [InlineData("toInt", """[ 12.7 ]""", "12")]                    // truncates by default
    [InlineData("toInt", """[ 12.5, "'round'" ]""", "13")]
    [InlineData("toInt", """[ -12.5, "'floor'" ]""", "-13")]
    [InlineData("toInt", """[ 12.1, "'ceil'" ]""", "13")]
    [InlineData("toInt", """[ "'0x1F'" ]""", "31")]
    [InlineData("toInt", """[ true ]""", "1")]
    [InlineData("toInt", """[ "'abc'" ]""", "null")]               // can't convert → null
    [InlineData("toInt", """[ 3000000000 ]""", "null")]            // out of int range
    [InlineData("toLong", """[ 3000000000 ]""", "3000000000")]
    [InlineData("convert", """[ 300, "'byte'" ]""", "null")]
    [InlineData("convert", """[ "'-7'", "'short'" ]""", "-7")]
    [InlineData("toDouble", """[ "'1.5'" ]""", "1.5")]
#if !NETFRAMEWORK // .NET Framework's double.Parse is not correctly rounded: 1e300 parses one ulp high
    [InlineData("toDouble", """[ "'1e300'" ]""", "1E+300")]
#endif
    [InlineData("toFloat", """[ "'1.25'" ]""", "1.25")]
    [InlineData("toDecimal", """[ "'1e3'" ]""", "1000")]
    [InlineData("toDecimal", """[ 7 ]""", "7")]
    [InlineData("toDecimal", """[ "'1,5'" ]""", "null")]           // no thousands separators
    public void Numbers(string fn, string args, string expected) => Assert.Equal(expected, Json(fn, args));

    [Theory]
    [InlineData("""[ 1234.5, "'N2'" ]""", "\"1,234.50\"")]
    [InlineData("""[ 255, "'X'" ]""", "\"FF\"")]
    [InlineData("""[ 3.10 ]""", "\"3.10\"")]
    [InlineData("""[ true ]""", "\"true\"")]
    [InlineData("""[ "input.obj" ]""", "\"{\\u0022a\\u0022:1}\"")]
    public void ToString_FormatsEveryType(string args, string expected)
        => Assert.Equal(expected, Json("toString", args, """{ "obj": { "a": 1 } }"""));

    [Theory]
    [InlineData("""[ "'yes'" ]""", "true")]
    [InlineData("""[ "'OFF'" ]""", "false")]
    [InlineData("""[ 0 ]""", "false")]
    [InlineData("""[ 2 ]""", "true")]
    [InlineData("""[ "'maybe'" ]""", "null")]
    public void ToBool(string args, string expected) => Assert.Equal(expected, Json("toBool", args));

    // ---- dates & epochs ----------------------------------------------------

    [Theory]
    [InlineData("""[ "'2024-01-15T10:30:00Z'", "'dd/MM/yyyy HH:mm'" ]""", "15/01/2024 10:30")]
    [InlineData("""[ "'15/01/2024'", "'iso'", "'dd/MM/yyyy'" ]""", "2024-01-15T00:00:00Z")]
    [InlineData("""[ "'2024-01-15 10:30'", "'date'", "'MM/dd/yyyy|yyyy-MM-dd HH:mm'" ]""", "2024-01-15")]
    [InlineData("""[ "'20240115'", "'iso'", "'yyyyMMdd'" ]""", "2024-01-15T00:00:00Z")]
    [InlineData("""[ "'2024-01-15T10:30:00Z'", "'yyyy-MM-dd HH:mm'", null, "'+05:00'" ]""", "2024-01-15 15:30")]
    [InlineData("""[ "'2024-01-15T10:30:00+05:00'", "'iso'" ]""", "2024-01-15T10:30:00+05:00")]
    [InlineData("""[ "'2024-01-15T10:30:00+05:00'", "'iso'", null, "'utc'" ]""", "2024-01-15T05:30:00Z")]
    [InlineData("""[ "'2024-01-15T10:30:00Z'", "'rfc1123'" ]""", "Mon, 15 Jan 2024 10:30:00 GMT")]
    [InlineData("""[ 1705314600, "'datetime'" ]""", "2024-01-15 10:30:00")]  // epoch number in
    public void FormatDate(string args, string expected) => Assert.Equal(expected, Eval("formatDate", args)!.GetValue<string>());

    [Fact]
    public void FormatDate_UnreadableDate_IsNull() => Assert.Null(Eval("formatDate", """[ "'not a date'", "'iso'" ]"""));

    [Theory]
    [InlineData("""[ "'2024-01-15T10:30:00Z'" ]""", 1705314600L)]
    [InlineData("""[ "'2024-01-15T10:30:00Z'", "'ms'" ]""", 1705314600000L)]
    [InlineData("""[ "'2024-01-15T10:30:00.123456Z'", "'us'" ]""", 1705314600123456L)]
    [InlineData("""[ "'2024-01-15T10:30:00Z'", "'ns'" ]""", 1705314600000000000L)]
    [InlineData("""[ "'1969-12-31T23:59:59.5Z'", "'s'" ]""", -1L)]   // rounds down, not toward zero
    [InlineData("""[ "'15.01.2024'", "'s'", "'dd.MM.yyyy'" ]""", 1705276800L)]
    [InlineData("""[ "'0001-01-01T00:00:00Z'", "'ticks'" ]""", 0L)]
    [InlineData("""[ 1705314600000, "'s'" ]""", 1705314600L)]          // ms in (auto-detected), s out
    public void ToEpoch(string args, long expected) => Assert.Equal(expected, Eval("toEpoch", args)!.GetValue<long>());

    [Theory]
    [InlineData("""[ 1705314600 ]""", "2024-01-15T10:30:00Z")]
    [InlineData("""[ 1705314600123 ]""", "2024-01-15T10:30:00.123Z")]          // auto: ms
    [InlineData("""[ 1705314600123456 ]""", "2024-01-15T10:30:00.123456Z")]    // auto: us
    [InlineData("""[ 1705314600123456789 ]""", "2024-01-15T10:30:00.1234567Z")] // auto: ns (100 ns precision)
    [InlineData("""[ "'1705314600'", "'s'", "'date'" ]""", "2024-01-15")]
    [InlineData("""[ 60, "'s'" ]""", "1970-01-01T00:01:00Z")]
    [InlineData("""[ 1705314600, "'s'", "'HH:mm'", "'Asia/Karachi'" ]""", "15:30")]
    public void FromEpoch(string args, string expected) => Assert.Equal(expected, Eval("fromEpoch", args)!.GetValue<string>());

    [Theory]
    [InlineData("""[ 1705314600, "'s'", "'ms'" ]""", 1705314600000L)]
    [InlineData("""[ 1705314600123, "'ms'", "'s'" ]""", 1705314600L)]
    [InlineData("""[ 1705314600, "'s'", "'ns'" ]""", 1705314600000000000L)]
    [InlineData("""[ 1705314600, "'s'", "'ticks'" ]""", 638409114000000000L)]
    public void ConvertEpoch(string args, long expected) => Assert.Equal(expected, Eval("convertEpoch", args)!.GetValue<long>());

    [Fact]
    public void Convert_CoversDateTypes()
    {
        Assert.Equal("2024-01-15", Eval("convert", """[ "'2024-01-15T10:30:00Z'", "'date'" ]""")!.GetValue<string>());
        Assert.Equal("10:30:00", Eval("toTime", """[ "'2024-01-15T10:30:00Z'" ]""")!.GetValue<string>());
        Assert.Equal("2024-01-15T10:30:00Z", Eval("toDateTime", """[ "'2024-01-15 10:30:00'" ]""")!.GetValue<string>());
        Assert.Equal(1705314600000L, Eval("convert", """[ "'2024-01-15T10:30:00Z'", "'epochMs'" ]""")!.GetValue<long>());
        Assert.Equal("2024-01-15T10:30:00Z", Eval("convert", """[ 1705314600000, "'datetime'", null, "'ms'" ]""")!.GetValue<string>());
    }

    [Fact]
    public void Epoch_UnknownUnit_Throws()
    {
        Assert.Throws<MappingException>(() => Eval("toEpoch", """[ "'2024-01-15'", "'fortnights'" ]"""));
        Assert.Throws<MappingException>(() => Eval("fromEpoch", """[ 1, "'fortnights'" ]"""));
    }

    [Fact]
    public void UnknownTimeZone_Throws()
        => Assert.Throws<MappingException>(() => Eval("formatDate", """[ "'2024-01-15'", "'iso'", null, "'Mars/Olympus'" ]"""));

    // ---- checkers ----------------------------------------------------------

    [Theory]
    [InlineData("'12'", "int", true)]
    [InlineData("'12.5'", "int", false)]   // strict: no rounding
    [InlineData("12.5", "double", true)]
    [InlineData("'abc'", "number", false)]
    [InlineData("3000000000", "int", false)]
    [InlineData("3000000000", "long", true)]
    [InlineData("'x'", "string", true)]
    [InlineData("5", "string", false)]
    [InlineData("'true'", "bool", true)]
    [InlineData("2", "bool", false)]       // strict: only 0/1
    [InlineData("'2024-01-15'", "datetime", true)]
    [InlineData("1705314600", "datetime", false)]  // a number isn't a date...
    [InlineData("1705314600", "epoch", true)]      // ...but it is an epoch
    [InlineData("'2024-01-15'", "epoch", false)]
    [InlineData("'" + V7 + "'", "uuid", true)]
    [InlineData("'not-a-uuid'", "uuid", false)]
    [InlineData("null", "null", true)]
    [InlineData("'x'", "null", false)]
    [InlineData("input.obj", "object", true)]
    [InlineData("input.list", "array", true)]
    public void IsType(string value, string type, bool expected)
        => Assert.Equal(expected, Eval("isType", $$"""[ {{Quote(value)}}, "'{{type}}'" ]""", """{ "obj": {}, "list": [] }""")!.GetValue<bool>());

    [Fact]
    public void IsType_UnknownType_Throws()
        => Assert.Throws<MappingException>(() => Eval("isType", """[ 1, "'quaternion'" ]"""));

    [Theory]
    [InlineData("5", "integer")]
    [InlineData("5.5", "number")]
    [InlineData("'5'", "string")]
    [InlineData("true", "boolean")]
    [InlineData("null", "null")]
    [InlineData("input.obj", "object")]
    public void TypeOf(string value, string expected)
        => Assert.Equal(expected, Eval("typeOf", $"[ {Quote(value)} ]", """{ "obj": {} }""")!.GetValue<string>());

    [Theory]
    [InlineData("'" + V4 + "'", "uuid")]
    [InlineData("'42'", "integer")]
    [InlineData("'4.2'", "number")]
    [InlineData("'TRUE'", "boolean")]
    [InlineData("'2024-01-15T10:30:00Z'", "datetime")]
    [InlineData("'hello'", "string")]
    [InlineData("42", "integer")]
    public void DetectType(string value, string expected)
        => Assert.Equal(expected, Eval("detectType", $"[ {Quote(value)} ]")!.GetValue<string>());

    // ---- UUID --------------------------------------------------------------

    [Theory]
    [InlineData(V1, 1)]
    [InlineData(V4, 4)]
    [InlineData(V6, 6)]
    [InlineData(V7, 7)]
    [InlineData("5df41881-3aed-3515-88a7-2f4a814cf09e", 3)]
    [InlineData("2ed6657d-e927-568b-95e1-2665a8aea6a2", 5)]
    [InlineData(Nil, 0)]
    [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff", 15)]
    public void UuidVersion(string uuid, int expected)
        => Assert.Equal(expected, Eval("uuidVersion", $"[ \"'{uuid}'\" ]")!.GetValue<int>());

    [Fact]
    public void UuidVersion_NotAnRfcUuid_IsNull()
        => Assert.Null(Eval("uuidVersion", """[ "'00000000-0000-4000-0000-000000000001'" ]""")); // variant bits 00

    [Theory]
    [InlineData(V4, null, true)]
    [InlineData(V4, "4", true)]
    [InlineData(V4, "v4", true)]
    [InlineData(V4, "7", false)]
    [InlineData(V7, "7", true)]
    [InlineData(V1, "1", true)]
    [InlineData(V6, "6", true)]
    [InlineData(Nil, "nil", true)]
    [InlineData(Nil, "4", false)]
    [InlineData("{" + V4 + "}", "4", true)]
    [InlineData("urn:uuid:" + V4, "any", true)]
    [InlineData("919108F752D143209BACF847DB4148A8", "4", true)]
    [InlineData("not-a-uuid", null, false)]
    [InlineData("abcdefghijklmnopqrstuv", null, false)]  // 22 chars of base64 is not a UUID here
    public void IsUuid(string value, string? version, bool expected)
    {
        var args = version is null ? $"[ \"'{value}'\" ]" : $"[ \"'{value}'\", \"'{version}'\" ]";
        Assert.Equal(expected, Eval("isUuid", args)!.GetValue<bool>());
    }

    [Fact]
    public void IsUuid_BadVersionSpec_Throws()
        => Assert.Throws<MappingException>(() => Eval("isUuid", $"[ \"'{V4}'\", \"'v99'\" ]"));

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    public void NewUuid_MakesTheRequestedVersion(int version)
    {
        var uuid = Eval("newUuid", $"[ {version} ]")!.GetValue<string>();
        Assert.True(Eval("isUuid", $"[ \"'{uuid}'\", {version} ]")!.GetValue<bool>());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(7)]
    public void NewUuid_TimeBased_CarriesTheCurrentTime(int version)
    {
        var uuid = Eval("newUuid", $"[ {version} ]")!.GetValue<string>();
        var created = DateTimeOffset.Parse(Eval("uuidTimestamp", $"[ \"'{uuid}'\" ]")!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(created, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public void NewUuid_NameBased_MatchesRfcVectors()
    {
        Assert.Equal("5df41881-3aed-3515-88a7-2f4a814cf09e", Eval("newUuid", """[ 3, "'dns'", "'www.example.com'" ]""")!.GetValue<string>());
        Assert.Equal("2ed6657d-e927-568b-95e1-2665a8aea6a2", Eval("newUuid", """[ 5, "'dns'", "'www.example.com'" ]""")!.GetValue<string>());
    }

    [Fact]
    public void NewUuid_V4ByDefault_AndFormatted()
    {
        var uuid = Eval("newUuid", """[ null, null, null, "'upper:n'" ]""")!.GetValue<string>();
        Assert.Matches("^[0-9A-F]{32}$", uuid);
        Assert.Equal(4, Eval("uuidVersion", $"[ \"'{uuid}'\" ]")!.GetValue<int>());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    public void NewUuid_UnsupportedVersion_Throws(int version)
        => Assert.Throws<MappingException>(() => Eval("newUuid", $"[ {version} ]"));

    [Theory]
    [InlineData(V1)]
    [InlineData(V6)]
    [InlineData(V7)]
    public void UuidTimestamp_ReadsRfcVectors(string uuid)
        => Assert.Equal("2022-02-22T19:22:22Z", Eval("uuidTimestamp", $"[ \"'{uuid}'\" ]")!.GetValue<string>());

    [Fact]
    public void UuidTimestamp_NotTimeBased_IsNull() => Assert.Null(Eval("uuidTimestamp", $"[ \"'{V4}'\" ]"));

    [Theory]
    [InlineData("919108F752D143209BACF847DB4148A8", null, V4)]
    [InlineData(V4, "n", "919108f752d143209bacf847db4148a8")]
    [InlineData(V4, "upper", "919108F7-52D1-4320-9BAC-F847DB4148A8")]
    [InlineData(V4, "b", "{" + V4 + "}")]
    [InlineData(V4, "urn", "urn:uuid:" + V4)]
    [InlineData(V4, "base64", "kZEI91LRQyCbrPhH20FIqA==")]
    [InlineData(V4, "base64url", "kZEI91LRQyCbrPhH20FIqA")]
    public void ToUuid_Layouts(string value, string? format, string expected)
    {
        var args = format is null ? $"[ \"'{value}'\" ]" : $"[ \"'{value}'\", \"'{format}'\" ]";
        Assert.Equal(expected, Eval("toUuid", args)!.GetValue<string>());
    }

    [Fact]
    public void ToUuid_ReadsBase64_OnlyWhenAsked()
    {
        Assert.Equal(V4, Eval("toUuid", """[ "'kZEI91LRQyCbrPhH20FIqA=='", null, "'base64'" ]""")!.GetValue<string>());
        Assert.Equal(V4, Eval("convert", """[ "'kZEI91LRQyCbrPhH20FIqA'", "'uuid'", null, "'base64'" ]""")!.GetValue<string>());
        Assert.Null(Eval("toUuid", """[ "'kZEI91LRQyCbrPhH20FIqA=='" ]"""));
    }

    // ---- composition -------------------------------------------------------

    [Fact]
    public void Convert_UnknownType_Throws()
        => Assert.Throws<MappingException>(() => Eval("convert", """[ 1, "'quaternion'" ]"""));

    [Fact]
    public void Convert_Null_IsNull() => Assert.Null(Eval("toInt", """[ "input.missing" ]"""));

    [Fact]
    public void Conversions_ChainInsideForeach()
    {
        var mapping = """
        {
          "target": { "type": "array", "from": "input.events[]", "map": {
            "id":      { "function": "toUuid", "args": [ "id", "'upper'" ] },
            "at":      { "function": "fromEpoch", "args": [ "ts", "'ms'", "'yyyy-MM-dd'" ] },
            "amount":  { "function": "toDecimal", "args": [ "amount" ] },
            "valid":   { "function": "isUuid", "args": [ "id", "'7'" ] }
          } }
        }
        """;
        var input = $$"""{ "events": [ { "id": "{{V7}}", "ts": 1705314600000, "amount": "12.50" } ] }""";

        var row = TestSupport.Run(mapping, input)[0]!;

        Assert.Equal(V7.ToUpperInvariant(), row["id"]!.GetValue<string>());
        Assert.Equal("2024-01-15", row["at"]!.GetValue<string>());
        Assert.Equal(12.50m, row["amount"]!.GetValue<decimal>());
        Assert.True(row["valid"]!.GetValue<bool>());
    }

    // Literal strings in the DSL are single-quoted inside a JSON string; paths,
    // numbers, booleans and null are written as-is.
    private static string Quote(string value) => value.StartsWith('\'') || value.StartsWith("input.", StringComparison.Ordinal)
        ? $"\"{value}\""
        : value;
}
