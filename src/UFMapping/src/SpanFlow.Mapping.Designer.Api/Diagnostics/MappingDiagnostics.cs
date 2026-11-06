using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Designer.Api.Diagnostics;

/// <summary>
/// This service's liveness/readiness probes, used in both modes. The Platform mode's startup checks
/// (what /readyz reports there) are in Platform/MappingDiagnostics.Platform.cs; the standalone mode
/// depends on nothing outside the process, so its /readyz has no checks and is ready once it answers.
/// </summary>
public static partial class MappingDiagnostics
{
    /// <summary>Checks tagged this way run for readiness only, never for liveness.</summary>
    public const string ReadyTag = "ready";

    /// <summary>
    /// Two endpoints, because an orchestrator asks two different questions: <c>/healthz</c> is "is this
    /// process alive?" and checks nothing external; <c>/readyz</c> is "should traffic come here?".
    /// Both anonymous — a probe carries no credentials, and neither reveals anything a caller could not
    /// learn by sending a request. The admin UI's service-status screen reads /readyz too, which is why
    /// this service needs them at all: without them, "no answer" from here is ambiguous between "not
    /// running" and "running, but has no such endpoint".
    /// </summary>
    public static void MapMappingHealthEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/healthz", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteAsync,
        }).AllowAnonymous();

        app.MapHealthChecks("/readyz", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteAsync,
        }).AllowAnonymous();
    }

    /// <summary>
    /// Writes a probe result as JSON, in the same shape Platform and DataFlow use — the admin UI reads
    /// all three with one parser. Built from typed values and written with a plain
    /// <see cref="Utf8JsonWriter"/> rather than the serializer, matching the other two services.
    /// </summary>
    private static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        var checks = new JsonObject();
        foreach (var (name, entry) in report.Entries)
        {
            var check = new JsonObject
            {
                ["status"] = entry.Status.ToString(),
                ["durationMs"] = Math.Round(entry.Duration.TotalMilliseconds, 1),
            };
            if (!string.IsNullOrWhiteSpace(entry.Description)) check["description"] = entry.Description;
            checks[name] = check;
        }

        var body = new JsonObject
        {
            ["status"] = report.Status.ToString(),
            ["totalDurationMs"] = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
        };
        if (checks.Count > 0) body["checks"] = checks;

        context.Response.ContentType = "application/json";
        await using var writer = new Utf8JsonWriter(context.Response.BodyWriter);
        body.WriteTo(writer);
        await writer.FlushAsync(context.RequestAborted);
    }
}
