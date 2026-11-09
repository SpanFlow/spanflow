
namespace SpanFlow.Libraries.Tests;

/// <summary>
/// The canonical form used to spot a saved query somebody has already written (E23-T11).
/// <para>
/// The two halves pull in opposite directions and both matter. It must see through the things a
/// database sees through — layout, case of keywords, comments, a trailing semicolon — or it catches
/// nothing. And it must NOT see through the things a database does not: a changed string literal is a
/// changed query, and calling those two the same would send someone to open a saved query that
/// returns different rows.
/// </para>
/// </summary>
public class SqlFingerprintTests
{
    [Theory]
    [InlineData("SELECT id FROM orders", "select id from orders")]
    [InlineData("SELECT id\n  FROM   orders", "select id from orders")]
    [InlineData("SELECT id FROM orders;", "select id from orders")]
    [InlineData("  SELECT id FROM orders  ", "select id from orders")]
    [InlineData("SELECT id FROM orders -- the recent ones", "select id from orders")]
    [InlineData("SELECT /* cols */ id FROM orders", "select id from orders")]
    public void Sees_through_layout_case_comments_and_a_trailing_semicolon(string sql, string expected)
    {
        Assert.Equal(expected, SqlFingerprint.Canonicalize(sql));
    }

    [Fact]
    public void Two_writings_of_one_query_are_the_same_query()
    {
        Assert.True(SqlFingerprint.AreSame(
            "select  ID, Total\nfrom Orders\nwhere Status = 'open';",
            "SELECT id, total FROM orders WHERE status = 'open'"));
    }

    [Fact]
    public void A_changed_string_literal_is_a_different_query()
    {
        // The whole reason the canonical form is not just ToLower(): Postgres does not return the same
        // rows for these, so neither may we.
        Assert.False(SqlFingerprint.AreSame(
            "select * from orders where region = 'ACME'",
            "select * from orders where region = 'acme'"));
    }

    [Fact]
    public void Whitespace_inside_a_literal_is_kept()
    {
        Assert.False(SqlFingerprint.AreSame(
            "select * from t where city = 'New  York'",
            "select * from t where city = 'New York'"));
    }

    [Fact]
    public void An_escaped_quote_does_not_end_the_literal()
    {
        // If '' were read as a close-then-open, everything after it would be folded as if it were
        // keywords, and two different literals could collapse onto one form.
        Assert.False(SqlFingerprint.AreSame(
            "select * from t where name = 'O''BRIEN'",
            "select * from t where name = 'O''brien'"));
    }

    [Fact]
    public void A_quoted_identifier_keeps_its_case_because_the_database_does()
    {
        Assert.False(SqlFingerprint.AreSame(
            """select * from "Order" """,
            """select * from "order" """));
    }

    [Fact]
    public void A_bare_identifier_folds_because_the_database_folds_it()
    {
        Assert.True(SqlFingerprint.AreSame("select * from Orders", "select * from orders"));
    }

    [Fact]
    public void A_different_query_is_not_a_duplicate()
    {
        Assert.False(SqlFingerprint.AreSame(
            "select id from orders where total > 100",
            "select id from orders where total > 200"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-- nothing but a comment")]
    public void Nothing_is_never_the_same_as_nothing(string? sql)
    {
        // An empty canonical form must not match another empty one, or every non-query would be
        // reported as a duplicate of the last non-query.
        Assert.Equal(string.Empty, SqlFingerprint.Canonicalize(sql));
        Assert.False(SqlFingerprint.AreSame(sql, sql));
    }
}
