using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace SpanFlow.Connectors.Files;

/// <summary>
/// CSV/TSV format over the RFC 4180 <see cref="Csv"/> reader/writer. Reads rows into JSON objects
/// keyed by the header (or by <c>columns[].name</c> / <c>field1…</c> when there is no header) and,
/// when <c>columns[].type</c> is given, coerces string cells to number/bool/datetime. Options live
/// under the request's <c>csv</c> object:
/// <code>{ "delimiter":",", "quote":"\"", "hasHeader":true, "encoding":"utf-8",
///          "columns":[{ "name":"amount","type":"number" },{ "name":"at","type":"datetime","format":"yyyy-MM-dd" }] }</code>
/// </summary>
public abstract class DelimitedFormat : IFileFormat
{
    public abstract string Format { get; }

    /// <summary>Delimiter used when the options don't override it (',' for csv, '\t' for tsv).</summary>
    protected abstract char DefaultDelimiter { get; }

    private sealed record Column(string Name, string? Type, string? DateFormat);

    private sealed record Options(char Delimiter, char Quote, bool HasHeader, Encoding Encoding, List<Column> Columns);

    private Options ReadOptions(JsonObject request)
    {
        var csv = request["csv"] as JsonObject ?? [];
        var delimiter = (csv["delimiter"]?.GetValue<string>() is { Length: > 0 } d) ? d[0] : DefaultDelimiter;
        var quote = (csv["quote"]?.GetValue<string>() is { Length: > 0 } q) ? q[0] : '"';
        var hasHeader = csv["hasHeader"]?.GetValue<bool>() ?? true;
        var encoding = ResolveEncoding(csv["encoding"]?.GetValue<string>());

        var columns = new List<Column>();
        if (csv["columns"] is JsonArray cols)
        {
            foreach (var col in cols.OfType<JsonObject>())
            {
                var name = col["name"]?.GetValue<string>();
                if (string.IsNullOrEmpty(name)) continue;
                columns.Add(new Column(name, col["type"]?.GetValue<string>(), col["format"]?.GetValue<string>()));
            }
        }

        return new Options(delimiter, quote, hasHeader, encoding, columns);
    }

    private static Encoding ResolveEncoding(string? name) =>
        string.IsNullOrWhiteSpace(name) ? new UTF8Encoding(false) : Encoding.GetEncoding(name);

    public bool SupportsRowStreaming => true;

    public async Task<JsonNode> ReadAsync(Stream stream, JsonObject request, CancellationToken ct)
    {
        // Materializing is the streaming read with every row kept — one row-shaping rule, so a
        // streamed row and a materialized one can never come out different.
        var rows = new JsonArray();
        await foreach (var row in ReadRowsAsync(stream, request, ct)) rows.Add(row);
        return rows;
    }

    public async IAsyncEnumerable<JsonNode> ReadRowsAsync(
        Stream stream, JsonObject request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var options = ReadOptions(request);
        // detectEncodingFromByteOrderMarks: honours a BOM if present, else uses the configured encoding.
        using var reader = new StreamReader(stream, options.Encoding, detectEncodingFromByteOrderMarks: true);

        var typesByName = options.Columns
            .Where(c => c.Type is not null)
            .ToDictionary(c => c.Name, c => c, StringComparer.Ordinal);

        string[]? header = null;
        var first = true;

        foreach (var record in Csv.ReadRecords(reader, options.Delimiter, options.Quote))
        {
            ct.ThrowIfCancellationRequested();
            if (first)
            {
                first = false;
                header = options.HasHeader
                    ? record
                    : ColumnNames(options.Columns, record.Length);
                if (options.HasHeader) continue;
            }

            var names = header ?? ColumnNames(options.Columns, record.Length);
            var row = new JsonObject();
            for (var i = 0; i < record.Length; i++)
            {
                var name = i < names.Length ? names[i] : $"field{i + 1}";
                row[name] = Coerce(record[i], typesByName.GetValueOrDefault(name));
            }
            yield return row;
        }

        await Task.CompletedTask; // the reader is synchronous; the async shape is the interface's
    }

    private static string[] ColumnNames(List<Column> columns, int count)
    {
        var names = new string[count];
        for (var i = 0; i < count; i++)
        {
            names[i] = i < columns.Count ? columns[i].Name : $"field{i + 1}";
        }
        return names;
    }

    private static JsonNode? Coerce(string value, Column? column)
    {
        if (column?.Type is null) return JsonValue.Create(value); // untyped → string
        if (value.Length == 0) return null; // an empty cell for a typed column is null

        return column.Type.ToLowerInvariant() switch
        {
            "number" => double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var n)
                ? JsonValue.Create(n)
                : JsonValue.Create(value),
            "bool" or "boolean" => ParseBool(value),
            "datetime" or "date" => ParseDate(value, column.DateFormat),
            _ => JsonValue.Create(value),
        };
    }

    private static JsonNode? ParseBool(string value) => value.Trim().ToLowerInvariant() switch
    {
        "true" or "1" or "yes" or "y" => JsonValue.Create(true),
        "false" or "0" or "no" or "n" => JsonValue.Create(false),
        _ => JsonValue.Create(value),
    };

    private static JsonNode? ParseDate(string value, string? format)
    {
        var parsed = format is not null
            ? DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact) ? exact : (DateTime?)null
            : DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var any) ? any : (DateTime?)null;
        return parsed is null
            ? JsonValue.Create(value)
            : JsonValue.Create(parsed.Value.ToString("o", CultureInfo.InvariantCulture));
    }

    public async Task WriteAsync(Stream stream, JsonNode payload, JsonObject request, bool suppressHeader, CancellationToken ct)
    {
        var options = ReadOptions(request);
        var rows = payload as JsonArray ?? [payload];
        var columns = WriteColumns(options.Columns, rows);

        await using var writer = new StreamWriter(stream, options.Encoding) { NewLine = "\r\n" };
        if (options.HasHeader && !suppressHeader)
        {
            Csv.WriteRecord(writer, columns, options.Delimiter, options.Quote);
        }
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var obj = row as JsonObject;
            var fields = columns.Select(c => Stringify(obj?[c])).ToList();
            Csv.WriteRecord(writer, fields, options.Delimiter, options.Quote);
        }
        await writer.FlushAsync(ct);
    }

    /// <summary>Column order from <c>columns[]</c> when given, else the union of keys across rows.</summary>
    private static List<string> WriteColumns(List<Column> columns, JsonArray rows)
    {
        if (columns.Count > 0) return columns.Select(c => c.Name).ToList();

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows.OfType<JsonObject>())
        {
            foreach (var kv in row)
            {
                if (seen.Add(kv.Key)) names.Add(kv.Key);
            }
        }
        return names;
    }

    private static string Stringify(JsonNode? node) => node switch
    {
        null => string.Empty,
        JsonValue value when value.TryGetValue<string>(out var s) => s,
        _ => node.ToJsonString(),
    };
}

/// <summary>Comma-separated values.</summary>
public sealed class CsvFormat : DelimitedFormat
{
    public override string Format => "csv";
    protected override char DefaultDelimiter => ',';
}

/// <summary>Tab-separated values — CSV with a tab delimiter.</summary>
public sealed class TsvFormat : DelimitedFormat
{
    public override string Format => "tsv";
    protected override char DefaultDelimiter => '\t';
}
