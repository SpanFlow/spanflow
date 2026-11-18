using SpanFlow.Mapping.Formats.Codecs;

namespace SpanFlow.Mapping.Formats;

/// <summary>One bundled EDI guide, as listed to a designer.</summary>
public sealed record EdiGuideInfo(string Name, string Standard, string Message, string? Version, string? Title);

/// <summary>
/// E73-T11: the EDI guides bundled with this library (X12 004010 850, 810, 856, 997; EDIFACT D96A ORDERS, INVOIC,
/// DESADV). They are data: the edi format's <c>guides</c> option takes "bundled", their names, or guides of your own.
/// </summary>
public static class EdiGuides
{
    public static IReadOnlyList<EdiGuideInfo> Bundled =>
        EdiLevel2.Bundled(FormatContext.Create(new EdiCodec(), null, FormatDirection.Decode))
            .Select(g => new EdiGuideInfo(g.Name, g.Standard, g.Message, g.Version, g.Title))
            .ToList();
}
