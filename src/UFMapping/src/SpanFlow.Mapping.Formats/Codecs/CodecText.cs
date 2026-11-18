using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SpanFlow.Mapping.Formats.Codecs;

/// <summary>Small text helpers the codecs share: line splitting, scalar text, type inference, separator names.</summary>
internal static class CodecText
{
    private static readonly Regex NumberShape = new(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?$", RegexOptions.CultureInvariant);

    /// <summary>Lines split on \r\n, \n or \r. A trailing line break does not make an empty last line.</summary>
    public static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '\r' && c != '\n') continue;
            lines.Add(text.Substring(start, i - start));
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            start = i + 1;
        }
        if (start < text.Length) lines.Add(text.Substring(start));
        return lines;
    }

    /// <summary>The text of a JSON scalar (numbers as written, booleans as true/false); null for null; false for objects and arrays.</summary>
    public static bool TryScalar(JsonNode? node, out string? text)
    {
        switch (node)
        {
            case null:
                text = null;
                return true;
            case JsonValue value:
                text = value.GetValueKind() switch
                {
                    JsonValueKind.String => value.GetValue<string>(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => value.ToJsonString(),
                };
                return true;
            default:
                text = null;
                return false;
        }
    }

    /// <summary>A JSON number from invariant number text, exactly as a parsed document would hold it.</summary>
    public static JsonNode Number(string invariantText) => JsonNode.Parse(invariantText)!;

    public static JsonNode Number(decimal value) => Number(Normalize(value).ToString(CultureInfo.InvariantCulture));

    /// <summary>Drops trailing zeros (100.00 -> 100), so a value reads the same however it was scaled.</summary>
    public static decimal Normalize(decimal value) => value / 1.000000000000000000000000000000000m;

    /// <summary>"infer" typing: plain numbers (no leading zeros, so ids like 007 stay text) and true/false; everything else text.</summary>
    /// <summary>
    /// A number from text, keeping its digits as written when they are already a JSON number (1500.00 stays 1500.00);
    /// other forms XSD allows (+5, 007, 1E3) are read and written in plain form.
    /// </summary>
    public static bool TryNumber(string text, bool integer, out JsonNode? number)
    {
        number = null;
        if (text.Length == 0) return false;
        if (integer && !IntegerShape.IsMatch(text)) return false;
        if (text.Length <= 28 && NumberShape.IsMatch(text))
        {
            number = Number(text);
            return true;
        }
        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return false;
        number = Number(value);
        return true;
    }

    private static readonly Regex IntegerShape = new(@"^[+-]?[0-9]+$", RegexOptions.CultureInvariant);

    public static JsonNode? Infer(string text)
    {
        if (text == "true") return JsonValue.Create(true);
        if (text == "false") return JsonValue.Create(false);
        if (text.Length <= 28 && NumberShape.IsMatch(text)) return Number(text);
        return JsonValue.Create(text);
    }

    /// <summary>A separator option, with friendly names for the invisible or awkward ones.</summary>
    public static string Separator(string value) => value.ToLowerInvariant() switch
    {
        "tab" => "\t",
        "pipe" => "|",
        "comma" => ",",
        "semicolon" => ";",
        "space" => " ",
        "lf" => "\n",
        "crlf" => "\r\n",
        _ => value,
    };

    public static string Join(IEnumerable<string> lines, string newline, bool trailing)
    {
        var text = new StringBuilder();
        var first = true;
        foreach (var line in lines)
        {
            if (!first) text.Append(newline);
            text.Append(line);
            first = false;
        }
        if (trailing && !first) text.Append(newline);
        return text.ToString();
    }

    /// <summary>The list a writer works on: the root array, or the named list inside the root object.</summary>
    public static JsonArray? ListOf(JsonNode? value, string key) => value switch
    {
        JsonArray array => array,
        JsonObject obj => obj.TryGetPropertyValue(key, out var inner) ? inner as JsonArray : null,
        _ => null,
    };
}
