using System.Text.Json.Nodes;

namespace SpanFlow.Connectors.Http.Auth;

/// <summary>DI-backed lookup of <see cref="IAuthStrategy"/> by <c>config.auth.type</c>.</summary>
public sealed class AuthStrategyRegistry
{
    private readonly Dictionary<string, IAuthStrategy> _byType;

    public AuthStrategyRegistry(IEnumerable<IAuthStrategy> strategies) =>
        _byType = strategies.ToDictionary(s => s.Type, StringComparer.OrdinalIgnoreCase);

    /// <summary>The static strategies (no HTTP/token needed) — convenient for tests.</summary>
    public static AuthStrategyRegistry CreateStatic() =>
        new([new NoneAuthStrategy(), new ApiKeyAuthStrategy(), new BasicAuthStrategy(), new BearerAuthStrategy(),
             new DigestAuthStrategy(), new HawkAuthStrategy(), new JwtBearerAuthStrategy(), new OAuth1AuthStrategy()]);

    /// <summary>Resolves by type; a null/blank type means "none". Unknown types throw.</summary>
    public IAuthStrategy Get(string? type)
    {
        var key = string.IsNullOrWhiteSpace(type) ? "none" : type;
        return _byType.TryGetValue(key, out var strategy)
            ? strategy
            : throw new InvalidOperationException(
                $"Unsupported auth type '{type}'. Known types: {string.Join(", ", _byType.Keys)}.");
    }

    /// <summary>Validates an <c>auth</c> config object (null/absent is valid = no auth).</summary>
    public IReadOnlyList<string> Validate(JsonObject? auth)
    {
        if (auth is null) return [];
        var type = auth["type"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(type) || type == "none") return [];
        if (!_byType.TryGetValue(type, out var strategy))
        {
            return [$"Unsupported auth type '{type}'."];
        }
        return strategy.Validate(auth);
    }

    /// <summary>Applies the auth in <paramref name="auth"/> to the request (no-op when absent).</summary>
    public Task ApplyAsync(HttpRequestMessage message, JsonObject? auth, CancellationToken ct) =>
        auth is null ? Task.CompletedTask : Get(auth["type"]?.GetValue<string>()).ApplyAsync(message, auth, ct);
}
