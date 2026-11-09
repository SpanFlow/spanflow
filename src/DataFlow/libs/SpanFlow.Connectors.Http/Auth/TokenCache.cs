using System.Collections.Concurrent;

namespace SpanFlow.Connectors.Http.Auth;

/// <summary>
/// Per-key in-memory token cache with single-flight acquisition: while one caller is acquiring a
/// token for a key, concurrent callers wait and reuse the result instead of each hitting the token
/// endpoint. Expiry-aware (with a small skew buffer, or the strategy's own renew-before rule). Keyed by
/// the auth config's hash. Since E68-T2 it sits in front of the shared <see cref="ISharedTokenStore"/>,
/// which is what makes the token the same across instances and restarts.
/// </summary>
public sealed class TokenCache
{
    /// <param name="Value">The token.</param>
    /// <param name="ExpiresAt">When it stops being valid; null = unknown.</param>
    /// <param name="RefreshToken">A refresh token, when the grant gave one.</param>
    /// <param name="Values">Named extra values a <c>loginRequest</c> pulled from its response (E68-T2).</param>
    /// <param name="FetchedAt">When it was fetched, so "renew before" can be capped at half the lifetime.</param>
    public sealed record Token(
        string Value, DateTimeOffset? ExpiresAt, string? RefreshToken,
        IReadOnlyDictionary<string, string>? Values = null, DateTimeOffset? FetchedAt = null);

    private readonly ConcurrentDictionary<string, Token> _tokens = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    /// <summary>Number of times an acquire actually ran — lets tests assert single-flight/caching.</summary>
    public int AcquireCount;

    public async Task<string> GetOrAcquireAsync(
        string key, Func<Token?, CancellationToken, Task<Token>> acquire, CancellationToken ct) =>
        (await GetOrAcquireTokenAsync(key, acquire, ct)).Value;

    /// <summary>
    /// The cached token when <paramref name="isUsable"/> (default: not within 5 s of expiry) says it still is,
    /// otherwise the result of one <paramref name="acquire"/> shared by every concurrent caller.
    /// </summary>
    public async Task<Token> GetOrAcquireTokenAsync(
        string key, Func<Token?, CancellationToken, Task<Token>> acquire, CancellationToken ct,
        Func<Token, bool>? isUsable = null)
    {
        isUsable ??= t => !IsExpired(t);
        if (_tokens.TryGetValue(key, out var cached) && isUsable(cached)) return cached;

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Re-check: another caller may have acquired while we waited.
            if (_tokens.TryGetValue(key, out var again) && isUsable(again)) return again;

            _tokens.TryGetValue(key, out var existing);
            var fresh = await acquire(existing, ct);
            Interlocked.Increment(ref AcquireCount);
            _tokens[key] = fresh;
            return fresh;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>The token this process holds for <paramref name="key"/>, usable or not (null when none).</summary>
    public Token? Peek(string key) => _tokens.TryGetValue(key, out var token) ? token : null;

    /// <summary>
    /// Marks the cached access token stale (forcing re-acquisition) but keeps its refresh token,
    /// so the next acquire can refresh instead of doing a full re-login.
    /// </summary>
    public void Invalidate(string key)
    {
        if (_tokens.TryGetValue(key, out var token))
        {
            _tokens[key] = token with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) };
        }
    }

    /// <summary>Drops the token for <paramref name="key"/> entirely, refresh token included ("Forget token").</summary>
    public void Remove(string key) => _tokens.TryRemove(key, out _);

    private static bool IsExpired(Token token) =>
        token.ExpiresAt is DateTimeOffset expires && expires <= DateTimeOffset.UtcNow.AddSeconds(5);
}
