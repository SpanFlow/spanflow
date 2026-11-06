namespace SpanFlow.Mapping.Designer.Api;

/// <summary>The two ways the designer API runs.</summary>
public enum DesignerMode
{
    /// <summary>
    /// One service of the SpanFlow stack: Platform sign-in, API keys, dynamic CORS, queue logging,
    /// consumption recording. Needs Platform (and whatever Platform's messaging is configured for).
    /// </summary>
    Platform,

    /// <summary>
    /// The designer on its own, for local or self-hosted use: no sign-in, no Platform, no message broker,
    /// no database. Design mappings in the bundled UI and execute them.
    /// </summary>
    Standalone,
}

public static class DesignerModes
{
    /// <summary>Whether this build has the Platform integration compiled in (PlatformIntegration.props).</summary>
    public static bool PlatformCompiledIn { get; } =
#if PLATFORM_INTEGRATION
        true;
#else
        false;
#endif

    /// <summary>
    /// <c>Platform:Enabled</c> decides when it is set. Unset, the mode is Platform when the build has the
    /// integration and a <c>Platform:BaseUrl</c> is configured (which is how every existing stack install
    /// runs, so nothing changes for them), and Standalone otherwise.
    /// </summary>
    public static DesignerMode Resolve(IConfiguration configuration)
    {
        var enabled = configuration.GetValue<bool?>("Platform:Enabled");
        if (enabled == true && !PlatformCompiledIn)
        {
            throw new InvalidOperationException(
                "Platform:Enabled is true, but this build of the designer has no Platform integration " +
                "(it was built without Platform.Client). Remove the setting to run standalone, or build with " +
                "-p:PlatformIntegration=true next to the Platform repository.");
        }

        if (enabled is bool explicitly) return explicitly ? DesignerMode.Platform : DesignerMode.Standalone;

        return PlatformCompiledIn && !string.IsNullOrWhiteSpace(configuration["Platform:BaseUrl"])
            ? DesignerMode.Platform
            : DesignerMode.Standalone;
    }
}
