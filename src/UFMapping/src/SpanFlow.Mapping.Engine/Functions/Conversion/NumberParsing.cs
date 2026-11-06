using System.Globalization;

namespace SpanFlow.Mapping.Engine;

/// <summary>Reads numbers out of converted JSON scalars and text (invariant culture).</summary>
internal static class NumberParsing
{
    // No thousands separators: "1,5" must not silently become 15.
    private const NumberStyles Styles = NumberStyles.Float;

    public static bool TryGetDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case int i: result = i; return true;
            case long l: result = l; return true;
            case decimal d: result = d; return true;
            case double db: return TryDoubleToDecimal(db, out result);
            case float f: return TryDoubleToDecimal(f, out result);
            case string s: return TryParseDecimal(s, out result);
            default: result = 0m; return false;
        }
    }

    public static bool TryGetDouble(object? value, out double result)
    {
        switch (value)
        {
            case int i: result = i; return true;
            case long l: result = l; return true;
            case decimal d: result = (double)d; return true;
            case double db: result = db; return double.IsFinite(db);
            case float f: result = f; return float.IsFinite(f);
            case string s: return TryParseDouble(s, out result);
            default: result = 0; return false;
        }
    }

    /// <summary>Plain decimal text, or hex with a <c>0x</c> prefix.</summary>
    public static bool TryParseDecimal(string text, out decimal result)
    {
        text = text.Trim();

        if (TryParseHex(text, out var hex))
        {
            result = hex;
            return true;
        }

        if (decimal.TryParse(text, Styles, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        // Exponents too large/small for decimal's own parser but still in range.
        return double.TryParse(text, Styles, CultureInfo.InvariantCulture, out var d) && TryDoubleToDecimal(d, out result);
    }

    public static bool TryParseDouble(string text, out double result)
    {
        text = text.Trim();

        if (TryParseHex(text, out var hex))
        {
            result = hex;
            return true;
        }

        return double.TryParse(text, Styles, CultureInfo.InvariantCulture, out result) && double.IsFinite(result);
    }

    /// <summary>True only for ordinary number text ("12", "-3.5", "1e3") — not hex, not dates.</summary>
    public static bool IsPlainNumber(string text)
        => decimal.TryParse(text.Trim(), Styles, CultureInfo.InvariantCulture, out _);

    private static bool TryParseHex(string text, out long value)
    {
        value = 0;
        return text.Length > 2
            && text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryDoubleToDecimal(double value, out decimal result)
    {
        result = 0m;
        if (!double.IsFinite(value))
        {
            return false;
        }

        try
        {
            result = (decimal)value;
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
