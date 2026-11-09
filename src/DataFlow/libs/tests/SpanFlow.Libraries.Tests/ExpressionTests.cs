using System.Text.Json.Nodes;

namespace SpanFlow.Libraries.Tests;

public class ExpressionTests
{
    private static ExpressionContext Context() => new()
    {
        Input = JsonNode.Parse("""
            {"count": 3, "name": "orders", "empty": "", "zero": 0, "flag": true,
             "items": [{"id": 10, "tag": "a"}, {"id": 20, "tag": "b"}],
             "nested": {"amount": {"total": 99.5}}}
            """),
        Trigger = JsonNode.Parse("""{"date": "2026-07-07"}"""),
        NodeOutputs = new Dictionary<string, JsonNode?>
        {
            ["n1"] = JsonNode.Parse("""{"total": 42, "rows": [1, 2, 3]}"""),
            ["n2"] = JsonNode.Parse("\"plain-string\""),
        },
    };

    // ---- paths ----

    [Theory]
    [InlineData("input.count", "3")]
    [InlineData("input.name", "\"orders\"")]
    [InlineData("input.items[0].id", "10")]
    [InlineData("input.items[1].tag", "\"b\"")]
    [InlineData("input.nested.amount.total", "99.5")]
    [InlineData("trigger.date", "\"2026-07-07\"")]
    [InlineData("nodes.n1.output.total", "42")]
    [InlineData("nodes.n1.output.rows[2]", "3")]
    [InlineData("nodes.n2.output", "\"plain-string\"")]
    [InlineData("input.items.length", "2")]
    [InlineData("input.name.length", "6")]
    [InlineData("nodes.n1.output.rows.length", "3")]
    public void Paths_resolve(string expression, string expectedJson)
    {
        var result = ExpressionEvaluator.Evaluate(expression, Context());
        Assert.Equal(expectedJson, result?.ToJsonString());
    }

    [Theory]
    [InlineData("input.missing")]
    [InlineData("input.items[99]")]
    [InlineData("input.items[0].missing.deeper")]
    [InlineData("nodes.ghost.output.x")]
    [InlineData("input.count.length")] // length of a number is null, not an error
    public void Missing_paths_are_null_not_errors(string expression)
    {
        Assert.Null(ExpressionEvaluator.Evaluate(expression, Context()));
    }

    // ---- operators ----

    [Theory]
    [InlineData("input.count == 3", true)]
    [InlineData("input.count != 3", false)]
    [InlineData("input.count > 2 && input.count < 4", true)]
    [InlineData("input.count > 3 || input.flag", true)]
    [InlineData("!input.flag", false)]
    [InlineData("!!input.flag", true)]
    [InlineData("input.name == 'orders'", true)]
    [InlineData("input.name != \"orders\"", false)]
    [InlineData("input.nested.amount.total >= 99.5", true)]
    [InlineData("input.nested.amount.total > 99.5", false)]
    [InlineData("(input.count > 5 || input.flag) && input.name == 'orders'", true)]
    [InlineData("input.missing == null", true)]
    [InlineData("input.count == null", false)]
    [InlineData("input.missing > 1", false)]
    [InlineData("input.items.length > 1", true)]
    [InlineData("'b' > 'a'", true)]
    [InlineData("-2 < 1", true)]
    [InlineData("exists(input.count)", true)]
    [InlineData("exists(input.missing)", false)]
    [InlineData("input.count == '3'", true)] // numeric strings compare numerically
    public void Operators_evaluate(string expression, bool expected)
    {
        Assert.Equal(expected, ExpressionEvaluator.EvaluateAsBool(expression, Context()));
    }

    // ---- truthiness ----

    [Theory]
    [InlineData("input.flag", true)]
    [InlineData("input.count", true)]
    [InlineData("input.zero", false)]
    [InlineData("input.empty", false)]
    [InlineData("input.name", true)]
    [InlineData("input.missing", false)]
    [InlineData("input.items", true)]
    public void Truthiness(string expression, bool expected)
    {
        Assert.Equal(expected, ExpressionEvaluator.EvaluateAsBool(expression, Context()));
    }

    // ---- syntax errors ----

    [Theory]
    [InlineData("input.count >")]
    [InlineData("(input.count")]
    [InlineData("input.count == 'unterminated")]
    [InlineData("input.items[")]
    [InlineData("== 3")]
    [InlineData("input.count ~ 3")]
    [InlineData("exists input.count")]
    public void Bad_syntax_throws(string expression)
    {
        Assert.Throws<ExpressionException>(() => ExpressionEvaluator.Evaluate(expression, Context()));
    }

    // ---- template rendering ----

    [Fact]
    public void Whole_string_placeholder_keeps_the_type()
    {
        var result = TemplateRenderer.RenderString("{{input.count}}", Context());
        Assert.Equal(3, result!.GetValue<double>());

        var array = TemplateRenderer.RenderString("{{nodes.n1.output.rows}}", Context());
        Assert.IsType<JsonArray>(array);
    }

    [Fact]
    public void Mixed_text_interpolates_as_string()
    {
        var result = TemplateRenderer.RenderString(
            "/orders/{{trigger.date}}?page={{input.count}}&missing={{input.nope}}", Context());
        Assert.Equal("/orders/2026-07-07?page=3&missing=", result!.GetValue<string>());
    }

    [Fact]
    public void Render_walks_objects_and_arrays_and_leaves_non_strings_alone()
    {
        var template = JsonNode.Parse("""
            {"method": "GET", "retries": 2, "path": "/o/{{trigger.date}}",
             "body": {"total": "{{nodes.n1.output.total}}", "tags": ["x", "{{input.items[0].tag}}"]}}
            """);

        var rendered = TemplateRenderer.Render(template, Context());

        Assert.Equal("GET", rendered?["method"]?.GetValue<string>());
        Assert.Equal(2, rendered?["retries"]?.GetValue<int>());
        Assert.Equal("/o/2026-07-07", rendered?["path"]?.GetValue<string>());
        Assert.Equal(42, rendered?["body"]?["total"]?.GetValue<double>());
        Assert.Equal("a", rendered?["body"]?["tags"]?[1]?.GetValue<string>());
    }

    // ---- SQL parameter bag (the injection-safe path) ----

    [Fact]
    public void RenderWithParameters_binds_instead_of_splicing()
    {
        var (text, parameters) = TemplateRenderer.RenderWithParameters(
            "select * from orders where day = {{trigger.date}} and total > {{nodes.n1.output.total}}",
            Context());

        Assert.Equal("select * from orders where day = @p0 and total > @p1", text);
        Assert.Equal(2, parameters.Count);
        Assert.Equal("2026-07-07", parameters[0].Value?.GetValue<string>());
        Assert.Equal(42, parameters[1].Value?.GetValue<double>());
    }

    [Fact]
    public void RenderWithParameters_reuses_a_parameter_for_repeated_expressions()
    {
        var (text, parameters) = TemplateRenderer.RenderWithParameters(
            "select {{input.count}} where a = {{input.count}} and b = {{input.name}}", Context());

        Assert.Equal("select @p0 where a = @p0 and b = @p1", text);
        Assert.Equal(2, parameters.Count);
    }

    [Theory]
    [InlineData("headers['X-GitHub-Event'] == \"push\"", true)]
    [InlineData("headers['X-GitHub-Event'] == \"pull_request\"", false)]
    [InlineData("body.type == \"invoice.paid\"", true)]
    [InlineData("body.amount > 100", true)]
    public void Evaluates_over_headers_and_body_roots(string expression, bool expected)
    {
        // E06-T6 routing: string-key bracket access reads hyphenated header names an object dot-path can't.
        var context = new ExpressionContext
        {
            Headers = JsonNode.Parse("""{"X-GitHub-Event": "push"}"""),
            Body = JsonNode.Parse("""{"type": "invoice.paid", "amount": 150}"""),
        };
        Assert.Equal(expected, ExpressionEvaluator.EvaluateAsBool(expression, context));
    }

    [Fact]
    public void RenderWithParameters_never_puts_values_in_the_text()
    {
        // Even a hostile value stays out of the command text.
        var context = new ExpressionContext { Input = JsonNode.Parse("""{"evil": "1; drop table users --"}""") };
        var (text, parameters) = TemplateRenderer.RenderWithParameters(
            "delete from t where id = {{input.evil}}", context);

        Assert.Equal("delete from t where id = @p0", text);
        Assert.DoesNotContain("drop table", text);
        Assert.Equal("1; drop table users --", Assert.Single(parameters).Value?.GetValue<string>());
    }

    // ---- unresolvable roots are an error, not a silent null ----

    [Theory]
    [InlineData("triger.region")]   // typo
    [InlineData("payload.amount")]  // a root this engine doesn't offer
    [InlineData("Input.amount")]    // roots are case-sensitive
    public void An_unknown_root_is_rejected(string expression)
    {
        var context = new ExpressionContext { Input = JsonNode.Parse("""{"amount": 1}""") };

        var error = Assert.Throws<ExpressionException>(() => ExpressionEvaluator.Evaluate(expression, context));
        Assert.Contains("Unknown root", error.Message);
        Assert.Contains("trigger", error.Message); // the message lists what is available
    }

    [Fact]
    public void A_known_root_that_is_unset_is_still_null()
    {
        // "absent" and "misspelled" must stay distinguishable: only the second is an error.
        var context = new ExpressionContext { Input = JsonNode.Parse("""{"amount": 1}""") };

        Assert.Null(ExpressionEvaluator.Evaluate("trigger.region", context));
        Assert.Null(ExpressionEvaluator.Evaluate("state.watermark", context));
        Assert.Null(ExpressionEvaluator.Evaluate("input.missing", context));
    }
}
