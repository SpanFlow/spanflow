using System.Globalization;
using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

// The built-in target types for ValueConverter — one class per kind of type.
// Each gets a plain CLR value (string, int, long, decimal, double, bool or a
// cloned JsonObject/JsonArray), never null.

/// <summary><c>string</c>. Numbers take a .NET numeric format (<c>N2</c>, <c>0.00</c>, <c>X</c>).</summary>
public sealed class StringTypeConverter : ITypeConverter
{
    public IReadOnlyList<string> Names { get; } = ["string", "text"];

    public bool TryConvert(object value, ConversionOptions options, out JsonNode? result)
    {
        result = null;
        if (options.Strict && value is not string)
        {
            return false;
        }

        var text = value switch
        {
            string s => s,
            bool b => b ? "true" : "false",
            JsonNode node => node.ToJsonString(),
            IFormattable number when !string.IsNullOrWhiteSpace(options.Format) => number.ToString(options.Format, CultureInfo.InvariantCulture),
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };

        result = JsonValue.Create(text);
        return true;
    }
}

/// <summary>
/// Whole numbers within [<c>min</c>, <c>max</c>]. A fraction is handled by the
/// rounding mode in <see cref="ConversionOptions.Format"/>: <c>truncate</c> (default),
/// <c>round</c> (half away from zero), <c>even</c> (banker's), <c>floor</c>, <c>ceil</c>.
/// Out of range fails rather than wrapping.
/// </summary>
public sealed class IntegerTypeConverter(IReadOnlyList<string> names, long min, long max) : ITypeConverter
{
    public IReadOnlyList<string> Names { get; } = names;

    public bool TryConvert(object value, ConversionOptions options, out JsonNode? result)
    {
        result = null;
        decimal number;

        if (value is bool flag)
        {
            if (options.Strict)
            {
                return false;
            }
            number = flag ? 1 : 0;
        }
        else if (!NumberParsing.TryGetDecimal(value, out number))
        {
            return false;
        }

        if (decimal.Truncate(number) != number)
        {
            if (options.Strict)
            {
                return false;
            }
            number = Round(number, options.Format);
        }

        if (number < min || number > max)
        {
            return false;
        }

        var whole = (long)number;
        result = whole is >= int.MinValue and <= int.MaxValue ? JsonValue.Create((int)whole) : JsonValue.Create(whole);
        return true;
    }

    private static decimal Round(decimal value, string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        null or "" or "truncate" or "trunc" => decimal.Truncate(value),
        "round" => Math.Round(value, MidpointRounding.AwayFromZero),
        "even" or "bankers" => Math.Round(value, MidpointRounding.ToEven),
        "floor" => decimal.Floor(value),
        "ceil" or "ceiling" => decimal.Ceiling(value),
        _ => throw new InvalidOperationException($"Unknown rounding mode '{mode}'. Use truncate, round, even, floor or ceil."),
    };
}

/// <summary><c>decimal</c> / <c>number</c> — exact base-10 (28 significant digits).</summary>
public sealed class DecimalTypeConverter : ITypeConverter
{
    public IReadOnlyList<string> Names { get; } = ["decimal", "number", "numeric", "money"];

    public bool TryConvert(object value, ConversionOptions options, out JsonNode? result)
    {
        result = NumberParsing.TryGetDecimal(value, out var number) ? JsonValue.Create(number) : null;
        return result is not null;
    }
}

/// <summary>Binary floating point: <c>double</c>, or <c>float</c> when <paramref name="single"/>.</summary>
public sealed class DoubleTypeConverter(IReadOnlyList<string> names, bool single) : ITypeConverter
{
    public IReadOnlyList<string> Names { get; } = names;

    public bool TryConvert(object value, ConversionOptions options, out JsonNode? result)
    {
        result = null;
        if (!NumberParsing.TryGetDouble(value, out var number))
        {
            return false;
        }

        if (single)
        {
            var narrow = (float)number;
            if (!float.IsFinite(narrow))
            {
                return false;
            }
            result = JsonValue.Create(narrow);
            return true;
        }

        result = JsonValue.Create(number);
        return true;
    }
}

/// <summary>
/// <c>bool</c>. Text: true/false, yes/no, y/n, on/off, t/f, 1/0. Numbers: 0 is false,
/// anything else true (only 0 and 1 in strict mode).
/// </summary>
public sealed class BooleanTypeConverter : ITypeConverter
{
    public IReadOnlyList<string> Names { get; } = ["bool", "boolean"];

    public bool TryConvert(object value, ConversionOptions options, out JsonNode? result)
    {
        result = null;
        bool? flag = value switch
        {
            bool b => b,
            string s => ParseText(s),
            _ when NumberParsing.TryGetDecimal(value, out var n) => options.Strict && n is not (0m or 1m) ? null : n != 0m,
            _ => null,
        };

        if (flag is null)
        {
            return false;
        }

        result = JsonValue.Create(flag.Value);
        return true;
    }

    private static bool? ParseText(string text) => text.Trim().ToLowerInvariant() switch
    {
        "true" or "yes" or "y" or "on" or "t" or "1" => true,
        "false" or "no" or "n" or "off" or "f" or "0" => false,
        _ => null,
    };
}

/// <summary>
/// A date and/or time, written as text in <see cref="ConversionOptions.Format"/>
/// (default per type: ISO 8601, <c>yyyy-MM-dd</c> or <c>HH:mm:ss</c>). Reads date
/// text or — outside strict mode — an epoch number.
/// </summary>
public sealed class DateTimeTypeConverter(IReadOnlyList<string> names, string defaultFormat) : ITypeConverter
{
    public IReadOnlyList<string> Names { get; } = names;

    public bool TryConvert(object value, ConversionOptions options, out JsonNode? result)
    {
        result = null;
        if (!DateTimeText.TryParse(value, options.InputFormat, acceptEpoch: !options.Strict, acceptText: true, out var instant))
        {
            return false;
        }

        var format = string.IsNullOrWhiteSpace(options.Format) ? defaultFormat : options.Format;
        result = JsonValue.Create(DateTimeText.Format(instant, format, options.TimeZone));
        return true;
    }
}

/// <summary>
/// An epoch number in one unit. Reads date text, or another epoch number whose unit
/// is <see cref="ConversionOptions.InputFormat"/> (auto-detected when not given).
/// In strict mode only numbers count.
/// </summary>
public sealed class EpochTypeConverter(EpochUnit unit) : ITypeConverter
{
    public IReadOnlyList<string> Names { get; } = Epoch.TypeAliases(unit);

    public bool TryConvert(object value, ConversionOptions options, out JsonNode? result)
    {
        result = null;
        if (!DateTimeText.TryParse(value, options.InputFormat, acceptEpoch: true, acceptText: !options.Strict, out var instant)
            || !Epoch.TryToEpoch(instant, unit, out var number))
        {
            return false;
        }

        result = JsonValue.Create(number);
        return true;
    }
}

/// <summary>
/// <c>uuid</c> / <c>guid</c>, written in the layout named by
/// <see cref="ConversionOptions.Format"/> (see <see cref="UuidTools.Format"/>).
/// Base64 input is read only when <see cref="ConversionOptions.InputFormat"/> is <c>base64</c>.
/// </summary>
public sealed class UuidTypeConverter : ITypeConverter
{
    public IReadOnlyList<string> Names { get; } = ["uuid", "guid"];

    public bool TryConvert(object value, ConversionOptions options, out JsonNode? result)
    {
        result = null;
        var allowBase64 = options.InputFormat?.Trim().StartsWith("base64", StringComparison.OrdinalIgnoreCase) == true;
        if (value is not string text || !UuidTools.TryParse(text, out var uuid, allowBase64))
        {
            return false;
        }

        result = JsonValue.Create(UuidTools.Format(uuid, options.Format));
        return true;
    }
}
