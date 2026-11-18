using System.Text;

namespace SpanFlow.Mapping.Formats;

/// <summary>
/// Raw data on either side of a mapping: text, or bytes when the encoding matters (a byte-counted layout, an
/// EBCDIC file, a file read straight from disk). Codecs ask for the shape they need with <see cref="AsText"/> or
/// <see cref="AsBytes"/>; nothing is converted until they do.
/// </summary>
public sealed class FormatPayload
{
    private FormatPayload(string? text, byte[]? bytes)
    {
        Text = text;
        Bytes = bytes;
    }

    /// <summary>The payload as given, when it was given as text.</summary>
    public string? Text { get; }

    /// <summary>The payload as given, when it was given as bytes.</summary>
    public byte[]? Bytes { get; }

    /// <summary>True when the payload was given as text.</summary>
    public bool IsText => Text is not null;

    public static FormatPayload FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new FormatPayload(text, null);
    }

    public static FormatPayload FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return new FormatPayload(null, bytes);
    }

    /// <summary>The payload as text. Bytes are read with <paramref name="encoding"/>, dropping its byte order mark.</summary>
    public string AsText(Encoding encoding)
    {
        if (Text is not null) return Text;
        var preamble = encoding.GetPreamble();
        // A UTF-8 encoding made without a BOM reports no preamble, but files written by editors still carry one.
        if (preamble.Length == 0 && encoding.CodePage == Encoding.UTF8.CodePage) preamble = Encoding.UTF8.GetPreamble();
        var skip = preamble.Length > 0 && StartsWith(Bytes!, preamble) ? preamble.Length : 0;
        return encoding.GetString(Bytes!, skip, Bytes!.Length - skip);
    }

    /// <summary>The payload as bytes. Text is written with <paramref name="encoding"/>, without a byte order mark.</summary>
    public byte[] AsBytes(Encoding encoding) => Bytes ?? encoding.GetBytes(Text!);

    /// <summary>The size checked against <see cref="FormatLimits.MaxInputBytes"/>.</summary>
    public long ByteCount(Encoding encoding) => Bytes?.LongLength ?? encoding.GetByteCount(Text!);

    public override string ToString() => AsText(new UTF8Encoding(false));

    private static bool StartsWith(byte[] data, byte[] prefix)
    {
        if (data.Length < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
        {
            if (data[i] != prefix[i]) return false;
        }
        return true;
    }
}
