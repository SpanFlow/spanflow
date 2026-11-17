namespace SpanFlow.Libraries.Playground;

/// <summary>The four panels; each has its own limits and its own per-caller allowance (D71.5).</summary>
public enum Panel
{
    Expressions,
    Http,
    Files,
    Database,
}

/// <summary>
/// Where the playground runs (D71.11). <see cref="Hosted"/>: the vendor's public server; HTTP sends only to the
/// built-in demo API, databases are only the configured demo connections, files are formats only, limits come from
/// the Studio and every run is reported to it. <see cref="SelfHosted"/>: someone's own <c>docker run</c>; HTTP sends
/// anywhere that server reaches, people add their own connections and stores, limits come from configuration, and
/// nothing is reported anywhere. Behind an access key unless the owner opens it.
/// </summary>
public enum PlaygroundMode
{
    SelfHosted,
    Hosted,
}

/// <summary>One panel's limits. The defaults are the E71 defaults table.</summary>
public sealed class PanelLimits
{
    public int PerMinute { get; set; }
    public int PerHour { get; set; }
    public int MaxBodyBytes { get; set; }
    public int MaxOutputBytes { get; set; }

    /// <summary>Rows read or returned at most (expressions batch, files, database).</summary>
    public int MaxRows { get; set; }

    public int TimeoutMs { get; set; }

    public PanelLimits Clone() => (PanelLimits)MemberwiseClone();

    /// <summary>What is wrong with these values, empty when they are usable. Checked at start and on every update.</summary>
    public IEnumerable<string> Validate(string panel)
    {
        if (PerMinute is < 1 or > 10_000) yield return $"{panel}: PerMinute must be 1 to 10000.";
        if (PerHour < PerMinute || PerHour > 1_000_000) yield return $"{panel}: PerHour must be at least PerMinute and at most 1000000.";
        if (MaxBodyBytes is < 1024 or > 8 * 1024 * 1024) yield return $"{panel}: MaxBodyBytes must be 1 KB to 8 MB.";
        if (MaxOutputBytes is < 1024 or > 32 * 1024 * 1024) yield return $"{panel}: MaxOutputBytes must be 1 KB to 32 MB.";
        if (MaxRows is < 1 or > 100_000) yield return $"{panel}: MaxRows must be 1 to 100000.";
        if (TimeoutMs is < 50 or > 120_000) yield return $"{panel}: TimeoutMs must be 50 to 120000.";
    }
}

/// <summary>Every limit the playground enforces at one moment: from configuration, or pushed by the Studio (hosted).</summary>
public sealed class PlaygroundLimits
{
    /// <summary>Off: every panel answers 503 <c>disabled</c>; the page still loads and says so.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Calls running at once across every caller and panel; past it 503 <c>busy</c>, no queue.</summary>
    public int MaxConcurrent { get; set; } = 8;

    public PanelLimits Expressions { get; set; } = new() { PerMinute = 60, PerHour = 1000, MaxBodyBytes = 64 * 1024, MaxOutputBytes = 256 * 1024, MaxRows = 500, TimeoutMs = 1000 };
    public PanelLimits Http { get; set; } = new() { PerMinute = 30, PerHour = 500, MaxBodyBytes = 64 * 1024, MaxOutputBytes = 256 * 1024, MaxRows = 1, TimeoutMs = 10_000 };
    public PanelLimits Files { get; set; } = new() { PerMinute = 20, PerHour = 300, MaxBodyBytes = 512 * 1024, MaxOutputBytes = 2 * 1024 * 1024, MaxRows = 1000, TimeoutMs = 5000 };
    public PanelLimits Database { get; set; } = new() { PerMinute = 30, PerHour = 500, MaxBodyBytes = 32 * 1024, MaxOutputBytes = 512 * 1024, MaxRows = 200, TimeoutMs = 5000 };

    public PanelLimits For(Panel panel) => panel switch
    {
        Panel.Expressions => Expressions,
        Panel.Http => Http,
        Panel.Files => Files,
        Panel.Database => Database,
        _ => throw new ArgumentOutOfRangeException(nameof(panel)),
    };

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (MaxConcurrent is < 1 or > 256) errors.Add("MaxConcurrent must be 1 to 256.");
        foreach (var panel in Enum.GetValues<Panel>())
        {
            var limits = For(panel);
            if (limits is null) errors.Add($"{panel}: missing.");
            else errors.AddRange(limits.Validate(panel.ToString()));
        }
        return errors;
    }
}

/// <summary>A database connection given in configuration (both modes; in hosted mode the only kind).</summary>
public sealed class ConfiguredConnection
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "postgres";
    public string ConnectionString { get; set; } = "";

    /// <summary>Show the whole connection string on the page (the hosted demo database: public on purpose).</summary>
    public bool ShowConnectionString { get; set; }

    /// <summary>Writes are rolled back unless this is set (self-hosted only; the hosted demo is read only).</summary>
    public bool AllowWrites { get; set; }
}

/// <summary>A file store given in configuration (self-hosted): the library's store config, e.g. <c>{ "store": "s3", ... }</c>.</summary>
public sealed class ConfiguredStore
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>local, s3, azureBlob or ftp, and that store's own settings.</summary>
    public Dictionary<string, string> Config { get; set; } = [];
}

/// <summary>The <c>Playground</c> section.</summary>
public sealed class PlaygroundOptions
{
    public const string SectionName = "Playground";

    public PlaygroundMode Mode { get; set; } = PlaygroundMode.SelfHosted;

    /// <summary>
    /// Proxies whose <c>X-Forwarded-For</c> is believed, as addresses or CIDR ranges; <c>private</c> stands for the
    /// private ranges and loopback. Unset means <c>private</c> (a list default would be appended to by configuration
    /// binding, not replaced).
    /// </summary>
    public List<string>? TrustedProxies { get; set; }

    /// <summary>Shown on the page ("Powered by ..."). Configuration, so the page never hard-codes the product name.</summary>
    public string ProductName { get; set; } = "SpanFlow";

    public string ProductUrl { get; set; } = "https://spanflow.app";
    public string? SourceUrl { get; set; } = "https://github.com/SpanFlow/spanflow";
    public string NuGetPrefix { get; set; } = "SpanFlow";

    /// <summary>The limits in force from the start (self-hosted: always; hosted: until the Studio's first push).</summary>
    public PlaygroundLimits Limits { get; set; } = new();

    // ---- self-hosted ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Self-hosted: every API call needs this key (<c>X-Playground-Key</c>, or <c>?key=</c> once on the page). Empty: a
    /// key is made at start and written to the log. The playground sends requests and opens connections for whoever
    /// holds it, so it is never open by accident.
    /// </summary>
    public string? AccessKey { get; set; }

    /// <summary>Self-hosted: no key at all. Only for a server nobody else can reach.</summary>
    public bool AllowAnonymous { get; set; }

    /// <summary>Self-hosted: where saved connections, stores, collections and environments live (mount a volume there).</summary>
    public string DataFile { get; set; } = "data/playground.json";

    /// <summary>Self-hosted: folders a <c>local</c> store may point into.</summary>
    public List<string>? LocalFileRoots { get; set; }

    // ---- both ------------------------------------------------------------------------------------------------------

    /// <summary>Connections from configuration. Hosted: the demo database(s). Self-hosted: ones the owner pins.</summary>
    public List<ConfiguredConnection> Connections { get; set; } = [];

    /// <summary>File stores from configuration (self-hosted).</summary>
    public List<ConfiguredStore> Stores { get; set; } = [];

    // ---- hosted ------------------------------------------------------------------------------------------------------

    /// <summary>The demo API's host name. Hosted: the only host the HTTP panel sends to (answered in process).</summary>
    public string DemoApiHost { get; set; } = "demo-api.internal";

    /// <summary>
    /// Hosted: the port Platform syncs on (limits in, run records out). Never proxied by the front door; 0 turns the
    /// sync off. Platform holds the secret; this service holds none.
    /// </summary>
    public int SyncPort { get; set; }

    /// <summary>Hosted: a header the edge proxy sets with the caller's country (Cloudflare's <c>CF-IPCountry</c>).</summary>
    public string? CountryHeader { get; set; }

    public bool IsHosted => Mode == PlaygroundMode.Hosted;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>(Limits.Validate());
        if (SyncPort is < 0 or > 65535) errors.Add("SyncPort must be 0 to 65535.");
        if (IsHosted && Stores.Count > 0) errors.Add("Stores: the hosted playground converts formats only; remove the stores.");
        foreach (var c in Connections)
        {
            if (string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.ConnectionString)) errors.Add("Connections: each needs an Id and a ConnectionString.");
            if (IsHosted && c.AllowWrites) errors.Add($"Connections: '{c.Id}' allows writes; the hosted playground is read only.");
        }
        return errors;
    }
}
