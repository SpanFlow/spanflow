using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClosedXML.Excel;

namespace SpanFlow.Connectors.Files;

/// <summary>
/// Excel (.xlsx) format via ClosedXML. Reads a worksheet (by name or 1-based index) or an explicit
/// A1-style range into JSON row objects — cells keep their Excel type (number/bool/datetime become
/// JSON values, dates as ISO 8601). Writes a fresh single-sheet workbook with an optional header row.
/// Options live under the request's <c>xlsx</c> object:
/// <code>{ "sheet":"Sheet1"|1, "range":"A1:F100", "hasHeader":true }</code>
/// A workbook is a whole-document format, so it is always rewritten in full (no append).
/// </summary>
public sealed class XlsxFormat : IFileFormat
{
    public string Format => "xlsx";

    public bool SupportsAppend => false;

    public Task<JsonNode> ReadAsync(Stream stream, JsonObject request, CancellationToken ct)
    {
        var options = request["xlsx"] as JsonObject ?? [];
        var hasHeader = options["hasHeader"]?.GetValue<bool>() ?? true;

        using var workbook = new XLWorkbook(stream);
        var sheet = ResolveSheet(workbook, options["sheet"]);
        var range = options["range"]?.GetValue<string>() is { Length: > 0 } spec ? sheet.Range(spec) : sheet.RangeUsed();

        var rows = new JsonArray();
        if (range is null) return Task.FromResult<JsonNode>(rows);

        var columns = range.ColumnCount();
        var names = new string[columns];
        var firstDataRow = 1;
        if (hasHeader)
        {
            for (var c = 1; c <= columns; c++)
            {
                var header = range.Cell(1, c).GetFormattedString();
                names[c - 1] = string.IsNullOrWhiteSpace(header) ? $"field{c}" : header;
            }
            firstDataRow = 2;
        }
        else
        {
            for (var c = 1; c <= columns; c++) names[c - 1] = $"field{c}";
        }

        var lastRow = range.RowCount();
        for (var r = firstDataRow; r <= lastRow; r++)
        {
            ct.ThrowIfCancellationRequested();
            var row = new JsonObject();
            for (var c = 1; c <= columns; c++)
            {
                row[names[c - 1]] = CellValue(range.Cell(r, c));
            }
            rows.Add(row);
        }

        return Task.FromResult<JsonNode>(rows);
    }

    public Task WriteAsync(Stream stream, JsonNode payload, JsonObject request, bool suppressHeader, CancellationToken ct)
    {
        var options = request["xlsx"] as JsonObject ?? [];
        var hasHeader = options["hasHeader"]?.GetValue<bool>() ?? true;
        var rows = payload as JsonArray ?? [payload];
        var columns = UnionKeys(rows);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(SheetName(options["sheet"]));

        var r = 1;
        if (hasHeader)
        {
            for (var c = 0; c < columns.Count; c++) sheet.Cell(r, c + 1).Value = columns[c];
            r++;
        }
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var obj = row as JsonObject;
            for (var c = 0; c < columns.Count; c++) SetCell(sheet.Cell(r, c + 1), obj?[columns[c]]);
            r++;
        }

        workbook.SaveAs(stream);
        return Task.CompletedTask;
    }

    private static IXLWorksheet ResolveSheet(XLWorkbook workbook, JsonNode? sheet)
    {
        if (sheet is JsonValue value)
        {
            if (value.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name))
            {
                return workbook.Worksheet(name);
            }
            if (value.TryGetValue<int>(out var index) && index >= 1)
            {
                return workbook.Worksheet(index); // ClosedXML positions are 1-based
            }
        }
        return workbook.Worksheets.First();
    }

    private static string SheetName(JsonNode? sheet) =>
        sheet is JsonValue value && value.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : "Sheet1";

    private static JsonNode? CellValue(IXLCell cell)
    {
        var value = cell.Value;
        return value.Type switch
        {
            XLDataType.Blank => null,
            XLDataType.Boolean => JsonValue.Create(value.GetBoolean()),
            XLDataType.Number => JsonValue.Create(value.GetNumber()),
            XLDataType.DateTime => JsonValue.Create(value.GetDateTime().ToString("o", CultureInfo.InvariantCulture)),
            XLDataType.TimeSpan => JsonValue.Create(value.GetTimeSpan().ToString()),
            XLDataType.Text => JsonValue.Create(value.GetText()),
            _ => JsonValue.Create(cell.GetFormattedString()), // e.g. an error cell
        };
    }

    private static void SetCell(IXLCell cell, JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            cell.Value = node is null ? Blank.Value : node.ToJsonString();
            return;
        }
        // Dispatch on JSON kind, not the CLR backing type: a JsonValue made from an int does not
        // satisfy TryGetValue<double>, so numbers are parsed from their JSON text instead.
        switch (value.GetValueKind())
        {
            case JsonValueKind.Number:
                cell.Value = double.Parse(value.ToJsonString(), CultureInfo.InvariantCulture);
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                cell.Value = value.GetValue<bool>();
                break;
            case JsonValueKind.Null:
                cell.Value = Blank.Value;
                break;
            default:
                cell.Value = value.TryGetValue<string>(out var s) ? s : value.ToJsonString();
                break;
        }
    }

    private static List<string> UnionKeys(JsonArray rows)
    {
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
}
