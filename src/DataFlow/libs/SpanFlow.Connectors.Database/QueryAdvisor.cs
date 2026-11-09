using System.Text;
using System.Text.RegularExpressions;

namespace SpanFlow.Connectors.Database;

/// <summary>How seriously to take one piece of advice.</summary>
public static class QueryAdviceSeverities
{
    /// <summary>Worth knowing. Never blocks anything.</summary>
    public const string Hint = "hint";

    /// <summary>This will probably hurt at some size. Blocks only when the policy is enforced.</summary>
    public const string Warn = "warn";

    /// <summary>Refused: the save or the execute does not happen.</summary>
    public const string Block = "block";
}

/// <summary>Stable identifiers, so a UI can style or suppress one kind without matching on prose.</summary>
public static class QueryAdviceCodes
{
    public const string SelectStar = "select-star";
    public const string UnboundedSelect = "unbounded-select";
}

/// <summary>One observation about a query.</summary>
/// <param name="Code">Stable id for the rule (e.g. "select-star").</param>
/// <param name="Severity">hint, warn or block (<see cref="QueryAdviceSeverities"/>).</param>
/// <param name="Message">What was noticed, in one sentence.</param>
/// <param name="Suggestion">The concrete edit, in this provider's spelling, when there is one.</param>
/// <param name="NodeId">The graph node the SQL came from, when it came from a graph (E22-T4). The advisor
/// itself never sets this — the pipeline save stamps it, so the designer can put the advice on the node
/// rather than making the reader find it from the message.</param>
public sealed record QueryAdvice(string Code, string Severity, string Message, string? Suggestion = null, string? NodeId = null);


/// <summary>What the advisor is allowed to say, and how hard.</summary>
/// <param name="Provider">The connection's db provider, so a suggestion is spelled the way this
/// database spells it — <c>LIMIT</c>, <c>TOP</c> and <c>FETCH FIRST</c> are not interchangeable.</param>
/// <param name="Enforce">The <c>db.query-policy</c> feature: an unbounded select becomes a block
/// rather than a warning.</param>
/// <param name="MaxRows">The deployment's row cap, quoted in the message when set.</param>
/// <param name="UsedColumns">The columns something downstream actually reads, when that is knowable.
/// Turns "select only what you use" from a platitude into a list.</param>
public sealed record QueryAdvisorOptions(
    string? Provider = null,
    bool Enforce = false,
    int MaxRows = 0,
    IReadOnlyCollection<string>? UsedColumns = null);

/// <summary>
/// Static inspection of SQL text (E22-T3): the two mistakes that turn a working pipeline into an
/// outage the first time the table gets big — <c>SELECT *</c>, and a select with neither a filter nor
/// a row limit.
/// <para>
/// <b>It is a heuristic, and that is a decision, not a shortcut.</b> A real grammar for four dialects
/// would reject valid SQL the day someone writes something it has not seen, and this runs on the save
/// path: being wrong here means refusing a query that works. So it reads tokens, not structure; it
/// only ever looks at statements that unambiguously start a read; and every rule is written to stay
/// quiet when it cannot be sure. A missed warning costs a warning. A false block costs trust.
/// </para>
/// <para>
/// String literals, comments and quoted identifiers are removed before anything is matched, so a
/// <c>WHERE</c> inside a string and a column actually named <c>limit</c> cannot be mistaken for the
/// keywords they look like.
/// </para>
/// </summary>
public static partial class QueryAdvisor
{
    /// <summary>Advice for one statement, in severity order. Empty for anything that is not a read.</summary>
    public static IReadOnlyList<QueryAdvice> Inspect(string? sql, QueryAdvisorOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(sql)) return [];
        options ??= new QueryAdvisorOptions();

        var stripped = Strip(sql);
        if (!IsRead(stripped)) return [];

        var advice = new List<QueryAdvice>();
        if (SelectsEverything(stripped)) advice.Add(SelectStarAdvice(options));
        if (IsUnbounded(stripped)) advice.Add(UnboundedAdvice(options));
        return advice;
    }

    /// <summary>True when any advice at this severity or above is present — the save/execute gate.</summary>
    public static bool IsBlocked(IReadOnlyList<QueryAdvice> advice) =>
        advice.Any(a => a.Severity == QueryAdviceSeverities.Block);

    // ---- rules -----------------------------------------------------------------------------------

    private static QueryAdvice SelectStarAdvice(QueryAdvisorOptions options)
    {
        // Naming the columns is only useful when we actually know them. "Select only what you use" with
        // no list is the kind of advice that gets ignored, so the two cases read differently on purpose.
        var used = options.UsedColumns?.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return used is { Count: > 0 }
            ? new QueryAdvice(
                QueryAdviceCodes.SelectStar, QueryAdviceSeverities.Hint,
                $"SELECT * fetches every column, but only {string.Join(", ", used)} "
                + $"{(used.Count == 1 ? "is" : "are")} read downstream.",
                $"SELECT {string.Join(", ", used)}")
            : new QueryAdvice(
                QueryAdviceCodes.SelectStar, QueryAdviceSeverities.Hint,
                "SELECT * fetches every column, including ones nothing reads — and it changes shape "
                + "when somebody adds a column to the table.",
                "List the columns you use instead of *.");
    }

    private static QueryAdvice UnboundedAdvice(QueryAdvisorOptions options)
    {
        var severity = options.Enforce ? QueryAdviceSeverities.Block : QueryAdviceSeverities.Warn;
        var cap = options.MaxRows > 0 ? options.MaxRows : 1000;
        var message = options.Enforce
            ? "This selects a whole table — no WHERE and no row limit — and the query policy for this "
              + "project refuses that."
            : "This selects a whole table — no WHERE and no row limit. It will work until the table is "
              + "big, and then it will take the run down with it.";

        return new QueryAdvice(
            QueryAdviceCodes.UnboundedSelect, severity, message, LimitSyntax(options.Provider, cap));
    }

    /// <summary>The row-limiting clause in this provider's spelling. Wrong syntax in a suggestion is
    /// worse than no suggestion — it is an error the reader has to debug on our behalf.</summary>
    private static string LimitSyntax(string? provider, int rows) => provider?.ToLowerInvariant() switch
    {
        "sqlserver" => $"Add a WHERE, or SELECT TOP ({rows}) …",
        // Postgres, MySQL and SQLite all take LIMIT; postgres also accepts FETCH FIRST, but LIMIT is
        // what people write.
        "postgres" or "mysql" or "sqlite" => $"Add a WHERE, or … LIMIT {rows}",
        _ => $"Add a WHERE, or a row limit (LIMIT {rows}; SQL Server: SELECT TOP ({rows}))",
    };

    // ---- shape detection -------------------------------------------------------------------------

    /// <summary>
    /// Whether this is a plain read we can reason about: SELECT, or a WITH … SELECT. Anything else —
    /// an INSERT, a call, a statement we cannot classify — gets no advice at all, because advice about
    /// a statement we misread is worse than silence.
    /// </summary>
    private static bool IsRead(string sql)
    {
        var first = FirstWord(sql);
        if (first.Equals("SELECT", StringComparison.OrdinalIgnoreCase)) return true;
        // A CTE is a read only if the body is: "WITH x AS (…) DELETE FROM …" is legal in postgres.
        return first.Equals("WITH", StringComparison.OrdinalIgnoreCase)
               && Keyword("SELECT").IsMatch(sql)
               && !Keyword("INSERT").IsMatch(sql)
               && !Keyword("UPDATE").IsMatch(sql)
               && !Keyword("DELETE").IsMatch(sql)
               && !Keyword("MERGE").IsMatch(sql);
    }

    private static bool SelectsEverything(string sql) => SelectStarPattern().IsMatch(sql);

    /// <summary>
    /// A select with nothing to bound it. Every clause below is treated as bounding, some of them
    /// generously — the point is to be sure before speaking, not to catch every case:
    /// <list type="bullet">
    /// <item>WHERE, obviously — even one that matches everything.</item>
    /// <item>LIMIT / TOP / FETCH / OFFSET / ROWNUM — an explicit row cap.</item>
    /// <item>GROUP BY, DISTINCT, or a top-level aggregate — the result is a summary, not the table.</item>
    /// <item>A parameter or template placeholder anywhere: the text we can see is not the text that runs.</item>
    /// </list>
    /// </summary>
    private static bool IsUnbounded(string sql)
    {
        if (!Keyword("FROM").IsMatch(sql)) return false; // SELECT 1, SELECT now() — nothing to scan

        foreach (var bounding in (string[])["WHERE", "LIMIT", "TOP", "FETCH", "OFFSET", "ROWNUM", "GROUP", "DISTINCT"])
        {
            if (Keyword(bounding).IsMatch(sql)) return false;
        }

        // A templated or parameterized query is usually a filter we cannot see yet ({{trigger.since}},
        // @since). Treating it as unbounded would warn about exactly the queries written carefully.
        if (PlaceholderPattern().IsMatch(sql)) return false;

        // COUNT(*) / SUM(x) with no GROUP BY returns one row, whatever the table holds.
        return !AggregatePattern().IsMatch(sql);
    }

    private static string FirstWord(string sql)
    {
        var match = FirstWordPattern().Match(sql);
        return match.Success ? match.Value : "";
    }

    // ---- lexical stripping -----------------------------------------------------------------------

    /// <summary>
    /// Removes comments, string literals and quoted identifiers, replacing each with a space so tokens
    /// on either side stay separate. What is left is only the parts of the statement that are SQL
    /// keywords and bare identifiers — the only parts any rule here is entitled to read.
    /// </summary>
    private static string Strip(string sql)
    {
        var output = new StringBuilder(sql.Length);
        var i = 0;

        while (i < sql.Length)
        {
            var c = sql[i];

            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                output.Append(' ');
                continue;
            }
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/')) i++;
                i = Math.Min(sql.Length, i + 2);
                output.Append(' ');
                continue;
            }
            if (c == '\'')
            {
                i++;
                // '' inside a literal is one escaped quote, not the end of the string.
                while (i < sql.Length && !(sql[i] == '\'' && (i + 1 >= sql.Length || sql[i + 1] != '\''))) i += sql[i] == '\'' ? 2 : 1;
                i++;
                output.Append(' ');
                continue;
            }
            if (c is '"' or '`' or '[')
            {
                var close = c switch { '"' => '"', '`' => '`', _ => ']' };
                i++;
                while (i < sql.Length && sql[i] != close) i++;
                i++;
                // A quoted identifier is still an identifier — but its *contents* must not be read as
                // keywords, so it becomes a placeholder word rather than nothing.
                output.Append(" id ");
                continue;
            }

            output.Append(c);
            i++;
        }

        return output.ToString();
    }

    // ---- patterns --------------------------------------------------------------------------------

    private static readonly Dictionary<string, Regex> Keywords = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A whole-word, case-insensitive match for one keyword, compiled once.</summary>
    private static Regex Keyword(string word)
    {
        lock (Keywords)
        {
            if (!Keywords.TryGetValue(word, out var regex))
            {
                Keywords[word] = regex = new Regex($@"\b{word}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }
            return regex;
        }
    }

    [GeneratedRegex(@"^\s*(\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex FirstWordPattern();

    /// <summary>SELECT *, SELECT t.*, SELECT DISTINCT * — a star in the select list, not a multiplication.</summary>
    [GeneratedRegex(@"\bselect\b(\s+(distinct|all))?\s+(\w+\s*\.\s*)?\*", RegexOptions.IgnoreCase)]
    private static partial Regex SelectStarPattern();

    /// <summary>{{ … }} templates and @/: bound parameters.</summary>
    [GeneratedRegex(@"\{\{|[@:]\w+", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"\b(count|sum|avg|min|max)\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex AggregatePattern();
}
