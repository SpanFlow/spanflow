using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

// Type conversion, checking, date/epoch and UUID transforms. They only read their
// arguments and hand off to ValueConverter / DateTimeText / Epoch / UuidTools, so
// how a conversion behaves is decided in one place (Functions/Conversion).
//
// A value that can't be converted gives null — wrap it in coalesce for a
// fallback, or guard it with an exception rule. A misspelt type, unit, format or
// time zone is a mistake in the mapping and throws.

internal static class TransformArgs
{
    /// <summary>The argument as text; missing, null or blank is null.</summary>
    public static string? Text(IReadOnlyList<JsonNode?> args, int index)
    {
        var text = JsonScalarConverter.ToText(args.ElementAtOrDefault(index));
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public static string Required(IReadOnlyList<JsonNode?> args, int index, string function, string what)
        => Text(args, index) ?? throw new InvalidOperationException($"{function} needs {what}.");
}

/// <summary><c>convert(value, type, [format], [inputFormat])</c> — to any registered type.</summary>
public sealed class ConvertTransform(ValueConverter converter) : ITransform
{
    public string Name => "convert";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
    {
        var type = TransformArgs.Required(args, 1, Name, "a target type, e.g. convert(value, 'int')");
        return converter.Convert(args.ElementAtOrDefault(0), type, new ConversionOptions
        {
            Format = TransformArgs.Text(args, 2),
            InputFormat = TransformArgs.Text(args, 3),
        });
    }
}

/// <summary>
/// A fixed-type shortcut for <see cref="ConvertTransform"/>: <c>toInt(value, [format], [inputFormat])</c>,
/// <c>toString</c>, <c>toDateTime</c>, <c>toUuid</c>, ...
/// </summary>
public sealed class ConvertToTransform(string name, string type, ValueConverter converter) : ITransform
{
    public string Name => name;

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
        => converter.Convert(args.ElementAtOrDefault(0), type, new ConversionOptions
        {
            Format = TransformArgs.Text(args, 1),
            InputFormat = TransformArgs.Text(args, 2),
        });
}

/// <summary>
/// <c>isType(value, type)</c> → true when the value is that type, or is text that
/// spells it exactly ("12" is an int, "12.5" is not). Also accepts <c>null</c>,
/// <c>object</c> and <c>array</c>.
/// </summary>
public sealed class IsTypeTransform(ValueConverter converter) : ITransform
{
    private static readonly ConversionOptions Strict = new() { Strict = true };

    public string Name => "isType";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
    {
        var value = args.ElementAtOrDefault(0);
        var type = TransformArgs.Required(args, 1, Name, "a type, e.g. isType(value, 'uuid')");

        var matches = type.Trim().ToLowerInvariant() switch
        {
            "null" => JsonScalarConverter.ToObject(value) is null,
            "object" => value is JsonObject,
            "array" => value is JsonArray,
            _ => converter.TryConvert(value, type, Strict, out _),
        };
        return JsonValue.Create(matches);
    }
}

/// <summary>
/// <c>typeOf(value)</c> → the JSON kind: <c>null</c>, <c>string</c>, <c>boolean</c>,
/// <c>integer</c>, <c>number</c>, <c>object</c> or <c>array</c>.
/// </summary>
public sealed class TypeOfTransform : ITransform
{
    public string Name => "typeOf";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args) => JsonValue.Create(Kind(args.ElementAtOrDefault(0)));

    internal static string Kind(JsonNode? value) => value switch
    {
        JsonObject => "object",
        JsonArray => "array",
        _ => JsonScalarConverter.ToObject(value) switch
        {
            null => "null",
            string => "string",
            bool => "boolean",
            int or long => "integer",
            _ => "number",
        },
    };
}

/// <summary>
/// <c>detectType(value)</c> — like <c>typeOf</c>, but looks inside text: a string
/// holding a UUID, whole number, number, true/false or date-time reports
/// <c>uuid</c>, <c>integer</c>, <c>number</c>, <c>boolean</c> or <c>datetime</c>.
/// </summary>
public sealed class DetectTypeTransform(ValueConverter converter) : ITransform
{
    private static readonly ConversionOptions Strict = new() { Strict = true };

    public string Name => "detectType";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
    {
        var value = args.ElementAtOrDefault(0);
        if (JsonScalarConverter.ToObject(value) is not string text || text.Trim().Length == 0)
        {
            return JsonValue.Create(TypeOfTransform.Kind(value));
        }

        var trimmed = text.Trim();
        string kind =
            UuidTools.TryParse(trimmed, out _) ? "uuid"
            : trimmed.Equals("true", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("false", StringComparison.OrdinalIgnoreCase) ? "boolean"
            : NumberParsing.IsPlainNumber(trimmed) && converter.TryConvert(value, "long", Strict, out _) ? "integer"
            : NumberParsing.IsPlainNumber(trimmed) ? "number"
            : converter.TryConvert(value, "datetime", Strict, out _) ? "datetime"
            : "string";
        return JsonValue.Create(kind);
    }
}

/// <summary>
/// <c>formatDate(value, format, [inputFormat], [timeZone])</c> — reads a date (text or
/// epoch number) and writes it in <c>format</c> (named, e.g. <c>iso</c>/<c>date</c>/<c>rfc1123</c>,
/// or a .NET pattern like <c>dd/MM/yyyy HH:mm</c>), optionally shifted into a time zone.
/// </summary>
public sealed class FormatDateTransform(ValueConverter converter) : ITransform
{
    public string Name => "formatDate";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
        => converter.Convert(args.ElementAtOrDefault(0), "datetime", new ConversionOptions
        {
            Format = TransformArgs.Text(args, 1),
            InputFormat = TransformArgs.Text(args, 2),
            TimeZone = DateTimeText.ResolveTimeZone(TransformArgs.Text(args, 3)),
        });
}

/// <summary>
/// <c>toEpoch(value, [unit], [inputFormat])</c> — a date (or epoch number) to an epoch
/// number in <c>s</c> (default), <c>ms</c>, <c>us</c>, <c>ns</c> or <c>ticks</c>.
/// </summary>
public sealed class ToEpochTransform(ValueConverter converter) : ITransform
{
    public string Name => "toEpoch";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
    {
        var unit = Epoch.ParseUnit(TransformArgs.Text(args, 1)) ?? EpochUnit.Seconds;
        return converter.Convert(args.ElementAtOrDefault(0), Epoch.TypeName(unit), new ConversionOptions
        {
            InputFormat = TransformArgs.Text(args, 2),
        });
    }
}

/// <summary>
/// <c>fromEpoch(value, [unit], [format], [timeZone])</c> — an epoch number to date text.
/// The unit defaults to <c>auto</c> (guessed from the number's size).
/// </summary>
public sealed class FromEpochTransform(ValueConverter converter) : ITransform
{
    public string Name => "fromEpoch";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
    {
        var unit = TransformArgs.Text(args, 1) ?? "auto";
        Epoch.ParseUnit(unit); // a misspelt unit must fail loudly, not be read as a date pattern

        return converter.Convert(args.ElementAtOrDefault(0), "datetime", new ConversionOptions
        {
            InputFormat = unit,
            Format = TransformArgs.Text(args, 2),
            TimeZone = DateTimeText.ResolveTimeZone(TransformArgs.Text(args, 3)),
        });
    }
}

/// <summary><c>convertEpoch(value, fromUnit, toUnit)</c> — e.g. seconds → milliseconds.</summary>
public sealed class ConvertEpochTransform(ValueConverter converter) : ITransform
{
    public string Name => "convertEpoch";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
    {
        var from = TransformArgs.Required(args, 1, Name, "a source unit (s, ms, us, ns, ticks or auto)");
        Epoch.ParseUnit(from);
        var to = Epoch.ParseUnit(TransformArgs.Required(args, 2, Name, "a target unit (s, ms, us, ns or ticks)"))
            ?? throw new InvalidOperationException("convertEpoch needs a concrete target unit, not 'auto'.");

        return converter.Convert(args.ElementAtOrDefault(0), Epoch.TypeName(to), new ConversionOptions { InputFormat = from });
    }
}

/// <summary>
/// <c>isUuid(value, [version])</c> → whether the value is a UUID, optionally of a
/// version: <c>1</c>–<c>8</c> (or <c>v7</c>), <c>nil</c>, <c>max</c> or <c>any</c>.
/// </summary>
public sealed class IsUuidTransform : ITransform
{
    public string Name => "isUuid";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
    {
        var version = TransformArgs.Text(args, 1);
        var matches = JsonScalarConverter.ToObject(args.ElementAtOrDefault(0)) is string text
            && UuidTools.TryParse(text, out var uuid)
            && UuidTools.Matches(uuid, version);
        return JsonValue.Create(matches);
    }
}

/// <summary>
/// <c>uuidVersion(value)</c> → 1–8, 0 for nil, 15 for max; null when it isn't an
/// RFC 9562 UUID.
/// </summary>
public sealed class UuidVersionTransform : ITransform
{
    public string Name => "uuidVersion";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
        => TransformArgs.Text(args, 0) is { } text && UuidTools.TryParse(text, out var uuid) && UuidTools.GetVersion(uuid) is { } version
            ? JsonValue.Create(version)
            : null;
}

/// <summary>
/// <c>newUuid([version], [namespace], [name], [format])</c> — version 4 by default;
/// 1, 3, 4, 5, 6, 7. v3/v5 need a namespace (dns, url, oid, x500 or a UUID) and a name.
/// </summary>
public sealed class NewUuidTransform : ITransform
{
    public string Name => "newUuid";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
    {
        var spec = TransformArgs.Text(args, 0)?.Trim().TrimStart('v', 'V') ?? "4";
        if (!int.TryParse(spec, out var version))
        {
            throw new InvalidOperationException($"newUuid version must be a number (1, 3, 4, 5, 6 or 7), got '{spec}'.");
        }

        var uuid = UuidTools.Create(version, TransformArgs.Text(args, 1), JsonScalarConverter.ToText(args.ElementAtOrDefault(2)));
        return JsonValue.Create(UuidTools.Format(uuid, TransformArgs.Text(args, 3)));
    }
}

/// <summary>
/// <c>uuidTimestamp(value, [format], [timeZone])</c> → when a v1, v6 or v7 UUID was
/// made, as date text; null for other versions.
/// </summary>
public sealed class UuidTimestampTransform : ITransform
{
    public string Name => "uuidTimestamp";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
        => TransformArgs.Text(args, 0) is { } text && UuidTools.TryParse(text, out var uuid) && UuidTools.TryGetTimestamp(uuid, out var created)
            ? JsonValue.Create(DateTimeText.Format(created, TransformArgs.Text(args, 1), DateTimeText.ResolveTimeZone(TransformArgs.Text(args, 2))))
            : null;
}
