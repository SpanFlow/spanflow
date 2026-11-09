using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpanFlow.Connectors.Files;

/// <summary>Whole-file JSON: reads the document as-is (array or object), writes the payload pretty-printed.</summary>
public sealed class JsonFormat : IFileFormat
{
    public string Format => "json";

    // A JSON document is written whole; appending a second document to the same file is invalid.
    public bool SupportsAppend => false;

    public async Task<JsonNode> ReadAsync(Stream stream, JsonObject request, CancellationToken ct)
    {
        var node = await JsonNode.ParseAsync(stream, cancellationToken: ct);
        return node ?? new JsonArray();
    }

    public async Task WriteAsync(Stream stream, JsonNode payload, JsonObject request, bool suppressHeader, CancellationToken ct)
    {
        await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        payload.WriteTo(writer);
        await writer.FlushAsync(ct);
    }
}

/// <summary>
/// Newline-delimited JSON: one JSON value per line. Reads into a JSON array (blank lines skipped);
/// writes each element of an array (or a lone value) on its own line, which makes it append-friendly.
/// </summary>
public sealed class NdjsonFormat : IFileFormat
{
    public string Format => "ndjson";

    public bool SupportsRowStreaming => true;

    public async Task<JsonNode> ReadAsync(Stream stream, JsonObject request, CancellationToken ct)
    {
        var rows = new JsonArray();
        await foreach (var row in ReadRowsAsync(stream, request, ct)) rows.Add(row);
        return rows;
    }

    public async IAsyncEnumerable<JsonNode> ReadRowsAsync(
        Stream stream, JsonObject request, [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (JsonNode.Parse(line) is { } row) yield return row;
        }
    }

    public async Task WriteAsync(Stream stream, JsonNode payload, JsonObject request, bool suppressHeader, CancellationToken ct)
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n" };
        var rows = payload as JsonArray ?? [payload];
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            await writer.WriteLineAsync((row?.ToJsonString() ?? "null").AsMemory(), ct);
        }
        await writer.FlushAsync(ct);
    }
}
