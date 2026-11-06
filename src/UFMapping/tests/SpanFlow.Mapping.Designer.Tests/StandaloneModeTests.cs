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
