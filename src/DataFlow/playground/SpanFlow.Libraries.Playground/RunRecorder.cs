using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SpanFlow.Libraries.Playground;

/// <summary>One call as Platform's Site analytics keeps it (the same fields as a hosted mapping call, E69-T8).</summary>
public sealed class PlaygroundCall
{
    public long Seq { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>The caller's address (trusted proxies only). Platform hashes it with the day and drops it.</summary>
    public string ClientAddress { get; set; } = "";

    public string? UserAgent { get; set; }
    public string? Country { get; set; }

    /// <summary>The panel and action: <c>/expressions/evaluate</c>, <c>/http/send</c>, <c>/database/query</c>, ...</summary>
    public string Path { get; set; } = "";

    /// <summary>ok, failed, invalid, too_large, timeout, rate_limited, busy, disabled.</summary>
    public string Outcome { get; set; } = Reasons.Ok;

    /// <summary><c>{ "request": ..., "response": ... }</c>, secrets masked, cut at <see cref="RunRecorder.MaxRunChars"/>.</summary>
    public string? Run { get; set; }

    public int? RunBytes { get; set; }
    public string? Error { get; set; }
    public int? DurationMs { get; set; }
}

/// <summary>
/// Hosted only: every call, kept in memory until Platform takes it on the sync port (D71.12). Platform acknowledges
/// what it saved, so a Platform outage loses nothing until the buffer is full; past that the oldest go first.
/// Self-hosted, nothing is recorded at all.
/// </summary>
public sealed class RunRecorder
{
    public const int MaxCalls = 10_000;
    public const int MaxRunChars = 64 * 1024;
    public const int MaxResponseChars = 16 * 1024;
    public const int MaxBatch = 500;

    private readonly LinkedList<PlaygroundCall> _calls = new();
    private readonly Lock _lock = new();
    private long _seq;

    public bool Enabled { get; init; }

    /// <summary>
    /// This process's id. A restart begins the sequence at 1 again; Platform sees the id change and starts its
    /// acknowledgement over, instead of skipping the new calls as already saved.
    /// </summary>
    public string Instance { get; } = Guid.NewGuid().ToString("N");

    public int Count
    {
        get { lock (_lock) return _calls.Count; }
    }

    public void Record(PlaygroundCall call)
    {
        if (!Enabled) return;
        lock (_lock)
        {
            call.Seq = ++_seq;
            _calls.AddLast(call);
            while (_calls.Count > MaxCalls) _calls.RemoveFirst();
        }
    }

    /// <summary>Drops what Platform saved (up to <paramref name="ackThrough"/>) and hands over the next batch.</summary>
    public IReadOnlyList<PlaygroundCall> Take(long ackThrough)
    {
        lock (_lock)
        {
            while (_calls.First is { } first && first.Value.Seq <= ackThrough) _calls.RemoveFirst();
            return _calls.Take(MaxBatch).ToList();
        }
    }

    /// <summary>What a call ran, as Platform keeps it: the request with secrets masked, and the answer (cut).</summary>
    public static (string Text, int Bytes) Describe(JsonNode? request, JsonNode? response, string? error)
    {
        var run = new JsonObject
        {
            ["request"] = Mask(request?.DeepClone()),
            // The answer of a send repeats the signed request (headers and wire text): masked the same way.
            ["response"] = response is null ? null : Cut(Mask(response.DeepClone())!.ToJsonString(), MaxResponseChars),
        };
        if (error is not null) run["error"] = error;
        var text = run.ToJsonString();
        var bytes = System.Text.Encoding.UTF8.GetByteCount(text);
        return (text.Length > MaxRunChars ? text[..MaxRunChars] : text, bytes);
    }

    private static JsonNode? Cut(string json, int max) => json.Length <= max ? JsonNode.Parse(json) : JsonValue.Create(json[..max] + " [cut]");

    // ---- masking ---------------------------------------------------------------------------------------------------

    private static readonly Regex SecretName = new(
        "^(password|passphrase|secret|clientsecret|client_secret|consumersecret|tokensecret|token|accesstoken|access_token|refreshtoken|" +
        "refresh_token|id_token|authkey|privatekey|apikey|api_key|connectionstring|secretaccesskey|sasToken|accountkey|value)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SecretHeader = new("^(authorization|proxy-authorization|cookie|set-cookie|x-api-key|api-key|x-auth-token)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SecretInConnectionString = new(@"(?i)\b(password|pwd)\s*=\s*[^;]*", RegexOptions.Compiled);

    /// <summary>A credential header's line in a request as it goes on the wire (<c>Authorization: Basic ...</c>).</summary>
    private static readonly Regex SecretHeaderLine = new(@"(?im)^(authorization|proxy-authorization|cookie|set-cookie|x-api-key|api-key|x-auth-token)[ \t]*:[^\r\n]*",
        RegexOptions.Compiled);

    /// <summary>A secret in a URL's query (<c>?api_key=...</c>, a JWT or OAuth 1.0a signature added to the query).</summary>
    private static readonly Regex SecretQueryParameter = new(
        @"(?i)([?&](?:api_key|apikey|key|token|access_token|id_token|refresh_token|client_secret|password|sig|signature|oauth_signature|oauth_token)=)[^&#\s""]*",
        RegexOptions.Compiled);

    /// <summary>A secret field inside JSON text (an answer's body is kept as text): <c>"access_token":"..."</c>.</summary>
    private static readonly Regex SecretJsonField = new(
        @"(?i)(""(?:password|secret|client_secret|access_token|refresh_token|id_token|token|api_key|apikey|authorization|cookie|x-api-key)""\s*:\s*)""(?:[^""\\]|\\.)*""",
        RegexOptions.Compiled);

    /// <summary>Every secret a string may carry, masked: header lines, query parameters, JSON fields, connection-string passwords.</summary>
    internal static string MaskText(string text)
    {
        text = SecretHeaderLine.Replace(text, "$1: ******");
        text = SecretQueryParameter.Replace(text, "$1******");
        text = SecretJsonField.Replace(text, "$1\"******\"");
        return SecretInConnectionString.Replace(text, "$1=******");
    }

    /// <summary>
    /// Replaces secrets with <c>******</c>: any property with a secret's name, any header row for credentials (a
    /// request's <c>{ key, value }</c> rows and the answer's <c>{ name, value }</c> ones alike), credential header lines
    /// in a request's wire text, and passwords inside connection strings. <c>value</c> is masked only where it sits
    /// beside a credential header's name, or in an auth object (an API key's value). The answer of a send repeats the
    /// signed request, so a Basic header (a password in base64) or a bearer token would otherwise be kept there.
    /// </summary>
    internal static JsonNode? Mask(JsonNode? node, bool inAuth = false)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var headerRow = (obj["key"] ?? obj["name"]) is JsonValue k && k.TryGetValue<string>(out var keyName) && SecretHeader.IsMatch(keyName);
                foreach (var name in obj.Select(p => p.Key).ToList())
                {
                    var child = obj[name];
                    var isAuth = inAuth || name.Equals("auth", StringComparison.OrdinalIgnoreCase);
                    if (child is JsonValue v && v.TryGetValue<string>(out var text))
                    {
                        var secret = name.Equals("value", StringComparison.OrdinalIgnoreCase)
                            ? headerRow || inAuth
                            : SecretName.IsMatch(name) || SecretHeader.IsMatch(name);
                        if (secret) obj[name] = "******";
                        else if (MaskText(text) is var masked && masked != text) obj[name] = masked;
                    }
                    else
                    {
                        Mask(child, isAuth);
                    }
                }
                return obj;
            }
            case JsonArray array:
                foreach (var item in array) Mask(item, inAuth);
                return array;
            default:
                return node;
        }
    }
}
