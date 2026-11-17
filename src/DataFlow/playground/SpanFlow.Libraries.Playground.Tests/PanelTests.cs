using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using static SpanFlow.Libraries.Playground.Tests.PlaygroundCalls;

namespace SpanFlow.Libraries.Playground.Tests;

/// <summary>Each panel does what the page says, with the library's own answer. Hosted mode unless a test says otherwise.</summary>
public class PanelTests : IClassFixture<PanelTests.Host>
{
    public sealed class Host : IDisposable
    {
        // Generous limits: these tests are about the panels, not the windows.
        public PlaygroundFactory Factory { get; } = new(new()
        {
            ["Playground:Limits:Expressions:PerMinute"] = "1000", ["Playground:Limits:Expressions:PerHour"] = "1000",
            ["Playground:Limits:Http:PerMinute"] = "1000", ["Playground:Limits:Http:PerHour"] = "1000",
            ["Playground:Limits:Files:PerMinute"] = "1000", ["Playground:Limits:Files:PerHour"] = "1000",
            ["Playground:Limits:Database:PerMinute"] = "1000", ["Playground:Limits:Database:PerHour"] = "1000",
        });

        public void Dispose() => Factory.Dispose();
    }

    private readonly HttpClient _client;

    public PanelTests(Host host) => _client = host.Factory.CreateClient();

    private Task<JsonObject> Ok(string path, object body) => _client.Ok(path, body);

    // ---------------------------------------------------------------- expressions

    [Fact]
    public async Task An_expression_answers_its_value_type_and_truthiness()
    {
        var answer = await Ok("/api/expressions/evaluate", new
        {
            mode = "expression",
            expression = "input.order.items.length",
            data = new { input = new { order = new { items = new[] { 1, 2, 3 } } } },
        });
        Assert.Equal(3, answer["result"]!.GetValue<int>());
        Assert.Equal("number", answer["type"]!.GetValue<string>());
        Assert.True(answer["truthy"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_template_interpolates_and_a_whole_placeholder_keeps_its_type()
    {
        var data = new { input = new { id = 42, name = "Ada" }, @params = new { greeting = "Hello" } };
        var text = await Ok("/api/expressions/evaluate", new { mode = "template", template = "{{ params.greeting }}, {{ input.name }} (#{{ input.id }})", data });
        Assert.Equal("Hello, Ada (#42)", text["result"]!.GetValue<string>());

        var typed = await Ok("/api/expressions/evaluate", new { mode = "template", template = "{{ input.id }}", data });
        Assert.Equal("number", typed["type"]!.GetValue<string>());

        var json = await Ok("/api/expressions/evaluate", new { mode = "json", template = new { id = "{{ input.id }}", label = "#{{ input.id }}" }, data });
        Assert.Equal(42, json["result"]!["id"]!.GetValue<int>());
        Assert.Equal("#42", json["result"]!["label"]!.GetValue<string>());
    }

    [Fact]
    public async Task Batch_runs_one_expression_over_every_row()
    {
        var answer = await Ok("/api/expressions/evaluate", new
        {
            mode = "batch",
            expression = "input.total > params.minimum && input.status != \"cancelled\"",
            data = new { input = new object[] { new { total = 378, status = "shipped" }, new { total = 120.5, status = "open" }, new { total = 500, status = "cancelled" } }, @params = new { minimum = 200 } },
        });
        Assert.Equal(3, answer["rowCount"]!.GetValue<int>());
        Assert.Equal(1, answer["matched"]!.GetValue<int>());
        Assert.Equal([true, false, false], answer["results"]!.AsArray().Select(r => r!["truthy"]!.GetValue<bool>()));
    }

    [Fact]
    public async Task A_bad_expression_is_the_library_s_message()
    {
        var response = await _client.Post("/api/expressions/evaluate", new { expression = "inptu.a" });
        Assert.Equal("failed", await response.Refusal(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Deep_nesting_is_refused_before_the_parser_and_the_service_stays_up()
    {
        // 30 000 open brackets (inside the 64 KB request cap) would overflow the recursive parser's default stack.
        var deep = new string('(', 30_000);
        Assert.Equal("too_large", await (await _client.Post("/api/expressions/evaluate", new { mode = "template", template = "{{" + deep + "}}" })).Refusal(HttpStatusCode.BadRequest));
        Assert.Equal("too_large", await (await _client.Post("/api/expressions/evaluate", new { expression = new string('!', 3000) + "true" })).Refusal(HttpStatusCode.BadRequest));

        var nested = new string('(', 60) + "1 == 1" + new string(')', 60);
        Assert.True((await Ok("/api/expressions/evaluate", new { expression = nested }))["result"]!.GetValue<bool>());
    }

    // ---------------------------------------------------------------- http

    private static object Request(object? auth = null, string method = "POST", string path = "/orders/42/notes", object? body = null, string? challenge = null) => new
    {
        method,
        url = Demo + path,
        @params = new[] { new { key = "tag", value = "a & b" } },
        headers = new[] { new { key = "Accept", value = "application/json" } },
        bodyType = body is null ? null : "json",
        body,
        auth,
        challenge,
    };

    private static string HeaderValue(JsonNode answer, string name) =>
        answer["headers"]!.AsArray().Single(h => h!["name"]!.GetValue<string>().Equals(name, StringComparison.OrdinalIgnoreCase))!["value"]!.GetValue<string>();

    /// <summary>What the demo API said about the request's sign-in.</summary>
    private static JsonNode Verdict(JsonObject sent) => JsonNode.Parse(sent["body"]!["text"]!.GetValue<string>())!["auth"]!;

    [Fact]
    public async Task Preview_shows_the_built_request_and_says_it_was_not_sent()
    {
        var answer = await Ok("/api/http/preview", Request(new { type = "apiKey", @in = "query", name = "api_key", value = "k1" }, body: new { note = "hi" }));

        Assert.False(answer["sent"]!.GetValue<bool>());
        Assert.Equal("https://demo-api.internal/orders/42/notes?tag=a%20%26%20b&api_key=k1", answer["url"]!.GetValue<string>());
        Assert.Equal("application/json", HeaderValue(answer, "Accept"));
        Assert.Equal("{\"note\":\"hi\"}", answer["body"]!["text"]!.GetValue<string>());
        Assert.StartsWith("POST /orders/42/notes?tag=a%20%26%20b&api_key=k1 HTTP/1.1\r\nHost: demo-api.internal\r\n", answer["wire"]!.GetValue<string>());
    }

    [Fact]
    public async Task Send_reaches_the_demo_API_and_shows_the_response()
    {
        var answer = await Ok("/api/http/send", Request(new { type = "bearer", token = "demo-token-abc" }, body: new { text = "Ship on Monday" }));

        Assert.True(answer["sent"]!.GetValue<bool>());
        Assert.Equal(201, answer["status"]!.GetValue<int>());
        Assert.Equal("Bearer demo-token-abc", HeaderValue(answer["request"]!, "Authorization"));
        var body = JsonNode.Parse(answer["body"]!["text"]!.GetValue<string>())!;
        Assert.Equal(42, body["orderId"]!.GetValue<int>());
        Assert.Equal("Ship on Monday", body["note"]!["text"]!.GetValue<string>());
        Assert.Contains(answer["headers"]!.AsArray(), h => h!["name"]!.GetValue<string>() == "X-Demo-Api");
    }

    public static TheoryData<string, object> SignedSchemes => new()
    {
        { "apiKey", new { type = "apiKey", @in = "header", name = "X-API-Key", value = "demo-key-123" } },
        { "basic", new { type = "basic", username = "ada", password = "analytical-engine" } },
        { "digest", new { type = "digest", username = "Mufasa", password = "Circle of Life", algorithm = "SHA-256" } },
        { "hawk", new { type = "hawk", authId = "dh37fgj492je", authKey = "werxhqb98rpaxn39848xrunpaw3489ruxnpa98w4rxn" } },
        { "oauth1", new { type = "oauth1", consumerKey = "xvz1evFS4wEEPTGEFPHBog", consumerSecret = "kAcSOqF21Fu85e7zjz7ZN2U4ZRhfV3WpwPAoE3Z7kBw", token = "370773112-GmHxMAgYyLbNEtIKZeRNFsMKPR9EyMZeS9weJAEb", tokenSecret = "LswwdoUaIvS8ltyTt5jkRh4J50vUPVVHtR2YPi5kE" } },
        { "jwtBearer", new { type = "jwtBearer", secret = "a-playground-secret-of-at-least-32-bytes", payload = new { sub = "ada" } } },
        { "oauth2ClientCredentials", new { type = "oauth2ClientCredentials", tokenUrl = Demo + "/oauth/token", clientId = "demo-client", clientSecret = "demo-client-secret" } },
        { "tokenEndpoint", new { type = "tokenEndpoint", tokenUrl = Demo + "/auth/login", bodyTemplate = new { username = "ada", password = "analytical-engine" }, tokenPath = "token" } },
        { "loginRequest", new { type = "loginRequest", request = new { method = "POST", url = "/auth/login", bodyType = "json", body = new { username = "ada", password = "analytical-engine" } }, token = new { from = "body", path = "token" } } },
    };

    [Theory]
    [MemberData(nameof(SignedSchemes))]
    public async Task Every_scheme_signs_and_the_demo_API_finds_the_signature_valid(string scheme, object auth)
    {
        // /orders/42 needs a sign-in, so a Digest client gets the 401 challenge it answers.
        var answer = await Ok("/api/http/send", Request(auth, method: "GET", path: "/orders/42"));

        Assert.Equal(200, answer["status"]!.GetValue<int>());
        var verdict = Verdict(answer);
        Assert.True(verdict["valid"]!.GetValue<bool>(), $"{scheme}: {verdict["detail"]}");
    }

    [Fact]
    public async Task A_wrong_secret_is_found_invalid()
    {
        var answer = await Ok("/api/http/send", Request(new { type = "oauth1", consumerKey = "xvz1evFS4wEEPTGEFPHBog", consumerSecret = "not-the-secret" }, path: "/echo/x", body: new { a = 1 }));
        Assert.False(Verdict(answer)["valid"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Digest_is_answered_after_the_server_s_challenge()
    {
        var answer = await Ok("/api/http/send", Request(new { type = "digest", username = "Mufasa", password = "Circle of Life" }, method: "GET", path: "/orders/42"));

        Assert.Equal(200, answer["status"]!.GetValue<int>());
        var timeline = answer["timeline"]!.AsArray();
        Assert.Equal(2, timeline.Count);
        Assert.Equal(401, timeline[0]!["status"]!.GetValue<int>());
        Assert.StartsWith("Digest ", HeaderValue(answer["request"]!, "Authorization"));
    }

    [Fact]
    public async Task Preview_of_Digest_answers_a_given_or_sample_challenge()
    {
        const string challenge = "Digest realm=\"http-auth@example.org\", qop=\"auth\", algorithm=SHA-256, nonce=\"7ypf/xlj9XXwfDPEoM4URrv/xwf94BcCAzFZH4GiTo0v\", opaque=\"FQhe\"";
        var given = await Ok("/api/http/preview", Request(new { type = "digest", username = "Mufasa", password = "Circle of Life" }, challenge: challenge));
        Assert.Contains("nonce=\"7ypf/xlj9XXwfDPEoM4URrv/xwf94BcCAzFZH4GiTo0v\"", HeaderValue(given, "Authorization"));
        Assert.Contains("the challenge you gave", given["note"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/x")]
    [InlineData("/relative")]
    public async Task Only_http_and_https_URLs(string url)
    {
        var response = await _client.Post("/api/http/send", new { url });
        Assert.Equal("invalid", await response.Refusal(HttpStatusCode.BadRequest));
    }

    // ---------------------------------------------------------------- files

    [Fact]
    public async Task CSV_reads_into_typed_rows_and_writes_back_as_NDJSON()
    {
        var answer = await Ok("/api/files/convert", new
        {
            from = "csv",
            to = "ndjson",
            content = "id,name\n1,\"Turing, Alan\"\n2,Ada",
            options = new { csv = new { columns = new[] { new { name = "id", type = "number" } } } },
        });
        Assert.Equal(2, answer["rowCount"]!.GetValue<int>());
        Assert.Equal("Turing, Alan", answer["rows"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("{\"id\":1,\"name\":\"Turing, Alan\"}\n{\"id\":2,\"name\":\"Ada\"}\n", answer["output"]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task JSON_rows_become_an_XLSX_download()
    {
        var answer = await Ok("/api/files/convert", new { from = "json", to = "xlsx", content = "[{\"a\":1,\"b\":\"x\"},{\"a\":2,\"b\":\"y\"}]" });
        var bytes = Convert.FromBase64String(answer["output"]!["base64"]!.GetValue<string>());
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
    }

    [Fact]
    public async Task A_row_of_thousands_of_columns_is_refused()
    {
        var header = string.Join(',', Enumerable.Range(0, 5000).Select(i => $"c{i}"));
        var response = await _client.Post("/api/files/convert", new { from = "csv", to = "xlsx", content = header + "\n" + new string(',', 4999) });
        Assert.Equal("too_large", await response.Refusal(HttpStatusCode.BadRequest));
    }

    // ---------------------------------------------------------------- database

    [Fact]
    public async Task Inspect_gives_advice_in_the_provider_s_spelling_and_the_fingerprint()
    {
        var answer = await Ok("/api/database/inspect", new { sql = "select *\n  FROM Orders -- all of them\n;", provider = "sqlserver" });
        var codes = answer["advice"]!.AsArray().Select(a => a!["code"]!.GetValue<string>()).ToList();
        Assert.Contains("select-star", codes);
        Assert.Contains("unbounded-select", codes);
        Assert.Contains(answer["advice"]!.AsArray(), a => a!["suggestion"]?.GetValue<string>()?.Contains("TOP") == true);
        Assert.Equal("select * from orders", answer["fingerprint"]!.GetValue<string>());
    }

    [Fact]
    public async Task Placeholders_are_bound_as_parameters_never_spliced()
    {
        var answer = await Ok("/api/database/query", new
        {
            sql = "SELECT name FROM customers WHERE country = {{ params.country }} OR name = {{ params.country }} ORDER BY id",
            @params = new { country = "GB' OR '1'='1" },
        });
        Assert.Equal("SELECT name FROM customers WHERE country = @p0 OR name = @p0 ORDER BY id", answer["boundSql"]!.GetValue<string>());
        Assert.Equal(0, answer["rowCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_read_runs_on_the_sample()
    {
        var answer = await Ok("/api/database/query", new
        {
            connectionId = "sample",
            sql = "SELECT c.name, count(o.id) AS orders FROM customers c JOIN orders o ON o.customer_id = c.id WHERE c.country = {{ params.c }} GROUP BY c.name ORDER BY orders DESC, c.name LIMIT 2",
            @params = new { c = "US" },
        });
        Assert.Equal(["name", "orders"], answer["columns"]!.AsArray().Select(c => c!.GetValue<string>()));
        Assert.Equal(2, answer["rows"]![0]!["orders"]!.GetValue<long>());
    }

    [Fact]
    public async Task The_hosted_connection_list_is_the_sample_and_the_configured_demo()
    {
        var list = await _client.GetFromJsonAsync<JsonArray>("/api/database/connections");
        Assert.Equal(["sample"], list!.Select(c => c!["id"]!.GetValue<string>()));
    }
}
