using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Formats.Codecs;

/// <summary>
/// JSON both ways: the pass-through codec, and what a mapping with no formats uses. Options: <c>ndjson</c> (one JSON
/// value per line; decodes to an array, encodes an array) and <c>indent</c> (pretty output). Empty input decodes to
/// null.
/// </summary>
public sealed class JsonCodec : IFormatCodec
{
    public const string NdjsonOption = "ndjson";
    public const string IndentOption = "indent";

    private static readonly IReadOnlyList<FormatOptionInfo> OptionList =
    [
        new(NdjsonOption, FormatOptionKind.Bool, FormatOptionUse.Both, "One JSON value per line: read into a list, and a list written one item per line."),
        new(IndentOption, FormatOptionKind.Bool, FormatOptionUse.EncodeOnly, "Write indented, readable JSON instead of one compact line."),
    ];

    public string Type => "json";

    public string ContentType => "application/json";

    public IReadOnlyList<FormatOptionInfo> Options => OptionList;

    public JsonNode? Decode(FormatPayload raw, FormatContext context)
    {
        var text = raw.AsText(context.Encoding);
        var documentOptions = new JsonDocumentOptions { MaxDepth = context.Limits.MaxDepth };

        if (!context.GetBool(NdjsonOption))
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return Parse(text, documentOptions, context, lineOffset: 0);
        }

        var items = new JsonArray();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;
            items.Add(Parse(line, documentOptions, context, lineOffset: i));
        }
        return items;
    }

    public FormatPayload Encode(JsonNode? value, FormatContext context)
    {
        if (context.GetBool(NdjsonOption))
        {
            if (value is not JsonArray items)
                throw context.Fail("ndjson output needs the mapping to produce a list (an array at the root).");
            var lineOptions = WriterOptions(indent: false);
            return FormatPayload.FromText(string.Join("\n", items.Select(item => ToJson(item, lineOptions))));
        }

        return FormatPayload.FromText(ToJson(value, WriterOptions(context.GetBool(IndentOption))));
    }

    private static JsonNode? Parse(string text, JsonDocumentOptions options, FormatContext context, int lineOffset)
    {
        try
        {
            return JsonNode.Parse(text, documentOptions: options);
        }
        catch (JsonException ex)
        {
            var location = new FormatLocation
            {
                Line = (int)(ex.LineNumber ?? 0) + 1 + lineOffset,
                Column = ex.BytePositionInLine is { } column ? (int)column + 1 : null,
            };
            throw context.Fail(Clean(ex.Message), location, ex);
        }
    }

    // Indented output uses "\n" on every OS (the writer would use Environment.NewLine), so the same mapping gives the
    // same bytes everywhere. A raw CR/LF can only come from the indenting: inside strings they are escaped.
    private static string ToJson(JsonNode? node, JsonSerializerOptions options) =>
        node is null ? "null" : options.WriteIndented ? node.ToJsonString(options).Replace("\r\n", "\n") : node.ToJsonString(options);

    // Output is data for a file or a response body, not markup, so non-ASCII text stays readable.
    private static JsonSerializerOptions WriterOptions(bool indent) => new()
    {
        WriteIndented = indent,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // System.Text.Json appends its own "LineNumber: n | BytePositionInLine: m." tail; the location says that better.
    private static string Clean(string message)
    {
        var cut = message.IndexOf(" LineNumber:", StringComparison.Ordinal);
        var text = (cut > 0 ? message.Substring(0, cut) : message).Trim();
        return text.Length == 0 ? "The JSON is not valid." : text;
    }
}
