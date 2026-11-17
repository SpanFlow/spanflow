using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using SpanFlow.Connectors.Http;
using SpanFlow.Connectors.Http.Auth;

namespace SpanFlow.Libraries.Playground.Panels;

/// <summary>
/// The HTTP panel, like Postman (D71.11). One request shape for both routes:
/// <c>{ method, url, params, headers, pathVariables, bodyType, body, settings, auth, challenge? }</c>, built by
/// <see cref="HttpRequestBuilder"/> and signed by the library's own strategies.
/// <list type="bullet">
/// <item><c>POST /api/http/preview</c>: the exact request, never sent.</item>
/// <item><c>POST /api/http/send</c>: sent, with the response. Hosted, the only destination is the in-process demo API
/// (<see cref="PlaygroundHttpClients"/>); self-hosted, anywhere the server reaches. A Digest 401 is answered and the call
/// sent once more; a token that the API refuses is fetched again once.</item>
/// </list>
/// </summary>
public static class HttpPanel
{
    /// <summary>Every scheme the library has. The token ones fetch from the demo API when hosted.</summary>
    public static readonly IReadOnlyList<string> AuthTypes =
        ["none", "apiKey", "basic", "bearer", "digest", "hawk", "oauth1", "jwtBearer", "oauth2ClientCredentials", "tokenEndpoint", "loginRequest"];

    /// <summary>A body is shown as text up to this size; past it, its first part and its length.</summary>
    private const int MaxBodyPreviewBytes = 64 * 1024;

    public static async Task<JsonNode> Preview(PanelCall call)
    {
        var ct = call.Cancellation;
        var registry = Registry(call);
        var (build, auth, authType) = Prepare(call.Body, registry);

        using var message = build();
        string? note = null;
        if (auth is not null && registry.Get(authType) is IChallengeAuthStrategy challenged)
        {
            var challenge = PlaygroundEndpoint.OptionalString(call.Body, "challenge") is { Length: > 0 } c ? c : SampleChallenge(message.RequestUri!.Host);
            var unauthorized = new HttpResponseMessage(HttpStatusCode.Unauthorized) { RequestMessage = message };
            if (!unauthorized.Headers.TryAddWithoutValidation("WWW-Authenticate", challenge) || !challenged.AcceptChallenge(unauthorized, auth))
                throw PanelRefusal.Invalid("\"challenge\" is a WWW-Authenticate value: Digest realm=\"...\", nonce=\"...\", qop=\"auth\", algorithm=SHA-256.");
            note = "Digest: the first call goes without credentials and the server's 401 names a nonce. This is the second call, answering "
                   + (call.Body["challenge"] is null ? "a sample challenge." : "the challenge you gave.");
        }
        else if (auth is not null && registry.Get(authType) is ITokenStatusStrategy)
        {
            note = "This scheme fetches a token first; the preview fetched it the way a send would.";
        }

        await registry.ApplyAsync(message, auth, ct);
        var sent = await Describe(message, ct);
        sent["sent"] = false;
        sent["auth"] = authType;
        sent["note"] = note;
        return sent;
    }

    public static async Task<JsonNode> Send(PanelCall call)
    {
        var ct = call.Cancellation;
        var registry = Registry(call);
        var (build, auth, authType) = Prepare(call.Body, registry);
        var settings = HttpRequestSettings.Parse(call.Body["settings"]);
        var clients = (PlaygroundHttpClients)call.Services.GetRequiredService<IHttpClientFactory>();
        using var client = clients.Create(settings.FollowRedirects);

        using var timeout = settings.TimeoutSeconds is { } seconds ? new CancellationTokenSource(TimeSpan.FromSeconds(seconds)) : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        var strategy = auth is null ? null : registry.Get(authType);
        var timeline = new JsonArray();
        var clock = Stopwatch.StartNew();

        HttpResponseMessage? response = null;
        JsonObject sent = [];
        for (var attempt = 1; ; attempt++)
        {
            response?.Dispose();
            using var message = build();
            await registry.ApplyAsync(message, auth, linked.Token);
            sent = await Describe(message, linked.Token);
            var started = clock.Elapsed;
            try
            {
                response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            }
            catch (HostRefusedException ex)
            {
                throw PanelRefusal.Invalid(ex.Message);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                throw PanelRefusal.Failed($"No answer within the request's own timeout ({settings.TimeoutSeconds} s).");
            }
            catch (HttpRequestException ex)
            {
                throw PanelRefusal.Failed($"Could not reach {message.RequestUri!.Host}: {ex.Message}");
            }

            timeline.Add(new JsonObject
            {
                ["attempt"] = attempt,
                ["status"] = (int)response.StatusCode,
                ["ms"] = Math.Round((clock.Elapsed - started).TotalMilliseconds, 1),
            });

            if (attempt > 1 || response.StatusCode != HttpStatusCode.Unauthorized || strategy is null) break;
            if (strategy is IChallengeAuthStrategy challenged && challenged.AcceptChallenge(response, auth!))
            {
                timeline[^1]!["note"] = "401 with a challenge: answered it and sent again.";
                continue;
            }
            if (strategy is IInvalidatableAuthStrategy token)
            {
                await token.InvalidateAsync(auth!, linked.Token);
                timeline[^1]!["note"] = "401: fetched a fresh token and sent again.";
                continue;
            }
            break;
        }

        using (response)
        {
            var cap = Math.Min(call.Limits.MaxOutputBytes / 2, settings.MaxResponseBytes is { } max ? (int)Math.Min(max, int.MaxValue) : int.MaxValue);
            var (bytes, truncated) = await ReadCapped(response, cap, linked.Token);
            var headers = new JsonArray();
            foreach (var (name, values) in response.Headers.NonValidated) headers.Add(Header(name, string.Join(", ", values)));
            foreach (var (name, values) in response.Content.Headers.NonValidated) headers.Add(Header(name, string.Join(", ", values)));

            var body = new JsonObject { ["bytes"] = bytes.Length, ["truncated"] = truncated };
            if (IsText(response.Content.Headers.ContentType) && TryUtf8(bytes, out var text)) body["text"] = text;
            else body["base64"] = Convert.ToBase64String(bytes);

            return new JsonObject
            {
                ["sent"] = true,
                ["request"] = sent,
                ["auth"] = authType,
                ["status"] = (int)response.StatusCode,
                ["reason"] = response.ReasonPhrase,
                ["ms"] = Math.Round(clock.Elapsed.TotalMilliseconds, 1),
                ["headers"] = headers,
                ["body"] = body,
                ["timeline"] = timeline,
            };
        }
    }

    // ---- building ----------------------------------------------------------------------------------------------

    private static AuthStrategyRegistry Registry(PanelCall call)
    {
        // A fresh registry per call for the static schemes: Digest keeps the challenges it has seen, and one caller's
        // must never be another's. The token schemes share one cache, keyed by their settings.
        var clients = call.Services.GetRequiredService<IHttpClientFactory>();
        var cache = call.Services.GetRequiredService<TokenCache>();
        return new AuthStrategyRegistry([
            new NoneAuthStrategy(), new ApiKeyAuthStrategy(), new BasicAuthStrategy(), new BearerAuthStrategy(),
            new DigestAuthStrategy(), new HawkAuthStrategy(), new JwtBearerAuthStrategy(), new OAuth1AuthStrategy(),
            new Oauth2ClientCredentialsStrategy(clients, cache), new TokenEndpointStrategy(clients, cache), new LoginRequestStrategy(clients, cache),
        ]);
    }

    /// <summary>Checks the request once and returns a builder: a send can need the same request twice (Digest, a new token).</summary>
    private static (Func<HttpRequestMessage> Build, JsonObject? Auth, string AuthType) Prepare(JsonObject body, AuthStrategyRegistry registry)
    {
        var url = PlaygroundEndpoint.RequireString(body, "url", 4096).Trim();
        var (baseUrl, path) = Split(url);

        var auth = body["auth"] switch
        {
            null => null,
            JsonObject a => (JsonObject)a.DeepClone(),
            _ => throw PanelRefusal.Invalid("\"auth\" is an object: { \"type\": \"bearer\", \"token\": \"...\" }."),
        };
        var authType = auth?["type"]?.GetValue<string>() is { Length: > 0 } t ? t : "none";
        if (!AuthTypes.Contains(authType, StringComparer.OrdinalIgnoreCase))
            throw PanelRefusal.Invalid($"\"auth.type\" is one of {string.Join(", ", AuthTypes)}.");
        if (authType == "none") auth = null;
        if (auth is not null && authType == LoginRequestStrategy.TypeName) auth[LoginRequestStrategy.BaseUrlKey] = baseUrl;
        var problems = registry.Validate(auth);
        if (problems.Count > 0) throw PanelRefusal.Invalid(string.Join(" ", problems));

        var method = body["method"]?.GetValue<string>() is { Length: > 0 } m ? m.ToUpperInvariant() : "GET";
        if (method.Length > 16 || !method.All(char.IsAsciiLetterUpper))
            throw PanelRefusal.Invalid("\"method\" is a word such as GET, POST, PUT, PATCH or DELETE.");

        var request = new JsonObject { ["path"] = path };
        foreach (var key in new[] { "params", "headers", "pathVariables", "bodyType", "body" })
            if (body[key] is { } value) request[key] = value.DeepClone();
        var settings = HttpRequestSettings.Parse(body["settings"]);
        var bodyType = request["bodyType"]?.GetValue<string>();
        if (bodyType is not null && !HttpBodyTypes.All.Contains(bodyType))
            throw PanelRefusal.Invalid($"\"bodyType\" is one of {string.Join(", ", HttpBodyTypes.All)}.");

        HttpRequestMessage Build()
        {
            var message = new HttpRequestMessage(new HttpMethod(method), HttpRequestBuilder.BuildUri(baseUrl, request, settings));
            if (message.RequestUri is not { Scheme: "http" or "https" }) throw PanelRefusal.Invalid("The request URL must stay http or https.");
            // GET and HEAD carry no body unless one was asked for (bodyType), as Postman does.
            if (bodyType is not null || method is not ("GET" or "HEAD")) message.Content = HttpRequestBuilder.BuildContent(request, null);
            HttpRequestBuilder.ApplyHeaders(message, new Dictionary<string, string>(), request, settings);
            return message;
        }

        Build().Dispose(); // the request is checked here, before anything is signed or sent
        return (Build, auth, authType);
    }

    /// <summary><c>https://host:port/path?query</c> → the origin and the rest (path variables like <c>:id</c> intact).</summary>
    private static (string BaseUrl, string Path) Split(string url)
    {
        var scheme = url.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0) throw PanelRefusal.Invalid("\"url\" is an absolute http or https URL.");
        var slash = url.IndexOfAny(['/', '?', '#'], scheme + 3);
        var baseUrl = slash < 0 ? url : url[..slash];
        var path = slash < 0 ? "/" : url[slash..];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var origin) || origin.Scheme is not ("http" or "https") || origin.Host.Length == 0)
            throw PanelRefusal.Invalid("\"url\" is an absolute http or https URL.");
        var hash = path.IndexOf('#');
        return (baseUrl, hash < 0 ? path : path[..hash]);
    }

    private static string SampleChallenge(string host)
    {
        var nonce = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        var opaque = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        return $"Digest realm=\"playground@{host}\", qop=\"auth\", algorithm=SHA-256, nonce=\"{nonce}\", opaque=\"{opaque}\"";
    }

    /// <summary>The request as it goes on the wire (HTTP/1.1 text), plus its parts.</summary>
    private static async Task<JsonObject> Describe(HttpRequestMessage message, CancellationToken ct)
    {
        var uri = message.RequestUri!;
        var headers = new JsonArray { Header("Host", uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}") };
        foreach (var (name, values) in message.Headers.NonValidated) headers.Add(Header(name, string.Join(", ", values)));

        JsonObject? bodyInfo = null;
        byte[] bytes = [];
        if (message.Content is not null)
        {
            bytes = await message.Content.ReadAsByteArrayAsync(ct);
            foreach (var (name, values) in message.Content.Headers.NonValidated) headers.Add(Header(name, string.Join(", ", values)));
            if (message.Content.Headers.ContentLength is null) headers.Add(Header("Content-Length", bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            var shown = bytes.AsSpan(0, Math.Min(bytes.Length, MaxBodyPreviewBytes));
            bodyInfo = new JsonObject { ["bytes"] = bytes.Length, ["truncated"] = bytes.Length > MaxBodyPreviewBytes };
            if (IsText(message.Content.Headers.ContentType) && TryUtf8(shown, out var text)) bodyInfo["text"] = text;
            else bodyInfo["base64"] = Convert.ToBase64String(shown);
        }

        var wire = new StringBuilder();
        wire.Append(message.Method.Method).Append(' ').Append(uri.PathAndQuery).Append(" HTTP/1.1\r\n");
        foreach (var header in headers) wire.Append(header!["name"]!.GetValue<string>()).Append(": ").Append(header["value"]!.GetValue<string>()).Append("\r\n");
        wire.Append("\r\n");
        if (bodyInfo?["text"]?.GetValue<string>() is { } bodyText) wire.Append(bodyText);
        else if (bytes.Length > 0) wire.Append($"[{bytes.Length:N0} bytes of binary content]");

        return new JsonObject
        {
            ["method"] = message.Method.Method,
            ["url"] = uri.AbsoluteUri,
            ["headers"] = headers,
            ["body"] = bodyInfo,
            ["wire"] = wire.ToString(),
        };
    }

    private static async Task<(byte[] Bytes, bool Truncated)> ReadCapped(HttpResponseMessage response, int cap, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (buffer.Length <= cap)
        {
            var read = await stream.ReadAsync(chunk, ct);
            if (read == 0) return (buffer.ToArray(), false);
            buffer.Write(chunk, 0, read);
        }
        return (buffer.ToArray()[..cap], true);
    }

    private static JsonObject Header(string name, string value) => new() { ["name"] = name, ["value"] = value };

    private static bool IsText(MediaTypeHeaderValue? type) =>
        type?.MediaType is not { } media
        || media.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || media.Contains("json", StringComparison.OrdinalIgnoreCase)
        || media.Contains("xml", StringComparison.OrdinalIgnoreCase)
        || media.Contains("javascript", StringComparison.OrdinalIgnoreCase)
        || media.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
        || media.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase)
        || media.Equals("application/graphql", StringComparison.OrdinalIgnoreCase);

    private static bool TryUtf8(ReadOnlySpan<byte> bytes, out string text)
    {
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = "";
            return false;
        }
    }
}
