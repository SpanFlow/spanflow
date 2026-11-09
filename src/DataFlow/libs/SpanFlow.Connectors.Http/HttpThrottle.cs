using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace SpanFlow.Connectors.Http;

/// <summary>
/// How an api connection paces its outbound calls and reacts to HTTP 429, parsed from the
/// connection config's <c>"throttle"</c> object (E14-T6). Distinct from the E03 auth retry: that one
/// replays a request whose *token* went stale, this one waits because the server said "too fast".
/// </summary>
public sealed record ThrottlePolicy
{
    /// <summary>Client-side pacing, in requests per second. 0 (the default) = send as fast as asked.</summary>
    public double RequestsPerSecond { get; init; }

    /// <summary>Whether a 429 is waited out and replayed. On by default — a 429 is not a real failure.</summary>
    public bool RetryOn429 { get; init; } = true;

    /// <summary>How many times a single request may be replayed after a 429 before the 429 is returned.</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// The longest wait honoured between 429 replays. A <c>Retry-After</c> asking for longer than this
    /// is not waited out at all — the 429 is returned so the node's onError policy decides, rather than
    /// a run silently parking for an hour.
    /// </summary>
    public int MaxRetryDelaySeconds { get; init; } = 60;

    public static readonly ThrottlePolicy Default = new();

    public static ThrottlePolicy Parse(JsonObject? throttle)
    {
        if (throttle is null) return Default;
        return new ThrottlePolicy
        {
            RequestsPerSecond = ReadDouble(throttle["requestsPerSecond"], 0),
            RetryOn429 = throttle["retryOn429"]?.GetValue<bool>() ?? true,
            MaxRetries = (int)ReadDouble(throttle["maxRetries"], 3),
            MaxRetryDelaySeconds = (int)ReadDouble(throttle["maxRetryDelaySeconds"], 60),
        };
    }

    private static double ReadDouble(JsonNode? node, double fallback) =>
        node is not null && double.TryParse(node.ToString(), out var value) && value >= 0 ? value : fallback;

    /// <summary>
    /// How long to wait before replaying a 429, or null when it should not be replayed at all
    /// (retries exhausted, disabled, or the server asked for longer than <see cref="MaxRetryDelaySeconds"/>).
    /// A response without a usable <c>Retry-After</c> backs off exponentially (1s, 2s, 4s…), capped the same way.
    /// </summary>
    public TimeSpan? DelayFor(HttpResponseMessage response, int attempt, DateTimeOffset now)
    {
        if (!RetryOn429 || attempt >= MaxRetries) return null;

        var cap = TimeSpan.FromSeconds(MaxRetryDelaySeconds);
        if (RetryAfter(response, now) is TimeSpan asked)
        {
            return asked > cap ? null : asked;
        }

        var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt));
        return backoff > cap ? cap : backoff;
    }

    /// <summary>Reads <c>Retry-After</c> in either form: delta-seconds, or an HTTP-date.</summary>
    private static TimeSpan? RetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        // An unparseable header reads back as null here, so a server sending junk gets the backoff.
        var delta = response.Headers.RetryAfter switch
        {
            { Delta: TimeSpan d } => d,
            { Date: DateTimeOffset date } => date - now,
            _ => (TimeSpan?)null,
        };
        return delta is null || delta <= TimeSpan.Zero ? null : delta;
    }
}

/// <summary>
/// Paces outbound calls per throttle key so a pipeline stays inside an API's rate limit instead of
/// discovering it through 429s. Keyed by base URL rather than connection id — the connector is handed
/// a config, not an identity, and a rate limit belongs to the API anyway: two connections to the same
/// base URL share one budget, which is what the far end enforces.
/// <para>
/// Registered as a singleton so the whole instance shares the gates. It is an in-process limiter:
/// with N API instances each keeps its own budget (the same caveat as the E14-T1 worker until
/// leader election in E14-T7).
/// </para>
/// </summary>
public sealed class HttpThrottleRegistry(TimeProvider? timeProvider = null, IDelayScheduler? delayScheduler = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly IDelayScheduler _delay = delayScheduler ?? new RealDelayScheduler();
    private readonly ConcurrentDictionary<string, Gate> _gates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Waits until this key's next slot is due. A non-positive rate means no pacing.</summary>
    public async Task AcquireAsync(string key, double requestsPerSecond, CancellationToken ct)
    {
        if (requestsPerSecond <= 0) return;

        var interval = TimeSpan.FromSeconds(1 / requestsPerSecond);
        var wait = _gates.GetOrAdd(key, _ => new Gate()).Reserve(_time.GetUtcNow(), interval);
        await WaitAsync(wait, ct);
    }

    public DateTimeOffset UtcNow => _time.GetUtcNow();

    public Task WaitAsync(TimeSpan delay, CancellationToken ct) =>
        delay <= TimeSpan.Zero ? Task.CompletedTask : _delay.DelayAsync(delay, ct);

    /// <summary>The pacing key for a base URL — trailing slashes and case don't make a second budget.</summary>
    public static string KeyFor(string baseUrl) => baseUrl.TrimEnd('/');

    /// <summary>
    /// One key's slot reservation. Callers take the next free slot and hand back how long to wait for
    /// it, so concurrent nodes hitting the same API queue up instead of all sleeping the same interval
    /// and then firing together. A caller canceled while waiting leaves its slot unused (the next
    /// caller waits it out) — under-using a budget is the safe direction to err.
    /// </summary>
    private sealed class Gate
    {
        private readonly Lock _lock = new();
        private DateTimeOffset _nextSlot;

        public TimeSpan Reserve(DateTimeOffset now, TimeSpan interval)
        {
            lock (_lock)
            {
                var slot = _nextSlot > now ? _nextSlot : now;
                _nextSlot = slot + interval;
                return slot - now;
            }
        }
    }
}
