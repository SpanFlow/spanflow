using System.Collections.Concurrent;

namespace SpanFlow.Libraries.Playground;

/// <summary>What the limiter decided about one call.</summary>
public sealed record Admission(string? Refusal, int Limit, int Remaining, int ResetSeconds, Admission.Slot? Lease)
{
    public bool Admitted => Refusal is null;

    /// <summary>A running call's place in the concurrency count. Released once, when the work really ends (D71.7).</summary>
    public sealed class Slot(CallLimiter owner) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) owner.Release();
        }
    }
}

/// <summary>
/// Per caller and panel, fixed windows of a minute and an hour; one server-wide concurrency count. The E69 limiter's
/// design (UFMapping's HostedCallLimiter): in memory, one instance, a refused call spends nothing, and only this
/// hour's callers are held.
/// </summary>
public sealed class CallLimiter(TimeProvider clock)
{
    private readonly ConcurrentDictionary<(Panel, string), Windows> _callers = new();
    private int _running;
    private long _nextSweepTicks;

    private sealed class Windows
    {
        public long MinuteStart;
        public int MinuteCount;
        public long HourStart;
        public int HourCount;
    }

    /// <summary>Calls running now, across all callers and panels.</summary>
    public int Running => Volatile.Read(ref _running);

    /// <summary>Callers held now (for tests: the table must not grow past the hour).</summary>
    internal int Tracked => _callers.Count;

    public Admission TryAdmit(Panel panel, string caller, PanelLimits limits, int maxConcurrent)
    {
        var now = clock.GetUtcNow();
        Sweep(now);

        // Concurrency first: a busy server refuses without spending the caller's allowance.
        if (Interlocked.Increment(ref _running) > maxConcurrent)
        {
            Interlocked.Decrement(ref _running);
            return new Admission(Reasons.Busy, limits.PerMinute, 0, 1, null);
        }

        var minute = Floor(now, TimeSpan.FromMinutes(1));
        var hour = Floor(now, TimeSpan.FromHours(1));
        var windows = _callers.GetOrAdd((panel, caller), _ => new Windows());
        lock (windows)
        {
            if (windows.MinuteStart != minute) (windows.MinuteStart, windows.MinuteCount) = (minute, 0);
            if (windows.HourStart != hour) (windows.HourStart, windows.HourCount) = (hour, 0);

            var minuteReset = Seconds(now, minute, TimeSpan.FromMinutes(1));
            if (windows.HourCount >= limits.PerHour)
            {
                Interlocked.Decrement(ref _running);
                return new Admission(Reasons.RateLimited, limits.PerHour, 0, Seconds(now, hour, TimeSpan.FromHours(1)), null);
            }

            if (windows.MinuteCount >= limits.PerMinute)
            {
                Interlocked.Decrement(ref _running);
                return new Admission(Reasons.RateLimited, limits.PerMinute, 0, minuteReset, null);
            }

            windows.MinuteCount++;
            windows.HourCount++;
            return new Admission(null, limits.PerMinute, limits.PerMinute - windows.MinuteCount, minuteReset, new Admission.Slot(this));
        }
    }

    internal void Release() => Interlocked.Decrement(ref _running);

    private static long Floor(DateTimeOffset now, TimeSpan window) => now.UtcTicks - now.UtcTicks % window.Ticks;

    private static int Seconds(DateTimeOffset now, long windowStart, TimeSpan window) =>
        Math.Max(1, (int)Math.Ceiling(TimeSpan.FromTicks(windowStart + window.Ticks - now.UtcTicks).TotalSeconds));

    /// <summary>Drops callers whose hour has passed, at most once a minute.</summary>
    private void Sweep(DateTimeOffset now)
    {
        var next = Interlocked.Read(ref _nextSweepTicks);
        if (now.UtcTicks < next || Interlocked.CompareExchange(ref _nextSweepTicks, now.UtcTicks + TimeSpan.TicksPerMinute, next) != next) return;
        var hour = Floor(now, TimeSpan.FromHours(1));
        foreach (var (key, windows) in _callers)
        {
            if (Volatile.Read(ref windows.HourStart) < hour) _callers.TryRemove(key, out _);
        }
    }
}

/// <summary>The <c>reason</c> of a refusal: E69's set plus <c>timeout</c>.</summary>
public static class Reasons
{
    public const string Ok = "ok";
    public const string Invalid = "invalid";
    public const string TooLarge = "too_large";
    public const string Failed = "failed";
    public const string Timeout = "timeout";
    public const string RateLimited = "rate_limited";
    public const string Busy = "busy";
    public const string Disabled = "disabled";
}
