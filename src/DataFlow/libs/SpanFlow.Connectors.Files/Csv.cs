using System.Text;

namespace SpanFlow.Connectors.Files;

/// <summary>
/// RFC 4180 delimited-text reader/writer (comma by default; tab for TSV). The rules that matter:
/// a field wrapped in the quote char may contain the delimiter, CR and LF literally; a doubled
/// quote inside a quoted field is one literal quote; whitespace is preserved. Reading is streaming
/// (char by char off a <see cref="TextReader"/>) so large files are not materialised as one string.
/// </summary>
public static class Csv
{
    /// <summary>Yields each record as its raw string fields. Fully blank lines are skipped.</summary>
    public static IEnumerable<string[]> ReadRecords(TextReader reader, char delimiter, char quote)
    {
        var field = new StringBuilder();
        var fields = new List<string>();
        var inQuotes = false;
        var fieldStarted = false; // a char (incl. an opening quote) has landed in the current field
        var recordStarted = false; // the current record has any content at all

        int read;
        while ((read = reader.Read()) >= 0)
        {
            var c = (char)read;

            if (inQuotes)
            {
                if (c == quote)
                {
                    if (reader.Peek() == quote) { field.Append(quote); reader.Read(); }
                    else inQuotes = false;
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            if (c == quote && !fieldStarted)
            {
                inQuotes = true;
                fieldStarted = true;
                recordStarted = true;
            }
            else if (c == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
                recordStarted = true;
            }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && reader.Peek() == '\n') reader.Read();
                if (recordStarted || field.Length > 0 || fields.Count > 0)
                {
                    fields.Add(field.ToString());
                    field.Clear();
                    yield return fields.ToArray();
                    fields.Clear();
                }
                fieldStarted = false;
                recordStarted = false;
            }
            else
            {
                field.Append(c);
                fieldStarted = true;
                recordStarted = true;
            }
        }

        // A final record not terminated by a newline.
        if (recordStarted || field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return fields.ToArray();
        }
    }

    /// <summary>Formats one field, quoting only when it contains the delimiter, quote, CR or LF.</summary>
    public static string FormatField(string? value, char delimiter, char quote)
    {
        value ??= string.Empty;
        var mustQuote = value.IndexOf(delimiter) >= 0
            || value.IndexOf(quote) >= 0
            || value.IndexOf('\r') >= 0
            || value.IndexOf('\n') >= 0;
        if (!mustQuote) return value;

        var escaped = value.Replace(quote.ToString(), $"{quote}{quote}");
        return $"{quote}{escaped}{quote}";
    }

    /// <summary>Writes one record (fields joined by the delimiter) followed by CRLF.</summary>
    public static void WriteRecord(TextWriter writer, IReadOnlyList<string?> fields, char delimiter, char quote)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0) writer.Write(delimiter);
            writer.Write(FormatField(fields[i], delimiter, quote));
        }
        writer.Write("\r\n");
    }
}
