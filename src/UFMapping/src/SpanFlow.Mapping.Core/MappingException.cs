namespace SpanFlow.Mapping.Core;

/// <summary>
/// A mapping failure with a human-readable message intended to be shown to the
/// user as-is (e.g. a validation error or a user-defined Exception node).
/// </summary>
public sealed class MappingException : Exception
{
    public MappingException(string message) : base(message)
    {
    }

    public MappingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
