using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using SpanFlow.Connectors.Http.Auth;
using SpanFlow.Libraries.Playground;
using SpanFlow.Libraries.Playground.Panels;

// E71: the playground for the open-source libraries, in two modes (D71.11). Hosted (the vendor's public server): no
// sign-in, the HTTP panel reaches only the in-process demo API, databases are only the configured demo, files are
// formats only, limits come from the Studio and runs go back to it over the sync port. Self-hosted (docker run): the
// real thing behind an access key, limits from configuration, nothing reported anywhere.
var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PlaygroundOptions>(builder.Configuration.GetSection(PlaygroundOptions.SectionName));
var settings = builder.Configuration.GetSection(PlaygroundOptions.SectionName).Get<PlaygroundOptions>() ?? new PlaygroundOptions();
if (settings.Validate() is { Count: > 0 } problems)
    throw new InvalidOperationException("Playground settings are not usable: " + string.Join(" ", problems));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CallLimiter>();
builder.Services.AddSingleton<LimitsSource>();
builder.Services.AddSingleton(new RunRecorder { Enabled = settings.IsHosted && settings.SyncPort > 0 });
builder.Services.AddSingleton<AccessKey>();
// Real time, always: Hawk, OAuth 1.0a and JWT signatures carry real timestamps, which the demo API checks.
builder.Services.AddSingleton(new DemoApi(settings.DemoApiHost, TimeProvider.System));
builder.Services.AddSingleton<PlaygroundHttpClients>();
builder.Services.AddSingleton<IHttpClientFactory>(sp => sp.GetRequiredService<PlaygroundHttpClients>());
builder.Services.AddSingleton<TokenCache>();
builder.Services.AddSingleton<Connections>();
builder.Services.AddSingleton<FileStores>();
if (!settings.IsHosted) builder.Services.AddSingleton<SavedData>();

// D71.8: the caller is the address left of the last trusted proxy; nothing a caller writes themselves is believed.
builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
{
    forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    forwarded.ForwardLimit = null;
    forwarded.KnownIPNetworks.Clear();
    forwarded.KnownProxies.Clear();
    foreach (var network in TrustedNetworks.Parse(settings.TrustedProxies)) forwarded.KnownIPNetworks.Add(network);
});

builder.WebHost.ConfigureKestrel(kestrel =>
{
    // Each route lowers this to its panel's cap (D71.6); this only has to sit above the largest a panel may have.
    kestrel.Limits.MaxRequestBodySize = 8 * 1024 * 1024 + 1024;
    kestrel.AddServerHeader = false;
});

var app = builder.Build();
_ = app.Services.GetRequiredService<AccessKey>(); // made (and logged) at start, not on the first call
if (!settings.IsHosted) _ = app.Services.GetRequiredService<SavedData>();

app.UseForwardedHeaders();

// The sync port answers /internal/sync and nothing else; the public port never answers /internal (D71.12).
app.Use(async (http, next) =>
{
    var onSyncPort = settings.SyncPort > 0 && http.Connection.LocalPort == settings.SyncPort;
    if (onSyncPort != http.Request.Path.StartsWithSegments("/internal"))
    {
        http.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});

// The page loads nothing from anywhere else and runs no inline script.
app.Use(async (http, next) =>
{
    var headers = http.Response.Headers;
    headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; "
                                    + "frame-ancestors 'none'; base-uri 'none'; form-action 'none'; object-src 'none'";
    headers.XContentTypeOptions = "nosniff";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
    headers.CacheControl = http.Request.Path.StartsWithSegments("/api") ? "no-store" : "no-cache";
    await next();
});

app.Use(AccessKey.Middleware);
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/healthz", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/readyz", () => Results.Ok(new { status = "Healthy" }));

app.MapGet("/api/info", (IOptions<PlaygroundOptions> options, LimitsSource limits, AccessKey key, FileStores stores) =>
{
    var o = options.Value;
    var current = limits.Current;
    var panels = new JsonObject();
    foreach (var panel in Enum.GetValues<Panel>())
    {
        var l = current.For(panel);
        panels[panel.ToString().ToLowerInvariant()] = new JsonObject
        {
            ["perMinute"] = l.PerMinute, ["perHour"] = l.PerHour, ["maxBodyBytes"] = l.MaxBodyBytes,
            ["maxOutputBytes"] = l.MaxOutputBytes, ["maxRows"] = l.MaxRows, ["timeoutMs"] = l.TimeoutMs,
        };
    }

    var info = new JsonObject
    {
        ["mode"] = o.IsHosted ? "hosted" : "selfhosted",
        ["enabled"] = current.Enabled,
        ["keyRequired"] = key.Value is not null,
        ["limitsFrom"] = o.IsHosted && limits.PushedAt is not null ? "studio" : "config",
        ["productName"] = o.ProductName,
        ["productUrl"] = o.ProductUrl,
        ["sourceUrl"] = o.SourceUrl,
        ["nugetPrefix"] = o.NuGetPrefix,
        // The libraries' own version, so the page's "C# code" names the packages this server really runs.
        ["nugetVersion"] = typeof(SpanFlow.Expressions.ExpressionEvaluator).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0",
        ["panels"] = panels,
        ["demoApi"] = new JsonObject
        {
            ["baseUrl"] = $"https://{o.DemoApiHost}",
            ["credentials"] = JsonSerializer.SerializeToNode(typeof(DemoApi.Credentials)
                .GetFields().ToDictionary(f => char.ToLowerInvariant(f.Name[0]) + f.Name[1..], f => (string)f.GetValue(null)!)),
        },
        ["authTypes"] = new JsonArray(HttpPanel.AuthTypes.Select(t => (JsonNode?)t).ToArray()),
        ["readFormats"] = new JsonArray(FilesPanel.ReadFormats.Where(f => !o.IsHosted || f != "xlsx").Select(t => (JsonNode?)t).ToArray()),
        ["writeFormats"] = new JsonArray(FilesPanel.WriteFormats.Select(t => (JsonNode?)t).ToArray()),
        ["providers"] = new JsonArray(DatabasePanel.Providers.Select(t => (JsonNode?)t).ToArray()),
        ["storeKinds"] = new JsonArray((o.IsHosted ? [] : FileStores.Kinds).Select(t => (JsonNode?)t).ToArray()),
        ["localFileRoots"] = new JsonArray((o.IsHosted ? [] : stores.AllowedRoots).Select(t => (JsonNode?)t).ToArray()),
        ["sampleTables"] = new JsonArray(SampleDatabase.Tables.Select(t => (JsonNode?)new JsonObject { ["table"] = t.Table, ["columns"] = t.Columns }).ToArray()),
    };
    return Results.Content(info.ToJsonString(PlaygroundEndpoint.JsonOptions), "application/json");
});

// ---- panels (limited, and recorded when hosted) -------------------------------------------------------------------

MapPanel("/api/expressions/evaluate", Panel.Expressions, "evaluate", ExpressionsPanel.Run);
MapPanel("/api/http/preview", Panel.Http, "preview", HttpPanel.Preview);
MapPanel("/api/http/send", Panel.Http, "send", HttpPanel.Send);
MapPanel("/api/files/convert", Panel.Files, "convert", FilesPanel.Convert);
MapPanel("/api/files/list", Panel.Files, "list", FilesPanel.List);
MapPanel("/api/database/inspect", Panel.Database, "inspect", DatabasePanel.Inspect);
MapPanel("/api/database/query", Panel.Database, "query", DatabasePanel.Query);

// ---- connections and stores: listed in both modes, managed only self-hosted ----------------------------------------

app.MapGet("/api/database/connections", (Connections connections) => Results.Ok(connections.List()));
app.MapGet("/api/files/stores", (FileStores stores) => Results.Ok(stores.List()));

if (!settings.IsHosted)
{
    var saved = app.MapGroup("/api");

    saved.MapGet("/saved", (SavedData data) => Results.Ok(data.Read(s => new { collections = s.Collections, environments = s.Environments, flows = s.Flows })));
    saved.MapPut("/saved/collections", (JsonArray collections, SavedData data) => GuardedAction(() => data.Change(s => s.Collections = collections)));
    saved.MapPut("/saved/environments", (JsonArray environments, SavedData data) => GuardedAction(() => data.Change(s => s.Environments = environments)));
    saved.MapPut("/saved/flows", (JsonArray flows, SavedData data) => GuardedAction(() => data.Change(s => s.Flows = flows)));

    saved.MapPost("/database/connections", (SavedConnection connection, SavedData data, Connections connections) => Guarded(() =>
    {
        if (string.IsNullOrWhiteSpace(connection.Name)) throw PanelRefusal.Invalid("Give the connection a name.");
        if (!DatabasePanel.Providers.Contains(connection.Provider)) throw PanelRefusal.Invalid($"\"provider\" is one of {string.Join(", ", DatabasePanel.Providers)}.");
        if (settings.Connections.Any(c => c.Id == connection.Id)) throw PanelRefusal.Invalid("That connection comes from configuration; change it there.");
        data.Change(s =>
        {
            var existing = s.Connections.FirstOrDefault(c => c.Id == connection.Id && connection.Id.Length > 0);
            // The page shows a masked string; saving it unchanged (or blank, or ******) keeps the one that is there.
            if (existing is not null
                && (string.IsNullOrWhiteSpace(connection.ConnectionString) || connection.ConnectionString == "******"
                    || connection.ConnectionString == Connections.Mask(existing.ConnectionString)))
                connection.ConnectionString = existing.ConnectionString;
            if (string.IsNullOrWhiteSpace(connection.ConnectionString)) throw PanelRefusal.Invalid("Give the connection string.");
            if (existing is not null) s.Connections.Remove(existing);
            if (connection.Id.Length == 0) connection.Id = Guid.NewGuid().ToString("N")[..12];
            s.Connections.Add(connection);
        });
        return connections.List().First(c => c.Id == connection.Id);
    }));
    saved.MapDelete("/database/connections/{id}", (string id, SavedData data) => GuardedAction(() => data.Change(s => s.Connections.RemoveAll(c => c.Id == id))));
    saved.MapPost("/database/connections/{id}/test", async (string id, Connections connections, CancellationToken ct) =>
    {
        try
        {
            var (provider, connectionString, _, _) = connections.Resolve(id);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await using var connection = Connections.Registry.Get(provider).CreateConnection(connectionString);
            await connection.OpenAsync(timeout.Token);
            return Results.Ok(new { ok = true, serverVersion = connection.ServerVersion });
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Results.Ok(new { ok = false, error = ex is OperationCanceledException ? "No answer within 15 s." : ex.Message });
        }
    });

    saved.MapPost("/files/stores", (SavedStore store, SavedData data, FileStores stores) => Guarded(() =>
    {
        if (string.IsNullOrWhiteSpace(store.Name)) throw PanelRefusal.Invalid("Give the store a name.");
        if (settings.Stores.Any(s => s.Id == store.Id)) throw PanelRefusal.Invalid("That store comes from configuration; change it there.");
        data.Change(s =>
        {
            var existing = s.Stores.FirstOrDefault(x => x.Id == store.Id && store.Id.Length > 0);
            if (existing is not null)
            {
                // Secrets left as ****** keep what is saved.
                foreach (var (key, value) in existing.Config)
                    if (store.Config[key] is JsonValue v && v.TryGetValue<string>(out var text) && text == "******") store.Config[key] = value?.DeepClone();
                s.Stores.Remove(existing);
            }
            stores.Create(store.Config); // the library's own checks: a bucket, a root inside the allowed folders, ...
            if (store.Id.Length == 0) store.Id = Guid.NewGuid().ToString("N")[..12];
            s.Stores.Add(store);
        });
        return stores.List().First(x => x.Id == store.Id);
    }));
    saved.MapGet("/files/stores/{id}/config", (string id, SavedData data) =>
        data.Read(s => s.Stores.FirstOrDefault(x => x.Id == id)) is { } store
            ? Results.Ok(new { store.Id, store.Name, config = RunRecorder.Mask(store.Config.DeepClone()) })
            : Results.NotFound());
    saved.MapDelete("/files/stores/{id}", (string id, SavedData data) => GuardedAction(() => data.Change(s => s.Stores.RemoveAll(x => x.Id == id))));
}

// ---- the sync port (hosted): Platform sends the Studio's limits and takes the recorded runs ----------------------------

if (settings.IsHosted && settings.SyncPort > 0)
{
    app.MapPost("/internal/sync", (SyncRequest request, LimitsSource limits, RunRecorder recorder, TimeProvider clock) =>
    {
        IReadOnlyList<string> refused = request.Limits is null ? [] : limits.Apply(request.Limits, clock.GetUtcNow());
        // An acknowledgement meant for an earlier process (before a restart) acknowledges nothing here.
        if (request.Instance is not null && request.Instance != recorder.Instance) request.AckThrough = 0;
        return Results.Ok(new
        {
            instance = recorder.Instance,
            limitsApplied = request.Limits is not null && refused.Count == 0,
            limitsRefused = refused,
            calls = recorder.Take(request.AckThrough),
            buffered = recorder.Count,
        });
    });
}

app.MapFallback("/api/{**rest}", () => PlaygroundEndpoint.Refuse(StatusCodes.Status404NotFound, Reasons.Invalid, "Not found. See GET /api/info."));

FilesPanel.WarmUp();

app.Logger.LogInformation("Libraries playground: {Mode} mode.", settings.IsHosted ? "hosted" : "self-hosted");
app.Run();

void MapPanel(string path, Panel panel, string action, Func<PanelCall, Task<JsonNode>> work) =>
    // The CancellationToken is not used: with HttpContext alone the lambda would bind as a RequestDelegate and its IResult be dropped.
    app.MapPost(path, (HttpContext http, CancellationToken _) => PlaygroundEndpoint.HandleAsync(panel, action, http, work));

static IResult Guarded<T>(Func<T> action)
{
    try
    {
        return Results.Ok(action());
    }
    catch (PanelRefusal refusal)
    {
        return PlaygroundEndpoint.Refuse(refusal.Status, refusal.Reason, refusal.Message);
    }
}

static IResult GuardedAction(Action action) => Guarded(() => { action(); return new { ok = true }; });

/// <summary>The entry point, public so the tests' WebApplicationFactory can host it.</summary>
public partial class Program;

namespace SpanFlow.Libraries.Playground
{
    /// <summary>What Platform sends on the sync port: the Studio's limits, and the last call it saved.</summary>
    public sealed class SyncRequest
    {
        public PlaygroundLimits? Limits { get; set; }
        public long AckThrough { get; set; }

        /// <summary>The instance <see cref="AckThrough"/> belongs to (from the previous answer); null on Platform's first sync.</summary>
        public string? Instance { get; set; }
    }

    /// <summary><c>Playground:TrustedProxies</c> as networks: addresses, CIDR ranges, or <c>private</c>.</summary>
    public static class TrustedNetworks
    {
        public static IEnumerable<System.Net.IPNetwork> Parse(IReadOnlyCollection<string>? entries)
        {
            foreach (var entry in entries is { Count: > 0 } ? entries : ["private"])
            {
                var value = entry.Trim();
                if (value.Equals("private", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var range in new[] { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8", "::1/128", "fc00::/7" })
                        yield return System.Net.IPNetwork.Parse(range);
                }
                else if (System.Net.IPNetwork.TryParse(value, out var network)) yield return network;
                else if (IPAddress.TryParse(value, out var address)) yield return new System.Net.IPNetwork(address, address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128);
                else throw new InvalidOperationException($"Playground:TrustedProxies: '{value}' is not an address, a CIDR range or 'private'.");
            }
        }
    }
}
