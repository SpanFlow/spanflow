#if NETSTANDARD2_0
namespace SpanFlow.Mapping.Engine;

/// <summary>
/// The few static BCL members the engine calls that netstandard2.0 lacks, added as C# 14 static
/// extension members so the call sites read the same on every target. Only compiled for
/// netstandard2.0; on net8.0+ the real members are used.
/// </summary>
internal static class NetStandardCompat
{
    extension(double)
    {
        public static bool IsFinite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);
    }

    extension(float)
    {
        public static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    }

    extension(DateTimeOffset)
    {
        public static DateTimeOffset UnixEpoch => new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    extension(TimeSpan)
    {
        public static long TicksPerMicrosecond => 10;
    }

    extension(TimeZoneInfo)
    {
        /// <summary>
        /// .NET Framework on Windows only knows Windows ids, so TimeZoneConverter maps IANA ids
        /// (e.g. 'Asia/Karachi') as well, the way .NET 8+ does natively.
        /// </summary>
        public static bool TryFindSystemTimeZoneById(string id, out TimeZoneInfo? zone)
        {
            if (TimeZoneConverter.TZConvert.TryGetTimeZoneInfo(id, out var found))
            {
                zone = found;
                return true;
            }

            zone = null;
            return false;
        }
    }
}
#endif
