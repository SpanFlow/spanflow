using System.Text.Json.Nodes;

namespace SpanFlow.Connectors.Files;

/// <summary>
/// Parses a file's bytes into the pipeline payload and back. Tabular formats (csv/tsv/xlsx) yield a
/// JSON array of row objects — the same shape a db retrieve produces, so transforms and delivers do
/// not care where rows came from. json/ndjson pass their structure through. One implementation per
/// <c>format</c>; the store supplies the stream.
/// </summary>
public interface IFileFormat
{
    /// <summary>The <c>format</c> discriminator this handles ("csv", "tsv", "json", "ndjson", "xlsx").</summary>
    string Format { get; }

    /// <summary>
    /// Whether new rows can be tacked onto an existing file's bytes. True for line-oriented text
    /// (csv/tsv/ndjson); false for whole-document formats (json, xlsx) that must be rewritten in full.
    /// </summary>
    bool SupportsAppend => true;

    /// <summary>
    /// Whether the format can hand back one row at a time instead of one document (E22-T1). True for
    /// the record-oriented formats (csv/tsv/ndjson), where a row is bounded by a line; false for
    /// whole-document formats (json, xlsx), which have to be parsed in full before any row is known.
    /// </summary>
    bool SupportsRowStreaming => false;

    /// <summary><paramref name="request"/> is the connector request (path/format + format options).</summary>
    Task<JsonNode> ReadAsync(Stream stream, JsonObject request, CancellationToken ct);

    /// <summary>
    /// The file's rows, one at a time, without ever holding them all. Only valid when
    /// <see cref="SupportsRowStreaming"/> is true. The caller owns <paramref name="stream"/> and must
    /// keep it open for the whole enumeration — this is a lazy sequence, so nothing is read until it
    /// is iterated.
    /// </summary>
    IAsyncEnumerable<JsonNode> ReadRowsAsync(Stream stream, JsonObject request, CancellationToken ct) =>
        throw new NotSupportedException($"'{Format}' files are read whole — they cannot be streamed row by row.");

    /// <summary>
    /// Emits <paramref name="payload"/>. <paramref name="suppressHeader"/> is set when appending to
    /// an existing file so a tabular writer does not repeat the header row.
    /// </summary>
    Task WriteAsync(Stream stream, JsonNode payload, JsonObject request, bool suppressHeader, CancellationToken ct);
}

/// <summary>DI-backed lookup of a file format by its <c>format</c> field.</summary>
public class FileFormatRegistry(IEnumerable<IFileFormat> formats)
{
    private readonly Dictionary<string, IFileFormat> _formats =
        formats.ToDictionary(f => f.Format, StringComparer.OrdinalIgnoreCase);

    public IFileFormat Get(string format) =>
        _formats.TryGetValue(format, out var impl)
            ? impl
            : throw new InvalidOperationException($"No file format registered for '{format}'.");
}
