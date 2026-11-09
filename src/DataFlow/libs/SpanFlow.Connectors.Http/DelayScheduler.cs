namespace SpanFlow.Connectors.Http;

/// <summary>Abstracts retry backoff waits so tests can run without real time.</summary>
public interface IDelayScheduler
{
    Task DelayAsync(TimeSpan delay, CancellationToken ct);
}

public sealed class RealDelayScheduler : IDelayScheduler
{
    public Task DelayAsync(TimeSpan delay, CancellationToken ct) =>
        delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, ct);
}
