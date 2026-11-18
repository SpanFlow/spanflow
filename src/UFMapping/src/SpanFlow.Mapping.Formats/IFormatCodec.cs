using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Formats;

/// <summary>
/// One wire format, both ways: <see cref="Decode"/> turns raw data into the JSON a mapping reads, <see cref="Encode"/>
/// turns mapped JSON into raw data. A codec holds no state; everything a call needs is in its
/// <see cref="FormatContext"/>. The rule every codec keeps: <c>Decode(Encode(x))</c> equals <c>x</c> for the JSON shape
/// its own <see cref="Decode"/> produces.
/// </summary>
public interface IFormatCodec
{
    /// <summary>The <c>type</c> a <c>FormatSpec</c> names it by, lower case (json, xml, text, edi, positional).</summary>
    string Type { get; }

    /// <summary>The media type of what <see cref="Encode"/> writes, e.g. <c>application/xml</c>.</summary>
    string ContentType { get; }

    /// <summary>
    /// Every option this codec reads, so settings can be checked before a run and the designer can draw a form for
    /// them. <c>encoding</c> is common to all codecs and is not listed here.
    /// </summary>
    IReadOnlyList<FormatOptionInfo> Options { get; }

    /// <summary>Raw data to JSON. Throws <see cref="MappingFormatException"/> with a location when it cannot.</summary>
    JsonNode? Decode(FormatPayload raw, FormatContext context);

    /// <summary>JSON to raw data. Throws <see cref="MappingFormatException"/> when the JSON does not fit the format.</summary>
    FormatPayload Encode(JsonNode? value, FormatContext context);
}

/// <summary>What kind of value an option takes.</summary>
public enum FormatOptionKind
{
    Bool,
    Int,
    String,
    StringList,
    Object,
    Any,
}

/// <summary>Which direction an option is read in.</summary>
public enum FormatOptionUse
{
    Both,
    DecodeOnly,
    EncodeOnly,
}

/// <summary>A codec option: its name, the value it takes, when it is read, and what it does.</summary>
public sealed class FormatOptionInfo
{
    public FormatOptionInfo(string name, FormatOptionKind kind, FormatOptionUse use, string description)
    {
        Name = name;
        Kind = kind;
        Use = use;
        Description = description;
    }

    public string Name { get; }

    public FormatOptionKind Kind { get; }

    public FormatOptionUse Use { get; }

    /// <summary>One plain sentence for the designer's option form.</summary>
    public string Description { get; }
}
