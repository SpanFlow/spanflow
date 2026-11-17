using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using static SpanFlow.Libraries.Playground.Tests.PlaygroundCalls;

namespace SpanFlow.Libraries.Playground.Tests;

/// <summary>
/// D71.2 to D71.4, D71.11 to D71.13: the hosted playground reaches no network, no file and no database but its own
/// demo; the self-hosted one does the real thing only for whoever holds its key. The container's internal-only network
/// and read-only file system are the second wall (compose); these tests are the first.
/// </summary>
public class IsolationTests
{
    /// <summary>Counts every outgoing request .NET's network handler starts.</summary>
    private sealed class OutboundRequests : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly List<IDisposable> _subscriptions = [];
        public List<string> Seen { get; } = [];

        public OutboundRequests() => _subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "HttpHandlerDiagnosticListener") lock (_subscriptions) _subscriptions.Add(listener.Subscribe(this));
        }

        public void OnNext(KeyValuePair<string, object?> evt)
        {
            if (evt.Key.EndsWith(".Start", StringComparison.Ordinal) || evt.Key == "System.Net.Http.Request") lock (Seen) Seen.Add(evt.Key);
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }

        public void Dispose()
        {
            lock (_subscriptions) foreach (var s in _subscriptions) s.Dispose();
        }
    }

    private static readonly Dictionary<string, string?> Roomy = new()
    {
        ["Playground:Limits:Http:PerMinute"] = "1000", ["Playground:Limits:Http:PerHour"] = "1000",
        ["Playground:Limits:Database:PerMinute"] = "1000", ["Playground:Limits:Database:PerHour"] = "1000",
        ["Playground:Limits:Files:PerMinute"] = "1000", ["Playground:Limits:Files:PerHour"] = "1000",
    };

    // ---- hosted: nothing leaves -------------------------------------------------------------------------------------

    [Fact]
    public async Task Hosted_sends_reach_only_the_in_process_demo_API_and_never_the_network()
    {
        // The probe itself must see a real request, or a quiet result would prove nothing.
        using (var check = new OutboundRequests())
        {
            using var real = new HttpClient(new SocketsHttpHandler()) { Timeout = TimeSpan.FromMilliseconds(300) };
            try { await real.GetAsync("http://127.0.0.1:9/"); } catch { /* nothing listens there; the attempt is what counts */ }
            Assert.NotEmpty(check.Seen);
        }

        await using var factory = new PlaygroundFactory(Roomy);
        var client = factory.CreateClient();
        using var outbound = new OutboundRequests();

        // The demo API: answered in process.
        Assert.Equal(200, (await client.Ok("/api/http/send", new { url = Demo + "/orders", auth = new { type = "bearer", token = "demo-token-abc" } }))["status"]!.GetValue<int>());
        // A token scheme whose token URL is somewhere else: refused before anything is sent.
        var token = await client.Post("/api/http/send", new { url = Demo + "/echo", auth = new { type = "oauth2ClientCredentials", tokenUrl = "http://10.255.255.1/token", clientId = "x", clientSecret = "y" } });
        Assert.Equal(HttpStatusCode.BadRequest, token.StatusCode);
        foreach (var url in new[] { "http://10.255.255.1:81/x", "http://localhost:5432/", "http://169.254.169.254/latest/meta-data/", "https://example.com/" })
        {
            var response = await client.Post("/api/http/send", new { url });
            Assert.Equal("invalid", await response.Refusal(HttpStatusCode.BadRequest));
        }

        Assert.Empty(outbound.Seen);
    }

    [Fact]
    public async Task Hosted_queries_only_the_configured_connections_never_a_string_from_the_request()
    {
        await using var factory = new PlaygroundFactory(Roomy);
        var client = factory.CreateClient();

        var response = await client.Post("/api/database/query", new { connectionId = "Host=10.0.0.1;Database=x;Username=u;Password=p", sql = "SELECT 1" });
        Assert.Equal("invalid", await response.Refusal(HttpStatusCode.NotFound));
        var anyField = await client.Post("/api/database/query", new { connectionString = "Data Source=/etc/x.db", sql = "SELECT 1 AS one" });
        Assert.Equal(1, (await anyField.Json())["rows"]![0]!["one"]!.GetValue<long>()); // the field is ignored: the sample ran
    }

    [Fact]
    public async Task Hosted_has_no_stores_and_no_way_to_add_connections_or_stores()
    {
        await using var factory = new PlaygroundFactory(Roomy);
        var client = factory.CreateClient();

        Assert.Equal("invalid", await (await client.Post("/api/files/convert", new { from = "csv", to = "json", source = new { storeId = "x", path = "a.csv" } })).Refusal(HttpStatusCode.BadRequest));
        Assert.Equal("invalid", await (await client.Post("/api/files/convert", new { from = "csv", to = "json", content = "a\n1", target = new { storeId = "x", path = "a.json" } })).Refusal(HttpStatusCode.BadRequest));
        Assert.Equal("invalid", await (await client.Post("/api/files/convert", new { from = "xlsx", to = "json", content = "UEsDBA==" })).Refusal(HttpStatusCode.BadRequest));

        foreach (var path in new[] { "/api/database/connections", "/api/files/stores", "/api/saved/collections", "/api/saved/flows" })
        {
            var response = await client.Post(path, new { name = "x", provider = "postgres", connectionString = "Host=x" });
            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"{path}: {(int)response.StatusCode}");
        }
        Assert.Empty(await client.GetFromJsonAsync<JsonArray>("/api/files/stores") ?? []);
    }

    [Fact]
    public async Task The_sync_port_answers_only_sync_and_the_public_port_never_does()
    {
        await using var factory = new PlaygroundFactory(Roomy);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.Post("/internal/sync", new { ackThrough = 0 })).StatusCode);

        factory.LocalPort = PlaygroundFactory.SyncPort;
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/info")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.Post("/internal/sync", new { ackThrough = 0 })).StatusCode);
    }

    [Fact]
    public async Task Runs_are_recorded_with_secrets_masked_and_handed_over_until_acknowledged()
    {
        await using var factory = new PlaygroundFactory(Roomy);
        var client = factory.CreateClient();
        await client.Ok("/api/http/send", new
        {
            url = Demo + "/echo",
            headers = new[] { new { key = "Authorization", value = "Bearer leaked" }, new { key = "X-Trace", value = "visible" } },
            auth = new { type = "basic", username = "ada", password = "analytical-engine" },
        });
        await client.Post("/api/expressions/evaluate", new { expression = "inptu.a" });

        factory.LocalPort = PlaygroundFactory.SyncPort;
        var first = await (await client.Post("/internal/sync", new { ackThrough = 0 })).Json();
        var calls = first["calls"]!.AsArray();
        Assert.Equal(2, calls.Count);
        Assert.Equal("/http/send", calls[0]!["path"]!.GetValue<string>());
        Assert.Equal("ok", calls[0]!["outcome"]!.GetValue<string>());
        Assert.Equal("failed", calls[1]!["outcome"]!.GetValue<string>());
        var run = calls[0]!["run"]!.GetValue<string>();
        Assert.DoesNotContain("analytical-engine", run);
        Assert.DoesNotContain("leaked", run);
        Assert.Contains("visible", run);
        Assert.Equal("10.0.0.5", calls[0]!["clientAddress"]!.GetValue<string>());

        var instance = first["instance"]!.GetValue<string>();
        var second = await (await client.Post("/internal/sync", new { ackThrough = calls[1]!["seq"]!.GetValue<long>(), instance })).Json();
        Assert.Empty(second["calls"]!.AsArray());

        // An acknowledgement for another instance (before a restart) drops nothing.
        await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression);
        factory.LocalPort = PlaygroundFactory.PublicPort;
        await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression);
        factory.LocalPort = PlaygroundFactory.SyncPort;
        var foreign = await (await client.Post("/internal/sync", new { ackThrough = 10_000, instance = "some-old-process" })).Json();
        Assert.NotEmpty(foreign["calls"]!.AsArray());
    }

    /// <summary>
    /// Found running it for real (2026-11-10): the answer of a send repeats the signed request, so its Authorization
    /// header (a Basic password in base64, a bearer token) and the wire text were kept in the Studio unmasked.
    /// </summary>
    [Fact]
    public async Task No_secret_is_kept_in_the_request_or_in_the_answer()
    {
        await using var factory = new PlaygroundFactory(Roomy);
        var client = factory.CreateClient();
        await client.Ok("/api/http/send", new { url = Demo + "/echo/a", auth = new { type = "basic", username = "ada", password = "analytical-engine" } });
        await client.Ok("/api/http/send", new { url = Demo + "/orders", auth = new { type = "bearer", token = "demo-token-abc" } });
        await client.Ok("/api/http/send", new { url = Demo + "/echo/b", auth = new { type = "apiKey", @in = "query", name = "api_key", value = "demo-key-123" } });
        await client.Ok("/api/http/send", new { url = Demo + "/echo/c", auth = new { type = "jwtBearer", addTo = "query", secret = "a-playground-secret-of-at-least-32-bytes", payload = new { sub = "ada" } } });
        await client.Ok("/api/http/send", new { url = Demo + "/echo/d", auth = new { type = "oauth2ClientCredentials", tokenUrl = Demo + "/oauth/token", clientId = "demo-client", clientSecret = "demo-client-secret" } });

        factory.LocalPort = PlaygroundFactory.SyncPort;
        var runs = (await (await client.Post("/internal/sync", new { ackThrough = 0 })).Json())["calls"]!.AsArray()
            .Select(c => c!["run"]!.GetValue<string>()).ToList();

        Assert.Equal(5, runs.Count);
        var all = string.Join("\n", runs);
        var basic = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("ada:analytical-engine"));
        foreach (var secret in new[] { basic, "analytical-engine", "demo-token-abc", "demo-key-123", "a-playground-secret-of-at-least-32-bytes", "eyJ", "demo-client-secret", "Bearer demo-" })
        {
            Assert.DoesNotContain(secret, all);
        }
        // What the visitor sent is still readable apart from the secrets.
        Assert.Contains("demo-api.internal/echo/a", all);
        Assert.Contains("Signature valid", all, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("GET /x HTTP/1.1\r\nAuthorization: Basic YWRhOnB3\r\nAccept: */*", "Basic YWRhOnB3")]
    [InlineData("https://api.example.com/x?api_key=k-123&page=2", "k-123")]
    [InlineData("{\"access_token\":\"tok-1\",\"expires_in\":300}", "tok-1")]
    [InlineData("Host=db;Password=hunter2;Database=x", "hunter2")]
    public void Secrets_inside_text_are_masked(string text, string secret)
    {
        var masked = RunRecorder.MaskText(text);
        Assert.DoesNotContain(secret, masked);
        Assert.Contains("******", masked);
    }

    [Fact]
    public async Task The_Studio_s_limits_apply_but_cannot_open_a_playground_the_deployment_closed()
    {
        await using var open = new PlaygroundFactory(Roomy);
        var client = open.CreateClient();
        open.LocalPort = PlaygroundFactory.SyncPort;
        var pushed = await (await client.Post("/internal/sync", new
        {
            ackThrough = 0,
            limits = new { enabled = true, maxConcurrent = 4, expressions = Panel(1), http = Panel(30), files = Panel(20), database = Panel(30) },
        })).Json();
        Assert.True(pushed["limitsApplied"]!.GetValue<bool>());
        open.LocalPort = PlaygroundFactory.PublicPort;
        Assert.Equal(HttpStatusCode.OK, (await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression)).StatusCode);
        Assert.Equal("studio", (await client.GetFromJsonAsync<JsonObject>("/api/info"))!["limitsFrom"]!.GetValue<string>());

        await using var closed = new PlaygroundFactory(new(Roomy) { ["Playground:Limits:Enabled"] = "false" });
        var closedClient = closed.CreateClient();
        closed.LocalPort = PlaygroundFactory.SyncPort;
        await closedClient.Post("/internal/sync", new { ackThrough = 0, limits = new { enabled = true, maxConcurrent = 4, expressions = Panel(60), http = Panel(30), files = Panel(20), database = Panel(30) } });
        closed.LocalPort = PlaygroundFactory.PublicPort;
        Assert.Equal("disabled", await (await closedClient.Post("/api/expressions/evaluate", PlaygroundCalls.Expression)).Refusal(HttpStatusCode.ServiceUnavailable));

        // A bad push is refused and the limits in force stay.
        open.LocalPort = PlaygroundFactory.SyncPort;
        var bad = await (await client.Post("/internal/sync", new { ackThrough = 0, limits = new { enabled = true, maxConcurrent = 0, expressions = Panel(1), http = Panel(1), files = Panel(1), database = Panel(1) } })).Json();
        Assert.False(bad["limitsApplied"]!.GetValue<bool>());
        Assert.NotEmpty(bad["limitsRefused"]!.AsArray());

        static object Panel(int perMinute) => new { perMinute, perHour = Math.Max(perMinute, 100), maxBodyBytes = 65536, maxOutputBytes = 262144, maxRows = 100, timeoutMs = 2000 };
    }

    [Fact]
    public void Masking_covers_auth_secrets_credential_headers_and_connection_string_passwords()
    {
        var masked = RunRecorder.Mask(JsonNode.Parse("""
            {
              "auth": { "type": "apiKey", "name": "X-API-Key", "value": "k-123", "nested": { "clientSecret": "s" } },
              "headers": [ { "key": "Cookie", "value": "sid=1" }, { "key": "Accept", "value": "text/plain" } ],
              "store": { "secretAccessKey": "aws", "bucket": "b" },
              "note": "Host=db;Password=hunter2;Database=x",
              "body": { "password": "p", "user": "ada" }
            }
            """))!.ToJsonString();

        foreach (var secret in new[] { "k-123", "\"s\"", "sid=1", "aws", "hunter2", "\"p\"" }) Assert.DoesNotContain(secret, masked);
        foreach (var kept in new[] { "X-API-Key", "text/plain", "\"b\"", "Host=db", "ada" }) Assert.Contains(kept, masked);
    }

    // ---- the sample database (both modes) ---------------------------------------------------------------------------

    public static TheoryData<string> Refused => new()
    {
        "ATTACH DATABASE '{0}' AS other",
        "VACUUM INTO '{0}'",
        "SELECT 1; ATTACH DATABASE '{0}' AS other",
        "PRAGMA writable_schema = ON",
        "PRAGMA temp_store_directory = '{0}'",
        "DELETE FROM orders",
        "INSERT INTO orders VALUES (99, 1, '2026-01-01', 'open')",
        "CREATE TABLE t (x)",
        "DROP TABLE orders",
        "SELECT load_extension('{0}')",
        "SELECT 1; SELECT 2",
        "BEGIN",
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task The_sample_database_refuses_anything_but_one_read_and_writes_no_file(string template)
    {
        var target = Path.Combine(Path.GetTempPath(), $"playground-{Guid.NewGuid():N}.db");
        await using var factory = new PlaygroundFactory(Roomy);

        var response = await factory.CreateClient().Post("/api/database/query", new { connectionId = "sample", sql = string.Format(template, target.Replace("'", "''")) });

        var reason = await response.Refusal(HttpStatusCode.BadRequest);
        Assert.Contains(reason, new[] { "invalid", "failed" });
        Assert.False(File.Exists(target), "a refused statement must not have created a file");
    }

    [Theory]
    [InlineData("SELECT length(randomblob(100000000))")]
    [InlineData("SELECT length(zeroblob(100000000))")]
    [InlineData("SELECT length(printf('%.*c', 100000000, 'x'))")]
    public async Task A_huge_value_is_refused_by_SQLite_itself(string sql)
    {
        await using var factory = new PlaygroundFactory(Roomy);
        var client = factory.CreateClient();
        var response = await client.Post("/api/database/query", new { sql });

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity });
        Assert.Contains(await response.Refusal(response.StatusCode), new[] { "failed", "timeout" });
        Assert.Equal(HttpStatusCode.OK, (await client.Post("/api/database/query", new { sql = "SELECT 1 AS one" })).StatusCode);
    }

    [Fact]
    public async Task Each_sample_query_gets_a_fresh_copy()
    {
        await using var factory = new PlaygroundFactory(Roomy);
        var client = factory.CreateClient();
        await client.Post("/api/database/query", new { sql = "DELETE FROM orders" });
        var count = await (await client.Post("/api/database/query", new { sql = "SELECT count(*) AS n FROM orders" })).Json();
        Assert.Equal(18, count["rows"]![0]!["n"]!.GetValue<long>());
    }

    // ---- self-hosted: the real thing, for whoever holds the key ------------------------------------------------------

    [Fact]
    public async Task Self_hosted_needs_its_key_on_every_call_but_the_info()
    {
        await using var factory = new PlaygroundFactory(Roomy, selfHosted: true);
        var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/info")).StatusCode);
        Assert.True((await anonymous.GetFromJsonAsync<JsonObject>("/api/info"))!["keyRequired"]!.GetValue<bool>());
        foreach (var path in new[] { "/api/expressions/evaluate", "/api/http/send", "/api/database/query", "/api/files/convert" })
        {
            var response = await anonymous.Post(path, new { });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("key_required", (await response.Json())["reason"]!.GetValue<string>());
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/database/connections")).StatusCode);

        var wrong = factory.CreateClient();
        wrong.DefaultRequestHeaders.Add("X-Playground-Key", "nope");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.Post("/api/expressions/evaluate", PlaygroundCalls.Expression)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateKeyedClient().Post("/api/expressions/evaluate", PlaygroundCalls.Expression)).StatusCode);
    }

    [Fact]
    public async Task Self_hosted_without_a_configured_key_makes_one_rather_than_staying_open()
    {
        await using var factory = new PlaygroundFactory(new(Roomy) { ["Playground:AccessKey"] = "" }, selfHosted: true);
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression)).StatusCode);
        var key = factory.Services.GetRequiredService<AccessKey>();
        Assert.True(key.Generated);
        Assert.Equal(32, key.Value!.Length);
    }

    /// <summary>A real HTTP server on a loopback port, for the self-hosted send.</summary>
    private static async Task<(WebApplication App, string BaseUrl)> StartTarget()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapPost("/hello/{name}", (string name, HttpRequest request) => Results.Json(new { hello = name, auth = request.Headers.Authorization.ToString() }, statusCode: 201));
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (app, address);
    }

    [Fact]
    public async Task Self_hosted_sends_for_real()
    {
        var (target, baseUrl) = await StartTarget();
        await using (target)
        {
            await using var factory = new PlaygroundFactory(Roomy, selfHosted: true);
            var answer = await factory.CreateKeyedClient().Ok("/api/http/send", new { method = "POST", url = baseUrl + "/hello/ada", auth = new { type = "bearer", token = "t0k3n" } });

            Assert.True(answer["sent"]!.GetValue<bool>());
            Assert.Equal(201, answer["status"]!.GetValue<int>());
            var body = JsonNode.Parse(answer["body"]!["text"]!.GetValue<string>())!;
            Assert.Equal("ada", body["hello"]!.GetValue<string>());
            Assert.Equal("Bearer t0k3n", body["auth"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task Self_hosted_connections_are_saved_masked_and_read_only_unless_allowed()
    {
        await using var factory = new PlaygroundFactory(Roomy, selfHosted: true);
        var client = factory.CreateKeyedClient();
        var file = Path.Combine(factory.DataFolder, "orders.db");

        var saved = await client.Ok("/api/database/connections", new { id = "", name = "Orders", provider = "sqlite", connectionString = $"Data Source={file};Password=hunter2", allowWrites = true });
        var id = saved["id"]!.GetValue<string>();
        Assert.DoesNotContain("hunter2", saved["display"]!.GetValue<string>());
        Assert.Contains("hunter2", await File.ReadAllTextAsync(Path.Combine(factory.DataFolder, "playground.json"))); // kept on the owner's volume

        // The password is not shown back; saving with the mask keeps it.
        await client.Ok("/api/database/connections", new { id, name = "Orders", provider = "sqlite", connectionString = "******", allowWrites = true });
        Assert.Contains("hunter2", await File.ReadAllTextAsync(Path.Combine(factory.DataFolder, "playground.json")));
    }

    [Fact]
    public async Task Self_hosted_flows_are_saved_on_the_volume_and_come_back()
    {
        await using var factory = new PlaygroundFactory(Roomy, selfHosted: true);
        var client = factory.CreateKeyedClient();
        var flows = new[]
        {
            new { id = "f1", name = "Orders to CSV", nodes = new object[] { new { id = "a", type = "http", name = "Get orders", x = 0, y = 40, config = new { method = "GET", url = "https://api.example.com/orders" } } }, edges = Array.Empty<object>() },
        };

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/saved/flows", flows)).StatusCode);
        Assert.Contains("Orders to CSV", await File.ReadAllTextAsync(Path.Combine(factory.DataFolder, "playground.json")));

        var saved = await client.GetFromJsonAsync<JsonObject>("/api/saved");
        Assert.Equal("Get orders", saved!["flows"]![0]!["nodes"]![0]!["name"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PutAsJsonAsync("/api/saved/flows", flows)).StatusCode);
    }

    [Fact]
    public async Task Info_names_the_library_version_the_page_puts_in_its_csharp()
    {
        await using var factory = new PlaygroundFactory(Roomy);
        var info = await factory.CreateClient().GetFromJsonAsync<JsonObject>("/api/info");
        var version = typeof(SpanFlow.Expressions.ExpressionEvaluator).Assembly.GetName().Version!;
        Assert.Equal($"{version.Major}.{version.Minor}.{version.Build}", info!["nugetVersion"]!.GetValue<string>());
    }

    [Fact]
    public async Task Self_hosted_read_only_connections_refuse_writes_and_roll_back_and_writable_ones_commit()
    {
        await using var factory = new PlaygroundFactory(Roomy, selfHosted: true);
        var client = factory.CreateKeyedClient();
        var file = Path.Combine(factory.DataFolder, "app.db");
        var writable = (await client.Ok("/api/database/connections", new { id = "", name = "W", provider = "sqlite", connectionString = $"Data Source={file}", allowWrites = true }))["id"]!.GetValue<string>();
        var readOnly = (await client.Ok("/api/database/connections", new { id = "", name = "R", provider = "sqlite", connectionString = $"Data Source={file}", allowWrites = false }))["id"]!.GetValue<string>();

        await client.Ok("/api/database/query", new { connectionId = writable, sql = "CREATE TABLE t (x)" });
        Assert.True((await client.Ok("/api/database/query", new { connectionId = writable, sql = "INSERT INTO t VALUES ({{ params.v }}) RETURNING x", @params = new { v = 7 } }))["committed"]!.GetValue<bool>());

        Assert.Equal("invalid", await (await client.Post("/api/database/query", new { connectionId = readOnly, sql = "DELETE FROM t" })).Refusal(HttpStatusCode.BadRequest));
        // A write that starts like a read runs inside a transaction that is rolled back.
        var sneaky = await client.Ok("/api/database/query", new { connectionId = readOnly, sql = "WITH a AS (SELECT 9) INSERT INTO t SELECT * FROM a RETURNING x" });
        Assert.False(sneaky["committed"]!.GetValue<bool>());
        Assert.Equal(1, (await client.Ok("/api/database/query", new { connectionId = readOnly, sql = "SELECT count(*) AS n FROM t" }))["rows"]![0]!["n"]!.GetValue<long>());
    }

    [Fact]
    public async Task Self_hosted_stores_read_and_write_inside_the_allowed_folders_only()
    {
        await using var factory = new PlaygroundFactory(Roomy, selfHosted: true);
        var client = factory.CreateKeyedClient();

        var outside = await client.Post("/api/files/stores", new { id = "", name = "Root", config = new { store = "local", rootPath = Path.GetTempPath() } });
        Assert.Equal("invalid", await outside.Refusal(HttpStatusCode.BadRequest));

        var store = (await client.Ok("/api/files/stores", new { id = "", name = "Files", config = new { store = "local", rootPath = factory.FilesFolder } }))["id"]!.GetValue<string>();
        var written = await client.Ok("/api/files/convert", new { from = "csv", to = "xlsx", content = "a,b\n1,2", target = new { storeId = store, path = "out/t.xlsx" } });
        Assert.Equal("out/t.xlsx", written["output"]!["written"]!["path"]!.GetValue<string>());
        Assert.True(File.Exists(Path.Combine(factory.FilesFolder, "out", "t.xlsx")));

        var read = await client.Ok("/api/files/convert", new { from = "xlsx", to = "json", source = new { storeId = store, path = "out/t.xlsx" } });
        Assert.Equal(1, read["rowCount"]!.GetValue<int>());

        var again = await client.Post("/api/files/convert", new { from = "csv", to = "xlsx", content = "a\n1", target = new { storeId = store, path = "out/t.xlsx" } });
        Assert.Equal("failed", await again.Refusal(HttpStatusCode.BadRequest)); // there already, and overwrite was not asked
        var escape = await client.Post("/api/files/convert", new { from = "csv", to = "csv", content = "a\n1", target = new { storeId = store, path = "../escape.csv" } });
        Assert.Equal("failed", await escape.Refusal(HttpStatusCode.BadRequest));
        Assert.False(File.Exists(Path.Combine(factory.DataFolder, "escape.csv")));
    }

    [Fact]
    public async Task Self_hosted_records_nothing_and_has_no_sync()
    {
        await using var factory = new PlaygroundFactory(Roomy, selfHosted: true);
        var client = factory.CreateKeyedClient();
        await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression);
        Assert.Equal(0, factory.Services.GetRequiredService<RunRecorder>().Count);
        factory.LocalPort = PlaygroundFactory.SyncPort;
        Assert.Equal(HttpStatusCode.NotFound, (await client.Post("/internal/sync", new { ackThrough = 0 })).StatusCode);
    }
}
