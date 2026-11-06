namespace SpanFlow.Mapping.Engine;

public enum EpochUnit
{
    /// <summary>Unix seconds.</summary>
    Seconds,
    /// <summary>Unix milliseconds (JavaScript <c>Date.now()</c>).</summary>
    Milliseconds,
    /// <summary>Unix microseconds.</summary>
    Microseconds,
    /// <summary>Unix nanoseconds (precision is 100 ns — a .NET tick).</summary>
    Nanoseconds,
    /// <summary>.NET ticks: 100 ns intervals since 0001-01-01 UTC (not since 1970).</summary>
    Ticks,
}

/// <summary>Epoch numbers ⇄ instants, in every <see cref="EpochUnit"/>.</summary>
public static class Epoch
{
    /// <summary>The conversion type name for each unit (<c>convert(x, 'epochMs')</c>).</summary>
    public static string TypeName(EpochUnit unit) => unit switch
    {
        EpochUnit.Seconds => "epoch",
        EpochUnit.Milliseconds => "epochMs",
        EpochUnit.Microseconds => "epochUs",
        EpochUnit.Nanoseconds => "epochNs",
        _ => "ticks",
    };

    /// <summary>Extra type names accepted for each unit.</summary>
    public static IReadOnlyList<string> TypeAliases(EpochUnit unit) => unit switch
    {
        EpochUnit.Seconds => ["epoch", "unix", "epochSeconds", "unixSeconds", "epoch_s"],
        EpochUnit.Milliseconds => ["epochMs", "epochMillis", "unixMs", "epoch_ms"],
        EpochUnit.Microseconds => ["epochUs", "epochMicros", "unixUs", "epoch_us"],
        EpochUnit.Nanoseconds => ["epochNs", "epochNanos", "unixNs", "epoch_ns"],
        _ => ["ticks", "dotnetTicks"],
    };

    /// <summary>
    /// Reads a unit name. Returns true when <paramref name="name"/> IS a unit name;
    /// <paramref name="unit"/> is then the unit, or null for <c>auto</c>.
    /// </summary>
    public static bool TryParseUnit(string? name, out EpochUnit? unit)
    {
        unit = null;
        switch (name?.Trim().ToLowerInvariant())
        {
            case "auto":
                return true;
            case "s" or "sec" or "secs" or "second" or "seconds" or "unix" or "epoch":
                unit = EpochUnit.Seconds; return true;
            case "ms" or "milli" or "millis" or "millisecond" or "milliseconds":
                unit = EpochUnit.Milliseconds; return true;
            case "us" or "µs" or "micro" or "micros" or "microsecond" or "microseconds":
                unit = EpochUnit.Microseconds; return true;
            case "ns" or "nano" or "nanos" or "nanosecond" or "nanoseconds":
                unit = EpochUnit.Nanoseconds; return true;
            case "tick" or "ticks":
                unit = EpochUnit.Ticks; return true;
            default:
                return false;
        }
    }

    /// <summary>Like <see cref="TryParseUnit"/>, but a misspelt unit is a mapping mistake and throws.</summary>
    public static EpochUnit? ParseUnit(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return TryParseUnit(name, out var unit)
            ? unit
            : throw new InvalidOperationException($"Unknown epoch unit '{name}'. Use s, ms, us, ns, ticks or auto.");
    }

    /// <summary>
    /// Guesses the unit from the size of the number, for the present era:
    /// below 1e11 is seconds, below 1e14 milliseconds, below 1e17 microseconds,
    /// anything larger nanoseconds.
    /// </summary>
    public static EpochUnit Detect(decimal value)
    {
        var magnitude = Math.Abs(value);
        return magnitude < 1e11m ? EpochUnit.Seconds
            : magnitude < 1e14m ? EpochUnit.Milliseconds
            : magnitude < 1e17m ? EpochUnit.Microseconds
            : EpochUnit.Nanoseconds;
    }

    /// <summary>Epoch number → UTC instant. A null unit auto-detects.</summary>
    public static bool TryFromEpoch(decimal value, EpochUnit? unit, out DateTimeOffset result)
    {
        result = default;
        var resolved = unit ?? Detect(value);

        try
        {
            if (resolved == EpochUnit.Ticks)
            {
                result = new DateTimeOffset(decimal.ToInt64(decimal.Truncate(value)), TimeSpan.Zero);
                return true;
            }

            var ticks = resolved switch
            {
                EpochUnit.Seconds => value * TimeSpan.TicksPerSecond,
                EpochUnit.Milliseconds => value * TimeSpan.TicksPerMillisecond,
                EpochUnit.Microseconds => value * TimeSpan.TicksPerMicrosecond,
                _ => value / 100m,
            };

            result = DateTimeOffset.UnixEpoch.AddTicks(decimal.ToInt64(decimal.Floor(ticks)));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
        {
            return false;
        }
    }

    /// <summary>UTC instant → epoch number, rounded down to a whole unit.</summary>
    public static bool TryToEpoch(DateTimeOffset value, EpochUnit unit, out long result)
    {
        var sinceUnix = value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;

        try
        {
            result = unit switch
            {
                EpochUnit.Seconds => FloorDiv(sinceUnix, TimeSpan.TicksPerSecond),
                EpochUnit.Milliseconds => FloorDiv(sinceUnix, TimeSpan.TicksPerMillisecond),
                EpochUnit.Microseconds => FloorDiv(sinceUnix, TimeSpan.TicksPerMicrosecond),
                EpochUnit.Nanoseconds => checked(sinceUnix * 100),
                _ => value.UtcTicks,
            };
            return true;
        }
        catch (OverflowException)
        {
            // Nanoseconds only fit a long between 1677 and 2262.
            result = 0;
            return false;
        }
    }

    // Integer division that rounds toward negative infinity, so an instant just
    // before 1970 is -1 s rather than 0 s.
    private static long FloorDiv(long value, long divisor)
    {
        var quotient = value / divisor;
        return value % divisor != 0 && value < 0 ? quotient - 1 : quotient;
    }
}
