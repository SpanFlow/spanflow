using System.Text.Json.Nodes;

namespace SpanFlow.Connectors.Files;

/// <summary>
/// A file whose bytes are the payload rather than rows in it (E38-T4): a rendered PDF, a generated
/// image, a spreadsheet an <c>export</c> node produced, a PDF about to be handed to the
/// <c>extract</c> node.
///
/// <para>
/// <b>Why this is a format and not a new connector.</b> Every store already writes bytes — local,
/// sftp, ftp, s3, azureBlob (E04-T4/T5) — and the format layer is the only thing that decides what
/// those bytes are. So one small format puts a rendered document into all five destinations with no
/// new storage code, which is exactly what E38-D4 asks for: one renderer, many sinks, through the
/// connector rule rather than around it.
/// </para>
/// <para>
/// <b>The payload it writes</b> is the file artifact the <c>renderDocument</c> and <c>export</c> nodes
/// already produce — an object carrying <c>contentBase64</c> — or a bare base64 string. A one-element
/// array (a fan-out that produced a single document) is unwrapped; a longer one is refused, because
/// writing four documents to one path would silently keep whichever landed last.
/// </para>
/// </summary>
public sealed class BinaryFormat : IFileFormat
{
    public string Format => "binary";

    /// <summary>
    /// A document is written whole. Appending one PDF to another does not make a longer PDF — it makes a
    /// file no reader will open — so this is refused by the connector with the right advice instead of
    /// producing corruption nobody notices until somebody opens it.
    /// </summary>
    public bool SupportsAppend => false;

    /// <summary>
    /// How much of a file a binary read may pull into memory, when the request does not say. A read turns
    /// the whole file into a base64 string in the payload, so the cap is the point: without one, pointing
    /// this at a backup archive takes the process down.
    /// </summary>
    public const long DefaultMaxBytes = 32L * 1024 * 1024;

    public async Task<JsonNode> ReadAsync(Stream stream, JsonObject request, CancellationToken ct)
    {
        var max = MaxBytes(request);
        using var buffer = new MemoryStream();

        // Copied with a cap rather than read whole and measured afterwards: the point is not to hold the
        // oversized file at all, so the refusal has to happen while it is still arriving.
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > max)
            {
                throw new InvalidOperationException(
                    $"binary read is over the {max:N0}-byte limit. Raise the request's 'maxBytes' if the file really is "
                    + "this large — a binary read holds the whole file in the payload as base64.");
            }
            buffer.Write(chunk, 0, read);
        }

        var bytes = buffer.ToArray();
        var path = request["path"]?.GetValue<string>() ?? "";
        return new JsonObject
        {
            ["filename"] = Path.GetFileName(path.Replace('\\', '/')),
            ["contentType"] = request["contentType"]?.GetValue<string>() ?? ContentTypes.ForPath(path),
            ["contentBase64"] = Convert.ToBase64String(bytes),
            ["size"] = bytes.Length,
        };
    }

    public async Task WriteAsync(
        Stream stream, JsonNode payload, JsonObject request, bool suppressHeader, CancellationToken ct)
    {
        var bytes = Bytes(payload);
        await stream.WriteAsync(bytes, ct);
    }

    private static long MaxBytes(JsonObject request) =>
        request["maxBytes"] is JsonValue value && value.TryGetValue<long>(out var max) && max > 0
            ? max
            : DefaultMaxBytes;

    /// <summary>
    /// The file's bytes out of the payload. Every refusal names what it found and what it wanted: "the
    /// payload is not a document" is a message an author can act on, and a silently empty file is not.
    /// </summary>
    public static byte[] Bytes(JsonNode? payload)
    {
        if (payload is JsonArray array)
        {
            if (array.Count == 1) return Bytes(array[0]);
            throw new InvalidOperationException(
                $"a binary write takes one document and the payload holds {array.Count}. Put this deliver inside a "
                + "foreach so each document gets its own path, or merge them into one document first.");
        }

        var encoded = payload switch
        {
            JsonObject obj when obj["contentBase64"] is JsonValue v && v.TryGetValue<string>(out var s) => s,
            JsonValue value when value.TryGetValue<string>(out var s) => s,
            null => throw new InvalidOperationException("a binary write has nothing to write — the payload is null."),
            _ => throw new InvalidOperationException(
                "a binary write needs a file artifact — an object with 'contentBase64' (what the renderDocument and "
                + "export nodes produce), or a base64 string. Use format 'json' to write the payload as JSON."),
        };

        try
        {
            return Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("a binary write's 'contentBase64' is not valid base64.");
        }
    }
}

/// <summary>What a file's extension says it is, for a binary read and a stored document's reference.</summary>
public static class ContentTypes
{
    private static readonly IReadOnlyDictionary<string, string> ByExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".svg"] = "image/svg+xml",
            [".csv"] = "text/csv",
            [".tsv"] = "text/tab-separated-values",
            [".txt"] = "text/plain",
            [".html"] = "text/html",
            [".json"] = "application/json",
            [".ndjson"] = "application/x-ndjson",
            [".xml"] = "application/xml",
            [".zip"] = "application/zip",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        };

    public static string ForPath(string? path) =>
        ByExtension.GetValueOrDefault(Path.GetExtension(path ?? ""), "application/octet-stream");
}
