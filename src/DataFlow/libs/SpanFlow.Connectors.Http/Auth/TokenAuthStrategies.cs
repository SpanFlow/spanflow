using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SpanFlow.Expressions;

namespace SpanFlow.Connectors.Http.Auth;

/// <summary>
/// Shared plumbing for token-acquiring auth strategies: cache-key derivation, single-flight
/// acquisition through <see cref="TokenCache"/>, applying the token as an Authorization header,
/// and invalidation (for retry-on-401). With an <see cref="ISharedTokenStore"/> (E68-T2) the token is
/// kept in the database too, so every instance and every restart uses the same one until it nears expiry;
/// without one (tests, the static registry) it lives in this process only, as before.
/// </summary>
public abstract class TokenAuthStrategyBase(IHttpClientFactory httpClientFactory, TokenCache cache, ISharedTokenStore? store = null)
    : IInvalidatableAuthStrategy, IVerifiableAuthStrategy, ITokenStatusStrategy
{
    public abstract string Type { get; }
    public abstract IReadOnlyList<string> Validate(JsonObject auth);

    protected abstract Task<TokenCache.Token> AcquireAsync(JsonObject auth, TokenCache.Token? existing, CancellationToken ct);

    /// <summary>Proves the credentials by doing a fresh token acquisition (throws on failure).</summary>
    public async Task VerifyAsync(JsonObject auth, CancellationToken ct)
    {
        var errors = Validate(auth);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        await AcquireAsync(auth, existing: null, ct);
    }

    /// <summary>Authorization header value; override for a custom format (tokenEndpoint headerFormat).</summary>
    protected virtual string HeaderValue(JsonObject auth, string token) => $"Bearer {token}";

    /// <summary>Puts the token on the request; the default is an Authorization header (<see cref="HeaderValue"/>).</summary>
    protected virtual void ApplyToken(HttpRequestMessage message, JsonObject auth, TokenCache.Token token) =>
        message.Headers.TryAddWithoutValidation("Authorization", HeaderValue(auth, token.Value));

    /// <summary>Whether a held token may still be sent. Default: not within 5 s of its expiry.</summary>
    protected virtual bool IsUsable(JsonObject auth, TokenCache.Token token) =>
        token.ExpiresAt is not DateTimeOffset expires || expires > DateTimeOffset.UtcNow.AddSeconds(5);

    public async Task ApplyAsync(HttpRequestMessage message, JsonObject auth, CancellationToken ct) =>
        ApplyToken(message, auth, await GetTokenAsync(auth, ct));

    /// <summary>The token to send: this process's copy, else the shared store's, else one fetch for everyone.</summary>
    protected async Task<TokenCache.Token> GetTokenAsync(JsonObject auth, CancellationToken ct)
    {
        bool Usable(TokenCache.Token t) => IsUsable(auth, t);
        return await cache.GetOrAcquireTokenAsync(CacheKey(auth), async (existing, c) =>
        {
            if (store is null) return await AcquireAsync(auth, existing, c);
            var fingerprint = await store.FingerprintAsync(auth, c);
            return await store.GetOrFetchAsync(fingerprint, Usable, (stored, c2) => AcquireAsync(auth, stored ?? existing, c2), c);
        }, ct, Usable);
    }

    public async Task InvalidateAsync(JsonObject auth, CancellationToken ct)
    {
        var key = CacheKey(auth);
        var refused = cache.Peek(key)?.Value;
        cache.Invalidate(key);
        if (store is not null) await store.InvalidateAsync(await store.FingerprintAsync(auth, ct), refused, ct);
    }

    public async Task<TokenStatus> StatusAsync(JsonObject auth, CancellationToken ct)
    {
        if (store is not null) return await store.StatusAsync(await store.FingerprintAsync(auth, ct), ct);
        var held = cache.Peek(CacheKey(auth));
        return new TokenStatus(held is not null, held?.ExpiresAt, held?.FetchedAt, null, null);
    }

    public async Task<TokenStatus> FetchNowAsync(JsonObject auth, CancellationToken ct)
    {
        await ForgetAsync(auth, ct);
        await GetTokenAsync(auth, ct);
        return await StatusAsync(auth, ct);
    }

    public async Task ForgetAsync(JsonObject auth, CancellationToken ct)
    {
        cache.Remove(CacheKey(auth));
        if (store is not null) await store.ForgetAsync(await store.FingerprintAsync(auth, ct), ct);
    }

    protected HttpClient Client() => httpClientFactory.CreateClient(HttpClientNames.Default);

    protected static string CacheKey(JsonObject auth) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(auth.ToJsonString())));

    protected static DateTimeOffset? ExpiryFrom(JsonObject response) =>
        response["expires_in"] is JsonNode e && double.TryParse(e.ToString(), out var seconds)
            ? DateTimeOffset.UtcNow.AddSeconds(seconds)
            : null;

    /// <summary>Extracts a string value from a response by a "$.a.b" / "a.b" path (reuses the expression evaluator).</summary>
    protected static string? ExtractPath(JsonNode response, string path)
    {
        var value = ExpressionEvaluator.Evaluate($"input.{path.TrimStart('$', '.')}", new ExpressionContext { Input = response });
        return value is JsonValue v && v.TryGetValue<string>(out var s) ? s : value?.ToString();
    }

    protected async Task<JsonObject> PostAsync(string url, string method, HttpContent content, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(new HttpMethod(method), url) { Content = content };
        using var response = await Client().SendAsync(message, ct);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(ct);
        return JsonNode.Parse(text) as JsonObject
            ?? throw new InvalidOperationException($"token endpoint '{url}' did not return a JSON object.");
    }
}

/// <summary>
/// OAuth2 client-credentials grant:
/// {"tokenUrl","clientId","clientSecret","scopes"?} → POST form → Bearer access_token.
/// </summary>
public sealed class Oauth2ClientCredentialsStrategy(IHttpClientFactory httpClientFactory, TokenCache cache, ISharedTokenStore? store = null)
    : TokenAuthStrategyBase(httpClientFactory, cache, store)
{
    public override string Type => "oauth2ClientCredentials";

    public override IReadOnlyList<string> Validate(JsonObject auth)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(auth["tokenUrl"]?.GetValue<string>())) errors.Add("oauth2ClientCredentials requires a 'tokenUrl'.");
        if (string.IsNullOrWhiteSpace(auth["clientId"]?.GetValue<string>())) errors.Add("oauth2ClientCredentials requires a 'clientId'.");
        if (string.IsNullOrWhiteSpace(auth["clientSecret"]?.GetValue<string>())) errors.Add("oauth2ClientCredentials requires a 'clientSecret'.");
        return errors;
    }

    protected override async Task<TokenCache.Token> AcquireAsync(JsonObject auth, TokenCache.Token? existing, CancellationToken ct)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "client_credentials"),
            new("client_id", auth["clientId"]!.GetValue<string>()),
            new("client_secret", auth["clientSecret"]!.GetValue<string>()),
        };
        if (auth["scopes"] is JsonArray scopes && scopes.Count > 0)
        {
            form.Add(new("scope", string.Join(" ", scopes.Select(s => s!.GetValue<string>()))));
        }

        var response = await PostAsync(auth["tokenUrl"]!.GetValue<string>(), "POST", new FormUrlEncodedContent(form), ct);
        var token = response["access_token"]?.GetValue<string>()
            ?? throw new InvalidOperationException("oauth2 token response had no 'access_token'.");
        return new TokenCache.Token(token, ExpiryFrom(response), RefreshToken: null);
    }
}

/// <summary>
/// Username/password (or arbitrary) login endpoint:
/// {"tokenUrl","method"?,"bodyTemplate","tokenPath","refreshTokenPath"?,"refreshUrl"?,"headerFormat"?}.
/// Full login POSTs bodyTemplate; when a refresh token + refreshUrl are known, refreshes instead
/// (falling back to a full login if the refresh fails).
/// </summary>
public sealed class TokenEndpointStrategy(IHttpClientFactory httpClientFactory, TokenCache cache, ISharedTokenStore? store = null)
    : TokenAuthStrategyBase(httpClientFactory, cache, store)
{
    public override string Type => "tokenEndpoint";

    public override IReadOnlyList<string> Validate(JsonObject auth)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(auth["tokenUrl"]?.GetValue<string>())) errors.Add("tokenEndpoint requires a 'tokenUrl'.");
        if (string.IsNullOrWhiteSpace(auth["tokenPath"]?.GetValue<string>())) errors.Add("tokenEndpoint requires a 'tokenPath'.");
        return errors;
    }

    protected override string HeaderValue(JsonObject auth, string token) =>
        (auth["headerFormat"]?.GetValue<string>() ?? "Bearer {token}").Replace("{token}", token);

    protected override async Task<TokenCache.Token> AcquireAsync(JsonObject auth, TokenCache.Token? existing, CancellationToken ct)
    {
        var tokenPath = auth["tokenPath"]!.GetValue<string>();
        var refreshTokenPath = auth["refreshTokenPath"]?.GetValue<string>();

        JsonObject? response = null;
        if (existing?.RefreshToken is string refreshToken && auth["refreshUrl"]?.GetValue<string>() is string refreshUrl)
        {
            try
            {
                var body = new JsonObject { ["refresh_token"] = refreshToken };
                response = await PostJsonAsync(refreshUrl, "POST", body, ct);
            }
            catch
            {
                response = null; // refresh failed → fall back to a full login
            }
        }

        response ??= await PostJsonAsync(
            auth["tokenUrl"]!.GetValue<string>(),
            auth["method"]?.GetValue<string>() ?? "POST",
            auth["bodyTemplate"] as JsonObject ?? new JsonObject(),
            ct);

        var token = ExtractPath(response, tokenPath)
            ?? throw new InvalidOperationException($"token response had no value at '{tokenPath}'.");
        var newRefresh = refreshTokenPath is not null ? ExtractPath(response, refreshTokenPath) : existing?.RefreshToken;
        return new TokenCache.Token(token, ExpiryFrom(response), newRefresh);
    }

    private Task<JsonObject> PostJsonAsync(string url, string method, JsonObject body, CancellationToken ct) =>
        PostAsync(url, method, new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), ct);
}
