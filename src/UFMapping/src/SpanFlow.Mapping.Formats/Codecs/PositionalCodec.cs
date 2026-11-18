using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Formats.Codecs;

/// <summary>
/// Positional data: meaning comes from position and length, as in bank, card (ISO 8583 style) and mainframe files.
/// A <c>layout</c> option describes the fields (fixed <c>length</c>, a <c>prefix</c> length written before the value
/// such as LL / LLL, or <c>rest</c>), their types, padding and groups, or several <c>records</c> chosen per line. The
/// layout is data: a new partner format is a new layout, never new code.
/// </summary>
public sealed class PositionalCodec : IFormatCodec
{
    private static readonly IReadOnlyList<FormatOptionInfo> OptionList =
    [
        new("layout", FormatOptionKind.Object, FormatOptionUse.Both, "The fields (or line records) with their lengths, types and padding."),
        new("unit", FormatOptionKind.String, FormatOptionUse.Both, "What lengths count: chars (default) or bytes of the encoding."),
        new("strict", FormatOptionKind.Bool, FormatOptionUse.DecodeOnly, "Fail when data is left after the last field (default true)."),
        new("newline", FormatOptionKind.String, FormatOptionUse.EncodeOnly, "Line break between records (default \\n)."),
        new("trailingNewline", FormatOptionKind.Bool, FormatOptionUse.EncodeOnly, "End the last record with a line break too."),
    ];

    public string Type => "positional";

    public string ContentType => "text/plain";

    public IReadOnlyList<FormatOptionInfo> Options => OptionList;

    public JsonNode? Decode(FormatPayload raw, FormatContext context) =>
        DecodeText(raw.AsText(context.Encoding), context);

    public FormatPayload Encode(JsonNode? value, FormatContext context) =>
        FormatPayload.FromText(EncodeText(value, context));

    /// <summary>Shared with the text codec's <c>fixed</c> mode.</summary>
    internal static JsonNode DecodeText(string text, FormatContext context)
    {
        var layout = PositionalLayout.Parse(context.GetObject("layout"), context);
        return new PositionalEngine(context, context.GetString("unit")).Decode(text, layout, context.GetBool("strict", true));
    }

    internal static string EncodeText(JsonNode? value, FormatContext context)
    {
        var layout = PositionalLayout.Parse(context.GetObject("layout"), context);
        return new PositionalEngine(context, context.GetString("unit"))
            .Encode(value, layout, CodecText.Separator(context.GetString("newline", "\n")!), context.GetBool("trailingNewline"));
    }
}
