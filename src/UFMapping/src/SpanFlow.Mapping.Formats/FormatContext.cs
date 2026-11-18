using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Formats;

/// <summary>Size and nesting limits every codec respects, so a hostile or broken input fails fast.</summary>
public sealed class FormatLimits
{
    public static FormatLimits Default { get; } = new();

    /// <summary>The largest raw input accepted, in bytes. 64 MB by default.</summary>
    public long MaxInputBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>The deepest nesting a decoded document may have. 64 by default, as System.Text.Json.</summary>
    public int MaxDepth { get; init; } = 64;
}

/// <summary>
/// Everything one decode or encode call needs: the checked options, the text encoding and the limits. Built by
/// <see cref="Create"/>, which refuses unknown options, options for the other direction and values of the wrong
/// kind before any data is touched.
/// </summary>
public sealed class FormatContext
{
    /// <summary>The option every codec accepts: the text encoding of the raw data (utf-8 when left out).</summary>
    public const string EncodingOption = "encoding";

    private readonly JsonObject _options;

    private FormatContext(string format, FormatDirection direction, JsonObject options, Encoding encoding, FormatLimits limits)
    {
        Format = format;
        Direction = direction;
        _options = options;
        Encoding = encoding;
        Limits = limits;
    }

    public string Format { get; }

    public FormatDirection Direction { get; }

    /// <summary>The raw data's text encoding: the <c>encoding</c> option, or UTF-8 without a byte order mark.</summary>
    public Encoding Encoding { get; }

    public FormatLimits Limits { get; }

    /// <summary>The options as given (after checking).</summary>
    public JsonObject Options => _options;

    public static FormatContext Create(IFormatCodec codec, JsonObject? options, FormatDirection direction, FormatLimits? limits = null)
    {
        options ??= new JsonObject();
        var known = codec.Options.ToDictionary(o => o.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in options)
        {
            if (string.Equals(pair.Key, EncodingOption, StringComparison.OrdinalIgnoreCase))
            {
                if (pair.Value is not JsonValue v || v.GetValueKind() != JsonValueKind.String)
                    throw Problem(codec.Type, direction, "Option 'encoding' must be a text value such as \"utf-8\".");
                continue;
            }

            if (!known.TryGetValue(pair.Key, out var info))
            {
                var names = codec.Options.Select(o => o.Name).Append(EncodingOption).OrderBy(n => n, StringComparer.Ordinal);
                throw Problem(codec.Type, direction, $"Unknown option '{pair.Key}'. Options for {codec.Type}: {string.Join(", ", names)}.");
            }

            if (info.Use == FormatOptionUse.EncodeOnly && direction == FormatDirection.Decode)
                throw Problem(codec.Type, direction, $"Option '{info.Name}' only applies when writing {codec.Type}, not when reading it.");
            if (info.Use == FormatOptionUse.DecodeOnly && direction == FormatDirection.Encode)
                throw Problem(codec.Type, direction, $"Option '{info.Name}' only applies when reading {codec.Type}, not when writing it.");

            if (!Fits(pair.Value, info.Kind))
                throw Problem(codec.Type, direction, $"Option '{info.Name}' must be {Describe(info.Kind)}.");
        }

        var encoding = ResolveEncoding(codec.Type, direction, options);
        return new FormatContext(codec.Type, direction, options, encoding, limits ?? FormatLimits.Default);
    }

    public bool GetBool(string name, bool fallback = false) =>
        Find(name) is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? v.GetValue<bool>() : fallback;

    public int GetInt(string name, int fallback = 0) =>
        Find(name) is JsonValue v && v.TryGetValue<int>(out var number) ? number : fallback;

    public string? GetString(string name, string? fallback = null) =>
        Find(name) is JsonValue v && v.TryGetValue<string>(out var text) ? text : fallback;

    public IReadOnlyList<string> GetStrings(string name) =>
        Find(name) is JsonArray items ? items.Select(i => i!.GetValue<string>()).ToList() : [];

    public JsonObject? GetObject(string name) => Find(name) as JsonObject;

    /// <summary>The raw option value, for <see cref="FormatOptionKind.Any"/> options.</summary>
    public JsonNode? Get(string name) => Find(name);

    /// <summary>A decode or encode failure for this format, with an optional location.</summary>
    public MappingFormatException Fail(string reason, FormatLocation? location = null, Exception? inner = null) =>
        new(Format, Direction, reason, location, inner);

    private JsonNode? Find(string name)
    {
        foreach (var pair in _options)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        }
        return null;
    }

    private static Encoding ResolveEncoding(string format, FormatDirection direction, JsonObject options)
    {
        string? name = null;
        foreach (var pair in options)
        {
            if (string.Equals(pair.Key, EncodingOption, StringComparison.OrdinalIgnoreCase)) name = pair.Value!.GetValue<string>();
        }

        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, "utf-8", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "utf8", StringComparison.OrdinalIgnoreCase))
            return new UTF8Encoding(false);

        EnsureCodePages();
        try
        {
            // D73.9: "ebcdic" is IBM037 (EBCDIC US/Canada), the common mainframe and card-network code page.
            return string.Equals(name, "ebcdic", StringComparison.OrdinalIgnoreCase) ? Encoding.GetEncoding(37) : Encoding.GetEncoding(name!);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw new MappingFormatException(format, direction,
                $"Unknown encoding '{name}'. Use a name such as utf-8, utf-16, us-ascii, iso-8859-1, ebcdic (IBM037) or another code page such as IBM500.", innerException: ex);
        }
    }

    /// <summary>
    /// Modern .NET knows only the Unicode encodings, ASCII and Latin-1 until the code page provider (in the shared
    /// framework) is registered. .NET Framework knows every Windows code page already, so netstandard2.0 needs nothing.
    /// </summary>
    private static void EnsureCodePages()
    {
#if NET
        CodePages.Ensure();
#endif
    }

#if NET
    /// <summary>
    /// Registers once. A static constructor, not a flag: the runtime makes every other thread wait until it has run,
    /// so no caller can see "registered" before the provider really is (a flag let parallel callers through early).
    /// </summary>
    private static class CodePages
    {
        static CodePages() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        public static void Ensure()
        {
        }
    }
#endif

    private static bool Fits(JsonNode? value, FormatOptionKind kind) => kind switch
    {
        FormatOptionKind.Any => true,
        FormatOptionKind.Bool => value is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
        FormatOptionKind.Int => value is JsonValue v && v.TryGetValue<int>(out _),
        FormatOptionKind.String => value is JsonValue v && v.GetValueKind() == JsonValueKind.String,
        FormatOptionKind.StringList => value is JsonArray items && items.All(i => i is JsonValue v && v.GetValueKind() == JsonValueKind.String),
        FormatOptionKind.Object => value is JsonObject,
        _ => false,
    };

    private static string Describe(FormatOptionKind kind) => kind switch
    {
        FormatOptionKind.Bool => "true or false",
        FormatOptionKind.Int => "a whole number",
        FormatOptionKind.String => "a text value",
        FormatOptionKind.StringList => "a list of text values",
        FormatOptionKind.Object => "an object",
        _ => "a value",
    };

    private static MappingFormatException Problem(string format, FormatDirection direction, string reason) => new(format, direction, reason);
}
