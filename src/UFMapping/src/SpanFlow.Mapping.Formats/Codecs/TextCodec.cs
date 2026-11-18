using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SpanFlow.Mapping.Formats.Codecs;

/// <summary>
/// Plain text, in the shape <c>mode</c> picks:
/// <list type="bullet">
/// <item><c>whole</c> (default): <c>{ "text": "..." }</c>.</item>
/// <item><c>lines</c>: <c>{ "lines": [ ... ] }</c>.</item>
/// <item><c>delimited</c>: <c>{ "rows": [ { column: value } ] }</c> (CSV, TSV, pipe ...: quotes, header, columns).</item>
/// <item><c>pattern</c>: each line matched by a regex with named groups into <c>rows</c>; written back with a <c>template</c>.</item>
/// <item><c>fixed</c>: fixed-width lines, read with a positional <c>layout</c> (same rules as the positional format).</item>
/// </list>
/// </summary>
public sealed class TextCodec : IFormatCodec
{
    private static readonly IReadOnlyList<FormatOptionInfo> OptionList =
    [
        new("mode", FormatOptionKind.String, FormatOptionUse.Both, "whole, lines, delimited, pattern or fixed."),
        new("newline", FormatOptionKind.String, FormatOptionUse.EncodeOnly, "Line break when writing: \\n (default), \\r\\n, lf or crlf."),
        new("trailingNewline", FormatOptionKind.Bool, FormatOptionUse.EncodeOnly, "End the last line with a line break too."),
        new("delimiter", FormatOptionKind.String, FormatOptionUse.Both, "delimited: the separator, e.g. , ; | or tab (default comma)."),
        new("quote", FormatOptionKind.String, FormatOptionUse.Both, "delimited: the quote character (default \"); empty for none."),
        new("header", FormatOptionKind.Bool, FormatOptionUse.Both, "delimited: the first line holds the column names (default true)."),
        new("columns", FormatOptionKind.StringList, FormatOptionUse.Both, "delimited: the column names and their order."),
        new("trim", FormatOptionKind.Bool, FormatOptionUse.DecodeOnly, "delimited and pattern: trim spaces around each value."),
        new("types", FormatOptionKind.String, FormatOptionUse.DecodeOnly, "string (default) or infer: read plain numbers and true/false as such."),
        new("pattern", FormatOptionKind.String, FormatOptionUse.DecodeOnly, "pattern: a regular expression with named groups, matched per line."),
        new("template", FormatOptionKind.String, FormatOptionUse.EncodeOnly, "pattern: one line per row, e.g. \"{name} owes {amount}\"."),
        new("layout", FormatOptionKind.Object, FormatOptionUse.Both, "fixed: the positional layout of the lines."),
        new("unit", FormatOptionKind.String, FormatOptionUse.Both, "fixed: lengths in chars (default) or bytes."),
        new("strict", FormatOptionKind.Bool, FormatOptionUse.DecodeOnly, "fixed: fail when a line is longer than its layout (default true)."),
    ];

    public string Type => "text";

    public string ContentType => "text/plain";

    public IReadOnlyList<FormatOptionInfo> Options => OptionList;

    public JsonNode? Decode(FormatPayload raw, FormatContext context)
    {
        var text = raw.AsText(context.Encoding);
        switch (Mode(context))
        {
            case "whole":
                return new JsonObject { ["text"] = text };
            case "lines":
                return new JsonObject { ["lines"] = new JsonArray(CodecText.SplitLines(text).Select(l => (JsonNode?)JsonValue.Create(l)).ToArray()) };
            case "delimited":
                return new JsonObject { ["rows"] = DecodeDelimited(text, context) };
            case "pattern":
                return DecodePattern(text, context);
            default:
                return PositionalCodec.DecodeText(text, context);
        }
    }

    public FormatPayload Encode(JsonNode? value, FormatContext context)
    {
        var newline = CodecText.Separator(context.GetString("newline", "\n")!);
        var trailing = context.GetBool("trailingNewline");
        switch (Mode(context))
        {
            case "whole":
            {
                var node = value is JsonObject obj ? obj["text"] : value;
                if (!CodecText.TryScalar(node, out var text)) throw context.Fail("whole mode writes $.text (or a text value at the root); the mapping gave an object or a list there.");
                return FormatPayload.FromText(text ?? "");
            }
            case "lines":
            {
                var lines = CodecText.ListOf(value, "lines") ?? throw context.Fail("lines mode writes $.lines (or a list at the root); the mapping gave neither.");
                return FormatPayload.FromText(CodecText.Join(lines.Select((line, i) => LineText(line, i, context)), newline, trailing));
            }
            case "delimited":
                return FormatPayload.FromText(EncodeDelimited(value, context, newline, trailing));
            case "pattern":
                return FormatPayload.FromText(EncodeTemplate(value, context, newline, trailing));
            default:
                return FormatPayload.FromText(PositionalCodec.EncodeText(value, context));
        }
    }

    private static string Mode(FormatContext context)
    {
        var mode = context.GetString("mode", "whole")!;
        return mode is "whole" or "lines" or "delimited" or "pattern" or "fixed"
            ? mode
            : throw context.Fail($"Option 'mode' must be whole, lines, delimited, pattern or fixed, not '{mode}'.");
    }

    private static string LineText(JsonNode? line, int index, FormatContext context) =>
        CodecText.TryScalar(line, out var text) ? text ?? "" : throw context.Fail("Each line must be a single value, not an object or a list.", new FormatLocation { Line = index + 1 });

    private static JsonNode? Typed(string value, FormatContext context, bool trim)
    {
        if (trim) value = value.Trim();
        return context.GetString("types", "string") switch
        {
            "string" => JsonValue.Create(value),
            "infer" => CodecText.Infer(value),
            var other => throw context.Fail($"Option 'types' must be string or infer, not '{other}'."),
        };
    }

    // ---------------------------------------------------------------- delimited

    private static (string Delimiter, char? Quote) Separators(FormatContext context)
    {
        var delimiter = CodecText.Separator(context.GetString("delimiter", ",")!);
        if (delimiter.Length == 0) throw context.Fail("Option 'delimiter' cannot be empty.");
        var quote = context.GetString("quote", "\"")!;
        if (quote.Length > 1) throw context.Fail("Option 'quote' must be one character, or empty for none.");
        if (quote.Length == 1 && delimiter.IndexOf(quote[0]) >= 0) throw context.Fail("The quote character cannot be part of the delimiter.");
        return (delimiter, quote.Length == 0 ? null : quote[0]);
    }

    private static JsonArray DecodeDelimited(string text, FormatContext context)
    {
        var (delimiter, quote) = Separators(context);
        var records = ParseDelimited(text, delimiter, quote, context);
        var header = context.GetBool("header", true);
        var trim = context.GetBool("trim");

        var names = context.GetStrings("columns").ToList();
        if (header && records.Count > 0)
        {
            var fromFile = records[0].Fields;
            records.RemoveAt(0);
            if (names.Count == 0) names = Unique(fromFile.Select(n => trim ? n.Trim() : n));
        }

        var rows = new JsonArray();
        foreach (var record in records)
        {
            var row = new JsonObject();
            var count = Math.Max(names.Count, record.Fields.Count);
            for (var i = 0; i < count; i++)
            {
                var name = i < names.Count ? names[i] : $"column{i + 1}";
                row[name] = Typed(i < record.Fields.Count ? record.Fields[i] : "", context, trim);
            }
            rows.Add(row);
        }
        return rows;
    }

    private static List<string> Unique(IEnumerable<string> names)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<string>();
        var index = 0;
        foreach (var raw in names)
        {
            index++;
            var name = raw.Length == 0 ? $"column{index}" : raw;
            if (seen.TryGetValue(name, out var n))
            {
                seen[name] = n + 1;
                name = $"{name}_{n + 1}";
            }
            else
            {
                seen[name] = 1;
            }
            result.Add(name);
        }
        return result;
    }

    private sealed class Record(int line, List<string> fields)
    {
        public int Line { get; } = line;
        public List<string> Fields { get; } = fields;
    }

    /// <summary>RFC 4180 reading with any delimiter: quoted fields may hold delimiters, quotes ("") and line breaks.</summary>
    private static List<Record> ParseDelimited(string text, string delimiter, char? quote, FormatContext context)
    {
        var records = new List<Record>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var line = 1;
        var recordLine = 1;
        var i = 0;
        var fieldStarted = false;

        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
            fieldStarted = false;
        }

        void EndRecord()
        {
            EndField();
            if (!(fields.Count == 1 && fields[0].Length == 0)) records.Add(new Record(recordLine, fields));
            fields = new List<string>();
        }

        while (i < text.Length)
        {
            var c = text[i];
            if (quote is { } q && c == q && !fieldStarted && field.Length == 0)
            {
                var quoteLine = line;
                var quoteColumn = i;
                i++;
                var closed = false;
                while (i < text.Length)
                {
                    if (text[i] == q)
                    {
                        if (i + 1 < text.Length && text[i + 1] == q)
                        {
                            field.Append(q);
                            i += 2;
                            continue;
                        }
                        i++;
                        closed = true;
                        break;
                    }
                    if (text[i] == '\n') line++;
                    field.Append(text[i]);
                    i++;
                }
                if (!closed) throw context.Fail("A quoted value is never closed.", new FormatLocation { Line = quoteLine, Offset = quoteColumn });
                fieldStarted = true;
                if (i < text.Length && !IsAt(text, i, delimiter) && text[i] is not ('\r' or '\n'))
                    throw context.Fail($"Unexpected '{text[i]}' after a closing quote.", new FormatLocation { Line = line, Offset = i });
                continue;
            }

            if (IsAt(text, i, delimiter))
            {
                EndField();
                i += delimiter.Length;
                fieldStarted = false;
                continue;
            }

            if (c is '\r' or '\n')
            {
                EndRecord();
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                i++;
                line++;
                recordLine = line;
                continue;
            }

            field.Append(c);
            fieldStarted = true;
            i++;
        }

        if (field.Length > 0 || fields.Count > 0 || fieldStarted) EndRecord();
        return records;
    }

    private static bool IsAt(string text, int index, string token) =>
        index + token.Length <= text.Length && string.CompareOrdinal(text, index, token, 0, token.Length) == 0;

    private static string EncodeDelimited(JsonNode? value, FormatContext context, string newline, bool trailing)
    {
        var (delimiter, quote) = Separators(context);
        var rows = CodecText.ListOf(value, "rows") ?? throw context.Fail("delimited mode writes $.rows (or a list at the root); the mapping gave neither.");

        var columns = context.GetStrings("columns").ToList();
        if (columns.Count == 0)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows.OfType<JsonObject>())
            {
                foreach (var pair in row)
                {
                    if (seen.Add(pair.Key)) columns.Add(pair.Key);
                }
            }
        }

        var lines = new List<string>();
        if (context.GetBool("header", true) && columns.Count > 0) lines.Add(string.Join(delimiter, columns.Select(c => Quote(c, delimiter, quote, context, 0, c))));

        for (var r = 0; r < rows.Count; r++)
        {
            var cells = rows[r] switch
            {
                JsonObject obj => columns.Select(c => (Name: c, Node: obj[c])).ToList(),
                JsonArray array => array.Select((n, i) => (Name: $"column{i + 1}", Node: n)).ToList(),
                _ => throw context.Fail("Each row must be an object (or a list of values).", new FormatLocation { Segment = r + 1 }),
            };
            lines.Add(string.Join(delimiter, cells.Select(cell =>
            {
                if (!CodecText.TryScalar(cell.Node, out var text))
                    throw context.Fail("A cell must be a single value, not an object or a list.", new FormatLocation { Segment = r + 1, Field = cell.Name });
                return Quote(text ?? "", delimiter, quote, context, r + 1, cell.Name);
            })));
        }
        return CodecText.Join(lines, newline, trailing);
    }

    private static string Quote(string text, string delimiter, char? quote, FormatContext context, int row, string column)
    {
        var needs = text.IndexOf(delimiter, StringComparison.Ordinal) >= 0 || text.IndexOfAny(['\r', '\n']) >= 0
            || (quote is { } q && text.IndexOf(q) >= 0) || (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[text.Length - 1])));
        if (!needs) return text;
        if (quote is not { } mark)
            throw context.Fail("A value holds the delimiter or a line break, and there is no quote character to protect it.", new FormatLocation { Segment = row, Field = column });
        return mark + text.Replace(mark.ToString(), new string(mark, 2)) + mark;
    }

    // ---------------------------------------------------------------- pattern

    private static JsonNode DecodePattern(string text, FormatContext context)
    {
        var pattern = context.GetString("pattern") ?? throw context.Fail("pattern mode needs the 'pattern' option: a regular expression with named groups.");
        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException ex)
        {
            throw context.Fail($"The pattern is not a valid regular expression: {ex.Message}", inner: ex);
        }

        var names = regex.GetGroupNames().Where(n => !int.TryParse(n, out _)).ToList();
        if (names.Count == 0) throw context.Fail("The pattern has no named groups, such as (?<name>\\w+).");
        var trim = context.GetBool("trim");

        var rows = new JsonArray();
        var unmatched = new JsonArray();
        var lines = CodecText.SplitLines(text);
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Length == 0) continue;
            Match match;
            try
            {
                match = regex.Match(lines[i]);
            }
            catch (RegexMatchTimeoutException ex)
            {
                throw context.Fail("The pattern took too long on this line.", new FormatLocation { Line = i + 1 }, ex);
            }

            if (!match.Success)
            {
                unmatched.Add(new JsonObject { ["line"] = i + 1, ["text"] = lines[i] });
                continue;
            }

            var row = new JsonObject();
            foreach (var name in names)
            {
                var group = match.Groups[name];
                row[name] = group.Success ? Typed(group.Value, context, trim) : null;
            }
            rows.Add(row);
        }

        var result = new JsonObject { ["rows"] = rows };
        if (unmatched.Count > 0) result["unmatched"] = unmatched;
        return result;
    }

    private static string EncodeTemplate(JsonNode? value, FormatContext context, string newline, bool trailing)
    {
        var template = context.GetString("template") ?? throw context.Fail("pattern mode writes with the 'template' option, e.g. \"{name} owes {amount}\".");
        var parts = ParseTemplate(template, context);
        var rows = CodecText.ListOf(value, "rows") ?? throw context.Fail("pattern mode writes $.rows (or a list at the root); the mapping gave neither.");

        var lines = new List<string>();
        for (var r = 0; r < rows.Count; r++)
        {
            var line = new StringBuilder();
            foreach (var (literal, path) in parts)
            {
                if (path is null)
                {
                    line.Append(literal);
                    continue;
                }

                JsonNode? node = rows[r];
                foreach (var step in path.Split('.')) node = (node as JsonObject)?[step];
                line.Append(CodecText.TryScalar(node, out var text) ? text : node!.ToJsonString());
            }
            lines.Add(line.ToString());
        }
        return CodecText.Join(lines, newline, trailing);
    }

    /// <summary>A template's parts: literal text, or a {path} placeholder. {{ and }} are literal braces.</summary>
    private static List<(string Literal, string? Path)> ParseTemplate(string template, FormatContext context)
    {
        var parts = new List<(string, string?)>();
        var literal = new StringBuilder();
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if ((c == '{' || c == '}') && i + 1 < template.Length && template[i + 1] == c)
            {
                literal.Append(c);
                i++;
                continue;
            }
            if (c == '}') throw context.Fail($"The template has a '}}' with no '{{' before it (at {i + 1}). Write }}}} for a literal brace.");
            if (c != '{')
            {
                literal.Append(c);
                continue;
            }

            var end = template.IndexOf('}', i + 1);
            if (end < 0) throw context.Fail($"The template has a '{{' that is never closed (at {i + 1}).");
            var path = template.Substring(i + 1, end - i - 1).Trim();
            if (path.Length == 0) throw context.Fail($"The template has an empty placeholder {{}} (at {i + 1}).");
            if (literal.Length > 0) parts.Add((literal.ToString(), null));
            literal.Clear();
            parts.Add(("", path));
            i = end;
        }
        if (literal.Length > 0) parts.Add((literal.ToString(), null));
        return parts;
    }
}
