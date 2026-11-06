using Xunit;

namespace SpanFlow.Mapping.Tests;

/// <summary>E48-T10: keyless hash and encoding functions in mappings.</summary>
public class HashEncodingTransformTests
{
    private static string Transform(string fn, string args) =>
        TestSupport.Rule($$"""{ "target": "r", "transform": { "function": "{{fn}}", "args": {{args}} } }""");

    private static string? Run(string fn, string args, string input = "{}") =>
        TestSupport.Run(Transform(fn, args), input)["r"]?.GetValue<string>();

    [Theory]
    [InlineData("sha256", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("sha384", "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7")]
    [InlineData("sha512", "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f")]
    [InlineData("md5Legacy", "900150983cd24fb0d6963f7d28e17f72")]
    public void Digests_match_the_published_test_vectors(string fn, string expected) =>
        Assert.Equal(expected, Run(fn, """[ "'abc'" ]"""));

    [Fact]
    public void A_digest_can_be_written_as_base64_or_base64url()
    {
        Assert.Equal("ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=", Run("sha256", """[ "'abc'", "'base64'" ]"""));
        Assert.Equal("ungWv48Bz-pBQUDeXa4iI7ADYaOWF3qctBD_YfIAFa0", Run("sha256", """[ "'abc'", "'base64url'" ]"""));
    }

    [Fact]
    public void A_lookup_key_from_an_email_composes_with_lower_and_trim()
    {
        // sha256(lower(trim(email))) — the canonical example from the spec, as nested functions.
        var rule = """{ "target": "r", "transform": { "function": "sha256", "args": [ { "function": "lower", "args": [ { "function": "trim", "args": [ "input.email" ] } ] } ] } }""";
        var result = TestSupport.Run(TestSupport.Rule(rule), """{ "email": " Ada@Example.com " }""");
        Assert.Equal("b5fc85e55755f9e0d030a10ab4429b6b2944855f9a0d60077fe832becbc41d72", result["r"]!.GetValue<string>());
    }

    [Fact]
    public void Numbers_and_booleans_hash_as_their_json_text_and_a_missing_value_gives_null()
    {
        Assert.Equal(Run("sha256", """[ "'42'" ]"""), Run("sha256", """[ "input.n" ]""", """{ "n": 42 }"""));
        Assert.Equal(Run("sha256", """[ "'true'" ]"""), Run("sha256", """[ "input.b" ]""", """{ "b": true }"""));
        Assert.Null(TestSupport.Run(Transform("sha256", """[ "input.missing" ]"""), "{}")["r"]);
    }

    [Theory]
    [InlineData("base64Encode", "base64Decode", "aMOpbGxvIC8_Jg==")]
    [InlineData("base64UrlEncode", "base64UrlDecode", "aMOpbGxvIC8_Jg")]
    [InlineData("hexEncode", "hexDecode", "68c3a96c6c6f202f3f26")]
    [InlineData("urlEncode", "urlDecode", "h%C3%A9llo%20%2F%3F%26")]
    public void Encodings_round_trip_utf8_text(string encode, string decode, string encoded)
    {
        const string text = "héllo /?&";
        var actual = Run(encode, $$"""[ "input.t" ]""", $$"""{ "t": "{{text}}" }""");
        if (encode == "base64Encode") Assert.Equal(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text)), actual);
        else Assert.Equal(encoded, actual);
        Assert.Equal(text, Run(decode, $$"""[ "input.e" ]""", $$"""{ "e": "{{actual}}" }"""));
    }

    [Theory]
    [InlineData("base64Decode", "!!!")]
    [InlineData("hexDecode", "zz")]
    [InlineData("hexDecode", "fffe")] // valid hex, not valid UTF-8
    public void Decoding_something_invalid_gives_null_like_the_convert_functions(string fn, string value) =>
        Assert.Null(TestSupport.Run(Transform(fn, """[ "input.v" ]"""), $$"""{ "v": "{{value}}" }""")["r"]);

    [Fact]
    public void An_unknown_digest_encoding_is_an_error_not_a_guess() =>
        Assert.ThrowsAny<Exception>(() => Run("sha256", """[ "'abc'", "'base32'" ]"""));

    [Fact]
    public void The_registry_lists_its_names()
    {
        var names = SpanFlow.Mapping.Engine.TransformRegistry.CreateDefault().Names;
        Assert.Contains("sha256", names);
        Assert.Contains("md5Legacy", names);
        Assert.Contains("urlDecode", names);
        Assert.DoesNotContain("md5", names); // the weak one only under its honest name
    }
}
