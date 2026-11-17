using System.Text;
using System.Text.Json.Nodes;
using SpanFlow.Connectors.Files;

namespace SpanFlow.Libraries.Playground.Panels;

/// <summary>
/// <c>POST /api/files/convert</c>: <c>{ from, to, content | source, target?, options? }</c>. A file is read into rows by
/// the library's format (CSV, TSV, NDJSON, JSON; XLSX from a store when self-hosted) and written back in another
/// (those, or XLSX). Hosted: formats only (D71.3): the text comes in the request, the result goes back in the answer,
/// XLSX is only written. Self-hosted: <c>source</c>/<c>target</c> name a store and a path, read and written with the
/// library's stores (<see cref="FileStores"/>).
/// <para><c>POST /api/files/list</c>: <c>{ storeId, path }</c>, one folder of a store (self-hosted).</para>
/// </summary>
public static class FilesPanel
{
    public static readonly IReadOnlyList<string> ReadFormats = ["csv", "tsv", "ndjson", "json", "xlsx"];
    public static readonly IReadOnlyList<string> WriteFormats = ["json", "csv", "tsv", "ndjson", "xlsx"];

    /// <summary>Columns in one row at most, and cells in an XLSX, so a line of commas cannot make a huge sheet.</summary>
    public const int MaxColumns = 200;
    public const int MaxXlsxCells = 50_000;

    /// <summary>The most read from a store for a whole-document format (JSON, XLSX); row formats stop at the row cap.</summary>
    public const int MaxStoreReadBytes = 16 * 1024 * 1024;

    private static readonly Dictionary<string, IFileFormat> Formats = new IFileFormat[]
    {
        new CsvFormat(), new TsvFormat(), new NdjsonFormat(), new JsonFormat(), new XlsxFormat(),
    }.ToDictionary(f => f.Format, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Writes one tiny workbook at start. ClosedXML's first workbook pays for its one-time setup (seconds on a cold
    /// process), which would otherwise land on a visitor's call and its time cap.
    /// </summary>
    public static void WarmUp()
    {
        using var output = new MemoryStream();
        Formats["xlsx"].WriteAsync(output, new JsonArray(new JsonObject { ["a"] = 1 }), new JsonObject(), false, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    public static async Task<JsonNode> Convert(PanelCall call)
    {
        var body = call.Body;
        var ct = call.Cancellation;
        var maxRows = call.Limits.MaxRows;

        var from = (PlaygroundEndpoint.OptionalString(body, "from") ?? "csv").ToLowerInvariant();
        var to = (PlaygroundEndpoint.OptionalString(body, "to") ?? "json").ToLowerInvariant();
        if (!ReadFormats.Contains(from)) throw PanelRefusal.Invalid($"\"from\" is one of {string.Join(", ", ReadFormats)}.");
        if (!WriteFormats.Contains(to)) throw PanelRefusal.Invalid($"\"to\" is one of {string.Join(", ", WriteFormats)}.");
        var options = Options(body["options"]);
        var stores = call.Services.GetRequiredService<FileStores>();

        // ---- read ----------------------------------------------------------------------------------------------
        Stream input;
        string? sourcePath = null;
        if (body["source"] is JsonObject source)
        {
            var (storeId, path) = Location(source, "source");
            sourcePath = path;
            var store = stores.Open(storeId);
            await using var opened = await store.OpenReadAsync(path, ct);
            input = await Copy(opened, MaxStoreReadBytes, ct);
        }
        else if (from == "xlsx")
        {
            // A workbook is a zip: from a stranger, a decompression bomb. Only from the owner's own stores.
            throw PanelRefusal.Invalid(call.Hosted
                ? "The hosted playground writes XLSX but does not read it. Paste CSV, TSV, NDJSON or JSON."
                : "Read an XLSX from a store (\"source\": { \"storeId\", \"path\" }).");
        }
        else
        {
            var content = PlaygroundEndpoint.RequireString(body, "content", call.Limits.MaxBodyBytes);
            input = new MemoryStream(new UTF8Encoding(false).GetBytes(content), writable: false);
        }

        var rows = new JsonArray();
        var truncated = false;
        await using (input)
        {
            var format = Formats[from];
            if (format.SupportsRowStreaming)
            {
                await foreach (var row in format.ReadRowsAsync(input, options, ct))
                {
                    if (rows.Count == maxRows) { truncated = true; break; }
                    rows.Add(Checked(row));
                }
            }
            else
            {
                var document = await format.ReadAsync(input, options, ct);
                var items = document is JsonArray array ? array.ToList() : [document];
                truncated = items.Count > maxRows;
                foreach (var item in items.Take(maxRows)) rows.Add(Checked(item?.DeepClone()));
            }
        }
        ct.ThrowIfCancellationRequested();

        var columns = rows.OfType<JsonObject>().SelectMany(r => r.Select(kv => kv.Key)).Distinct(StringComparer.Ordinal).ToList();
        if (to == "xlsx" && (long)columns.Count * Math.Max(1, rows.Count) > MaxXlsxCells)
            throw PanelRefusal.TooLarge($"An XLSX here holds at most {MaxXlsxCells:N0} cells; this one would have {(long)columns.Count * rows.Count:N0}.");

        // ---- write ---------------------------------------------------------------------------------------------
        using var output = new MemoryStream();
        await Formats[to].WriteAsync(output, rows, options, suppressHeader: false, ct);
        var bytes = output.ToArray();

        var file = new JsonObject
        {
            ["format"] = to,
            ["contentType"] = ContentType(to),
            ["fileName"] = "converted." + to,
            ["bytes"] = bytes.Length,
        };

        if (body["target"] is JsonObject target)
        {
            var (storeId, path) = Location(target, "target");
            var overwrite = target["overwrite"] is JsonValue o && o.TryGetValue<bool>(out var yes) && yes;
            var store = stores.Open(storeId);
            try
            {
                await using var write = await store.OpenWriteAsync(path, overwrite ? FileWriteMode.Overwrite : FileWriteMode.Create, ct);
                await write.WriteAsync(bytes, ct);
            }
            catch (IOException ex)
            {
                throw PanelRefusal.Failed(overwrite ? ex.Message : $"{ex.Message} Tick overwrite to replace it.");
            }
            file["written"] = new JsonObject { ["storeId"] = storeId, ["path"] = path };
            file["fileName"] = Path.GetFileName(path);
        }

        // Shown on the page and downloadable either way (the cap on the answer still applies).
        if (to == "xlsx") file["base64"] = System.Convert.ToBase64String(bytes);
        else file["text"] = new UTF8Encoding(false).GetString(bytes);

        return new JsonObject
        {
            ["rowCount"] = rows.Count,
            ["truncated"] = truncated,
            ["maxRows"] = maxRows,
            ["source"] = sourcePath,
            ["columns"] = new JsonArray(columns.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray()),
            ["rows"] = rows.DeepClone(),
            ["output"] = file,
        };
    }

    public static async Task<JsonNode> List(PanelCall call)
    {
        var storeId = PlaygroundEndpoint.RequireString(call.Body, "storeId", 200);
        var path = PlaygroundEndpoint.OptionalString(call.Body, "path") ?? "";
        var store = call.Services.GetRequiredService<FileStores>().Open(storeId);
        IReadOnlyList<FileEntryDto> entries;
        try
        {
            entries = await store.ListAsync(path, call.Cancellation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException && ex is not PanelRefusal)
        {
            throw PanelRefusal.Failed(ex.Message);
        }
        return new JsonObject
        {
            ["storeId"] = storeId,
            ["path"] = path,
            ["entries"] = System.Text.Json.JsonSerializer.SerializeToNode(entries.Take(call.Limits.MaxRows), PlaygroundEndpoint.JsonOptions),
            ["truncated"] = entries.Count > call.Limits.MaxRows,
        };
    }

    private static (string StoreId, string Path) Location(JsonObject node, string what)
    {
        var storeId = PlaygroundEndpoint.RequireString(node, "storeId", 200);
        var path = PlaygroundEndpoint.RequireString(node, "path", 1024);
        if (string.IsNullOrWhiteSpace(path)) throw PanelRefusal.Invalid($"\"{what}.path\" names a file in the store.");
        return (storeId, path);
    }

    private static async Task<MemoryStream> Copy(Stream from, int max, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;
        while ((read = await from.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > max) throw PanelRefusal.TooLarge($"The file is larger than {max / 1024 / 1024} MB, the most the playground reads.");
        }
        buffer.Position = 0;
        return buffer;
    }

    /// <summary>
    /// The format options a visitor may set: <c>csv</c> (delimiter, quote, hasHeader, columns) and <c>xlsx</c> (sheet,
    /// hasHeader). Nothing that names a path or a store; the text encoding is always UTF-8.
    /// </summary>
    private static JsonObject Options(JsonNode? node)
    {
        var options = new JsonObject();
        if (node is null) return options;
        if (node is not JsonObject given) throw PanelRefusal.Invalid("\"options\" is { \"csv\": { ... }, \"xlsx\": { ... } }.");

        if (given["csv"] is JsonObject csv)
        {
            var kept = new JsonObject();
            foreach (var key in new[] { "delimiter", "quote", "hasHeader", "columns" })
            {
                if (csv[key] is { } value) kept[key] = value.DeepClone();
            }
            options["csv"] = kept;
        }
        if (given["xlsx"] is JsonObject xlsx)
        {
            var kept = new JsonObject();
            if (xlsx["sheet"] is JsonValue sheet && sheet.TryGetValue<string>(out var name) && name.Length is > 0 and <= 31) kept["sheet"] = name;
            if (xlsx["hasHeader"] is { } header) kept["hasHeader"] = header.DeepClone();
            options["xlsx"] = kept;
        }
        return options;
    }

    private static JsonNode? Checked(JsonNode? row)
    {
        if (row is JsonObject obj && obj.Count > MaxColumns)
            throw PanelRefusal.TooLarge($"A row has {obj.Count} columns; the playground reads at most {MaxColumns}.");
        return row;
    }

    private static string ContentType(string format) => format switch
    {
        "json" => "application/json",
        "ndjson" => "application/x-ndjson",
        "csv" => "text/csv",
        "tsv" => "text/tab-separated-values",
        "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/octet-stream",
    };
}
