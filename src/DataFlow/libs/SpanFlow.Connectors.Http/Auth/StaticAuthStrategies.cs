using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace SpanFlow.Connectors.Http.Auth;

/// <summary>No authentication (the default when config.auth is absent).</summary>
public sealed class NoneAuthStrategy : IAuthStrategy
{
    public string Type => "none";
    public IReadOnlyList<string> Validate(JsonObject auth) => [];
    public Task ApplyAsync(HttpRequestMessage message, JsonObject auth, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>API key in a header or query parameter: {"in":"header"|"query","name":"...","value":"..."}.</summary>
public sealed class ApiKeyAuthStrategy : IAuthStrategy
{
    public string Type => "apiKey";

    public IReadOnlyList<string> Validate(JsonObject auth)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(auth["name"]?.GetValue<string>())) errors.Add("apiKey auth requires a 'name'.");
        if (string.IsNullOrWhiteSpace(auth["value"]?.GetValue<string>())) errors.Add("apiKey auth requires a 'value'.");
        var location = auth["in"]?.GetValue<string>();
        if (location is not null && location is not ("header" or "query" or "cookie"))
        {
            errors.Add("apiKey auth 'in' must be 'header', 'query' or 'cookie'.");
        }
        return errors;
    }

    public Task ApplyAsync(HttpRequestMessage message, JsonObject auth, CancellationToken ct)
    {
        var name = auth["name"]!.GetValue<string>();
        var value = auth["value"]!.GetValue<string>();

        if (auth["in"]?.GetValue<string>() == "query")
        {
            var uri = message.RequestUri!;
            var separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
            message.RequestUri = new Uri($"{uri}{separator}{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
        }
        else if (auth["in"]?.GetValue<string>() == "cookie")
        {
            // E68-T3: appended to any Cookie header already on the request.
            var cookie = $"{name}={value}";
            if (message.Headers.TryGetValues("Cookie", out var existing))
            {
                cookie = string.Join("; ", existing) + "; " + cookie;
                message.Headers.Remove("Cookie");
            }
            message.Headers.TryAddWithoutValidation("Cookie", cookie);
        }
        else
        {
            message.Headers.TryAddWithoutValidation(name, value);
        }
        return Task.CompletedTask;
    }
}

/// <summary>HTTP Basic: {"username":"...","password":"..."} → Authorization: Basic base64(user:pass).</summary>
public sealed class BasicAuthStrategy : IAuthStrategy
{
    public string Type => "basic";

    public IReadOnlyList<string> Validate(JsonObject auth)
    {
        var errors = new List<string>();
        if (auth["username"]?.GetValue<string>() is null) errors.Add("basic auth requires a 'username'.");
        if (auth["password"]?.GetValue<string>() is null) errors.Add("basic auth requires a 'password'.");
        return errors;
    }

    public Task ApplyAsync(HttpRequestMessage message, JsonObject auth, CancellationToken ct)
    {
        var username = auth["username"]!.GetValue<string>();
        var password = auth["password"]!.GetValue<string>();
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        return Task.CompletedTask;
    }
}

/// <summary>Static bearer token: {"token":"..."} → Authorization: Bearer &lt;token&gt;.</summary>
public sealed class BearerAuthStrategy : IAuthStrategy
{
    public string Type => "bearer";

    public IReadOnlyList<string> Validate(JsonObject auth) =>
        string.IsNullOrWhiteSpace(auth["token"]?.GetValue<string>()) ? ["bearer auth requires a 'token'."] : [];

    public Task ApplyAsync(HttpRequestMessage message, JsonObject auth, CancellationToken ct)
    {
        // E68-T3: "prefix" changes the scheme word ("Token", "JWT"); an empty prefix sends the token alone.
        var prefix = auth["prefix"] is JsonValue p && p.TryGetValue<string>(out var text) ? text : "Bearer";
        var token = auth["token"]!.GetValue<string>();
        message.Headers.Remove("Authorization"); // replaces a static Authorization header, as it always has
        message.Headers.TryAddWithoutValidation("Authorization", prefix.Length == 0 ? token : $"{prefix} {token}");
        return Task.CompletedTask;
    }
}
