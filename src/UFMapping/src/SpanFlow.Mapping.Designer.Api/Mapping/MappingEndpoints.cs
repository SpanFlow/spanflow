using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using SpanFlow.Mapping.Core.Models;
using SpanFlow.Mapping.Engine;

namespace SpanFlow.Mapping.Designer.Api.Mapping;

/// <summary>Mapping endpoints and contracts shared by both modes.</summary>
internal static class MappingEndpoints
{
    public const string ExecutePath = "/api/mapping/execute";
    public const string TracePath = "/api/mapping/trace";

    /// <summary>
    /// Debug trace: evaluate one probe mapping per node and time each, so the designer can show what every
    /// node produced (and how long it took). The caller adds the auth requirement its mode needs.
    /// </summary>
    public static RouteHandlerBuilder MapTrace(IEndpointRouteBuilder app) =>
        app.MapPost(TracePath, ([FromBody] TraceRequest request, [FromServices] MappingEngine engine) =>
        {
            var results = new List<object>();
            foreach (var probe in request.Probes)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var output = engine.Execute(probe.Mapping, request.InputObject);
                    sw.Stop();
                    results.Add(new { id = probe.Id, output, elapsedMs = sw.Elapsed.TotalMilliseconds });
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    results.Add(new { id = probe.Id, output = (JsonNode?)null, elapsedMs = sw.Elapsed.TotalMilliseconds, error = ex.Message });
                }
            }

            return Results.Ok(results);
        })
        .WithName("TraceMapping");

    /// <summary>The 400 a failed execution answers with, in both modes. The designer reads <c>Error</c>.</summary>
    public static IResult ExecutionFailed(Exception ex, bool isDevelopment) =>
        Results.BadRequest(new
        {
            Error = ex.Message,
            StackTrace = isDevelopment ? ex.StackTrace : null
        });
}

public class ExecutionRequest
{
    public required MappingDefinition Mapping { get; set; }
    public required JsonNode InputObject { get; set; }
}

public class TraceRequest
{
    public required JsonNode InputObject { get; set; }
    public List<TraceProbe> Probes { get; set; } = [];
}

public class TraceProbe
{
    public required string Id { get; set; }
    public required MappingDefinition Mapping { get; set; }
}
