using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SpanFlow.Mapping.Designer.Api.Mapping;
using SpanFlow.Mapping.Engine;

namespace SpanFlow.Mapping.Designer.Api.Standalone;

/// <summary>
/// The standalone mode: the mapping designer and its engine, with nothing else. No sign-in, no Platform,
/// no message broker, no database, so it runs from <c>docker run</c> or <c>dotnet run</c> alone. Meant for
/// a developer's machine or a self-hosted install behind the owner's own network controls: every
/// endpoint is anonymous, so put it behind a reverse proxy with authentication before exposing it.
/// </summary>
internal static class StandaloneDesigner
{
    public const string CorsPolicy = "StandaloneCors";

    public static void AddServices(WebApplicationBuilder builder)
    {
        // Kestrel binds endpoints from its section by itself but not its limits; the same keys the Platform
        // mode reads (Kestrel:Limits), so MaxRequestBodySize caps a mapping test's sample payload here too.
        builder.WebHost.ConfigureKestrel(kestrel =>
            builder.Configuration.GetSection("Kestrel:Limits").Bind(kestrel.Limits));

        builder.Services.AddHealthChecks();
        builder.Services.AddProblemDetails();

        // The bundled UI is served by this same process, so it needs no CORS. Cors:Origins opens the API to a
        // UI served from somewhere else (a dev server, say).
        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
        builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
        {
            if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
        }));
    }

    public static void UsePipeline(WebApplication app)
    {
        // An unhandled error answers with a trace id the log line carries too, never an empty 500.
        app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
        {
            var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
            app.Logger.LogError(error, "Unhandled error (trace {TraceId})", traceId);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { Error = "Something went wrong.", TraceId = traceId });
        }));

        app.UseCors(CorsPolicy);
    }

    public static void MapEndpoints(WebApplication app, bool isDevelopment)
    {
        app.MapPost(MappingEndpoints.ExecutePath, ([FromBody] ExecutionRequest request, [FromServices] MappingEngine engine) =>
        {
            try
            {
                return Results.Ok(engine.Execute(request.Mapping, request.InputObject));
            }
            catch (Exception ex)
            {
                return MappingEndpoints.ExecutionFailed(ex, isDevelopment);
            }
        })
        .WithName("ExecuteMapping");

        MappingEndpoints.MapTrace(app);

        // Lets the UI (and a person with curl) tell which mode answered.
        app.MapGet("/api/designer/info", () => Results.Ok(new
        {
            mode = "standalone",
            version = typeof(StandaloneDesigner).Assembly.GetName().Version?.ToString(),
            engine = typeof(MappingEngine).Assembly.GetName().Version?.ToString(),
        }))
        .WithName("DesignerInfo");

        // An unknown API path is a 404, not the UI's index.html: a client that calls a route this mode does
        // not have (the Platform sign-in proxy, say) gets an answer it can read.
        app.Map("/api/{**rest}", () => Results.NotFound(new { Error = "No such endpoint in the standalone designer." }));
    }
}
