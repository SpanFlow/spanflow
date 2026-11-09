using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SpanFlow.Connectors.Http;

namespace SpanFlow.Connectors.Http.Auth;

/// <summary>
/// A token fetched with a request the user writes (E68-T2, D68.6), in any body type the api connector can
/// send, so a form-encoded <c>grant_type=password&amp;…</c> login works as well as a JSON one.
/// <code>
/// {
///   "type": "loginRequest",
///   "request": { "method": "POST", "url": "/oauth/token", "bodyType": "urlencoded", "body": [ … ] },
///   "token":   { "from": "body", "path": "access_token" },          // or header / cookie + "name"
///   "values":  { "refreshToken": { "from": "body", "path": "refresh_token" } },
///   "apply":   [ { "in": "header", "name": "Authorization", "value": "Bearer {{token}}" } ],
///   "lifetime": { "seconds": 3300 },                                 // or expiresInPath; a JWT's exp otherwise
///   "renewBeforeSeconds": 60,
///   "refreshRequest": { "method": "POST", "url": "/oauth/token", "bodyType": "urlencoded",
///                       "body": [ { "key": "refresh_token", "value": "{{refreshToken}}" } ] }
/// }
/// </code>
/// <c>request.url</c> is a path on the connection's base URL (like any request on it) or a full URL. The
/// token is kept in the shared store, renewed before it expires and fetched again when the API answers 401.
/// </summary>
public sealed partial class LoginRequestStrategy(
    IHttpClientFactory httpClientFactory, TokenCache cache, ISharedTokenStore? store = null)
    : TokenAuthStrategyBase(httpClientFactory, cache, store), IDefaultRetryOnStrategy
{
    public const string TypeName = "loginRequest";

    /// <summary>Added to the auth by the connector: the connection's base URL, which a relative login URL is on.</summary>
    public const string BaseUrlKey = "connectionBaseUrl";

    private const int DefaultRenewBeforeSeconds = 60;
    private const int DefaultTimeoutSeconds = 30;

    private readonly IHttpClientFactory _clients = httpClientFactory;

    public override string Type => TypeName;

    public JsonObject DefaultRetryOn => new() { ["statusCodes"] = new JsonArray(401) };

    public override IReadOnlyList<string> Validate(JsonObject auth)
    {
        var errors = new List<string>();
        if (auth["request"] is not JsonObject request)
        {
            errors.Add("loginRequest needs a 'request': the call that returns the token.");
        }
        else if (string.IsNullOrWhiteSpace(request["url"]?.GetValue<string>()))
        {
            errors.Add("loginRequest 'request' needs a 'url' (a path on the connection, or a full URL).");
        }

        ValidateSource(auth["token"], "token", errors);
        if (auth["values"] is JsonObject values)
        {
            foreach (var (name, spec) in values) ValidateSource(spec, $"values.{name}", errors);
        }

        if (auth["apply"] is JsonArray apply)
        {
            foreach (var row in apply)
            {
                var place = row?["in"]?.GetValue<string>() ?? "header";
                if (place is not ("header" or "query" or "cookie")) errors.Add($"loginRequest apply 'in' must be header, query or cookie, not '{place}'.");
                if (string.IsNullOrWhiteSpace(row?["name"]?.GetValue<string>())) errors.Add("every loginRequest apply row needs a 'name'.");
            }
        }

        if (auth["refreshRequest"] is JsonObject refresh && string.IsNullOrWhiteSpace(refresh["url"]?.GetValue<string>()))
        {
            errors.Add("loginRequest 'refreshRequest' needs a 'url'.");
        }
        return errors;
    }

    private static void ValidateSource(JsonNode? spec, string what, List<string> errors)
    {
        if (spec is null) return;
        var from = spec["from"]?.GetValue<string>() ?? "body";
        if (from is not ("body" or "header" or "cookie")) errors.Add($"loginRequest {what} 'from' must be body, header or cookie, not '{from}'.");
        if (from is "header" or "cookie" && string.IsNullOrWhiteSpace(spec["name"]?.GetValue<string>()))
        {
            errors.Add($"loginRequest {what} from {from} needs a 'name'.");
        }
    }

    // ---------------------------------------------------------------- fetching

    protected override async Task<TokenCache.Token> AcquireAsync(JsonObject auth, TokenCache.Token? existing, CancellationToken ct)
    {
        if (existing?.RefreshToken is not null && auth["refreshRequest"] is JsonObject refreshRequest)
        {
            try
            {
                var refreshed = await SendAsync(auth, refreshRequest, existing, ct);
                return ReadToken(auth, refreshed, existing);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A refused refresh token means a full login, not a failed run.
            }
        }

        var response = await SendAsync(auth, (JsonObject)auth["request"]!, existing: null, ct);
        return ReadToken(auth, response, existing: null);
    }

    private sealed record LoginResponse(Dictionary<string, List<string>> Headers, string Body);

    private async Task<LoginResponse> SendAsync(JsonObject auth, JsonObject template, TokenCache.Token? existing, CancellationToken ct)
    {
        var request = (JsonObject)Render(template.DeepClone(), existing)!;
        var url = request["url"]!.GetValue<string>();
        string baseUrl;
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
        {
            baseUrl = absolute.GetLeftPart(UriPartial.Authority);
            request["path"] = absolute.PathAndQuery;
        }
        else
        {
            baseUrl = auth[BaseUrlKey]?.GetValue<string>()
                ?? throw new InvalidOperationException($"loginRequest url '{url}' is relative, and there is no connection base URL to put it on; use a full URL.");
            request["path"] = url;
        }

        var settings = HttpRequestSettings.Parse(request["settings"]);
        using var message = new HttpRequestMessage(
            new HttpMethod(request["method"]?.GetValue<string>()?.ToUpperInvariant() ?? "POST"),
            HttpRequestBuilder.BuildUri(baseUrl, request, settings));
        message.Content = HttpRequestBuilder.BuildContent(request, explicitBody: null);
        HttpRequestBuilder.ApplyHeaders(message, new Dictionary<string, string>(), request, settings);

        var client = _clients.CreateClient(settings.FollowRedirects ? HttpClientNames.Default : HttpClientNames.NoRedirect);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var seconds = settings.TimeoutSeconds ?? DefaultTimeoutSeconds;
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(message, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"The login request took longer than {seconds} s.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"The login request answered {(int)response.StatusCode} {response.ReasonPhrase}: {Snippet(body)}");
            }

            var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, values) in response.Headers.Concat(response.Content.Headers))
            {
                if (!headers.TryGetValue(name, out var list)) headers[name] = list = [];
                list.AddRange(values);
            }
            return new LoginResponse(headers, body);
        }
    }

    private static TokenCache.Token ReadToken(JsonObject auth, LoginResponse response, TokenCache.Token? existing)
    {
        JsonNode? json;
        try { json = string.IsNullOrWhiteSpace(response.Body) ? null : JsonNode.Parse(response.Body); }
        catch (System.Text.Json.JsonException) { json = null; }

        var tokenSpec = auth["token"];
        var token = Extract(tokenSpec, "access_token", response, json)
            ?? throw new InvalidOperationException($"The login response has no token at {Describe(tokenSpec, "access_token")}.");

        var values = new Dictionary<string, string>();
        string? refresh = null;
        if (auth["values"] is JsonObject wanted)
        {
            foreach (var (name, spec) in wanted)
            {
                if (Extract(spec, name, response, json) is not string value) continue;
                if (name == SharedTokenKeys.Refresh) refresh = value;
                else values[name] = value;
            }
        }
        // A refresh answer that leaves out the refresh token means "keep using the one you have".
        refresh ??= existing?.RefreshToken;

        var now = DateTimeOffset.UtcNow;
        return new TokenCache.Token(token, ExpiryOf(auth["lifetime"], token, json, now), refresh, values, now);
    }

    /// <summary>
    /// When the token expires. A lifetime the user typed (<c>seconds</c>) wins; else the response's
    /// <c>expiresInPath</c> (default <c>expires_in</c>); else a JWT token's own <c>exp</c>. Null when nothing says.
    /// </summary>
    internal static DateTimeOffset? ExpiryOf(JsonNode? lifetime, string token, JsonNode? json, DateTimeOffset now)
    {
        if (lifetime?["seconds"] is JsonNode s && double.TryParse(s.ToString(), out var fixedSeconds) && fixedSeconds > 0)
        {
            return now.AddSeconds(fixedSeconds);
        }

        var path = lifetime?["expiresInPath"]?.GetValue<string>() ?? "expires_in";
        if (json is not null && ExtractPath(json, path) is string inText && double.TryParse(inText, out var expiresIn) && expiresIn > 0)
        {
            return now.AddSeconds(expiresIn);
        }

        var useJwt = lifetime?["fromJwt"] is not JsonValue j || !j.TryGetValue<bool>(out var flag) || flag;
        return useJwt ? JwtExpiry(token) : null;
    }

    /// <summary>The <c>exp</c> of a JWT (header.payload.signature), read without verifying: we only need to know when to renew.</summary>
    internal static DateTimeOffset? JwtExpiry(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var claims = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return claims?["exp"] is JsonValue exp && exp.TryGetValue<long>(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static string? Extract(JsonNode? spec, string defaultPath, LoginResponse response, JsonNode? json)
    {
        var from = spec?["from"]?.GetValue<string>() ?? "body";
        switch (from)
        {
            case "header":
                var header = spec!["name"]!.GetValue<string>();
                return response.Headers.TryGetValue(header, out var values) ? values.FirstOrDefault() : null;
            case "cookie":
                var cookie = spec!["name"]!.GetValue<string>();
                if (!response.Headers.TryGetValue("Set-Cookie", out var setCookies)) return null;
                foreach (var line in setCookies)
                {
                    var pair = line.Split(';', 2)[0];
                    var eq = pair.IndexOf('=');
                    if (eq > 0 && pair[..eq].Trim() == cookie) return pair[(eq + 1)..].Trim();
                }
                return null;
            default:
                var path = spec?["path"]?.GetValue<string>() ?? defaultPath;
                if (path is "" or "$")
                {
                    // The whole body is the token (some login endpoints answer with just the text).
                    return json is JsonValue v && v.TryGetValue<string>(out var whole) ? whole
                        : json is null && response.Body.Trim().Length > 0 ? response.Body.Trim() : null;
                }
                return json is null ? null : ExtractPath(json, path) is { Length: > 0 } found ? found : null;
        }
    }

    private static string Describe(JsonNode? spec, string defaultPath)
    {
        var from = spec?["from"]?.GetValue<string>() ?? "body";
        return from == "body"
            ? $"body path '{spec?["path"]?.GetValue<string>() ?? defaultPath}'"
            : $"{from} '{spec?["name"]?.GetValue<string>()}'";
    }

    private static string Snippet(string body)
    {
        var text = body.ReplaceLineEndings(" ").Trim();
        return text.Length <= 300 ? text : text[..300] + "…";
    }

    // ---------------------------------------------------------------- using it

    /// <summary>
    /// Renew <c>renewBeforeSeconds</c> (default 60) before expiry, but never more than half the token's lifetime
    /// before it, so a token that lives 60 s isn't fetched again on every call.
    /// </summary>
    protected override bool IsUsable(JsonObject auth, TokenCache.Token token)
    {
        if (token.ExpiresAt is not DateTimeOffset expires) return true;
        var renewBefore = TimeSpan.FromSeconds(
            auth["renewBeforeSeconds"] is JsonNode r && int.TryParse(r.ToString(), out var seconds) && seconds >= 0 ? seconds : DefaultRenewBeforeSeconds);
        if (token.FetchedAt is DateTimeOffset fetched && (expires - fetched) / 2 < renewBefore) renewBefore = (expires - fetched) / 2;
        return DateTimeOffset.UtcNow < expires - renewBefore;
    }

    protected override void ApplyToken(HttpRequestMessage message, JsonObject auth, TokenCache.Token token)
    {
        var rows = auth["apply"] as JsonArray
            ?? [new JsonObject { ["in"] = "header", ["name"] = "Authorization", ["value"] = "Bearer {{token}}" }];
        foreach (var row in rows)
        {
            if (row is null) continue;
            var name = row["name"]!.GetValue<string>();
            var value = RenderText(row["value"]?.GetValue<string>() ?? "{{token}}", token);
            switch (row["in"]?.GetValue<string>() ?? "header")
            {
                case "query":
                    var uri = message.RequestUri!;
                    var separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
                    message.RequestUri = new Uri($"{uri}{separator}{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
                    break;
                case "cookie":
                    var cookie = $"{name}={value}";
                    if (message.Headers.TryGetValues("Cookie", out var existing))
                    {
                        cookie = string.Join("; ", existing) + "; " + cookie;
                        message.Headers.Remove("Cookie");
                    }
                    message.Headers.TryAddWithoutValidation("Cookie", cookie);
                    break;
                default:
                    message.Headers.Remove(name);
                    message.Headers.TryAddWithoutValidation(name, value);
                    break;
            }
        }
    }

    [GeneratedRegex(@"\{\{\s*(?<name>token|refreshToken|values\.[A-Za-z0-9_\-]+)\s*\}\}")]
    private static partial Regex Placeholder();

    /// <summary>Fills <c>{{token}}</c>, <c>{{refreshToken}}</c> and <c>{{values.name}}</c>; anything else is left as written.</summary>
    internal static string RenderText(string text, TokenCache.Token? token) =>
        Placeholder().Replace(text, match => match.Groups["name"].Value switch
        {
            "token" => token?.Value ?? "",
            "refreshToken" => token?.RefreshToken ?? "",
            var other => token?.Values is { } values && values.TryGetValue(other["values.".Length..], out var v) ? v : "",
        });

    private static JsonNode? Render(JsonNode? node, TokenCache.Token? token) => node switch
    {
        JsonObject obj => new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key, Render(p.Value, token)))),
        JsonArray array => new JsonArray(array.Select(item => Render(item, token)).ToArray()),
        JsonValue v when v.TryGetValue<string>(out var s) => JsonValue.Create(RenderText(s, token)),
        _ => node?.DeepClone(),
    };
}
