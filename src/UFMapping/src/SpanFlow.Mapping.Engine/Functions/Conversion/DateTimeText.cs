using System.Globalization;

namespace SpanFlow.Mapping.Engine;

/// <summary>Parsing and formatting dates for the conversion functions (invariant culture).</summary>
public static class DateTimeText
{
    /// <summary>The named ISO 8601 format — the default output for a date-time.</summary>
    public const string Iso = "iso";

    private const DateTimeStyles Styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces;

    // Friendly names for common formats. Anything else is used as a .NET format string.
    private static readonly Dictionary<string, string> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["iso"] = Iso,
        ["iso8601"] = Iso,
        ["rfc3339"] = Iso,
        ["date"] = "yyyy-MM-dd",
        ["time"] = "HH:mm:ss",
        ["datetime"] = "yyyy-MM-dd HH:mm:ss",
        ["rfc1123"] = "R",
        ["rfc7231"] = "R",
        ["http"] = "R",
        ["sortable"] = "s",
        ["roundtrip"] = "O",
    };

    public static string ResolveFormat(string format)
        => Named.TryGetValue(format.Trim(), out var resolved) ? resolved : format;

    /// <summary>
    /// Formats an instant. Null/empty format is ISO 8601 (<c>Z</c> for UTC,
    /// fractional seconds only when present). <paramref name="zone"/> shifts the
    /// instant into that zone first.
    /// </summary>
    public static string Format(DateTimeOffset value, string? format, TimeZoneInfo? zone = null)
    {
        if (zone is not null)
        {
            value = TimeZoneInfo.ConvertTime(value, zone);
        }

        var resolved = ResolveFormat(string.IsNullOrWhiteSpace(format) ? Iso : format);
        if (resolved == Iso)
        {
            resolved = value.Offset == TimeSpan.Zero
                ? "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"
                : "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz";
        }

        return value.ToString(resolved, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads an instant from a converted JSON scalar.
    /// <list type="bullet">
    /// <item>A number (or number text) is an epoch value — in the unit named by
    /// <paramref name="inputFormat"/>, else auto-detected — when <paramref name="acceptEpoch"/>.</item>
    /// <item>Other text is parsed with the <c>|</c>-separated <paramref name="inputFormat"/>
    /// patterns, else as any common date text; no offset means UTC.</item>
    /// </list>
    /// </summary>
    public static bool TryParse(object? value, string? inputFormat, bool acceptEpoch, bool acceptText, out DateTimeOffset result)
    {
        result = default;
        var unitGiven = Epoch.TryParseUnit(inputFormat, out var unit);

        switch (value)
        {
            case string text:
                text = text.Trim();
                if (text.Length == 0)
                {
                    return false;
                }

                // "1700000000" is an epoch unless a date pattern says otherwise
                // (e.g. "20240115" with "yyyyMMdd").
                var isEpochText = unitGiven || (string.IsNullOrWhiteSpace(inputFormat) && NumberParsing.IsPlainNumber(text));
                if (isEpochText)
                {
                    return acceptEpoch
                        && NumberParsing.TryParseDecimal(text, out var number)
                        && Epoch.TryFromEpoch(number, unit, out result);
                }

                return acceptText && TryParseText(text, inputFormat, out result);

            case int or long or decimal or double or float:
                return acceptEpoch
                    && NumberParsing.TryGetDecimal(value, out var epoch)
                    && Epoch.TryFromEpoch(epoch, unit, out result);

            default:
                return false;
        }
    }

    /// <summary>
    /// A time zone by IANA or Windows id, <c>utc</c>, <c>local</c>, or a fixed
    /// offset like <c>+05:30</c>. Null/empty is null. An unknown id throws.
    /// </summary>
    public static TimeZoneInfo? ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        id = id.Trim();
        switch (id.ToLowerInvariant())
        {
            case "utc" or "z" or "gmt":
                return TimeZoneInfo.Utc;
            case "local":
                return TimeZoneInfo.Local;
        }

        if (id[0] is '+' or '-' && TimeSpan.TryParse(id.Substring(1), CultureInfo.InvariantCulture, out var offset))
        {
            offset = id[0] == '-' ? -offset : offset;
            return TimeZoneInfo.CreateCustomTimeZone(id, offset, id, id);
        }

        return TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)
            ? zone
            : throw new InvalidOperationException($"Unknown time zone '{id}'. Use an IANA id (e.g. 'America/New_York'), 'utc', 'local' or an offset like '-05:00'.");
    }

    private static bool TryParseText(string text, string? inputFormat, out DateTimeOffset result)
    {
        if (string.IsNullOrWhiteSpace(inputFormat))
        {
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, Styles, out result);
        }

        foreach (var format in inputFormat.Split('|').Select(f => f.Trim()).Where(f => f.Length > 0))
        {
            var resolved = ResolveFormat(format);
            var parsed = resolved == Iso
                ? DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, Styles, out result)
                : DateTimeOffset.TryParseExact(text, resolved, CultureInfo.InvariantCulture, Styles, out result);
            if (parsed)
            {
                return true;
            }
        }

        result = default;
        return false;
    }
}
