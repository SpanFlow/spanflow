namespace SpanFlow.Connectors.Database;

/// <summary>DI-backed lookup of <see cref="IDbProviderAdapter"/> by provider key.</summary>
public sealed class DbProviderRegistry
{
    /// <summary>Used when a connection config omits "provider" (back-compat with pre-provider configs).</summary>
    public const string DefaultProvider = "postgres";

    private readonly Dictionary<string, IDbProviderAdapter> _byName;

    public DbProviderRegistry(IEnumerable<IDbProviderAdapter> adapters)
        : this(adapters, wrap: null)
    {
    }

    /// <summary>
    /// With <paramref name="wrap"/>, every adapter handed out is the wrapped one: the hook for a decorator that
    /// checks, logs or limits what an adapter may do (for example, refusing servers a connection string names
    /// that the host does not allow).
    /// </summary>
    public DbProviderRegistry(IEnumerable<IDbProviderAdapter> adapters, Func<IDbProviderAdapter, IDbProviderAdapter>? wrap) =>
        _byName = adapters.ToDictionary(
            a => a.Provider,
            a => wrap is null ? a : wrap(a),
            StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Providers => _byName.Keys;

    public IDbProviderAdapter Get(string? provider)
    {
        var key = string.IsNullOrWhiteSpace(provider) ? DefaultProvider : provider;
        return _byName.TryGetValue(key, out var adapter)
            ? adapter
            : throw new InvalidOperationException(
                $"Unsupported db provider '{provider}'. Known providers: {string.Join(", ", _byName.Keys)}.");
    }
}
