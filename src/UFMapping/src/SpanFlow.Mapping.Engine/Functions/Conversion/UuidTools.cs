using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SpanFlow.Mapping.Engine;

/// <summary>
/// UUID parsing, version checks, generation (v1, v3, v4, v5, v6, v7), timestamp
/// extraction and layouts, per RFC 9562. Bytes are always handled big-endian
/// (network order) — the order the version and variant bits are defined in.
/// </summary>
public static class UuidTools
{
    public static readonly Guid Max = new("ffffffff-ffff-ffff-ffff-ffffffffffff");

    public static readonly Guid DnsNamespace = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
    public static readonly Guid UrlNamespace = new("6ba7b811-9dad-11d1-80b4-00c04fd430c8");
    public static readonly Guid OidNamespace = new("6ba7b812-9dad-11d1-80b4-00c04fd430c8");
    public static readonly Guid X500Namespace = new("6ba7b814-9dad-11d1-80b4-00c04fd430c8");

    // v1/v6 count 100 ns intervals from the start of the Gregorian calendar.
    private static readonly long GregorianStartTicks = new DateTime(1582, 10, 15, 0, 0, 0, DateTimeKind.Utc).Ticks;

    /// <summary>
    /// Reads any common layout: <c>d</c> (hyphenated), <c>n</c> (32 hex), <c>b</c>
    /// <c>{}</c>, <c>p</c> <c>()</c>, <c>x</c>, and <c>urn:uuid:</c>. Base64 only when
    /// <paramref name="allowBase64"/> — short random text would otherwise pass as a UUID.
    /// </summary>
    public static bool TryParse(string? text, out Guid value, bool allowBase64 = false)
    {
        value = Guid.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("urn:uuid:", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[9..];
        }

        if (Guid.TryParse(trimmed, out value))
        {
            return true;
        }

        return allowBase64 && TryParseBase64(trimmed, out value);
    }

    /// <summary>
    /// The version nibble (1–8) for an RFC 9562 UUID, 0 for the nil UUID, 15 for
    /// the max UUID, or null when the variant bits say it isn't an RFC UUID at all
    /// (e.g. a Microsoft COM GUID).
    /// </summary>
    public static int? GetVersion(Guid value)
    {
        if (value == Guid.Empty)
        {
            return 0;
        }

        if (value == Max)
        {
            return 15;
        }

        var bytes = ToBytes(value);
        return (bytes[8] & 0xC0) == 0x80 ? bytes[6] >> 4 : null;
    }

    /// <summary>
    /// Whether <paramref name="value"/> matches a version spec: null/<c>any</c>,
    /// <c>nil</c>, <c>max</c>, or a version number <c>1</c>–<c>8</c> (also <c>v7</c>).
    /// A malformed spec throws.
    /// </summary>
    public static bool Matches(Guid value, string? versionSpec)
    {
        var spec = versionSpec?.Trim().ToLowerInvariant();
        switch (spec)
        {
            case null or "" or "any":
                return true;
            case "nil":
                return value == Guid.Empty;
            case "max":
                return value == Max;
        }

        var digits = spec.StartsWith('v') ? spec[1..] : spec;
        if (int.TryParse(digits, out var version) && version is >= 1 and <= 8)
        {
            return GetVersion(value) == version;
        }

        throw new InvalidOperationException($"Unknown UUID version '{versionSpec}'. Use 1-8, nil, max or any.");
    }

    /// <summary>
    /// Makes a new UUID. v3/v5 are name-based and deterministic, so they need a
    /// namespace (dns, url, oid, x500 or any UUID) and a name.
    /// </summary>
    public static Guid Create(int version, string? namespaceId = null, string? name = null) => version switch
    {
        1 => TimeBased(reordered: false),
        3 => NameBased(3, namespaceId, name),
        4 => Guid.NewGuid(),
        5 => NameBased(5, namespaceId, name),
        6 => TimeBased(reordered: true),
        7 => Guid.CreateVersion7(),
        _ => throw new InvalidOperationException(
            $"Generating UUID v{version} is not supported. newUuid makes versions 1, 3, 4, 5, 6 and 7 (v2 and v8 can still be checked with isUuid)."),
    };

    /// <summary>The creation time held in a v1, v6 or v7 UUID.</summary>
    public static bool TryGetTimestamp(Guid value, out DateTimeOffset result)
    {
        result = default;
        var bytes = ToBytes(value);

        try
        {
            switch (GetVersion(value))
            {
                case 1:
                    var v1 = ((long)(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(6)) & 0x0FFF) << 48)
                        | ((long)BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4)) << 32)
                        | BinaryPrimitives.ReadUInt32BigEndian(bytes);
                    result = new DateTimeOffset(GregorianStartTicks + v1, TimeSpan.Zero);
                    return true;

                case 6:
                    var v6 = ((long)BinaryPrimitives.ReadUInt32BigEndian(bytes) << 28)
                        | ((long)BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4)) << 12)
                        | (long)(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(6)) & 0x0FFF);
                    result = new DateTimeOffset(GregorianStartTicks + v6, TimeSpan.Zero);
                    return true;

                case 7:
                    var unixMs = ((long)BinaryPrimitives.ReadUInt32BigEndian(bytes) << 16)
                        | BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
                    result = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
                    return true;

                default:
                    return false;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes a UUID in a layout: <c>d</c> (default), <c>n</c>/<c>hex</c>, <c>b</c>,
    /// <c>p</c>, <c>x</c>, <c>urn</c>, <c>base64</c>, <c>base64url</c>. Prefix with
    /// <c>upper:</c> (or use <c>upper</c> alone) for upper-case hex.
    /// </summary>
    public static string Format(Guid value, string? format)
    {
        var layout = format?.Trim() ?? string.Empty;
        var upper = false;

        if (layout.Equals("upper", StringComparison.OrdinalIgnoreCase))
        {
            (upper, layout) = (true, "d");
        }
        else if (layout.StartsWith("upper:", StringComparison.OrdinalIgnoreCase))
        {
            (upper, layout) = (true, layout[6..]);
        }

        var text = layout.ToLowerInvariant() switch
        {
            "" or "d" or "default" => value.ToString("D"),
            "n" or "hex" => value.ToString("N"),
            "b" => value.ToString("B"),
            "p" => value.ToString("P"),
            "x" => value.ToString("X"),
            "urn" => "urn:uuid:" + value.ToString("D"),
            "base64" => Convert.ToBase64String(ToBytes(value)),
            "base64url" => Convert.ToBase64String(ToBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
            _ => throw new InvalidOperationException(
                $"Unknown UUID format '{format}'. Use d, n, b, p, x, urn, base64 or base64url (prefix 'upper:' for upper case)."),
        };

        // Upper-casing base64 would change the bytes it encodes.
        return upper && !layout.StartsWith("base64", StringComparison.OrdinalIgnoreCase)
            ? text.ToUpperInvariant()
            : text;
    }

    public static Guid ResolveNamespace(string? namespaceId) => namespaceId?.Trim().ToLowerInvariant() switch
    {
        "dns" => DnsNamespace,
        "url" => UrlNamespace,
        "oid" => OidNamespace,
        "x500" => X500Namespace,
        _ when TryParse(namespaceId, out var custom) => custom,
        _ => throw new InvalidOperationException(
            $"UUID v3/v5 need a namespace: dns, url, oid, x500 or a UUID (got '{namespaceId}')."),
    };

    private static byte[] ToBytes(Guid value)
    {
        var bytes = new byte[16];
        WriteBigEndian(value, bytes);
        return bytes;
    }

    private static bool TryParseBase64(string text, out Guid value)
    {
        value = Guid.Empty;
        var normalized = text.Replace('-', '+').Replace('_', '/');
        normalized = normalized.Length == 22 ? normalized + "==" : normalized;
        if (normalized.Length != 24)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[18];
        if (!Convert.TryFromBase64String(normalized, bytes, out var written) || written != 16)
        {
            return false;
        }

        value = FromBigEndian(bytes[..16]);
        return true;
    }

    private static Guid NameBased(int version, string? namespaceId, string? name)
    {
        if (name is null)
        {
            throw new InvalidOperationException($"UUID v{version} needs a name: newUuid({version}, namespace, name).");
        }

        var ns = ToBytes(ResolveNamespace(namespaceId));
        var data = new byte[ns.Length + Encoding.UTF8.GetByteCount(name)];
        ns.CopyTo(data, 0);
        Encoding.UTF8.GetBytes(name, data.AsSpan(ns.Length));

        // MD5/SHA-1 are what RFC 9562 defines for v3/v5 — identifiers, not security.
#pragma warning disable CA5350, CA5351
        var hash = version == 3 ? MD5.HashData(data) : SHA1.HashData(data);
#pragma warning restore CA5350, CA5351

        var bytes = hash.AsSpan(0, 16).ToArray();
        SetVersionAndVariant(bytes, version);
        return FromBigEndian(bytes);
    }

    private static Guid TimeBased(bool reordered)
    {
        var timestamp = DateTime.UtcNow.Ticks - GregorianStartTicks; // 60 bits
        var bytes = new byte[16];

        if (reordered)
        {
            // v6: the same timestamp as v1, most significant bits first, so it sorts by time.
            BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)(timestamp >> 28));
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), (ushort)(timestamp >> 12));
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), (ushort)(timestamp & 0x0FFF));
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)timestamp);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), (ushort)(timestamp >> 32));
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), (ushort)((timestamp >> 48) & 0x0FFF));
        }

        // Random clock sequence and node. The node's multicast bit is set so it can
        // never collide with a real MAC address (RFC 9562 §6.10).
        RandomNumberGenerator.Fill(bytes.AsSpan(8));
        bytes[10] |= 0x01;

        SetVersionAndVariant(bytes, reordered ? 6 : 1);
        return FromBigEndian(bytes);
    }

    private static void SetVersionAndVariant(byte[] bytes, int version)
    {
        bytes[6] = (byte)((bytes[6] & 0x0F) | (version << 4));
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
    }

    // Guid's own layout stores the first three fields little-endian; RFC 9562 is big-endian throughout.
    // .NET 8+ converts directly; netstandard2.0 swaps those three fields by hand.
    private static Guid FromBigEndian(ReadOnlySpan<byte> bytes)
    {
#if NET8_0_OR_GREATER
        return new Guid(bytes, bigEndian: true);
#else
        var b = bytes[..16].ToArray();
        SwapGuidFields(b);
        return new Guid(b);
#endif
    }

    private static void WriteBigEndian(Guid value, byte[] destination)
    {
#if NET8_0_OR_GREATER
        value.TryWriteBytes(destination, bigEndian: true, out _);
#else
        var b = value.ToByteArray();
        SwapGuidFields(b);
        b.CopyTo(destination, 0);
#endif
    }

#if !NET8_0_OR_GREATER
    private static void SwapGuidFields(byte[] b)
    {
        Array.Reverse(b, 0, 4);
        Array.Reverse(b, 4, 2);
        Array.Reverse(b, 6, 2);
    }
#endif
}
