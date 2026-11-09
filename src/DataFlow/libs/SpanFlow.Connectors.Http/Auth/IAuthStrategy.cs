using System.Text.Json.Nodes;

namespace SpanFlow.Connectors.Http.Auth;

/// <summary>
/// Applies one authentication scheme to an outgoing API request. One implementation per
/// <c>config.auth.type</c> ("none"/"apiKey"/"basic"/"bearer" today; oauth2/tokenEndpoint in E03-T4),
/// resolved by <see cref="AuthStrategyRegistry"/> and applied by the HTTP connector that sends the request.
/// </summary>
public interface IAuthStrategy
{
    /// <summary>Matched (case-insensitively) against <c>config.auth.type</c>.</summary>
    string Type { get; }

    /// <summary>Config errors (empty when valid) — surfaced by the connection test.</summary>
    IReadOnlyList<string> Validate(JsonObject auth);

    /// <summary>Mutates the request (headers and/or URI) to carry credentials before it is sent.</summary>
    Task ApplyAsync(HttpRequestMessage message, JsonObject auth, CancellationToken ct);
}

/// <summary>
/// A token-based strategy whose cached credential can be discarded — the connector calls this
/// after a response matches <c>auth.retryOn</c>, so the next send re-acquires and replays (E03-T4).
/// </summary>
public interface IInvalidatableAuthStrategy : IAuthStrategy
{
    Task InvalidateAsync(JsonObject auth, CancellationToken ct);
}

/// <summary>
/// A strategy whose credentials can be proven live — a connection "test" calls this to actually
/// acquire a token, so a bad clientSecret / password surfaces at test time (E03-T5). Throws on failure.
/// </summary>
public interface IVerifiableAuthStrategy : IAuthStrategy
{
    Task VerifyAsync(JsonObject auth, CancellationToken ct);
}

/// <summary>
/// A strategy that fetches and keeps a token (E68-T2): its state can be shown, a fresh one fetched on demand,
/// and the kept one forgotten. None of these ever hand the token out.
/// </summary>
public interface ITokenStatusStrategy : IAuthStrategy
{
    Task<TokenStatus> StatusAsync(JsonObject auth, CancellationToken ct);
    Task<TokenStatus> FetchNowAsync(JsonObject auth, CancellationToken ct);
    Task ForgetAsync(JsonObject auth, CancellationToken ct);
}

/// <summary>
/// A strategy whose token should be fetched again when the API refuses it, even when the config names no
/// <c>retryOn</c> (E68-T2): the connector uses <see cref="DefaultRetryOn"/> in that case.
/// </summary>
public interface IDefaultRetryOnStrategy : IAuthStrategy
{
    JsonObject DefaultRetryOn { get; }
}

/// <summary>
/// A challenge-response scheme (Digest, E68-T3): the first call goes out without credentials, the server's 401
/// names a nonce, and the connector sends once more after <see cref="AcceptChallenge"/> stored it.
/// </summary>
public interface IChallengeAuthStrategy : IAuthStrategy
{
    /// <summary>True when the 401 carried a challenge this strategy can now answer; false sends no retry.</summary>
    bool AcceptChallenge(HttpResponseMessage response, JsonObject auth);
}
