namespace SpanFlow.Mapping.Engine;

public sealed class TransformRegistry
{
    private readonly Dictionary<string, ITransform> _transforms;

    private TransformRegistry(IEnumerable<ITransform> transforms)
    {
        _transforms = transforms.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The built-in transforms. Add a new one to this list.</summary>
    public static TransformRegistry CreateDefault()
    {
        var converter = ValueConverter.Default;

        return new TransformRegistry(
        [
            new ConcatTransform(),
            new UpperTransform(),
            new LowerTransform(),
            new AddTransform(),
            new MultiplyTransform(),
            new CoalesceTransform(),
            // string
            new TrimTransform(),
            new ReplaceTransform(),
            new SubstringTransform(),
            new LengthTransform(),
            // number
            new SubtractTransform(),
            new DivideTransform(),
            new ModTransform(),
            new RoundTransform(),
            new FloorTransform(),
            new CeilTransform(),
            new AbsTransform(),
            // type conversion — all through ValueConverter (Functions/Conversion)
            new ConvertTransform(converter),
            new ConvertToTransform("toString", "string", converter),
            new ConvertToTransform("toInt", "int", converter),
            new ConvertToTransform("toLong", "long", converter),
            new ConvertToTransform("toDouble", "double", converter),
            new ConvertToTransform("toFloat", "float", converter),
            new ConvertToTransform("toDecimal", "decimal", converter),
            new ConvertToTransform("toBool", "bool", converter),
            new ConvertToTransform("toDateTime", "datetime", converter),
            new ConvertToTransform("toDate", "date", converter),
            new ConvertToTransform("toTime", "time", converter),
            new ConvertToTransform("toUuid", "uuid", converter),
            new IsTypeTransform(converter),
            new TypeOfTransform(),
            new DetectTypeTransform(converter),
            // date / epoch
            new FormatDateTransform(converter),
            new ToEpochTransform(converter),
            new FromEpochTransform(converter),
            new ConvertEpochTransform(converter),
            // uuid
            new IsUuidTransform(),
            new UuidVersionTransform(),
            new NewUuidTransform(),
            new UuidTimestampTransform(),
            // hash & encoding (E48-T10) — keyless and deterministic only; keyed crypto stays in pipeline nodes
            DigestTransform.Sha256(),
            DigestTransform.Sha384(),
            DigestTransform.Sha512(),
            DigestTransform.Md5Legacy(),
            .. TextCodecTransform.All(),
        ]);
    }

    /// <summary>Every registered function name — for checking the designer's list against the engine.</summary>
    public IReadOnlyCollection<string> Names => _transforms.Keys;

    public ITransform Get(string name)
    {
        if (_transforms.TryGetValue(name, out var transform))
        {
            return transform;
        }

        throw new InvalidOperationException($"Transform '{name}' is not registered.");
    }
}
