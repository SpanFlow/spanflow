using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace SpanFlow.Connectors.Http.Auth;

/// <summary>
/// JWT Bearer (E68-T3): the server signs a JWT itself and sends it on every call.
/// <code>
/// { "type": "jwtBearer", "algorithm": "RS256", "privateKey": "-----BEGIN PRIVATE KEY-----…",   // or "secret" for HS*
///   "payload": { "iss": "svc-sync", "aud": "https://api.example.com" }, "header": { "kid": "k1" },
///   "lifetimeSeconds": 300, "addTo": "header", "prefix": "Bearer", "queryName": "token" }
/// </code>
/// <c>iat</c>, <c>exp</c> (now + lifetime) and <c>jti</c> are added unless the payload sets them. The signed token
/// is reused until 30 s (or half its lifetime) before it expires. HS256/384/512 sign with the secret (UTF-8, or
/// base64 with <c>secretBase64</c>); RS*, PS* and ES* with the PEM private key. Keys from an E48 key ring are not
/// wired in yet.
/// </summary>
public sealed class JwtBearerAuthStrategy : IAuthStrategy
{
    public string Type => "jwtBearer";

    private static readonly string[] Algorithms =
        ["HS256", "HS384", "HS512", "RS256", "RS384", "RS512", "PS256", "PS384", "PS512", "ES256", "ES384", "ES512"];

    private sealed record Signed(string Token, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, Signed> _signed = new();

    public IReadOnlyList<string> Validate(JsonObject auth)
    {
        var errors = new List<string>();
        var algorithm = auth["algorithm"]?.GetValue<string>() ?? "HS256";
        if (!Algorithms.Contains(algorithm)) errors.Add($"jwtBearer 'algorithm' must be one of {string.Join(", ", Algorithms)}.");
        else if (algorithm.StartsWith("HS") && string.IsNullOrEmpty(auth["secret"]?.GetValue<string>())) errors.Add($"jwtBearer {algorithm} requires a 'secret'.");
        else if (!algorithm.StartsWith("HS") && string.IsNullOrWhiteSpace(auth["privateKey"]?.GetValue<string>())) errors.Add($"jwtBearer {algorithm} requires a PEM 'privateKey'.");
        if (auth["payload"] is not null and not JsonObject) errors.Add("jwtBearer 'payload' must be an object of claims.");
        if (auth["addTo"]?.GetValue<string>() is string addTo && addTo is not ("header" or "query")) errors.Add("jwtBearer 'addTo' must be header or query.");
        if (errors.Count == 0)
        {
            try { Sign(auth, DateTimeOffset.UtcNow); } // a key that doesn't import is a config error, not a run failure
            catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException)
            {
                errors.Add($"jwtBearer key could not be used: {ex.Message}");
            }
        }
        return errors;
    }

    public Task ApplyAsync(HttpRequestMessage message, JsonObject auth, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(auth.ToJsonString())));
        if (!_signed.TryGetValue(key, out var signed) || now >= signed.ExpiresAt - Margin(signed))
        {
            _signed[key] = signed = Sign(auth, now);
        }

        if (auth["addTo"]?.GetValue<string>() == "query")
        {
            var name = auth["queryName"]?.GetValue<string>() ?? "token";
            var uri = message.RequestUri!;
            message.RequestUri = new Uri($"{uri}{(string.IsNullOrEmpty(uri.Query) ? "?" : "&")}{Uri.EscapeDataString(name)}={Uri.EscapeDataString(signed.Token)}");
        }
        else
        {
            var prefix = auth["prefix"] is JsonValue p && p.TryGetValue<string>(out var text) ? text : "Bearer";
            message.Headers.Remove("Authorization");
            message.Headers.TryAddWithoutValidation("Authorization", prefix.Length == 0 ? signed.Token : $"{prefix} {signed.Token}");
        }
        return Task.CompletedTask;
    }

    private static TimeSpan Margin(Signed signed)
    {
        var half = (signed.ExpiresAt - signed.IssuedAt) / 2;
        return half < TimeSpan.FromSeconds(30) ? half : TimeSpan.FromSeconds(30);
    }

    private static Signed Sign(JsonObject auth, DateTimeOffset now)
    {
        var algorithm = auth["algorithm"]?.GetValue<string>() ?? "HS256";
        var lifetime = auth["lifetimeSeconds"] is JsonNode l && int.TryParse(l.ToString(), out var seconds) && seconds > 0 ? seconds : 300;

        var header = auth["header"] is JsonObject extra ? (JsonObject)extra.DeepClone() : new JsonObject();
        header["alg"] = algorithm;
        header["typ"] ??= "JWT";

        var claims = auth["payload"] is JsonObject payload ? (JsonObject)payload.DeepClone() : new JsonObject();
        claims["iat"] ??= now.ToUnixTimeSeconds();
        claims["exp"] ??= now.AddSeconds(lifetime).ToUnixTimeSeconds();
        claims["jti"] ??= Guid.NewGuid().ToString("N");
        var expiresAt = claims["exp"] is JsonValue e && e.TryGetValue<long>(out var exp) ? DateTimeOffset.FromUnixTimeSeconds(exp) : now.AddSeconds(lifetime);

        var signingInput = $"{B64(Encoding.UTF8.GetBytes(header.ToJsonString()))}.{B64(Encoding.UTF8.GetBytes(claims.ToJsonString()))}";
        var signature = SignBytes(auth, algorithm, Encoding.ASCII.GetBytes(signingInput));
        return new Signed($"{signingInput}.{B64(signature)}", now, expiresAt);
    }

    internal static byte[] SignBytes(JsonObject auth, string algorithm, byte[] data)
    {
        var hash = algorithm[2..] switch { "384" => HashAlgorithmName.SHA384, "512" => HashAlgorithmName.SHA512, _ => HashAlgorithmName.SHA256 };
        switch (algorithm[..2])
        {
            case "HS":
                var secret = auth["secret"]!.GetValue<string>();
                var key = auth["secretBase64"] is JsonValue b && b.TryGetValue<bool>(out var isBase64) && isBase64
                    ? Convert.FromBase64String(secret)
                    : Encoding.UTF8.GetBytes(secret);
                return hash.Name switch
                {
                    "SHA384" => HMACSHA384.HashData(key, data),
                    "SHA512" => HMACSHA512.HashData(key, data),
                    _ => HMACSHA256.HashData(key, data),
                };
            case "ES":
                using (var ecdsa = ECDsa.Create())
                {
                    ecdsa.ImportFromPem(auth["privateKey"]!.GetValue<string>());
                    return ecdsa.SignData(data, hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation); // JWS wants r||s
                }
            default:
                using (var rsa = RSA.Create())
                {
                    rsa.ImportFromPem(auth["privateKey"]!.GetValue<string>());
                    return rsa.SignData(data, hash, algorithm.StartsWith("PS") ? RSASignaturePadding.Pss : RSASignaturePadding.Pkcs1);
                }
        }
    }

    internal static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
