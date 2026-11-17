using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SpanFlow.Libraries.Playground;

/// <summary>
/// The limits in force now. Self-hosted: configuration, for good. Hosted: configuration until Platform's first sync,
/// then whatever a platform admin last saved in the Studio (D71.5). A bad push is refused and the current limits stay.
/// </summary>
public sealed class LimitsSource(IOptions<PlaygroundOptions> options)
{
    private readonly bool _openByDeployment = options.Value.Limits.Enabled;
    private volatile PlaygroundLimits _current = Copy(options.Value.Limits);

    public PlaygroundLimits Current => _current;

    /// <summary>When the Studio's limits last arrived; null while on configuration.</summary>
    public DateTimeOffset? PushedAt { get; private set; }

    /// <summary>Replaces the limits; returns the problems instead when they are not usable.</summary>
    public IReadOnlyList<string> Apply(PlaygroundLimits limits, DateTimeOffset at)
    {
        var problems = limits.Validate();
        if (problems.Count > 0) return problems;
        var copy = Copy(limits);
        // The deployment's switch (Playground__Limits__Enabled, compose LIBRARIES_PLAYGROUND_PUBLIC) is a hard off: the
        // Studio can close an open playground, never open one the installation did not open (D71.9).
        copy.Enabled &= _openByDeployment;
        _current = copy;
        PushedAt = at;
        return [];
    }

    private static PlaygroundLimits Copy(PlaygroundLimits limits) =>
        JsonSerializer.Deserialize<PlaygroundLimits>(JsonSerializer.Serialize(limits))!;
}
