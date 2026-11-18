using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using SpanFlow.Mapping.Core.Models;
using SpanFlow.Mapping.Engine;
using SpanFlow.Mapping.Formats;

namespace SpanFlow.Mapping.Designer.Api.Mapping;

/// <summary>Mapping endpoints and contracts shared by both modes. The caller adds the auth requirement its mode needs.</summary>
internal static class MappingEndpoints
{
    public const string ExecutePath = "/api/mapping/execute";
    public const string TracePath = "/api/mapping/trace";

    /// <summary>E73: raw input in, raw output out, with the JSON in the middle kept for the preview.</summary>
    public const string RunPath = "/api/mapping/run";

    /// <summary>E73: only the decode step: the sample "seen as JSON", which the canvas reads its fields from.</summary>
    public const string DecodePath = "/api/mapping/decode";

    /// <summary>E73: every format and its options, so the designer draws the option forms from the codecs themselves.</summary>
    public const string FormatsPath = "/api/mapping/formats";

    /// <summary>E73-T9: the bundled starter layouts (ISO 8583, the positional example) to copy into a mapping side.</summary>
    public const string StartersPath = "/api/mapping/formats/starters";

    /// <summary>E73-T11: the bundled EDI guides (names for the edi format's guides option).</summary>
    public const string EdiGuidesPath = "/api/mapping/formats/edi-guides";

    /// <summary>
    /// Debug trace: evaluate one probe mapping per node and time each, so the designer can show what every
    /// node produced (and how long it took). With <c>rawInput</c> (+ <c>inputFormat</c>) the sample is decoded once
    /// and every probe reads the decoded JSON.
    /// </summary>
    public static RouteHandlerBuilder MapTrace(IEndpointRouteBuilder app) =>
        app.MapPost(TracePath, ([FromBody] TraceRequest request, [FromServices] MappingEngine engine, [FromServices] MappingRunner runner) =>
        {
            JsonNode? input;
            try
            {
                input = ResolveInput(runner, request.InputFormat, request.RawInput, request.InputObject);
            }
            catch (MappingFormatException ex)
            {
                return FormatFailed(ex);
            }

            var results = new List<object>();
            foreach (var probe in request.Probes)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var output = engine.Execute(probe.Mapping, input?.DeepClone());
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

    public static RouteHandlerBuilder MapRun(IEndpointRouteBuilder app, bool isDevelopment) =>
        app.MapPost(RunPath, ([FromBody] RunRequest request, [FromServices] MappingRunner runner) =>
        {
            try
            {
                var result = request.RawInput is not null
                    ? runner.Run(request.Mapping, request.RawInput)
                    : runner.RunDecoded(request.Mapping, request.InputObject);
                return Results.Ok(Describe(result));
            }
            catch (Exception ex)
            {
                return ExecutionFailed(ex, isDevelopment);
            }
        })
        .WithName("RunMapping");

    public static RouteHandlerBuilder MapDecode(IEndpointRouteBuilder app) =>
        app.MapPost(DecodePath, ([FromBody] DecodeRequest request, [FromServices] MappingRunner runner) =>
        {
            try
            {
                return Results.Ok(new { format = request.Format?.Type ?? FormatRegistry.DefaultType, decoded = runner.Decode(request.Format, FormatPayload.FromText(request.RawInput)) });
            }
            catch (MappingFormatException ex)
            {
                return FormatFailed(ex);
            }
        })
        .WithName("DecodeSample");

    public static RouteHandlerBuilder MapFormats(IEndpointRouteBuilder app) =>
        app.MapGet(FormatsPath, ([FromServices] MappingRunner runner) => Results.Ok(DescribeFormats(runner.Formats)))
        .WithName("MappingFormats");

    public static RouteHandlerBuilder MapStarters(IEndpointRouteBuilder app) =>
        app.MapGet(StartersPath, () => Results.Ok(DescribeStarters()))
        .WithName("MappingFormatStarters");

    public static RouteHandlerBuilder MapEdiGuides(IEndpointRouteBuilder app) =>
        app.MapGet(EdiGuidesPath, () => Results.Ok(EdiGuides.Bundled.Select(g => new { name = g.Name, standard = g.Standard, message = g.Message, version = g.Version, title = g.Title }).ToList()))
        .WithName("MappingEdiGuides");

    public static object DescribeStarters() =>
        StarterLayouts.All.Select(s => new { name = s.Name, title = s.Title, format = s.Format, description = s.Description, options = s.Options }).ToList();

    /// <summary>The run's answer: the output as text with its media type, and the JSON before and after mapping.</summary>
    public static object Describe(MappingRunResult result) => new
    {
        inputFormat = result.InputFormat,
        outputFormat = result.OutputFormat,
        contentType = result.ContentType,
        output = result.OutputText,
        decoded = result.Decoded,
        mapped = result.Mapped,
    };

    public static object DescribeFormats(FormatRegistry formats) =>
        formats.Codecs.OrderBy(c => c.Type, StringComparer.Ordinal).Select(codec => new
        {
            type = codec.Type,
            contentType = codec.ContentType,
            options = codec.Options
                .Select(o => new { name = o.Name, kind = Camel(o.Kind.ToString()), use = Camel(o.Use.ToString()), description = o.Description })
                .Append(new { name = FormatContext.EncodingOption, kind = "string", use = "both", description = "Text encoding of the raw data, e.g. utf-8 (default), utf-16, iso-8859-1, ebcdic (IBM037) or another code page." }),
        }).ToList();

    /// <summary>The input a run reads: the raw sample decoded with its format, else the JSON given as is.</summary>
    public static JsonNode? ResolveInput(MappingRunner runner, FormatSpec? format, string? rawInput, JsonNode? inputObject) =>
        rawInput is not null ? runner.Decode(format, FormatPayload.FromText(rawInput)) : inputObject;

    /// <summary>The 400 a failed execution answers with, in both modes. The designer reads <c>Error</c>.</summary>
    public static IResult ExecutionFailed(Exception ex, bool isDevelopment) =>
        ex is MappingFormatException format
            ? FormatFailed(format)
            : Results.BadRequest(new
            {
                Error = ex.Message,
                StackTrace = isDevelopment ? ex.StackTrace : null
            });

    /// <summary>A format refusal, with where it happened so the designer can point at the line, offset or field.</summary>
    public static IResult FormatFailed(MappingFormatException ex) =>
        Results.BadRequest(new
        {
            Error = ex.Message,
            Format = ex.Format,
            Direction = Camel(ex.Direction.ToString()),
            Location = ex.Location is null ? null : new
            {
                ex.Location.Line,
                ex.Location.Column,
                ex.Location.Offset,
                ex.Location.Segment,
                ex.Location.Field,
            },
        });

    private static string Camel(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);
}

public class ExecutionRequest
{
    public required MappingDefinition Mapping { get; set; }
    public required JsonNode InputObject { get; set; }
}

/// <summary>E73: <c>rawInput</c> (text in the mapping's input format) or <c>inputObject</c> (JSON already).</summary>
public class RunRequest
{
    public required MappingDefinition Mapping { get; set; }
    public string? RawInput { get; set; }
    public JsonNode? InputObject { get; set; }
}

public class DecodeRequest
{
    /// <summary>The input format; json when left out.</summary>
    public FormatSpec? Format { get; set; }
    public required string RawInput { get; set; }
}

public class TraceRequest
{
    /// <summary>The input as JSON. Left out when <see cref="RawInput"/> is sent.</summary>
    public JsonNode? InputObject { get; set; }

    /// <summary>E73: the sample as text in <see cref="InputFormat"/>; decoded once, then every probe reads it.</summary>
    public string? RawInput { get; set; }

    public FormatSpec? InputFormat { get; set; }

    public List<TraceProbe> Probes { get; set; } = [];
}

public class TraceProbe
{
    public required string Id { get; set; }
    public required MappingDefinition Mapping { get; set; }
}
