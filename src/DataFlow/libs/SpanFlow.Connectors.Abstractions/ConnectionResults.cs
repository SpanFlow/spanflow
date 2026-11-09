namespace SpanFlow.Connectors;

/// <summary>The outcome of a cheap reachability or validity check of a connection's config.</summary>
/// <param name="Ok">True when the connection works.</param>
/// <param name="Detail">What was checked, or why it failed.</param>
public record ConnectionCheck(bool Ok, string? Detail = null);

/// <summary>The outcome of delivering a payload to an external system.</summary>
/// <param name="Success">True when the system accepted it.</param>
/// <param name="Detail">What happened, or why it failed.</param>
public record DeliveryResult(bool Success, string? Detail = null);
