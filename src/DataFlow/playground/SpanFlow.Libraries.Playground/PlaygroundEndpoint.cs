using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace SpanFlow.Libraries.Playground;

/// <summary>A panel's own refusal (bad input, too much of it, or the library said no).</summary>
public sealed class PanelRefusal(int status, string reason, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Reason { get; } = reason;

    public static PanelRefusal Invalid(string message) => new(StatusCodes.Status400BadRequest, Reasons.Invalid, message);
    public static PanelRefusal TooLarge(string message) => new(StatusCodes.Status400BadRequest, Reasons.TooLarge, message);
    public static PanelRefusal Failed(string message) => new(StatusCodes.Status400BadRequest, Reasons.Failed, message);
    public static PanelRefusal NotFound(string message) => new(StatusCodes.Status404NotFound, Reasons.Invalid, message);
}

/// <summary>What a panel's work gets: the parsed body, its limits, the mode, and a token that fires at the time cap.</summary>
public sealed record PanelCall(JsonObject Body, PanelLimits Limits, PlaygroundOptions Options, IServiceProvider Services, CancellationToken Cancellation)
{
    public bool Hosted => Options.IsHosted;
}

/// <summary>
/// Every panel route goes through <see cref="HandleAsync"/>: the checks of D71.6, cheapest first, then the panel's
/// work on its own thread under the time cap (D71.7), then the answer's size. Refusals are E69's
/// <c>{ "error", "reason" }</c> with <c>Retry-After</c> and <c>RateLimit-*</c> headers. Hosted, every call is recorded
/// for the Studio, refused or not (D71.12).
/// </summary>
public static class PlaygroundEndpoint
{
    /// <summary>The work's thread stack: deep enough that no input the caps allow can overflow it.</summary>
    internal const int WorkStackBytes = 16 * 1024 * 1024;

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web) { WriteIndented = false };

    public static async Task<IResult> HandleAsync(Panel panel, string action, HttpContext http, Func<PanelCall, Task<JsonNode>> work)
    {
        var services = http.RequestServices;
        var options = services.GetRequiredService<IOptions<PlaygroundOptions>>().Value;
        var all = services.GetRequiredService<LimitsSource>().Current;
        var limiter = services.GetRequiredService<CallLimiter>();
        var recorder = services.GetRequiredService<RunRecorder>();
        var limits = all.For(panel);
        var caller = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var clock = Stopwatch.StartNew();

        JsonObject? body = null;
        JsonNode? answer = null;
        (int Status, string Reason, string Message)? refusal = null;
        try
        {
            if (!all.Enabled)
            {
                refusal = (StatusCodes.Status503ServiceUnavailable, Reasons.Disabled, "The libraries playground is switched off on this server.");
                return Refuse(refusal.Value);
            }

            var admission = limiter.TryAdmit(panel, caller, limits, all.MaxConcurrent);
            SetRateLimitHeaders(http, admission);
            if (!admission.Admitted)
            {
                http.Response.Headers.RetryAfter = admission.ResetSeconds.ToString(CultureInfo.InvariantCulture);
                refusal = admission.Refusal == Reasons.Busy
                    ? (StatusCodes.Status503ServiceUnavailable, Reasons.Busy, "Too many calls are running. Try again in a moment.")
                    : (StatusCodes.Status429TooManyRequests, Reasons.RateLimited,
                        $"Rate limit reached for this panel ({limits.PerMinute} calls a minute, {limits.PerHour} an hour). Try again in {admission.ResetSeconds} s.");
                return Refuse(refusal.Value);
            }

            var lease = admission.Lease!;
            var handedOver = false;
            try
            {
                (body, var bodyRefusal) = await ReadBodyAsync(http, limits);
                if (bodyRefusal is not null)
                {
                    refusal = bodyRefusal;
                    return Refuse(refusal.Value);
                }

                handedOver = true;
                var outcome = await RunAsync(new PanelCall(body!, limits, options, services, default), limits, lease, work);
                if (outcome.Refusal is not null)
                {
                    refusal = outcome.Refusal;
                    return Refuse(refusal.Value);
                }

                answer = outcome.Answer;
                var json = answer!.ToJsonString(JsonOptions);
                if (Encoding.UTF8.GetByteCount(json) > limits.MaxOutputBytes)
                {
                    answer = null;
                    refusal = (StatusCodes.Status422UnprocessableEntity, Reasons.TooLarge,
                        $"The answer is larger than {limits.MaxOutputBytes:N0} bytes, the limit for this panel.");
                    return Refuse(refusal.Value);
                }
                return Results.Content(json, "application/json", Encoding.UTF8);
            }
            finally
            {
                // Once the work has started, its own thread releases the slot when it really ends (D71.7).
                if (!handedOver) lease.Dispose();
            }
        }
        finally
        {
            if (recorder.Enabled)
            {
                var (run, bytes) = body is null && answer is null ? ((string?)null, 0) : RunRecorder.Describe(body, answer, refusal?.Message);
                recorder.Record(new PlaygroundCall
                {
                    OccurredAt = DateTimeOffset.UtcNow,
                    ClientAddress = caller,
                    UserAgent = http.Request.Headers.UserAgent.ToString(),
                    Country = options.CountryHeader is { Length: > 0 } header && http.Request.Headers[header].ToString() is { Length: 2 } country ? country : null,
                    Path = $"/{panel.ToString().ToLowerInvariant()}/{action}",
                    Outcome = refusal?.Reason ?? Reasons.Ok,
                    Run = run,
                    RunBytes = run is null ? null : bytes,
                    Error = refusal?.Message,
                    DurationMs = (int)clock.ElapsedMilliseconds,
                });
            }
        }
    }

    private sealed record Outcome(JsonNode? Answer, (int Status, string Reason, string Message)? Refusal);

    /// <summary>
    /// The panel's work on a thread of its own with a large stack. At the time cap the caller is answered 422
    /// <c>timeout</c> and the token fires; the slot stays taken until the work returns.
    /// </summary>
    private static async Task<Outcome> RunAsync(PanelCall call, PanelLimits limits, Admission.Slot lease, Func<PanelCall, Task<JsonNode>> work)
    {
        var cts = new CancellationTokenSource(limits.TimeoutMs);
        var done = new TaskCompletionSource<Outcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var answer = work(call with { Cancellation = cts.Token }).GetAwaiter().GetResult();
                done.TrySetResult(new Outcome(answer, null));
            }
            catch (PanelRefusal refusal)
            {
                done.TrySetResult(new Outcome(null, (refusal.Status, refusal.Reason, refusal.Message)));
            }
            catch (OperationCanceledException)
            {
                // Only the time cap cancels anything here (the token, or SQLite's progress handler).
                done.TrySetResult(new Outcome(null, TimedOut(limits)));
            }
            catch (Exception ex)
            {
                // A library's own message is the useful part of the playground; it never carries a stack trace.
                done.TrySetResult(new Outcome(null, (StatusCodes.Status400BadRequest, Reasons.Failed, ex.Message)));
            }
            finally
            {
                lease.Dispose();
                cts.Dispose();
            }
        }, WorkStackBytes)
        {
            IsBackground = true,
            Name = "playground-work",
        };
        thread.Start();

        var finished = await Task.WhenAny(done.Task, Task.Delay(limits.TimeoutMs + 100));
        return finished == done.Task ? await done.Task : new Outcome(null, TimedOut(limits));
    }

    private static (int, string, string) TimedOut(PanelLimits limits) =>
        (StatusCodes.Status422UnprocessableEntity, Reasons.Timeout, $"Stopped after {limits.TimeoutMs:N0} ms, the time limit for this panel.");

    /// <summary>Content type, size and JSON checks (D71.6), E69's order.</summary>
    private static async Task<(JsonObject? Body, (int, string, string)? Refusal)> ReadBodyAsync(HttpContext http, PanelLimits limits)
    {
        if (http.Request.ContentType is not { } contentType || !contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            return (null, (StatusCodes.Status415UnsupportedMediaType, Reasons.Invalid, "Send the request as application/json."));

        if (http.Request.ContentLength > limits.MaxBodyBytes) return (null, TooLargeInput(limits));

        // The request's own cap, so a body without a Content-Length is cut off at the same size.
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } cap) cap.MaxRequestBodySize = limits.MaxBodyBytes;

        try
        {
            using var buffer = new MemoryStream();
            await http.Request.Body.CopyToAsync(buffer, http.RequestAborted);
            if (buffer.Length > limits.MaxBodyBytes) return (null, TooLargeInput(limits));
            var node = JsonNode.Parse(buffer.ToArray(), documentOptions: new JsonDocumentOptions { MaxDepth = 64 });
            return node is JsonObject body
                ? (body, null)
                : (null, (StatusCodes.Status400BadRequest, Reasons.Invalid, "The body must be a JSON object."));
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return (null, TooLargeInput(limits));
        }
        catch (JsonException)
        {
            return (null, (StatusCodes.Status400BadRequest, Reasons.Invalid, "The body is not valid JSON (or nests deeper than 64 levels)."));
        }
    }

    private static (int, string, string) TooLargeInput(PanelLimits limits) =>
        (StatusCodes.Status413PayloadTooLarge, Reasons.TooLarge, $"The request is larger than {limits.MaxBodyBytes:N0} bytes, the limit for this panel.");

    private static IResult Refuse((int Status, string Reason, string Message) refusal) => Refuse(refusal.Status, refusal.Reason, refusal.Message);

    internal static IResult Refuse(int status, string reason, string message) =>
        Results.Json(new { error = message, reason }, statusCode: status);

    /// <summary>The IETF draft RateLimit headers, for the minute window (the one a caller meets first).</summary>
    private static void SetRateLimitHeaders(HttpContext http, Admission admission)
    {
        var headers = http.Response.Headers;
        headers["RateLimit-Limit"] = admission.Limit.ToString(CultureInfo.InvariantCulture);
        headers["RateLimit-Remaining"] = Math.Max(0, admission.Remaining).ToString(CultureInfo.InvariantCulture);
        headers["RateLimit-Reset"] = admission.ResetSeconds.ToString(CultureInfo.InvariantCulture);
    }

    // ---- helpers the panels share ----------------------------------------------------------------------------

    internal static string RequireString(JsonObject body, string name, int maxLength)
    {
        if (body[name] is not JsonValue value || !value.TryGetValue<string>(out var text))
            throw PanelRefusal.Invalid($"Send \"{name}\" as a string.");
        if (text.Length > maxLength)
            throw PanelRefusal.TooLarge($"\"{name}\" is {text.Length:N0} characters; the limit is {maxLength:N0}.");
        return text;
    }

    internal static string? OptionalString(JsonObject body, string name)
    {
        if (body[name] is null) return null;
        if (body[name] is JsonValue value && value.TryGetValue<string>(out var text)) return text;
        throw PanelRefusal.Invalid($"\"{name}\" must be a string.");
    }
}
