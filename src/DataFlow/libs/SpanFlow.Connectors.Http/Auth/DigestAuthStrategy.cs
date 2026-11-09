using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SpanFlow.Connectors.Http.Auth;

/// <summary>
/// HTTP Digest (RFC 7616, E68-T3): <c>{"username", "password", "algorithm"?, "realm"?}</c>. The first call to a
/// server goes without credentials; its 401 names a realm and a nonce, which are kept (per credentials and host),
/// and the call is sent once more with a response computed from them. Later calls answer the kept nonce straight
/// away, counting <c>nc</c> up, until the server says it went stale. Algorithms: MD5, MD5-sess, SHA-256,
/// SHA-256-sess; qop <c>auth</c> (preferred) or <c>auth-int</c>. The password never leaves the server.
/// </summary>
public sealed partial class DigestAuthStrategy : IChallengeAuthStrategy
{
    public string Type => "digest";

    private static readonly string[] Algorithms = ["MD5", "MD5-sess", "SHA-256", "SHA-256-sess"];

    /// <summary>A challenge being answered: what the server said, and how many times its nonce was used.</summary>
    private sealed class Challenge(string realm, string nonce, string? opaque, string algorithm, string? qop)
    {
        public string Realm { get; } = realm;
        public string Nonce { get; } = nonce;
        public string? Opaque { get; } = opaque;
        public string Algorithm { get; } = algorithm;
        public string? Qop { get; } = qop;
        public int Count;
    }

    private readonly ConcurrentDictionary<string, Challenge> _challenges = new();

    public IReadOnlyList<string> Validate(JsonObject auth)
    {
        var errors = new List<string>();
        if (auth["username"]?.GetValue<string>() is null) errors.Add("digest auth requires a 'username'.");
        if (auth["password"]?.GetValue<string>() is null) errors.Add("digest auth requires a 'password'.");
        if (auth["algorithm"]?.GetValue<string>() is string algorithm && !Algorithms.Contains(algorithm, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"digest auth 'algorithm' must be one of {string.Join(", ", Algorithms)}.");
        }
        return errors;
    }

    public async Task ApplyAsync(HttpRequestMessage message, JsonObject auth, CancellationToken ct)
    {
        if (!_challenges.TryGetValue(Key(auth, message.RequestUri!), out var challenge)) return; // first call: get the challenge

        var nc = Interlocked.Increment(ref challenge.Count);
        var cnonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        byte[]? body = challenge.Qop == "auth-int" && message.Content is not null ? await message.Content.ReadAsByteArrayAsync(ct) : null;
        message.Headers.Remove("Authorization");
        message.Headers.TryAddWithoutValidation("Authorization", Header(
            auth["username"]!.GetValue<string>(), auth["password"]!.GetValue<string>(), message.Method.Method,
            message.RequestUri!.PathAndQuery, challenge.Realm, challenge.Nonce, challenge.Opaque, challenge.Algorithm,
            challenge.Qop, nc, cnonce, body));
    }

    public bool AcceptChallenge(HttpResponseMessage response, JsonObject auth)
    {
        var offered = response.Headers.WwwAuthenticate
            .Where(h => h.Scheme.Equals("Digest", StringComparison.OrdinalIgnoreCase) && h.Parameter is not null)
            .Select(h => Parameters(h.Parameter!))
            .Where(p => p.ContainsKey("nonce"))
            .ToList();
        if (offered.Count == 0) return false;

        // The configured algorithm if the server offers it, else the strongest it offers.
        var wanted = auth["algorithm"]?.GetValue<string>();
        string AlgorithmOf(Dictionary<string, string> p) => p.TryGetValue("algorithm", out var a) ? a : "MD5";
        var chosen = offered.FirstOrDefault(p => wanted is not null && AlgorithmOf(p).Equals(wanted, StringComparison.OrdinalIgnoreCase))
            ?? offered.OrderByDescending(p => AlgorithmOf(p).StartsWith("SHA-256", StringComparison.OrdinalIgnoreCase)).First();
        var algorithm = Algorithms.FirstOrDefault(a => a.Equals(AlgorithmOf(chosen), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The server asks for digest algorithm '{AlgorithmOf(chosen)}', which is not supported.");

        string? qop = null;
        if (chosen.TryGetValue("qop", out var qops))
        {
            var options = qops.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            qop = options.Contains("auth") ? "auth" : options.Contains("auth-int") ? "auth-int" : null;
        }

        var key = Key(auth, response.RequestMessage!.RequestUri!);
        var stale = chosen.TryGetValue("stale", out var s) && s.Equals("true", StringComparison.OrdinalIgnoreCase);
        // Same nonce again and not stale: the credentials were refused. Don't loop on it.
        if (_challenges.TryGetValue(key, out var previous) && previous.Nonce == chosen["nonce"] && !stale) return false;

        _challenges[key] = new Challenge(chosen.GetValueOrDefault("realm", ""), chosen["nonce"], chosen.GetValueOrDefault("opaque"), algorithm, qop);
        return true;
    }

    /// <summary>The Authorization header value (RFC 7616 §3.4), pure so the RFC's examples can test it.</summary>
    internal static string Header(
        string username, string password, string method, string uri, string realm, string nonce, string? opaque,
        string algorithm, string? qop, int nc, string cnonce, byte[]? body = null)
    {
        var sha = algorithm.StartsWith("SHA-256", StringComparison.OrdinalIgnoreCase);
        string H(string text) => Hex(sha ? SHA256.HashData(Encoding.UTF8.GetBytes(text)) : MD5.HashData(Encoding.UTF8.GetBytes(text)));

        var ha1 = H($"{username}:{realm}:{password}");
        if (algorithm.EndsWith("-sess", StringComparison.OrdinalIgnoreCase)) ha1 = H($"{ha1}:{nonce}:{cnonce}");
        var ha2 = qop == "auth-int"
            ? H($"{method}:{uri}:{Hex(sha ? SHA256.HashData(body ?? []) : MD5.HashData(body ?? []))}")
            : H($"{method}:{uri}");
        var ncText = nc.ToString("x8");
        var response = qop is null ? H($"{ha1}:{nonce}:{ha2}") : H($"{ha1}:{nonce}:{ncText}:{cnonce}:{qop}:{ha2}");

        var header = new StringBuilder("Digest ")
            .Append($"username=\"{Quote(username)}\", realm=\"{Quote(realm)}\", uri=\"{Quote(uri)}\", algorithm={algorithm}, ")
            .Append($"nonce=\"{Quote(nonce)}\"");
        if (qop is not null) header.Append($", nc={ncText}, cnonce=\"{cnonce}\", qop={qop}");
        header.Append($", response=\"{response}\"");
        if (opaque is not null) header.Append($", opaque=\"{Quote(opaque)}\"");
        return header.ToString();
    }

    [GeneratedRegex(@"(?<key>[A-Za-z0-9_\-]+)\s*=\s*(?:""(?<quoted>(?:[^""\\]|\\.)*)""|(?<plain>[^,\s]*))")]
    private static partial Regex Parameter();

    internal static Dictionary<string, string> Parameters(string text)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Parameter().Matches(text))
        {
            // A quoted-string's only escape is a backslash before the next character (RFC 7230 §3.2.6).
            parameters[m.Groups["key"].Value] = m.Groups["quoted"].Success
                ? Regex.Replace(m.Groups["quoted"].Value, @"\\(.)", "$1")
                : m.Groups["plain"].Value;
        }
        return parameters;
    }

    /// <summary>Per credentials and host: the same server answers one nonce for every path.</summary>
    private static string Key(JsonObject auth, Uri uri) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(auth.ToJsonString() + "|" + uri.GetLeftPart(UriPartial.Authority))));

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    private static string Quote(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
