using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SpanFlow.Connectors.Http.Auth;

namespace SpanFlow.Libraries.Playground.Panels;

/// <summary>
/// The demo API (D71.11): what the hosted playground's HTTP panel really sends to, answered inside this process by an
/// <see cref="HttpMessageHandler"/>, so no socket is ever opened. It knows a set of public demo credentials for every
/// sign-in scheme and says, in each answer, whether the request's signature checked out, using the same signing code
/// the library signs with. Self-hosted, it answers at the same host name too, so a fresh install has something to try.
/// </summary>
public sealed class DemoApi(string host, TimeProvider clock)
{
    /// <summary>The demo credentials, shown on the page and used by the examples.</summary>
    public static class Credentials
    {
        public const string ApiKey = "demo-key-123";
        public const string Username = "ada";
        public const string Password = "analytical-engine";
        public const string BearerToken = "demo-token-abc";
        public const string DigestUsername = "Mufasa";
        public const string DigestPassword = "Circle of Life";
        public const string HawkId = "dh37fgj492je";
        public const string HawkKey = "werxhqb98rpaxn39848xrunpaw3489ruxnpa98w4rxn";
        public const string ConsumerKey = "xvz1evFS4wEEPTGEFPHBog";
        public const string ConsumerSecret = "kAcSOqF21Fu85e7zjz7ZN2U4ZRhfV3WpwPAoE3Z7kBw";
        public const string OAuthToken = "370773112-GmHxMAgYyLbNEtIKZeRNFsMKPR9EyMZeS9weJAEb";
        public const string OAuthTokenSecret = "LswwdoUaIvS8ltyTt5jkRh4J50vUPVVHtR2YPi5kE";
        public const string JwtSecret = "a-playground-secret-of-at-least-32-bytes";
        public const string ClientId = "demo-client";
        public const string ClientSecret = "demo-client-secret";
    }

    public string Host { get; } = host;
    public string Realm => $"playground@{Host}";

    private const int MaxIssued = 10_000;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _tokens = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _nonces = new();

    private static readonly JsonArray Orders =
    [
        new JsonObject { ["id"] = 41, ["customer"] = "Grace Hopper", ["total"] = 378.0, ["status"] = "shipped" },
        new JsonObject { ["id"] = 42, ["customer"] = "Ada Lovelace", ["total"] = 120.5, ["status"] = "open" },
        new JsonObject { ["id"] = 43, ["customer"] = "Alan Turing", ["total"] = 79.0, ["status"] = "cancelled" },
    ];

    public HttpMessageHandler Handler(bool refuseOtherHosts) => new DemoHandler(this, refuseOtherHosts);

    private sealed class DemoHandler(DemoApi api, bool refuseOtherHosts) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!string.Equals(request.RequestUri?.Host, api.Host, StringComparison.OrdinalIgnoreCase))
            {
                if (refuseOtherHosts)
                    throw new HostRefusedException($"The hosted playground sends only to its demo API ({api.Host}). Run the playground yourself (docker) to call any API.");
                throw new InvalidOperationException("The demo handler only answers the demo API.");
            }
            var response = await api.AnswerAsync(request, ct);
            response.RequestMessage = request; // as a real handler does; Digest reads the host from it
            return response;
        }
    }

    // ---- routing -------------------------------------------------------------------------------------------------

    internal async Task<HttpResponseMessage> AnswerAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
        var path = uri.AbsolutePath.TrimEnd('/');
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var method = request.Method.Method;
        var auth = Verify(request, body);

        if (segments.Length == 0) return Json(HttpStatusCode.OK, Index());
        switch (segments[0])
        {
            case "echo":
                return Json(HttpStatusCode.OK, Echo(request, body, auth));
            case "status" when segments.Length == 2 && int.TryParse(segments[1], out var code) && code is >= 200 and <= 599:
                return Json((HttpStatusCode)code, new JsonObject { ["status"] = code });
            case "slow":
                var ms = int.TryParse(Query(uri, "ms"), out var wanted) ? Math.Clamp(wanted, 0, 8000) : 1500;
                await Task.Delay(TimeSpan.FromMilliseconds(ms), clock, ct);
                return Json(HttpStatusCode.OK, new JsonObject { ["waitedMs"] = ms });
            case "oauth" when segments.Length == 2 && segments[1] == "token" && method == "POST":
                return IssueClientToken(request, body);
            case "auth" when segments.Length == 2 && segments[1] == "login" && method == "POST":
                return IssueLoginToken(request, body);
            case "orders":
                if (!auth.Valid) return Unauthorized(auth);
                return Orders_(segments, method, body, auth);
            default:
                return Json(HttpStatusCode.NotFound, new JsonObject { ["error"] = $"No {method} {path} on the demo API.", ["try"] = Index()["endpoints"]!.DeepClone() });
        }
    }

    private JsonObject Index() => new()
    {
        ["name"] = "Playground demo API",
        ["endpoints"] = new JsonArray(
            "ANY /echo/... (says what arrived and whether its sign-in checks out)",
            "GET /orders, GET /orders/{id}, POST /orders/{id}/notes (need any valid sign-in)",
            "GET /status/{code}", "GET /slow?ms=1500",
            "POST /oauth/token (client_credentials)", "POST /auth/login ({ username, password })"),
    };

    private HttpResponseMessage Orders_(string[] segments, string method, byte[] body, AuthVerdict auth)
    {
        if (segments.Length == 1 && method == "GET") return Json(HttpStatusCode.OK, new JsonObject { ["orders"] = Orders.DeepClone(), ["auth"] = auth.ToJson() });
        if (segments.Length >= 2 && int.TryParse(segments[1], out var id))
        {
            var order = Orders.OfType<JsonObject>().FirstOrDefault(o => o["id"]!.GetValue<int>() == id);
            if (order is null) return Json(HttpStatusCode.NotFound, new JsonObject { ["error"] = $"No order {id}." });
            if (segments.Length == 2 && method == "GET") return Json(HttpStatusCode.OK, new JsonObject { ["order"] = order.DeepClone(), ["auth"] = auth.ToJson() });
            if (segments.Length == 3 && segments[2] == "notes" && method == "POST")
            {
                var note = TryJson(body);
                return Json(HttpStatusCode.Created, new JsonObject
                {
                    ["id"] = RandomNumberGenerator.GetInt32(1000, 9999),
                    ["orderId"] = id,
                    ["note"] = note?.DeepClone() ?? Encoding.UTF8.GetString(body),
                    ["auth"] = auth.ToJson(),
                });
            }
        }
        return Json(HttpStatusCode.MethodNotAllowed, new JsonObject { ["error"] = "Not on the demo API." });
    }

    private static JsonObject Echo(HttpRequestMessage request, byte[] body, AuthVerdict auth)
    {
        var headers = new JsonObject();
        foreach (var (name, values) in request.Headers.NonValidated)
        {
            // Credentials are not echoed back: the answer's "auth" says whether they checked out, and an echo would
            // put a password (Basic) or a token into every copy of the answer.
            headers[name] = name is "Authorization" or "Proxy-Authorization" or "Cookie" or "X-API-Key"
                ? "(received and checked: see auth)"
                : string.Join(", ", values);
        }
        if (request.Content is not null)
            foreach (var (name, values) in request.Content.Headers.NonValidated) headers[name] = string.Join(", ", values);
        var query = new JsonObject();
        foreach (var (name, value) in OAuth1AuthStrategy.ParseForm(request.RequestUri!.Query.TrimStart('?')))
            query[name] = value;
        return new JsonObject
        {
            ["method"] = request.Method.Method,
            ["path"] = request.RequestUri.AbsolutePath,
            ["query"] = query,
            ["headers"] = headers,
            ["body"] = TryJson(body)?.DeepClone() ?? (body.Length == 0 ? null : Encoding.UTF8.GetString(body)),
            ["auth"] = auth.ToJson(),
        };
    }

    // ---- tokens --------------------------------------------------------------------------------------------------

    private HttpResponseMessage IssueClientToken(HttpRequestMessage request, byte[] body)
    {
        var form = OAuth1AuthStrategy.ParseForm(Encoding.UTF8.GetString(body)).ToDictionary(p => p.Key, p => p.Value);
        var id = form.GetValueOrDefault("client_id");
        var secret = form.GetValueOrDefault("client_secret");
        if (request.Headers.Authorization is { Scheme: "Basic", Parameter: { } basic } && Decode(basic) is { } pair)
            (id, secret) = pair;
        if (form.GetValueOrDefault("grant_type") != "client_credentials")
            return Json(HttpStatusCode.BadRequest, new JsonObject { ["error"] = "unsupported_grant_type" });
        if (id != Credentials.ClientId || secret != Credentials.ClientSecret)
            return Json(HttpStatusCode.Unauthorized, new JsonObject { ["error"] = "invalid_client" });
        return Json(HttpStatusCode.OK, new JsonObject { ["access_token"] = Issue(), ["token_type"] = "Bearer", ["expires_in"] = 300 });
    }

    private HttpResponseMessage IssueLoginToken(HttpRequestMessage request, byte[] body)
    {
        var json = TryJson(body) as JsonObject;
        var form = json is null ? OAuth1AuthStrategy.ParseForm(Encoding.UTF8.GetString(body)).ToDictionary(p => p.Key, p => p.Value) : null;
        var user = json?["username"]?.ToString() ?? form?.GetValueOrDefault("username");
        var password = json?["password"]?.ToString() ?? form?.GetValueOrDefault("password");
        if (user != Credentials.Username || password != Credentials.Password)
            return Json(HttpStatusCode.Unauthorized, new JsonObject { ["error"] = "Wrong username or password." });
        return Json(HttpStatusCode.OK, new JsonObject { ["token"] = Issue(), ["expires_in"] = 300 });
    }

    private string Issue()
    {
        var now = clock.GetUtcNow();
        if (_tokens.Count >= MaxIssued)
            foreach (var (t, until) in _tokens) if (until < now) _tokens.TryRemove(t, out _);
        if (_tokens.Count >= MaxIssued) _tokens.Clear();
        var token = "demo-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        _tokens[token] = now.AddMinutes(5);
        return token;
    }

    // ---- verification --------------------------------------------------------------------------------------------

    internal sealed record AuthVerdict(string Scheme, bool Valid, string Detail)
    {
        public JsonObject ToJson() => new() { ["scheme"] = Scheme, ["valid"] = Valid, ["detail"] = Detail };
    }

    internal AuthVerdict Verify(HttpRequestMessage request, byte[] body)
    {
        var uri = request.RequestUri!;
        if (request.Headers.TryGetValues("X-API-Key", out var keys) || Query(uri, "api_key") is not null)
        {
            var key = keys?.FirstOrDefault() ?? Query(uri, "api_key");
            return key == Credentials.ApiKey ? new("apiKey", true, "The API key matches.") : new("apiKey", false, $"Unknown API key; the demo key is {Credentials.ApiKey}.");
        }

        if (Query(uri, "oauth_signature") is not null) return VerifyOAuth1Query(request);
        if (Query(uri, "token") is { } queryToken) return VerifyBearer(queryToken, "jwtBearer (query)");

        var header = request.Headers.TryGetValues("Authorization", out var values) ? values.FirstOrDefault() : null;
        if (string.IsNullOrWhiteSpace(header)) return new("none", false, "No credentials were sent.");
        var space = header.IndexOf(' ');
        var scheme = space < 0 ? header : header[..space];
        var parameter = space < 0 ? "" : header[(space + 1)..].Trim();

        return scheme.ToLowerInvariant() switch
        {
            "basic" => Decode(parameter) is ({ } u, { } p) && u == Credentials.Username && p == Credentials.Password
                ? new("basic", true, "Username and password match.")
                : new("basic", false, $"Wrong username or password; the demo login is {Credentials.Username} / {Credentials.Password}."),
            "bearer" => VerifyBearer(parameter, "bearer"),
            "digest" => VerifyDigest(request, parameter, body),
            "hawk" => VerifyHawk(request, parameter, body),
            "oauth" => VerifyOAuth1Header(request, parameter, body),
            _ => new(scheme, false, $"The demo API does not know the {scheme} scheme."),
        };
    }

    private AuthVerdict VerifyBearer(string token, string scheme)
    {
        if (token == Credentials.BearerToken) return new(scheme, true, "The demo token matches.");
        if (_tokens.TryGetValue(token, out var until))
            return until > clock.GetUtcNow() ? new(scheme, true, "A token this API issued, still valid.") : new(scheme, false, "That token has expired; fetch a new one.");

        var parts = token.Split('.');
        if (parts.Length != 3) return new(scheme, false, $"Unknown token; the demo token is {Credentials.BearerToken}.");
        try
        {
            var header = JsonNode.Parse(Base64Url(parts[0]))!;
            if (header["alg"]?.GetValue<string>() != "HS256") return new("jwtBearer", false, "The demo API checks HS256 tokens only.");
            var expected = Base64UrlEncode(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Credentials.JwtSecret), Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}")));
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[2])))
                return new("jwtBearer", false, "The JWT's signature does not match the demo secret.");
            var claims = JsonNode.Parse(Base64Url(parts[1]))!;
            if (claims["exp"] is JsonValue exp && exp.TryGetValue<long>(out var seconds) && DateTimeOffset.FromUnixTimeSeconds(seconds) < clock.GetUtcNow())
                return new("jwtBearer", false, "The JWT has expired.");
            return new("jwtBearer", true, $"JWT signature valid (HS256), subject {claims["sub"]?.ToString() ?? "none"}.");
        }
        catch (Exception)
        {
            return new("jwtBearer", false, "That is not a readable JWT.");
        }
    }

    private AuthVerdict VerifyDigest(HttpRequestMessage request, string parameter, byte[] body)
    {
        var p = DigestAuthStrategy.Parameters(parameter);
        if (!p.TryGetValue("nonce", out var nonce) || !_nonces.ContainsKey(nonce)) return new("digest", false, "Unknown or old nonce: ask again without credentials to get a challenge.");
        if (p.GetValueOrDefault("username") != Credentials.DigestUsername) return new("digest", false, $"Unknown user; the demo user is {Credentials.DigestUsername}.");
        var algorithm = p.GetValueOrDefault("algorithm") ?? "MD5";
        var qop = p.GetValueOrDefault("qop");
        var nc = int.TryParse(p.GetValueOrDefault("nc"), System.Globalization.NumberStyles.HexNumber, null, out var n) ? n : 0;
        var expected = DigestAuthStrategy.Header(Credentials.DigestUsername, Credentials.DigestPassword, request.Method.Method,
            p.GetValueOrDefault("uri") ?? request.RequestUri!.PathAndQuery, p.GetValueOrDefault("realm") ?? Realm, nonce, p.GetValueOrDefault("opaque"),
            algorithm, qop, nc, p.GetValueOrDefault("cnonce") ?? "", body);
        return DigestAuthStrategy.Parameters(expected["Digest ".Length..]).GetValueOrDefault("response") == p.GetValueOrDefault("response")
            ? new("digest", true, $"Digest response valid ({algorithm}).")
            : new("digest", false, $"The digest response does not match; the demo password is \"{Credentials.DigestPassword}\".");
    }

    private AuthVerdict VerifyHawk(HttpRequestMessage request, string parameter, byte[] body)
    {
        var p = DigestAuthStrategy.Parameters(parameter);
        if (p.GetValueOrDefault("id") != Credentials.HawkId) return new("hawk", false, $"Unknown Hawk id; the demo id is {Credentials.HawkId}.");
        if (!long.TryParse(p.GetValueOrDefault("ts"), out var ts) || Math.Abs(clock.GetUtcNow().ToUnixTimeSeconds() - ts) > 300)
            return new("hawk", false, "The Hawk timestamp is more than 5 minutes off.");
        var hash = p.GetValueOrDefault("hash");
        foreach (var algorithm in new[] { "sha256", "sha1" })
        {
            var expected = HawkAuthStrategy.Header(Credentials.HawkId, Credentials.HawkKey, algorithm, request.Method.Method, request.RequestUri!, ts,
                p.GetValueOrDefault("nonce") ?? "", hash, p.GetValueOrDefault("ext"), p.GetValueOrDefault("app"), p.GetValueOrDefault("dlg"));
            if (DigestAuthStrategy.Parameters(expected["Hawk ".Length..]).GetValueOrDefault("mac") != p.GetValueOrDefault("mac")) continue;
            if (hash is not null)
            {
                var media = request.Content?.Headers.ContentType?.MediaType ?? "";
                if (HawkAuthStrategy.PayloadHash(algorithm, media, body) != hash) return new("hawk", false, "The MAC matches but the payload hash does not: the body changed.");
            }
            return new("hawk", true, $"Hawk MAC valid ({algorithm}){(hash is null ? "" : ", payload hash valid")}.");
        }
        return new("hawk", false, "The Hawk MAC does not match the demo key.");
    }

    private AuthVerdict VerifyOAuth1Header(HttpRequestMessage request, string parameter, byte[] body)
    {
        var p = DigestAuthStrategy.Parameters(parameter).ToDictionary(kv => kv.Key, kv => Uri.UnescapeDataString(kv.Value));
        var form = request.Content?.Headers.ContentType?.MediaType == "application/x-www-form-urlencoded"
            ? OAuth1AuthStrategy.ParseForm(Encoding.UTF8.GetString(body))
            : [];
        return CheckOAuth1(request.Method.Method, request.RequestUri!, p.Where(kv => kv.Key != "realm" && kv.Key != "oauth_signature"), form, p.GetValueOrDefault("oauth_signature"));
    }

    private AuthVerdict VerifyOAuth1Query(HttpRequestMessage request)
    {
        var uri = request.RequestUri!;
        var all = OAuth1AuthStrategy.ParseForm(uri.Query.TrimStart('?'));
        var signature = all.FirstOrDefault(kv => kv.Key == "oauth_signature").Value;
        var rest = string.Join("&", all.Where(kv => kv.Key != "oauth_signature").Select(kv => $"{OAuth1AuthStrategy.Encode(kv.Key)}={OAuth1AuthStrategy.Encode(kv.Value)}"));
        var unsigned = new UriBuilder(uri) { Query = rest }.Uri;
        return CheckOAuth1(request.Method.Method, unsigned, [], [], signature);
    }

    private static AuthVerdict CheckOAuth1(string method, Uri uri, IEnumerable<KeyValuePair<string, string>> oauth, IEnumerable<KeyValuePair<string, string>> form, string? signature)
    {
        var list = oauth.ToList();
        var allParams = list.Concat(OAuth1AuthStrategy.ParseForm(uri.Query.TrimStart('?'))).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (allParams.GetValueOrDefault("oauth_consumer_key") != Credentials.ConsumerKey) return new("oauth1", false, $"Unknown consumer key; the demo key is {Credentials.ConsumerKey}.");
        var signatureMethod = allParams.GetValueOrDefault("oauth_signature_method") ?? "HMAC-SHA1";
        if (!signatureMethod.StartsWith("HMAC") && signatureMethod != "PLAINTEXT") return new("oauth1", false, "The demo API checks HMAC and PLAINTEXT signatures.");
        var tokenSecret = allParams.ContainsKey("oauth_token") ? Credentials.OAuthTokenSecret : null;
        var expected = OAuth1AuthStrategy.Signature(method, uri, list, form, Credentials.ConsumerSecret, tokenSecret, signatureMethod);
        return expected == signature
            ? new("oauth1", true, $"OAuth 1.0a signature valid ({signatureMethod}).")
            : new("oauth1", false, "The OAuth 1.0a signature does not match the demo consumer and token secrets.");
    }

    private HttpResponseMessage Unauthorized(AuthVerdict verdict)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        if (_nonces.Count >= MaxIssued) _nonces.Clear();
        _nonces[nonce] = clock.GetUtcNow();
        var response = Json(HttpStatusCode.Unauthorized, new JsonObject { ["error"] = "Sign in to use /orders.", ["auth"] = verdict.ToJson() });
        response.Headers.TryAddWithoutValidation("WWW-Authenticate", $"Digest realm=\"{Realm}\", qop=\"auth\", algorithm=SHA-256, nonce=\"{nonce}\", opaque=\"demo\"");
        response.Headers.TryAddWithoutValidation("WWW-Authenticate", $"Bearer realm=\"{Realm}\"");
        return response;
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------

    private static HttpResponseMessage Json(HttpStatusCode status, JsonNode body)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        response.Headers.TryAddWithoutValidation("X-Demo-Api", "playground");
        return response;
    }

    private static JsonNode? TryJson(byte[] body)
    {
        if (body.Length == 0) return null;
        try { return JsonNode.Parse(body); } catch (System.Text.Json.JsonException) { return null; }
    }

    private static string? Query(Uri uri, string name) =>
        OAuth1AuthStrategy.ParseForm(uri.Query.TrimStart('?')).FirstOrDefault(kv => kv.Key == name).Value;

    private static (string, string)? Decode(string basic)
    {
        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(basic));
            var colon = text.IndexOf(':');
            return colon < 0 ? null : (text[..colon], text[(colon + 1)..]);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Base64Url(string text) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/').PadRight((text.Length + 3) / 4 * 4, '=')));

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>The hosted playground was asked to send somewhere other than its demo API.</summary>
public sealed class HostRefusedException(string message) : InvalidOperationException(message);
