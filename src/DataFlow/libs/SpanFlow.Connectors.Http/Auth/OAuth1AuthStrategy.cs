using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace SpanFlow.Connectors.Http.Auth;

/// <summary>
/// OAuth 1.0a request signing (RFC 5849, E68-T3).
/// <code>
/// { "type": "oauth1", "consumerKey": "…", "consumerSecret": "…", "token"?: "…", "tokenSecret"?: "…",
///   "signatureMethod": "HMAC-SHA1" | "HMAC-SHA256" | "HMAC-SHA512" | "RSA-SHA1" | "RSA-SHA256" | "RSA-SHA512" | "PLAINTEXT",
///   "privateKey"?: "PEM (RSA-*)", "addTo": "header" | "query" | "body", "realm"?, "callback"?, "verifier"?,
///   "includeBodyHash"?: false }
/// </code>
/// The signature base string covers the method, the URL, its query parameters and, for a form-encoded body, the
/// body's fields, which is why this runs after the body is built. <c>addTo: body</c> needs a form-encoded body.
/// <c>includeBodyHash</c> adds <c>oauth_body_hash</c> for any other body.
/// </summary>
public sealed class OAuth1AuthStrategy : IAuthStrategy
{
    public string Type => "oauth1";

    private static readonly string[] Methods =
        ["HMAC-SHA1", "HMAC-SHA256", "HMAC-SHA512", "RSA-SHA1", "RSA-SHA256", "RSA-SHA512", "PLAINTEXT"];

    private const string FormMediaType = "application/x-www-form-urlencoded";

    public IReadOnlyList<string> Validate(JsonObject auth)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(auth["consumerKey"]?.GetValue<string>())) errors.Add("oauth1 auth requires a 'consumerKey'.");
        var method = SignatureMethod(auth);
        if (!Methods.Contains(method)) errors.Add($"oauth1 'signatureMethod' must be one of {string.Join(", ", Methods)}.");
        else if (method.StartsWith("RSA") && string.IsNullOrWhiteSpace(auth["privateKey"]?.GetValue<string>())) errors.Add($"oauth1 {method} requires a PEM 'privateKey'.");
        else if (!method.StartsWith("RSA") && auth["consumerSecret"]?.GetValue<string>() is null) errors.Add("oauth1 auth requires a 'consumerSecret'.");
        if (auth["addTo"]?.GetValue<string>() is string addTo && addTo is not ("header" or "query" or "body")) errors.Add("oauth1 'addTo' must be header, query or body.");
        return errors;
    }

    private static string SignatureMethod(JsonObject auth) => auth["signatureMethod"]?.GetValue<string>()?.ToUpperInvariant() ?? "HMAC-SHA1";

    public async Task ApplyAsync(HttpRequestMessage message, JsonObject auth, CancellationToken ct)
    {
        var method = SignatureMethod(auth);
        var addTo = auth["addTo"]?.GetValue<string>() ?? "header";
        var isForm = message.Content?.Headers.ContentType?.MediaType?.Equals(FormMediaType, StringComparison.OrdinalIgnoreCase) == true;
        var body = message.Content is null ? null : await message.Content.ReadAsByteArrayAsync(ct);
        if (addTo == "body" && !isForm) throw new InvalidOperationException("oauth1 addTo 'body' needs a form-encoded (urlencoded) body.");

        var oauth = new List<KeyValuePair<string, string>>
        {
            new("oauth_consumer_key", auth["consumerKey"]!.GetValue<string>()),
            new("oauth_nonce", Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()),
            new("oauth_signature_method", method),
            new("oauth_timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
            new("oauth_version", "1.0"),
        };
        if (auth["token"]?.GetValue<string>() is { Length: > 0 } token) oauth.Add(new("oauth_token", token));
        if (auth["callback"]?.GetValue<string>() is { Length: > 0 } callback) oauth.Add(new("oauth_callback", callback));
        if (auth["verifier"]?.GetValue<string>() is { Length: > 0 } verifier) oauth.Add(new("oauth_verifier", verifier));
        if (auth["includeBodyHash"] is JsonValue h && h.TryGetValue<bool>(out var bodyHash) && bodyHash && !isForm)
        {
            var hash = method.EndsWith("512") ? SHA512.HashData(body ?? []) : method.EndsWith("256") ? SHA256.HashData(body ?? []) : SHA1.HashData(body ?? []);
            oauth.Add(new("oauth_body_hash", Convert.ToBase64String(hash)));
        }

        var formFields = isForm && body is not null ? ParseForm(Encoding.UTF8.GetString(body)) : [];
        var signature = Signature(message.Method.Method, message.RequestUri!, oauth, formFields,
            auth["consumerSecret"]?.GetValue<string>() ?? "", auth["tokenSecret"]?.GetValue<string>(), method,
            auth["privateKey"]?.GetValue<string>());
        oauth.Add(new("oauth_signature", signature));

        switch (addTo)
        {
            case "query":
                var uri = message.RequestUri!;
                var query = string.Join("&", oauth.Select(p => $"{Encode(p.Key)}={Encode(p.Value)}"));
                message.RequestUri = new Uri($"{uri}{(string.IsNullOrEmpty(uri.Query) ? "?" : "&")}{query}");
                break;
            case "body":
                var text = Encoding.UTF8.GetString(body!);
                var appended = string.Join("&", oauth.Select(p => $"{Encode(p.Key)}={Encode(p.Value)}"));
                var content = new StringContent(text.Length == 0 ? appended : $"{text}&{appended}", Encoding.UTF8);
                content.Headers.ContentType = new MediaTypeHeaderValue(FormMediaType);
                message.Content = content;
                break;
            default:
                var realm = auth["realm"]?.GetValue<string>();
                var parameters = oauth.Select(p => $"{Encode(p.Key)}=\"{Encode(p.Value)}\"");
                if (realm is not null) parameters = parameters.Prepend($"realm=\"{realm.Replace("\"", "\\\"")}\"");
                message.Headers.Remove("Authorization");
                message.Headers.TryAddWithoutValidation("Authorization", "OAuth " + string.Join(", ", parameters));
                break;
        }
    }

    /// <summary>The <c>oauth_signature</c> (RFC 5849 §3.4), pure so a published example can test it.</summary>
    internal static string Signature(
        string httpMethod, Uri uri, IEnumerable<KeyValuePair<string, string>> oauth, IEnumerable<KeyValuePair<string, string>> formFields,
        string consumerSecret, string? tokenSecret, string signatureMethod, string? privateKeyPem = null)
    {
        var all = oauth.Concat(ParseForm(uri.Query.TrimStart('?'))).Concat(formFields)
            .Select(p => (Key: Encode(p.Key), Value: Encode(p.Value)))
            .OrderBy(p => p.Key, StringComparer.Ordinal).ThenBy(p => p.Value, StringComparer.Ordinal);
        var normalized = string.Join("&", all.Select(p => $"{p.Key}={p.Value}"));

        var port = uri.IsDefaultPort ? "" : $":{uri.Port}";
        var baseUri = $"{uri.Scheme.ToLowerInvariant()}://{uri.Host.ToLowerInvariant()}{port}{uri.AbsolutePath}";
        var baseString = $"{httpMethod.ToUpperInvariant()}&{Encode(baseUri)}&{Encode(normalized)}";
        var key = $"{Encode(consumerSecret)}&{Encode(tokenSecret ?? "")}";
        var data = Encoding.ASCII.GetBytes(baseString);

        switch (signatureMethod)
        {
            case "PLAINTEXT":
                return key;
            case "HMAC-SHA1": return Convert.ToBase64String(HMACSHA1.HashData(Encoding.ASCII.GetBytes(key), data));
            case "HMAC-SHA256": return Convert.ToBase64String(HMACSHA256.HashData(Encoding.ASCII.GetBytes(key), data));
            case "HMAC-SHA512": return Convert.ToBase64String(HMACSHA512.HashData(Encoding.ASCII.GetBytes(key), data));
            default:
                using (var rsa = RSA.Create())
                {
                    rsa.ImportFromPem(privateKeyPem ?? throw new InvalidOperationException($"oauth1 {signatureMethod} requires a 'privateKey'."));
                    var hash = signatureMethod switch { "RSA-SHA256" => HashAlgorithmName.SHA256, "RSA-SHA512" => HashAlgorithmName.SHA512, _ => HashAlgorithmName.SHA1 };
                    return Convert.ToBase64String(rsa.SignData(data, hash, RSASignaturePadding.Pkcs1));
                }
        }
    }

    /// <summary>
    /// RFC 3986 percent-encoding as OAuth 1 requires it: everything but ALPHA, DIGIT, <c>-._~</c> is encoded, as
    /// upper-case hex of its UTF-8 bytes. (<see cref="Uri.EscapeDataString(string)"/> leaves <c>!*'()</c> alone on some runtimes.)
    /// </summary>
    internal static string Encode(string value)
    {
        var result = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            if (b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9'
                or (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~')
            {
                result.Append((char)b);
            }
            else
            {
                result.Append('%').Append(b.ToString("X2"));
            }
        }
        return result.ToString();
    }

    /// <summary>Name/value pairs of a query string or form body, decoded (<c>+</c> is a space, as forms write it).</summary>
    internal static List<KeyValuePair<string, string>> ParseForm(string text) =>
        text.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                var eq = pair.IndexOf('=');
                var name = eq < 0 ? pair : pair[..eq];
                var value = eq < 0 ? "" : pair[(eq + 1)..];
                return new KeyValuePair<string, string>(Decode(name), Decode(value));
            })
            .ToList();

    private static string Decode(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));
}
