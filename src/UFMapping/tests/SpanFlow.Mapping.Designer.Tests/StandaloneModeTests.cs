using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using SpanFlow.Mapping.Designer.Api;
using Xunit;

namespace SpanFlow.Mapping.Designer.Tests;

/// <summary>
/// The standalone designer: no sign-in, no Platform. In a Platform build it is chosen with
/// Platform:Enabled=false; a build without Platform.Client has nothing else.
/// </summary>
public sealed class StandaloneModeTests : IDisposable
{
    private readonly DesignerFactory _factory = new(new Dictionary<string, string?>
    {
        ["Platform:Enabled"] = "false",
        // Pointing nowhere on purpose: the standalone mode must not touch Platform at all.
        ["Platform:BaseUrl"] = "http://127.0.0.1:9",
    });

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Execute_runs_a_mapping_without_any_credentials()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/mapping/execute", Samples.Json(Samples.ExecuteBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("Ada Lovelace", (string?)body["fullName"]);
        Assert.Equal("London", (string?)body["city"]);
    }

    [Fact]
    public async Task Execute_reports_a_broken_mapping_as_400_with_Error()
    {
        using var client = _factory.CreateClient();
        const string broken = """
            { "mapping": { "target": { "type": "object", "mappings": [ { "target": "x", "transform": { "function": "noSuchFunction", "args": [] } } ] } },
              "inputObject": {} }
            """;

        var response = await client.PostAsync("/api/mapping/execute", Samples.Json(broken));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.False(string.IsNullOrWhiteSpace((string?)body["error"]));
    }

    [Fact]
    public async Task Trace_runs_each_probe_and_reports_a_failing_one_in_place()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/mapping/trace", Samples.Json(Samples.TraceBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray();
        Assert.Equal(2, results.Count);
        Assert.Equal("Ada", (string?)results[0]!["output"]!["v"]);
        Assert.NotNull(results[1]!["error"]);
    }

    [Fact]
    public async Task Run_turns_raw_xml_into_csv_and_keeps_the_json_in_the_middle()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/mapping/run", Samples.Json(Samples.XmlToCsvRunBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("sku,qty\nA,2\nB,1", (string?)body["output"]);
        Assert.Equal("text/plain", (string?)body["contentType"]);
        Assert.Equal("xml", (string?)body["inputFormat"]);
        Assert.Equal("A", (string?)body["decoded"]!["Order"]!["Line"]![0]!["@sku"]);
        Assert.Equal("1", (string?)body["mapped"]![1]!["qty"]);
    }

    [Fact]
    public async Task Run_without_formats_is_json_in_json_out()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/mapping/run", Samples.Json(Samples.ExecuteBody));

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("Ada Lovelace", (string?)body["mapped"]!["fullName"]);
        Assert.Equal("""{"fullName":"Ada Lovelace","city":"London"}""", (string?)body["output"]);
        Assert.Equal("application/json", (string?)body["contentType"]);
    }

    [Fact]
    public async Task Decode_shows_the_sample_as_json_and_a_bad_sample_says_where()
    {
        using var client = _factory.CreateClient();
        const string layout = """{ "type": "positional", "options": { "layout": { "fields": [ { "name": "country", "length": 2 }, { "name": "account", "length": 13, "type": "numeric" } ] } } }""";

        var ok = JsonNode.Parse(await (await client.PostAsync("/api/mapping/decode", Samples.Json($$"""{ "format": {{layout}}, "rawInput": "PK0001111223344" }"""))).Content.ReadAsStringAsync())!;
        Assert.Equal("0001111223344", (string?)ok["decoded"]!["account"]);

        var bad = await client.PostAsync("/api/mapping/decode", Samples.Json($$"""{ "format": {{layout}}, "rawInput": "PK00011" }"""));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var problem = JsonNode.Parse(await bad.Content.ReadAsStringAsync())!;
        Assert.Equal("positional", (string?)problem["format"]);
        Assert.Equal("decode", (string?)problem["direction"]);
        Assert.Equal("account", (string?)problem["location"]!["field"]);
        Assert.Equal(2, (int?)problem["location"]!["offset"]);
    }

    [Fact]
    public async Task Formats_lists_every_codec_with_its_options()
    {
        using var client = _factory.CreateClient();

        var formats = JsonNode.Parse(await client.GetStringAsync("/api/mapping/formats"))!.AsArray();

        Assert.Equal(["edi", "json", "positional", "text", "xml"], formats.Select(f => (string)f!["type"]!).ToArray());
        var xml = formats.Single(f => (string?)f!["type"] == "xml")!;
        var arrays = xml["options"]!.AsArray().Single(o => (string?)o!["name"] == "arrays")!;
        Assert.Equal("stringList", (string?)arrays["kind"]);
        Assert.Equal("decodeOnly", (string?)arrays["use"]);
        Assert.Contains(xml["options"]!.AsArray(), o => (string?)o!["name"] == "encoding");
    }

    [Fact]
    public async Task Edi_guides_list_the_bundled_ones()
    {
        using var client = _factory.CreateClient();

        var guides = JsonNode.Parse(await client.GetStringAsync("/api/mapping/formats/edi-guides"))!.AsArray();

        Assert.Equal(7, guides.Count);
        var po = guides.Single(g => (string?)g!["name"] == "x12-850")!;
        Assert.Equal("850", (string?)po["message"]);
        Assert.Equal("Purchase order", (string?)po["title"]);
    }

    [Fact]
    public async Task Starters_list_the_bundled_layouts_and_one_decodes_a_card_message()
    {
        using var client = _factory.CreateClient();

        var starters = JsonNode.Parse(await client.GetStringAsync("/api/mapping/formats/starters"))!.AsArray();

        Assert.Equal(["iso8583-1987", "iso8583-1987-binary", "pk-account"], starters.Select(s => (string)s!["name"]!).ToArray());
        var iso = starters[0]!;
        Assert.Equal("positional", (string?)iso["format"]);

        // The starter's options are a working input format as they come.
        var decode = new JsonObject
        {
            ["format"] = new JsonObject { ["type"] = "positional", ["options"] = iso["options"]!.DeepClone() },
            ["rawInput"] = "0200" + "7020000000808000" + "16" + "4111111111111111" + "000000" + "000000010000" + "123456" + "TERM0001" + "840",
        };
        var response = await client.PostAsync("/api/mapping/decode", Samples.Json(decode.ToJsonString()));
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("4111111111111111", (string?)body["decoded"]!["pan"]);
        Assert.Equal("840", (string?)body["decoded"]!["currencyCodeTransaction"]);
    }

    [Fact]
    public async Task Trace_decodes_a_raw_sample_once_for_every_probe()
    {
        using var client = _factory.CreateClient();
        const string body = """
            { "rawInput": "PK0001111223344",
              "inputFormat": { "type": "positional", "options": { "layout": { "fields": [ { "name": "country", "length": 2 }, { "name": "account", "length": 13, "type": "numeric" } ] } } },
              "probes": [ { "id": "n1", "mapping": { "target": { "type": "object", "mappings": [ { "target": "v", "from": "input.country" } ] } } },
                          { "id": "n2", "mapping": { "target": { "type": "object", "mappings": [ { "target": "v", "from": "input.account" } ] } } } ] }
            """;

        var results = JsonNode.Parse(await (await client.PostAsync("/api/mapping/trace", Samples.Json(body))).Content.ReadAsStringAsync())!.AsArray();

        Assert.Equal("PK", (string?)results[0]!["output"]!["v"]);
        Assert.Equal("0001111223344", (string?)results[1]!["output"]!["v"]);
    }

    [Fact]
    public async Task Info_says_standalone()
    {
        using var client = _factory.CreateClient();

        var body = JsonNode.Parse(await client.GetStringAsync("/api/designer/info"))!;

        Assert.Equal("standalone", (string?)body["mode"]);
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/refresh")]
    public async Task The_Platform_sign_in_proxy_is_not_there(string path)
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(path, Samples.Json("""{"username":"a","password":"b"}"""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/readyz")]
    public async Task Probes_answer_healthy(string path)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", (string?)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["status"]);
    }
}

public sealed class DesignerModeResolutionTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void Enabled_false_is_standalone_whatever_else_is_set() =>
        Assert.Equal(DesignerMode.Standalone,
            DesignerModes.Resolve(Config(("Platform:Enabled", "false"), ("Platform:BaseUrl", "http://platform"))));

    [Fact]
    public void No_Platform_section_is_standalone() =>
        Assert.Equal(DesignerMode.Standalone, DesignerModes.Resolve(Config()));

    [Fact]
    public void A_Platform_BaseUrl_means_Platform_only_when_the_build_has_it() =>
        Assert.Equal(
            DesignerModes.PlatformCompiledIn ? DesignerMode.Platform : DesignerMode.Standalone,
            DesignerModes.Resolve(Config(("Platform:BaseUrl", "http://platform"))));

    [Fact]
    public void Enabled_true_without_the_integration_refuses_to_start()
    {
        if (DesignerModes.PlatformCompiledIn)
        {
            Assert.Equal(DesignerMode.Platform, DesignerModes.Resolve(Config(("Platform:Enabled", "true"))));
            return;
        }

        Assert.Throws<InvalidOperationException>(() => DesignerModes.Resolve(Config(("Platform:Enabled", "true"))));
    }
}
