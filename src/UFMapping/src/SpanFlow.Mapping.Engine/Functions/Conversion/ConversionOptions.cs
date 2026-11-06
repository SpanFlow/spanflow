namespace SpanFlow.Mapping.Engine;

/// <summary>
/// How a single conversion should behave. Every field is optional; what each one
/// means depends on the target type (see the converter for that type).
/// </summary>
public sealed record ConversionOptions
{
    public static readonly ConversionOptions Default = new();

    /// <summary>
    /// Output shape: a date format (named like <c>iso</c>/<c>date</c> or a .NET
    /// pattern like <c>dd/MM/yyyy</c>), a numeric format (<c>N2</c>, <c>0.00</c>,
    /// <c>X</c>), a UUID layout (<c>d</c>, <c>n</c>, <c>base64</c>, ...) or an
    /// integer rounding mode (<c>truncate</c>, <c>round</c>, <c>floor</c>, <c>ceil</c>).
    /// </summary>
    public string? Format { get; init; }

    /// <summary>
    /// How to read the input: date pattern(s) separated by <c>|</c>, an epoch unit
    /// (<c>s</c>, <c>ms</c>, <c>us</c>, <c>ns</c>, <c>ticks</c>, <c>auto</c>) for a
    /// number read as a date, or <c>base64</c> for a UUID.
    /// </summary>
    public string? InputFormat { get; init; }

    /// <summary>Time zone a date is shown in. Null keeps the parsed offset.</summary>
    public TimeZoneInfo? TimeZone { get; init; }

    /// <summary>
    /// Checker mode (<c>isType</c>): only accept a value that already IS the type,
    /// or text that spells it exactly — no rounding 12.5 to an int, no reading a
    /// number as an epoch date, no treating 2 as <c>true</c>.
    /// </summary>
    public bool Strict { get; init; }
}
