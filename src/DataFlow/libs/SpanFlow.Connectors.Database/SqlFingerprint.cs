using System.Text;

namespace SpanFlow.Connectors.Database;

/// <summary>
/// A canonical form of a SQL statement, used to recognise that two saved queries are the same query
/// written differently (E23-T11).
/// <para>
/// What it ignores is what a database ignores: comments, how the statement is laid out, whether the
/// keywords are shouted, and a trailing semicolon. What it keeps is everything that changes the
/// result — and that emphatically includes <b>string literals, exactly as written</b>. Postgres will
/// not give you the same rows for <c>'ACME'</c> and <c>'acme'</c>, so neither will this: case-folding
/// the whole statement would have declared two different queries identical, which is a far worse
/// mistake than failing to spot a duplicate.
/// </para>
/// <para>
/// Quoted identifiers are kept verbatim for the same reason — <c>"Order"</c> and <c>"order"</c> are
/// two tables in Postgres — while bare identifiers, which the database itself case-folds, are folded
/// here too.
/// </para>
/// <para>
/// This is a heuristic like the advisor beside it, and it is used the same careful way: a match means
/// "you have probably written this already", offered as a refusal the author can answer by opening
/// the existing query. It never rewrites, and it never runs anything.
/// </para>
/// </summary>
public static class SqlFingerprint
{
    /// <summary>
    /// The canonical form, or an empty string for SQL that is empty or only comments. Two statements
    /// are "the same query" exactly when their canonical forms are equal (ordinal comparison — the
    /// folding has already happened here, so a case-insensitive compare on top would undo the care
    /// taken over literals).
    /// </summary>
    public static string Canonicalize(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return string.Empty;

        var output = new StringBuilder(sql.Length);
        var i = 0;

        while (i < sql.Length)
        {
            var c = sql[i];

            // -- line comment
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                AppendSpace(output);
                continue;
            }

            // /* block comment */ — unterminated is treated as running to the end, which is what a
            // database would tell you too, just less politely.
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/')) i++;
                i = Math.Min(sql.Length, i + 2);
                AppendSpace(output);
                continue;
            }

            // A string literal is data. It is copied through untouched, including its case and its
            // internal whitespace: 'New  York' is not 'New York'.
            if (c == '\'')
            {
                output.Append(c);
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'' && i + 1 < sql.Length && sql[i + 1] == '\'')
                    {
                        output.Append("''"); // an escaped quote, not the end
                        i += 2;
                        continue;
                    }
                    output.Append(sql[i]);
                    if (sql[i] == '\'') { i++; break; }
                    i++;
                }
                continue;
            }

            // A quoted identifier names a case-sensitive object; keep it, quotes and all.
            if (c is '"' or '`' or '[')
            {
                var close = c switch { '"' => '"', '`' => '`', _ => ']' };
                output.Append(c);
                i++;
                while (i < sql.Length && sql[i] != close) output.Append(sql[i++]);
                if (i < sql.Length) output.Append(sql[i++]);
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                AppendSpace(output);
                i++;
                continue;
            }

            output.Append(char.ToLowerInvariant(c));
            i++;
        }

        // Trailing semicolons and spaces say nothing about what the statement does.
        return output.ToString().TrimEnd(' ', ';').TrimStart();
    }

    /// <summary>Whether two statements are the same query, ignoring layout, case and comments.</summary>
    public static bool AreSame(string? left, string? right)
    {
        var canonical = Canonicalize(left);
        return canonical.Length > 0 && canonical == Canonicalize(right);
    }

    /// <summary>Whitespace runs collapse to one space; a leading run is dropped by the final trim.</summary>
    private static void AppendSpace(StringBuilder output)
    {
        if (output.Length > 0 && output[^1] != ' ') output.Append(' ');
    }
}
