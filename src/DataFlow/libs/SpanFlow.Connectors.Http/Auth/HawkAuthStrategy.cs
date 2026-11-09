using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace SpanFlow.Connectors.Http.Auth;

/// <summary>
/// Hawk (E68-T3): <c>{"authId", "authKey", "algorithm": "sha256"|"sha1", "user"?, "ext"?, "app"?, "dlg"?,
/// "includePayloadHash"?: true}</c>. Every call is signed with an HMAC over the method, path, host, port, a
/// timestamp and a fresh nonce, and (by default) a hash of the body, which is why this runs after the body is
/// built. The key never leaves the server. <c>user</c> is sent as the Hawk <c>ext</c> when no <c>ext</c> is set.
/// </summary>
public sealed class HawkAuthStrategy : IAuthStrategy
{
    public string Type => "hawk";

    public IReadOnlyList<string> Validate(JsonObject auth)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(auth["authId"]?.GetValue<string>())) errors.Add("hawk auth requires an 'authId'.");
        if (string.IsNullOrWhiteSpace(auth["authKey"]?.GetValue<string>())) errors.Add("hawk auth requires an 'authKey'.");
        if (auth["algorithm"]?.GetValue<string>() is string a && a is not ("sha256" or "sha1"))
        {
            errors.Add("hawk auth 'algorithm' must be sha256 or sha1.");
        }
        return errors;
    }

    public async Task ApplyAsync(HttpRequestMessage message, JsonObject auth, CancellationToken ct)
    {
        string? hash = null;
        var includeHash = auth["includePayloadHash"] is not JsonValue v || !v.TryGetValue<bool>(out var include) || include;
        if (includeHash && message.Content is not null)
        {
            var payload = await message.Content.ReadAsByteArrayAsync(ct);
            hash = PayloadHash(Algorithm(auth), message.Content.Headers.ContentType?.MediaType ?? "", payload);
        }

        var ext = auth["ext"]?.GetValue<string>() ?? auth["user"]?.GetValue<string>();
        message.Headers.Remove("Authorization");
        message.Headers.TryAddWithoutValidation("Authorization", Header(
            auth["authId"]!.GetValue<string>(), auth["authKey"]!.GetValue<string>(), Algorithm(auth),
            message.Method.Method, message.RequestUri!, DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(6)).Replace('+', 'a').Replace('/', 'b').TrimEnd('='),
            hash, ext, auth["app"]?.GetValue<string>(), auth["dlg"]?.GetValue<string>()));
    }

    private static string Algorithm(JsonObject auth) => auth["algorithm"]?.GetValue<string>() ?? "sha256";

    /// <summary>The Authorization header for one request; pure so the Hawk spec's examples can test it.</summary>
    internal static string Header(
        string id, string key, string algorithm, string method, Uri uri, long ts, string nonce,
        string? hash, string? ext, string? app = null, string? dlg = null)
    {
        var normalized = new StringBuilder("hawk.1.header\n")
            .Append(ts).Append('\n')
            .Append(nonce).Append('\n')
            .Append(method.ToUpperInvariant()).Append('\n')
            .Append(uri.PathAndQuery).Append('\n')
            .Append(uri.Host.ToLowerInvariant()).Append('\n')
            .Append(uri.Port).Append('\n')
            .Append(hash ?? "").Append('\n')
            .Append((ext ?? "").Replace("\\", "\\\\").Replace("\n", "\\n")).Append('\n'); // Hawk escapes these two in the MAC
        if (app is not null) normalized.Append(app).Append('\n').Append(dlg ?? "").Append('\n');

        var mac = Convert.ToBase64String(Hmac(algorithm, Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(normalized.ToString())));
        var header = new StringBuilder($"Hawk id=\"{id}\", ts=\"{ts}\", nonce=\"{nonce}\"");
        if (hash is not null) header.Append($", hash=\"{hash}\"");
        if (!string.IsNullOrEmpty(ext)) header.Append($", ext=\"{Escape(ext)}\"");
        header.Append($", mac=\"{mac}\"");
        if (app is not null)
        {
            header.Append($", app=\"{app}\"");
            if (dlg is not null) header.Append($", dlg=\"{dlg}\"");
        }
        return header.ToString();
    }

    /// <summary>Hawk's payload hash: the media type (no parameters, lower case) and the body, under the same algorithm.</summary>
    internal static string PayloadHash(string algorithm, string mediaType, byte[] payload)
    {
        var prefix = Encoding.UTF8.GetBytes($"hawk.1.payload\n{mediaType.Split(';')[0].Trim().ToLowerInvariant()}\n");
        var data = new byte[prefix.Length + payload.Length + 1];
        prefix.CopyTo(data, 0);
        payload.CopyTo(data, prefix.Length);
        data[^1] = (byte)'\n';
        return Convert.ToBase64String(algorithm == "sha1" ? SHA1.HashData(data) : SHA256.HashData(data));
    }

    private static byte[] Hmac(string algorithm, byte[] key, byte[] data) =>
        algorithm == "sha1" ? HMACSHA1.HashData(key, data) : HMACSHA256.HashData(key, data);

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
