using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

/// <summary>
/// Keyless, deterministic hash and encoding functions for mappings (E48-T10): <c>sha256</c>, <c>sha384</c>,
/// <c>sha512</c>, <c>md5Legacy</c>, and <c>base64Encode/Decode</c>, <c>base64UrlEncode/Decode</c>,
/// <c>hexEncode/Decode</c>, <c>urlEncode/Decode</c>.
///
/// <para><b>Keyless only, on purpose.</b> A mapping has nowhere safe to keep a key, so anything keyed (HMAC, a
/// peppered hash, encryption), anything random and anything password-related stays in pipeline nodes. MD5 is
/// offered for matching a partner's existing checksum and carries its weakness in its name.</para>
///
/// <para><b>Text in, text out.</b> A string is hashed/encoded as its UTF-8 bytes; a number or boolean as its JSON
/// text (<c>12.5</c>, <c>true</c>); an object or array as its compact JSON. Null gives null — a missing field
/// should not quietly become the hash of an empty string. A decode of something that is not valid in that
/// format (or not valid UTF-8 once decoded) gives null, like the convert functions; wrap it in
/// <c>coalesce</c> for a fallback.</para>
/// </summary>
internal static class HashEncodingText
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The bytes a value stands for, or null for a null/missing value.</summary>
    public static string? Text(JsonNode? node) => node switch
    {
        null => null,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        _ => node.ToJsonString(),
    };

    public static string Encode(byte[] bytes, string? encoding) => (encoding?.Trim().ToLowerInvariant()) switch
    {
        null or "" or "hex" => Convert.ToHexString(bytes).ToLowerInvariant(),
        "base64" => Convert.ToBase64String(bytes),
        "base64url" => Base64Url(bytes),
        var other => throw new InvalidOperationException($"Unknown hash encoding '{other}'. Use hex, base64 or base64url."),
    };

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Base64 or base64url, padded or not.</summary>
    public static byte[]? FromBase64(string text, bool url)
    {
        var normalized = text.Trim();
        if (url) normalized = normalized.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
        try { return Convert.FromBase64String(normalized); }
        catch (FormatException) { return null; }
    }

    public static JsonNode? Utf8(byte[]? bytes)
    {
        if (bytes is null) return null;
        try { return JsonValue.Create(StrictUtf8.GetString(bytes)); }
        catch (DecoderFallbackException) { return null; }
    }
}

/// <summary><c>sha256(value, [encoding])</c> and friends — a digest of the value, hex by default.</summary>
public sealed class DigestTransform(string name, Func<byte[], byte[]> digest) : ITransform
{
    public string Name => name;

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
    {
        if (HashEncodingText.Text(args.ElementAtOrDefault(0)) is not { } text) return null;
        var encoding = HashEncodingText.Text(args.ElementAtOrDefault(1));
        return JsonValue.Create(HashEncodingText.Encode(digest(Encoding.UTF8.GetBytes(text)), encoding));
    }

    public static DigestTransform Sha256() => new("sha256", SHA256.HashData);
    public static DigestTransform Sha384() => new("sha384", SHA384.HashData);
    public static DigestTransform Sha512() => new("sha512", SHA512.HashData);

#pragma warning disable CA5351 // interop only, and the name says so
    public static DigestTransform Md5Legacy() => new("md5Legacy", MD5.HashData);
#pragma warning restore CA5351
}

/// <summary>An encode/decode pair over UTF-8 text: <c>base64Encode(value)</c>, <c>base64Decode(value)</c>, …</summary>
public sealed class TextCodecTransform(string name, Func<string, JsonNode?> codec) : ITransform
{
    public string Name => name;

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args) =>
        HashEncodingText.Text(args.ElementAtOrDefault(0)) is { } text ? codec(text) : null;

    public static IEnumerable<TextCodecTransform> All() =>
    [
        new("base64Encode", t => JsonValue.Create(Convert.ToBase64String(Encoding.UTF8.GetBytes(t)))),
        new("base64Decode", t => HashEncodingText.Utf8(HashEncodingText.FromBase64(t, url: false))),
        new("base64UrlEncode", t => JsonValue.Create(HashEncodingText.Base64Url(Encoding.UTF8.GetBytes(t)))),
        new("base64UrlDecode", t => HashEncodingText.Utf8(HashEncodingText.FromBase64(t, url: true))),
        new("hexEncode", t => JsonValue.Create(Convert.ToHexString(Encoding.UTF8.GetBytes(t)).ToLowerInvariant())),
        new("hexDecode", t => HashEncodingText.Utf8(HexBytes(t))),
        new("urlEncode", t => JsonValue.Create(Uri.EscapeDataString(t))),
        new("urlDecode", t => JsonValue.Create(Uri.UnescapeDataString(t))),
    ];

    private static byte[]? HexBytes(string text)
    {
        try { return Convert.FromHexString(text.Trim()); }
        catch (FormatException) { return null; }
    }
}
