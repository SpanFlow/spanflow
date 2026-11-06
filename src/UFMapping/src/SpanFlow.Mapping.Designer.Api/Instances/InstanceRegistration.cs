using System.Net.Http.Json;
using System.Reflection;
using Microsoft.Extensions.Options;

namespace SpanFlow.Mapping.Designer.Api.Instances;

/// <summary>
/// How this instance announces itself to DataFlow's registry (E28-T3), bound from the
/// <c>Instance</c> configuration section. Everything is optional: with no <see cref="DataFlowBaseUrl"/>
/// or no <see cref="RegistrationSecret"/> the service simply does not register, which is what a
/// single-instance install wants and is exactly how it behaved before this existed.
/// </summary>
public class InstanceRegistrationOptions
{
    public const string SectionName = "Instance";

    /// <summary>This instance's name; empty becomes <c>machine:pid</c>.</summary>
    public string? Id { get; set; }

    /// <summary>The pool this instance serves. A label DataFlow's assignments name.</summary>
    public string PoolKey { get; set; } = "default";

    /// <summary>
    /// Where a caller reaches this instance. Unlike a DataFlow instance, this one is worth setting:
    /// the whole point of registering a UFMapping instance is so something can call it, and a row
    /// without a URL says a process exists without saying how to reach it.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>DataFlow's base URL — where the registry lives. Empty disables registration.</summary>
    public string? DataFlowBaseUrl { get; set; }

    /// <summary>
    /// The shared secret DataFlow expects in <c>X-Instance-Secret</c> (its
    /// <c>Instance:RegistrationSecret</c>). Empty disables registration.
    /// </summary>
    public string? RegistrationSecret { get; set; }

    /// <summary>How often to check in. Must stay well under DataFlow's <c>Instance:DownAfter</c>.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>
/// Tells DataFlow this mapping instance is up, and keeps telling it (E28-T3).
///
/// <para>Heartbeats rather than a one-off registration, because health in that registry is
/// <b>derived from silence</b> — a process that dies cannot write down that it died, so the only
/// honest liveness signal is a recent check-in. Stopping cleanly says so explicitly, which is what
/// separates a deploy from a crash in the ops view.</para>
///
/// <para>Every failure here is a warning and never fatal. Not being listed is a reporting problem;
/// refusing to serve mappings over it would be an outage, and this service's actual job — running
/// the engine for the designer — does not depend on DataFlow being reachable at all.</para>
/// </summary>
public sealed class InstanceRegistrationService(
    IHttpClientFactory httpFactory,
    IOptions<InstanceRegistrationOptions> options,
    ILogger<InstanceRegistrationService> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    /// <summary>The role this service registers as, matching DataFlow's ServiceInstanceRoles.</summary>
    private const string Role = "ufmapping";

    private const string SecretHeader = "X-Instance-Secret";

    private readonly InstanceRegistrationOptions _options = options.Value;
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <summary>Unique per process: two instances on one host share a machine name but not a pid.</summary>
    private string InstanceId =>
        string.IsNullOrWhiteSpace(_options.Id)
            ? $"{Environment.MachineName}:{Environment.ProcessId}"
            : _options.Id.Trim();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.DataFlowBaseUrl)
            || string.IsNullOrWhiteSpace(_options.RegistrationSecret))
        {
            logger.LogInformation(
                "Instance registration is off (set Instance:DataFlowBaseUrl and Instance:RegistrationSecret "
                + "to have this instance appear in DataFlow's registry).");
            return;
        }

        var startedAt = _clock.GetUtcNow();
        var interval = _options.HeartbeatInterval > TimeSpan.Zero
            ? _options.HeartbeatInterval
            : TimeSpan.FromSeconds(15);

        while (!stoppingToken.IsCancellationRequested)
        {
            await SendAsync(startedAt, stopping: false, stoppingToken);

            try { await Task.Delay(interval, _clock, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        // On the way out, on an uncancelled token: this is precisely the write shutdown exists to make,
        // and skipping it would leave the instance reading as "down" until its heartbeat aged out.
        await SendAsync(startedAt, stopping: true, CancellationToken.None);
    }

    private async Task SendAsync(DateTimeOffset startedAt, bool stopping, CancellationToken ct)
    {
        try
        {
            var http = httpFactory.CreateClient("dataflow-registry");
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/internal/instances/heartbeat")
            {
                Content = JsonContent.Create(new
                {
                    id = InstanceId,
                    role = Role,
                    poolKey = _options.PoolKey,
                    url = _options.Url,
                    version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(),
                    startedAt,
                    stopping,
                }),
            };
            request.Headers.TryAddWithoutValidation(SecretHeader, _options.RegistrationSecret);

            var response = await http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                // 401 here means the two secrets disagree, which is worth saying plainly — it is the
                // one failure that never fixes itself by waiting.
                logger.LogWarning(
                    "DataFlow refused this instance's registration ({Status}). Check that Instance:RegistrationSecret "
                    + "matches DataFlow's.", (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not reach DataFlow's instance registry; will try again.");
        }
    }
}
