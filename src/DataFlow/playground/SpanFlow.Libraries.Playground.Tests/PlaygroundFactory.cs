using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace SpanFlow.Libraries.Playground.Tests;

/// <summary>
/// The playground in memory, with settings a test may override and a clock it can move. Hosted by default (the public
/// server's rules); <c>selfHosted: true</c> for someone's own install, with the key <see cref="Key"/> and its data in a
/// temp folder. Every request arrives from <see cref="RemoteAddress"/> (a TestServer request has none) on
/// <see cref="LocalPort"/>, so the forwarded-headers and sync-port rules apply.
/// </summary>
public sealed class PlaygroundFactory : WebApplicationFactory<Program>
{
    public const string Key = "test-key";
    public const int PublicPort = 8080;
    public const int SyncPort = 8081;

    private readonly Dictionary<string, string?> _settings;

    public PlaygroundFactory(Dictionary<string, string?>? settings = null, string remoteAddress = "10.0.0.5", bool selfHosted = false)
    {
        _settings = settings ?? [];
        RemoteAddress = remoteAddress;
        SelfHosted = selfHosted;
        DataFolder = Path.Combine(Path.GetTempPath(), "playground-tests", Guid.NewGuid().ToString("N"));
    }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 10, 12, 0, 5, TimeSpan.Zero));

    public string RemoteAddress { get; }
    public bool SelfHosted { get; }

    /// <summary>Self-hosted: where its saved data and its one allowed <c>local</c> folder live.</summary>
    public string DataFolder { get; }
    public string FilesFolder => Path.Combine(DataFolder, "files");

    /// <summary>The port the next requests arrive on (<see cref="SyncPort"/> to call the sync route).</summary>
    public int LocalPort { get; set; } = PublicPort;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        if (SelfHosted)
        {
            builder.UseSetting("Playground:Mode", "SelfHosted");
            builder.UseSetting("Playground:AccessKey", Key);
            builder.UseSetting("Playground:DataFile", Path.Combine(DataFolder, "playground.json"));
            builder.UseSetting("Playground:LocalFileRoots:0", FilesFolder);
            Directory.CreateDirectory(FilesFolder);
        }
        else
        {
            builder.UseSetting("Playground:Mode", "Hosted");
            builder.UseSetting("Playground:SyncPort", SyncPort.ToString());
        }
        foreach (var (key, value) in _settings) builder.UseSetting(key, value);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IStartupFilter>(new ConnectionFilter(this));
        });
    }

    /// <summary>A client that sends the key on every call (self-hosted).</summary>
    public HttpClient CreateKeyedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Playground-Key", Key);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { if (Directory.Exists(DataFolder)) Directory.Delete(DataFolder, recursive: true); } catch (IOException) { }
    }

    private sealed class ConnectionFilter(PlaygroundFactory factory) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((http, inner) =>
            {
                http.Connection.RemoteIpAddress = IPAddress.Parse(factory.RemoteAddress);
                http.Connection.LocalPort = factory.LocalPort;
                return inner(http);
            });
            next(app);
        };
    }
}

public static class PlaygroundCalls
{
    public static Task<HttpResponseMessage> Post(this HttpClient client, string path, object body, string? forwardedFor = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (forwardedFor is not null) request.Headers.Add("X-Forwarded-For", forwardedFor);
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> PostRaw(this HttpClient client, string path, string json, string contentType = "application/json")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, contentType) };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return client.SendAsync(request);
    }

    public static async Task<JsonObject> Json(this HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();

    /// <summary>The refusal shape every panel shares: <c>{ error, reason }</c> and nothing else.</summary>
    public static async Task<string> Refusal(this HttpResponseMessage response, HttpStatusCode status)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(status == response.StatusCode, $"expected {(int)status}, got {(int)response.StatusCode}: {text}");
        var body = JsonNode.Parse(text)!.AsObject();
        Assert.Equal(2, body.Count);
        Assert.False(string.IsNullOrWhiteSpace(body["error"]!.GetValue<string>()));
        return body["reason"]!.GetValue<string>();
    }

    /// <summary>An answer that must be 200, with its body.</summary>
    public static async Task<JsonObject> Ok(this HttpClient client, string path, object body)
    {
        var response = await client.Post(path, body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {text}");
        return JsonNode.Parse(text)!.AsObject();
    }

    public static readonly object Expression = new { expression = "input.a > 1", data = new { input = new { a = 2 } } };

    public const string Demo = "https://demo-api.internal";
}
