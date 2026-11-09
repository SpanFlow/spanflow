using System.Text.Json.Nodes;

namespace SpanFlow.Connectors.Http.Auth;

/// <summary>What the status panel may know about a stored token: never the token itself.</summary>
public sealed record TokenStatus(
    bool HasToken, DateTimeOffset? ExpiresAt, DateTimeOffset? FetchedAt, string? LastError, DateTimeOffset? LastErrorAt);

/// <summary>
/// Tokens shared by every run, sync job and instance (E68-T2, D68.7). One row per auth config (an HMAC
/// fingerprint of it), encrypted. A fetch happens under a lock that spans instances, so when a token nears
/// expiry one caller logs in and the rest read what it wrote.
/// </summary>
public interface ISharedTokenStore
{
    /// <summary>The key for an auth config: HMAC-SHA256 under this install's secret pepper.</summary>
    Task<string> FingerprintAsync(JsonObject auth, CancellationToken ct);

    /// <summary>
    /// The stored token when <paramref name="isUsable"/> accepts it; otherwise <paramref name="fetch"/> runs once
    /// under the cross-instance lock (handed the stored token, so a refresh token can be used) and its result is
    /// stored. A failed fetch is recorded for the status panel and rethrown.
    /// </summary>
    Task<TokenCache.Token> GetOrFetchAsync(
        string fingerprint, Func<TokenCache.Token, bool> isUsable,
        Func<TokenCache.Token?, CancellationToken, Task<TokenCache.Token>> fetch, CancellationToken ct);

    /// <summary>
    /// Marks the stored token stale (refresh values kept), but only while it is still <paramref name="staleValue"/>:
    /// another instance may already have replaced the token that was just refused.
    /// </summary>
    Task InvalidateAsync(string fingerprint, string? staleValue, CancellationToken ct);

    /// <summary>Deletes the stored token and its values.</summary>
    Task ForgetAsync(string fingerprint, CancellationToken ct);

    Task<TokenStatus> StatusAsync(string fingerprint, CancellationToken ct);
}

/// <summary>Names of the values an <see cref="ISharedTokenStore"/> keeps beside a token.</summary>
public static class SharedTokenKeys
{
    /// <summary>The refresh token a login or token endpoint returned, used to renew the access token.</summary>
    public const string Refresh = "refreshToken";
}
