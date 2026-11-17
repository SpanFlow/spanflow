using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace SpanFlow.Libraries.Playground;

/// <summary>
/// Self-hosted: the key every <c>/api</c> call needs (D71.13). The self-hosted playground sends requests and opens
/// connections from the owner's network for whoever can call it, so it is never open by accident: no configured key
/// means one is made at start and written to the log. <c>AllowAnonymous</c> turns it off. Hosted: no key; the hosted
/// playground can only reach the demo API and the demo database.
/// </summary>
public sealed class AccessKey
{
    public const string Header = "X-Playground-Key";

    public AccessKey(IOptions<PlaygroundOptions> options, ILogger<AccessKey> logger)
    {
        var o = options.Value;
        if (o.IsHosted || o.AllowAnonymous) return;
        if (!string.IsNullOrWhiteSpace(o.AccessKey))
        {
            Value = o.AccessKey.Trim();
            return;
        }

        Value = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        Generated = true;
        logger.LogWarning(
            "Libraries playground: no Playground:AccessKey was set, so this one was made for this run: {Key}. " +
            "Open the page with ?key={Key} once, or set Playground__AccessKey to keep a key across restarts.", Value, Value);
    }

    /// <summary>Null: no key needed.</summary>
    public string? Value { get; }

    public bool Generated { get; }

    public bool Matches(string? candidate) =>
        Value is null
        || (candidate is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate), Encoding.UTF8.GetBytes(Value)));

    /// <summary>Every <c>/api</c> route but <c>/api/info</c> (the page needs it to ask for the key).</summary>
    public static Func<HttpContext, Func<Task>, Task> Middleware => async (http, next) =>
    {
        var key = http.RequestServices.GetRequiredService<AccessKey>();
        if (key.Value is not null
            && http.Request.Path.StartsWithSegments("/api")
            && !http.Request.Path.Equals("/api/info")
            && !key.Matches(http.Request.Headers[Header].FirstOrDefault()))
        {
            await Results.Json(new { error = "This playground needs its access key (the X-Playground-Key header).", reason = "key_required" },
                statusCode: StatusCodes.Status401Unauthorized).ExecuteAsync(http);
            return;
        }
        await next();
    };
}
