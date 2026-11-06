using Microsoft.Extensions.Options;
using SpanFlow.Mapping.Designer.Api;
using SpanFlow.Mapping.Designer.Api.Diagnostics;
using SpanFlow.Mapping.Designer.Api.Standalone;
using SpanFlow.Mapping.Engine;
#if PLATFORM_INTEGRATION
using SpanFlow.Mapping.Designer.Api.PlatformMode;
#endif

var builder = WebApplication.CreateBuilder(args);

// Two modes (DesignerMode): Platform, the service the SpanFlow stack runs, and Standalone, the designer on its
// own with no sign-in and no dependencies. Platform:Enabled picks; see DesignerModes.Resolve. A build without
// Platform.Client (PlatformIntegration.props) has only the standalone mode.
var mode = DesignerModes.Resolve(builder.Configuration);

#if PLATFORM_INTEGRATION
if (mode == DesignerMode.Platform) PlatformDesigner.AddDiagnostics(builder);
#endif

builder.Services.AddOpenApi();

#if PLATFORM_INTEGRATION
if (mode == DesignerMode.Platform) PlatformDesigner.AddServices(builder);
else
#endif
StandaloneDesigner.AddServices(builder);

builder.Services.AddSingleton<MappingEngine>();

// Appear in DataFlow's instance registry (E28-T3), so the ops view can show this process and the
// resolver can choose between several of them. Off unless both the URL and the secret are set — a
// single-instance install needs none of it and behaves exactly as it did before.
builder.Services.Configure<SpanFlow.Mapping.Designer.Api.Instances.InstanceRegistrationOptions>(
    builder.Configuration.GetSection(SpanFlow.Mapping.Designer.Api.Instances.InstanceRegistrationOptions.SectionName));
builder.Services.AddHttpClient("dataflow-registry", (sp, http) =>
{
    var opts = sp.GetRequiredService<IOptions<SpanFlow.Mapping.Designer.Api.Instances.InstanceRegistrationOptions>>().Value;
    // A base address is required even when registration is off, because the factory builds the client
    // before the hosted service decides not to use it.
    var baseUrl = string.IsNullOrWhiteSpace(opts.DataFlowBaseUrl) ? "http://localhost" : opts.DataFlowBaseUrl;
    http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    // Short: a registry that is slow to answer must not hold a heartbeat open past its own interval.
    http.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<SpanFlow.Mapping.Designer.Api.Instances.InstanceRegistrationService>();

var app = builder.Build();

var isDevelopment = app.Environment.IsDevelopment();

#if PLATFORM_INTEGRATION
if (mode == DesignerMode.Platform) await PlatformDesigner.UsePipelineAsync(app);
else
#endif
StandaloneDesigner.UsePipeline(app);

if (isDevelopment)
{
    app.MapOpenApi();
}

// Serve the React SPA from wwwroot/ in production.
app.UseDefaultFiles();
app.UseStaticFiles();

// Liveness/readiness probes (E14-T8) are mapped near the end of this file, after the routes, and
// before the SPA fallback. They used to be two literals here that answered {"status":"Healthy"}
// unconditionally — true of "is the process alive?" and worth nothing for "should traffic come here?",
// since this service's readiness is entirely about settings that must match Platform's and a stub
// reports a mismatched secret as healthy. See Diagnostics/MappingDiagnostics.

#if PLATFORM_INTEGRATION
if (mode == DesignerMode.Platform) PlatformDesigner.MapEndpoints(app, isDevelopment);
else
#endif
StandaloneDesigner.MapEndpoints(app, isDevelopment);

app.MapMappingHealthEndpoints();

// Fallback: any non-API route returns index.html so React Router works. Mapped after the probes, or
// /readyz would be swallowed by it and answer with the SPA — which reads to a caller as "something is
// listening but it isn't this service", the one diagnosis that must not be wrong.
app.MapFallbackToFile("index.html");

#if PLATFORM_INTEGRATION
if (mode == DesignerMode.Platform)
{
    await PlatformDesigner.RunAsync(app);
    return;
}
#endif

app.Logger.LogInformation("SpanFlow mapping designer running standalone: no sign-in, every endpoint is open.");
await app.RunAsync();

/// <summary>The entry point, public so the tests' WebApplicationFactory can host it.</summary>
public partial class Program;
