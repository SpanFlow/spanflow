namespace SpanFlow.Connectors.Database;

/// <summary>
/// Thrown by <see cref="IDbProviderAdapter.ValidateIntrospectable"/> when the database itself is not
/// there yet — today only file-backed SQLite. Distinct from a plain
/// <see cref="InvalidOperationException"/> because this refusal is *repairable*: the caller can
/// create the empty database and carry on, so the API answers it with an offer ("create it") rather
/// than with a sentence and a dead end.
/// </summary>
public sealed class DatabaseNotInitializedException(string provider, string target, string message)
    : InvalidOperationException(message)
{
    /// <summary>Provider key of the connection that cannot be browsed (e.g. "sqlite").</summary>
    public string Provider { get; } = provider;

    /// <summary>What would be created — for SQLite, the fully resolved file path.</summary>
    public string Target { get; } = target;
}
