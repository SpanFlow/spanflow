using SpanFlow.Mapping.Formats.Codecs;

namespace SpanFlow.Mapping.Formats;

/// <summary>The codecs a runner can use, by <see cref="IFormatCodec.Type"/>.</summary>
public sealed class FormatRegistry
{
    /// <summary>The type used when a format is left out.</summary>
    public const string DefaultType = "json";

    private readonly Dictionary<string, IFormatCodec> _codecs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every codec this library ships.</summary>
    public static FormatRegistry CreateDefault() => new FormatRegistry()
        .Register(new JsonCodec())
        .Register(new TextCodec())
        .Register(new XmlCodec())
        .Register(new PositionalCodec())
        .Register(new EdiCodec());

    public IReadOnlyCollection<IFormatCodec> Codecs => _codecs.Values;

    /// <summary>Adds a codec. A second codec for the same type is refused; use <see cref="Replace"/> to swap one.</summary>
    public FormatRegistry Register(IFormatCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        if (_codecs.ContainsKey(codec.Type)) throw new InvalidOperationException($"A codec for '{codec.Type}' is already registered.");
        _codecs[codec.Type] = codec;
        return this;
    }

    /// <summary>Adds a codec, or swaps the one registered for its type.</summary>
    public FormatRegistry Replace(IFormatCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        _codecs[codec.Type] = codec;
        return this;
    }

    public bool TryGet(string? type, out IFormatCodec codec) =>
        _codecs.TryGetValue(string.IsNullOrWhiteSpace(type) ? DefaultType : type!, out codec!);

    /// <summary>The codec for <paramref name="type"/> (json when empty), or a user-facing error naming the known types.</summary>
    public IFormatCodec Get(string? type, FormatDirection direction)
    {
        if (TryGet(type, out var codec)) return codec;
        var known = string.Join(", ", _codecs.Keys.OrderBy(k => k, StringComparer.Ordinal));
        throw new MappingFormatException(type!, direction, $"Unknown format '{type}'. Known formats: {known}.");
    }
}
