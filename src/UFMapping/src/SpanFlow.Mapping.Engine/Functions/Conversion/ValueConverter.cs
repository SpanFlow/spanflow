using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

/// <summary>
/// One target type for <see cref="ValueConverter"/>. The input is a plain CLR value
/// (string, int, long, decimal, double, bool, or a cloned JsonObject/JsonArray) and
/// is never null. Return false when the value can't be converted.
/// </summary>
public interface ITypeConverter
{
    /// <summary>Type names this converter answers to (matched case-insensitively).</summary>
    IReadOnlyList<string> Names { get; }

    bool TryConvert(object value, ConversionOptions options, out JsonNode? result);
}

/// <summary>
/// The single place type conversion lives. Each target type is an
/// <see cref="ITypeConverter"/> looked up by name, and every conversion transform
/// (<c>convert</c>, <c>toInt</c>, <c>isType</c>, <c>formatDate</c>, <c>toEpoch</c>, ...)
/// goes through here — so adding a type, or changing how one converts, is one class
/// plus a line in <see cref="CreateDefault"/>.
/// </summary>
public sealed class ValueConverter
{
    private readonly Dictionary<string, ITypeConverter> _converters = new(StringComparer.OrdinalIgnoreCase);

    public ValueConverter(IEnumerable<ITypeConverter> converters)
    {
        foreach (var converter in converters)
        {
            foreach (var name in converter.Names)
            {
                // A later registration replaces an earlier one, so a caller can
                // override a built-in type without rebuilding the whole list.
                _converters[name] = converter;
            }
        }
    }

    public static ValueConverter Default { get; } = CreateDefault();

    /// <summary>The built-in target types. Add a new one to this list.</summary>
    public static ValueConverter CreateDefault() => new(
    [
        new StringTypeConverter(),
        new IntegerTypeConverter(["int", "int32", "integer"], int.MinValue, int.MaxValue),
        new IntegerTypeConverter(["long", "int64"], long.MinValue, long.MaxValue),
        new IntegerTypeConverter(["short", "int16"], short.MinValue, short.MaxValue),
        new IntegerTypeConverter(["byte", "uint8"], byte.MinValue, byte.MaxValue),
        new DecimalTypeConverter(),
        new DoubleTypeConverter(["double", "float64"], single: false),
        new DoubleTypeConverter(["float", "single", "float32"], single: true),
        new BooleanTypeConverter(),
        new DateTimeTypeConverter(["datetime", "timestamp"], DateTimeText.Iso),
        new DateTimeTypeConverter(["date"], "yyyy-MM-dd"),
        new DateTimeTypeConverter(["time"], "HH:mm:ss"),
        new EpochTypeConverter(EpochUnit.Seconds),
        new EpochTypeConverter(EpochUnit.Milliseconds),
        new EpochTypeConverter(EpochUnit.Microseconds),
        new EpochTypeConverter(EpochUnit.Nanoseconds),
        new EpochTypeConverter(EpochUnit.Ticks),
        new UuidTypeConverter(),
    ]);

    public IReadOnlyCollection<string> TypeNames => _converters.Keys;

    public bool IsKnownType(string type) => _converters.ContainsKey(type);

    /// <summary>Converts <paramref name="value"/>, or returns null when it can't be.</summary>
    public JsonNode? Convert(JsonNode? value, string type, ConversionOptions? options = null)
        => TryConvert(value, type, options, out var result) ? result : null;

    /// <summary>
    /// Converts <paramref name="value"/> to <paramref name="type"/>. A null input
    /// never converts. An unknown type name is a mapping mistake, so it throws.
    /// </summary>
    public bool TryConvert(JsonNode? value, string type, ConversionOptions? options, out JsonNode? result)
    {
        var converter = Resolve(type);
        result = null;

        var raw = JsonScalarConverter.ToObject(value);
        return raw is not null && converter.TryConvert(raw, options ?? ConversionOptions.Default, out result);
    }

    private ITypeConverter Resolve(string type)
    {
        if (!string.IsNullOrWhiteSpace(type) && _converters.TryGetValue(type.Trim(), out var converter))
        {
            return converter;
        }

        throw new InvalidOperationException(
            $"Unknown conversion type '{type}'. Known types: {string.Join(", ", _converters.Keys.Order(StringComparer.OrdinalIgnoreCase))}.");
    }
}
