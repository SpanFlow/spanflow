using System.Net;
using System.Text;
using Microsoft.Extensions.Time.Testing;

namespace SpanFlow.Libraries.Playground.Tests;

/// <summary>D71.5 / D71.6: the limits per panel and per caller, the order of the checks, and E69's refusal shape.</summary>
public class LimitTests
{
    private static PlaygroundFactory Factory(params (string Key, string Value)[] settings) =>
        new(settings.ToDictionary(s => s.Key, s => (string?)s.Value));

    [Fact]
    public async Task Past_the_minute_limit_a_caller_gets_429_with_Retry_After_and_RateLimit_headers()
    {
        await using var factory = Factory(("Playground:Limits:Expressions:PerMinute", "3"));
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            var ok = await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal("3", ok.Headers.GetValues("RateLimit-Limit").Single());
            Assert.Equal((2 - i).ToString(), ok.Headers.GetValues("RateLimit-Remaining").Single());
        }

        var refused = await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression);
        Assert.Equal("rate_limited", await refused.Refusal(HttpStatusCode.TooManyRequests));
        Assert.Equal("55", refused.Headers.GetValues("Retry-After").Single()); // the clock is at :05 of the minute
        Assert.Equal("0", refused.Headers.GetValues("RateLimit-Remaining").Single());
        Assert.Equal("55", refused.Headers.GetValues("RateLimit-Reset").Single());

        // The next minute opens a new window.
        factory.Clock.Advance(TimeSpan.FromSeconds(55));
        Assert.Equal(HttpStatusCode.OK, (await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression)).StatusCode);
    }

    [Fact]
    public async Task The_hour_limit_holds_across_minutes()
    {
        await using var factory = Factory(("Playground:Limits:Expressions:PerMinute", "2"), ("Playground:Limits:Expressions:PerHour", "3"));
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression)).StatusCode);
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, (await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression)).StatusCode);

        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var refused = await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression);
        Assert.Equal("rate_limited", await refused.Refusal(HttpStatusCode.TooManyRequests));
        Assert.Equal("3", refused.Headers.GetValues("RateLimit-Limit").Single());
        Assert.True(int.Parse(refused.Headers.GetValues("Retry-After").Single()) > 60, "the hour window resets at the hour, not the minute");
    }

    [Fact]
    public async Task Each_panel_has_its_own_allowance()
    {
        await using var factory = Factory(("Playground:Limits:Expressions:PerMinute", "1"));
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression)).StatusCode);

        var sql = await client.Post("/api/database/inspect", new { sql = "SELECT id FROM customers WHERE id = 1" });
        Assert.Equal(HttpStatusCode.OK, sql.StatusCode);
        Assert.Equal("30", sql.Headers.GetValues("RateLimit-Limit").Single());
    }

    [Fact]
    public async Task Callers_behind_the_trusted_proxy_are_told_apart_by_X_Forwarded_For()
    {
        await using var factory = Factory(("Playground:Limits:Expressions:PerMinute", "1"));
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression, "203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression, "203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression, "203.0.113.2")).StatusCode);

        // A caller cannot pick a fresh address by writing one to the left of what the proxy appended.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression, "198.51.100.9, 203.0.113.1")).StatusCode);
    }

    [Fact]
    public async Task An_untrusted_peer_cannot_choose_its_address_with_X_Forwarded_For()
    {
        await using var factory = new PlaygroundFactory(new() { ["Playground:Limits:Expressions:PerMinute"] = "1" }, remoteAddress: "198.51.100.7");
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression, "203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression, "203.0.113.2")).StatusCode);
    }

    [Fact]
    public async Task A_refused_call_does_not_spend_the_allowance()
    {
        await using var factory = Factory(("Playground:Limits:Expressions:PerMinute", "2"));
        var client = factory.CreateClient();

        // Admitted but invalid: it counts (it got as far as the work's checks). Refused by the window: it does not.
        Assert.Equal(HttpStatusCode.OK, (await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression)).StatusCode);
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.TooManyRequests, (await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression)).StatusCode);

        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, (await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.Post("/api/expressions/evaluate", PlaygroundCalls.Expression)).StatusCode);
    }

    [Fact]
    public async Task Switched_off_every_panel_answers_503_disabled()
    {
        await using var factory = Factory(("Playground:Limits:Enabled", "false"));
        var client = factory.CreateClient();

        foreach (var path in new[] { "/api/expressions/evaluate", "/api/http/preview", "/api/files/convert", "/api/database/inspect", "/api/database/query" })
        {
            Assert.Equal("disabled", await (await client.Post(path, new { })).Refusal(HttpStatusCode.ServiceUnavailable));
        }

        var info = await (await client.GetAsync("/api/info")).Json();
        Assert.False(info["enabled"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Only_JSON_is_taken()
    {
        await using var factory = Factory();
        var response = await factory.CreateClient().PostRaw("/api/expressions/evaluate", "expression=1", "application/x-www-form-urlencoded");
        Assert.Equal("invalid", await response.Refusal(HttpStatusCode.UnsupportedMediaType));
    }

    [Fact]
    public async Task A_body_over_the_panel_cap_is_413_with_or_without_a_Content_Length()
    {
        await using var factory = Factory(("Playground:Limits:Database:MaxBodyBytes", "2048"));
        var client = factory.CreateClient();
        var big = "{\"sql\":\"SELECT '" + new string('x', 4000) + "'\"}";

        Assert.Equal("too_large", await (await client.PostRaw("/api/database/inspect", big)).Refusal(HttpStatusCode.RequestEntityTooLarge));

        var chunked = new HttpRequestMessage(HttpMethod.Post, "/api/database/inspect")
        {
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(big))),
        };
        chunked.Content.Headers.ContentType = new("application/json");
        chunked.Headers.TransferEncodingChunked = true;
        Assert.Equal("too_large", await (await client.SendAsync(chunked)).Refusal(HttpStatusCode.RequestEntityTooLarge));

        // The other panels keep their own, larger caps.
        Assert.Equal(HttpStatusCode.OK, (await client.PostRaw("/api/expressions/evaluate", "{\"mode\":\"template\",\"template\":\"" + new string('x', 4000) + "\"}")).StatusCode);
    }

    [Fact]
    public async Task Broken_or_too_deep_JSON_is_400_invalid()
    {
        await using var factory = Factory();
        var client = factory.CreateClient();
        Assert.Equal("invalid", await (await client.PostRaw("/api/expressions/evaluate", "{\"expression\":")).Refusal(HttpStatusCode.BadRequest));
        Assert.Equal("invalid", await (await client.PostRaw("/api/expressions/evaluate", "[1,2]")).Refusal(HttpStatusCode.BadRequest));

        var deep = "{\"expression\":\"1\",\"data\":" + string.Concat(Enumerable.Repeat("[", 80)) + string.Concat(Enumerable.Repeat("]", 80)) + "}";
        Assert.Equal("invalid", await (await client.PostRaw("/api/expressions/evaluate", deep)).Refusal(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task An_answer_over_the_panel_cap_is_422_too_large()
    {
        await using var factory = Factory(("Playground:Limits:Files:MaxOutputBytes", "2048"));
        var csv = "a,b\n" + string.Concat(Enumerable.Range(0, 200).Select(i => $"{i},value number {i}\n"));
        var response = await factory.CreateClient().Post("/api/files/convert", new { from = "csv", to = "json", content = csv });
        Assert.Equal("too_large", await response.Refusal(HttpStatusCode.UnprocessableEntity));
    }

    [Fact]
    public async Task Rows_are_cut_at_the_panel_cap()
    {
        await using var factory = Factory(("Playground:Limits:Files:MaxRows", "3"), ("Playground:Limits:Database:MaxRows", "4"));
        var client = factory.CreateClient();

        var files = await (await client.Post("/api/files/convert", new { from = "csv", to = "csv", content = "a\n1\n2\n3\n4\n5" })).Json();
        Assert.Equal(3, files["rowCount"]!.GetValue<int>());
        Assert.True(files["truncated"]!.GetValue<bool>());
        Assert.Equal("a\r\n1\r\n2\r\n3\r\n", files["output"]!["text"]!.GetValue<string>());

        var db = await (await client.Post("/api/database/query", new { sql = "SELECT id FROM customers ORDER BY id" })).Json();
        Assert.Equal(4, db["rowCount"]!.GetValue<int>());
        Assert.True(db["truncated"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_query_past_the_time_cap_is_stopped_and_answered_422_timeout()
    {
        await using var factory = Factory(("Playground:Limits:Database:TimeoutMs", "300"));
        var client = factory.CreateClient();
        var started = System.Diagnostics.Stopwatch.StartNew();

        var response = await client.Post("/api/database/query", new { sql = "WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM n) SELECT count(*) FROM n" });

        Assert.Equal("timeout", await response.Refusal(HttpStatusCode.UnprocessableEntity));
        Assert.True(started.ElapsedMilliseconds < 5000, $"took {started.ElapsedMilliseconds} ms");

        // The query was interrupted, not abandoned: its slot comes back.
        var limiter = (CallLimiter)factory.Services.GetService(typeof(CallLimiter))!;
        for (var i = 0; i < 50 && limiter.Running > 0; i++) await Task.Delay(20);
        Assert.Equal(0, limiter.Running);
    }

    [Fact]
    public void Server_wide_concurrency_refuses_with_busy_and_spends_nothing()
    {
        var limiter = new CallLimiter(new FakeTimeProvider());
        var limits = new PanelLimits { PerMinute = 10, PerHour = 100 };

        var first = limiter.TryAdmit(Panel.Http, "a", limits, maxConcurrent: 2);
        var second = limiter.TryAdmit(Panel.Files, "b", limits, maxConcurrent: 2);
        var third = limiter.TryAdmit(Panel.Http, "c", limits, maxConcurrent: 2);

        Assert.True(first.Admitted && second.Admitted);
        Assert.Equal(Reasons.Busy, third.Refusal);
        Assert.Equal(2, limiter.Running);

        first.Lease!.Dispose();
        first.Lease.Dispose(); // twice is once
        Assert.Equal(1, limiter.Running);
        var fourth = limiter.TryAdmit(Panel.Http, "c", limits, maxConcurrent: 2);
        Assert.True(fourth.Admitted);
        Assert.Equal(9, fourth.Remaining); // the busy refusal did not count against "c"
    }

    [Fact]
    public void Callers_from_a_past_hour_are_forgotten()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));
        var limiter = new CallLimiter(clock);
        var limits = new PanelLimits { PerMinute = 10, PerHour = 100 };
        for (var i = 0; i < 20; i++) limiter.TryAdmit(Panel.Expressions, $"caller-{i}", limits, 100).Lease!.Dispose();
        Assert.Equal(20, limiter.Tracked);

        clock.Advance(TimeSpan.FromHours(1));
        limiter.TryAdmit(Panel.Expressions, "new", limits, 100).Lease!.Dispose();
        Assert.Equal(1, limiter.Tracked);
    }

    [Theory]
    [InlineData("Playground:Limits:Http:PerMinute", "0")]
    [InlineData("Playground:Limits:Files:MaxBodyBytes", "999999999")]
    [InlineData("Playground:Limits:Database:TimeoutMs", "5")]
    [InlineData("Playground:Limits:MaxConcurrent", "0")]
    [InlineData("Playground:TrustedProxies:0", "not-an-address")]
    public void Unusable_settings_stop_the_start(string key, string value)
    {
        using var factory = Factory((key, value));
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }
}
