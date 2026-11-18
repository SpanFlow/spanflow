using System.Text;

namespace SpanFlow.Mapping.Formats;

/// <summary>Which way a codec was working when something went wrong.</summary>
public enum FormatDirection
{
    /// <summary>Raw input to JSON.</summary>
    Decode,

    /// <summary>JSON to raw output.</summary>
    Encode,
}

/// <summary>
/// Where in the raw data a problem is. Every part is optional; a codec fills what it knows (a text codec the line
/// and column, a positional codec the offset and field, an EDI codec the segment).
/// </summary>
public sealed class FormatLocation
{
    /// <summary>1-based line.</summary>
    public int? Line { get; init; }

    /// <summary>1-based column within <see cref="Line"/>.</summary>
    public int? Column { get; init; }

    /// <summary>0-based character (or byte, for byte-counted layouts) offset from the start.</summary>
    public long? Offset { get; init; }

    /// <summary>1-based segment number (EDI) or record number (positional records).</summary>
    public int? Segment { get; init; }

    /// <summary>The field, element or JSON path concerned.</summary>
    public string? Field { get; init; }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Line is not null) parts.Add($"line {Line}");
        if (Column is not null) parts.Add($"column {Column}");
        if (Offset is not null) parts.Add($"offset {Offset}");
        if (Segment is not null) parts.Add($"segment {Segment}");
        if (Field is not null) parts.Add($"field '{Field}'");
        return string.Join(", ", parts);
    }
}

/// <summary>
/// A format could not be read or written, or its settings are wrong. <see cref="Exception.Message"/> is meant to be
/// shown to the user as is: it names the format, the direction and, when known, the place.
/// </summary>
public sealed class MappingFormatException : Exception
{
    public MappingFormatException(string format, FormatDirection direction, string reason, FormatLocation? location = null, Exception? innerException = null)
        : base(Compose(format, direction, reason, location), innerException)
    {
        Format = format;
        Direction = direction;
        Reason = reason;
        Location = location;
    }

    /// <summary>The codec type, e.g. <c>xml</c>.</summary>
    public string Format { get; }

    public FormatDirection Direction { get; }

    /// <summary>The problem alone, without the format and place around it.</summary>
    public string Reason { get; }

    public FormatLocation? Location { get; }

    private static string Compose(string format, FormatDirection direction, string reason, FormatLocation? location)
    {
        var text = new StringBuilder(direction == FormatDirection.Decode ? $"Reading {format}: " : $"Writing {format}: ").Append(reason);
        var where = location?.ToString();
        if (!string.IsNullOrEmpty(where)) text.Append(" (").Append(where).Append(')');
        return text.ToString();
    }
}
