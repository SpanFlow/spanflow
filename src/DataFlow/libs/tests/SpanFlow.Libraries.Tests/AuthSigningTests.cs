using System.Text;
using System.Text.Json.Nodes;

namespace SpanFlow.Libraries.Tests;

/// <summary>
/// SpanFlow.Connectors.Http's signing schemes against their published examples: Digest (RFC 7616), Hawk (the
/// spec's examples), OAuth 1.0 (HMAC-SHA1 and RFC 3986 encoding) and JWT bearer config validation.
/// </summary>
public class AuthSigningTests
{
    // ---------------------------------------------------------------- Digest

    [Theory]
    // RFC 7616 §3.9.1: the same request answered with MD5 and with SHA-256.
    [InlineData("MD5", "8ca523f5e9506fed4657c9700eebdbec")]
    [InlineData("SHA-256", "753927fa0e85d155564e2e272a28d1802ca10daf4496794697cf8db5856cb6c1")]
    public void Digest_matches_the_RFC_7616_example(string algorithm, string expected)
    {
        var header = DigestAuthStrategy.Header(
            "Mufasa", "Circle of Life", "GET", "/dir/index.html", "http-auth@example.org",
            "7ypf/xlj9XXwfDPEoM4URrv/xwf94BcCAzFZH4GiTo0v", "FQhe/qaU925kfnzjCev0ciny7QMkPqMAFRtzCUYo5tdS",
            algorithm, "auth", 1, "f2/wE4q74E6zIJEtWaHKaf5wv/H5QzzpXusqGemxURZJ");

        var parameters = DigestAuthStrategy.Parameters(header["Digest ".Length..]);
        Assert.Equal(expected, parameters["response"]);
        Assert.Equal("00000001", parameters["nc"]);
        Assert.Equal("FQhe/qaU925kfnzjCev0ciny7QMkPqMAFRtzCUYo5tdS", parameters["opaque"]);
    }

    // ---------------------------------------------------------------- Hawk

    private const string HawkKey = "werxhqb98rpaxn39848xrunpaw3489ruxnpa98w4rxn";

    [Fact]
    public void Hawk_matches_the_spec_example_without_a_payload()
    {
        var header = HawkAuthStrategy.Header("dh37fgj492je", HawkKey, "sha256", "GET",
            new Uri("http://example.com:8000/resource/1?b=1&a=2"), 1353832234, "j4h3g2", hash: null, ext: "some-app-ext-data");

        Assert.Contains("mac=\"6R4rV5iE+NPoym+WwjeHzjAGXUtLNIxmo1vpMofpLAE=\"", header);
        Assert.StartsWith("Hawk id=\"dh37fgj492je\", ts=\"1353832234\", nonce=\"j4h3g2\"", header);
    }

    [Fact]
    public void Hawk_matches_the_spec_example_with_a_payload_hash()
    {
        var hash = HawkAuthStrategy.PayloadHash("sha256", "text/plain", Encoding.UTF8.GetBytes("Thank you for flying Hawk"));
        Assert.Equal("Yi9LfIIFRtBEPt74PVmbTF/xVAwPn7ub15ePICfgnuY=", hash);

        var header = HawkAuthStrategy.Header("dh37fgj492je", HawkKey, "sha256", "POST",
            new Uri("http://example.com:8000/resource/1?b=1&a=2"), 1353832234, "j4h3g2", hash, ext: "some-app-ext-data");
        Assert.Contains("mac=\"aSe1DERmZuRl3pI36/9BdZmnErTw3sNzOOAUlfeKjVw=\"", header);
    }

    // ---------------------------------------------------------------- OAuth 1.0

    [Fact]
    public void OAuth1_matches_the_widely_published_signing_example()
    {
        // The example Twitter's developer docs used to explain OAuth 1.0a signing (HMAC-SHA1, query + form body).
        var oauth = new List<KeyValuePair<string, string>>
        {
            new("oauth_consumer_key", "xvz1evFS4wEEPTGEFPHBog"),
            new("oauth_nonce", "kYjzVBB8Y0ZFabxSWbWovY3uYSQ2pTgmZeNu2VS4cg"),
            new("oauth_signature_method", "HMAC-SHA1"),
            new("oauth_timestamp", "1318622958"),
            new("oauth_token", "370773112-GmHxMAgYyLbNEtIKZeRNFsMKPR9EyMZeS9weJAEb"),
            new("oauth_version", "1.0"),
        };
        var form = new List<KeyValuePair<string, string>> { new("status", "Hello Ladies + Gentlemen, a signed OAuth request!") };

        var signature = OAuth1AuthStrategy.Signature("POST", new Uri("https://api.twitter.com/1.1/statuses/update.json?include_entities=true"),
            oauth, form, "kAcSOqF21Fu85e7zjz7ZN2U4ZRhfV3WpwPAoE3Z7kBw", "LswwdoUaIvS8ltyTt5jkRh4J50vUPVVHtR2YPi5kE", "HMAC-SHA1");

        Assert.Equal("hCtSmYh+iHYCEqBWrE7C7hYmtUk=", signature);
    }

    [Theory]
    [InlineData("Ladies + Gentlemen", "Ladies%20%2B%20Gentlemen")]
    [InlineData("a!*'()b", "a%21%2A%27%28%29b")]
    [InlineData("~-._", "~-._")]
    [InlineData("☃", "%E2%98%83")]
    public void OAuth1_percent_encoding_follows_RFC_3986(string raw, string encoded) =>
        Assert.Equal(encoded, OAuth1AuthStrategy.Encode(raw));

    // ---------------------------------------------------------------- JWT bearer

    [Fact]
    public void JwtBearer_validation_catches_a_missing_or_broken_key()
    {
        var strategy = new JwtBearerAuthStrategy();
        Assert.Contains(strategy.Validate(new JsonObject { ["algorithm"] = "HS256" }), e => e.Contains("'secret'"));
        Assert.Contains(strategy.Validate(new JsonObject { ["algorithm"] = "RS256" }), e => e.Contains("'privateKey'"));
        Assert.Contains(strategy.Validate(new JsonObject { ["algorithm"] = "RS256", ["privateKey"] = "not a pem" }), e => e.Contains("could not be used"));
        Assert.Contains(strategy.Validate(new JsonObject { ["algorithm"] = "none", ["secret"] = "x" }), e => e.Contains("must be one of"));
    }

    // ---------------------------------------------------------------- registry

    [Fact]
    public void The_static_registry_knows_every_built_in_scheme()
    {
        var registry = AuthStrategyRegistry.CreateStatic();
        foreach (var type in new[] { "none", "apiKey", "basic", "bearer", "digest", "hawk", "oauth1", "jwtBearer" })
        {
            Assert.Equal(type, registry.Get(type).Type);
        }
    }
}
