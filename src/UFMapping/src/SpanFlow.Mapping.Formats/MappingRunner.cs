using System.Text;
using System.Text.Json.Nodes;
using SpanFlow.Mapping.Core.Models;
using SpanFlow.Mapping.Engine;

namespace SpanFlow.Mapping.Formats;

/// <summary>
/// Runs a mapping from raw input to raw output: decode with the definition's input format, run
/// <see cref="MappingEngine"/> unchanged on the JSON, encode with the output format. Every format pair goes through
/// JSON in the middle, so the engine and every node work the same for all of them. A definition without
/// <c>formats</c> is JSON in, JSON out. The designer preview, the pipeline transform step and the hosted API all
/// call this, so they cannot disagree.
/// </summary>
public sealed class MappingRunner
{
    /// <summary>The source name the decoded input is given to the engine under (as <c>MappingEngine.Execute</c> does).</summary>
    public const string InputSource = "input";

    private readonly MappingEngine _engine;
    private readonly FormatRegistry _formats;
    private readonly FormatLimits _limits;

    public MappingRunner()
        : this(new MappingEngine(), FormatRegistry.CreateDefault())
    {
    }

    public MappingRunner(MappingEngine engine, FormatRegistry formats, FormatLimits? limits = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _formats = formats ?? throw new ArgumentNullException(nameof(formats));
        _limits = limits ?? FormatLimits.Default;
    }

    public FormatRegistry Formats => _formats;

    /// <summary>Raw text in, raw output out.</summary>
    public MappingRunResult Run(MappingDefinition definition, string input, IReadOnlyDictionary<string, JsonNode?>? otherSources = null) =>
        Run(definition, FormatPayload.FromText(input), otherSources);

    /// <summary>
    /// Raw input in, raw output out. <paramref name="otherSources"/> are extra named sources a multi-source mapping
    /// reads (already JSON); the decoded input is always the <c>input</c> source.
    /// </summary>
    public MappingRunResult Run(MappingDefinition definition, FormatPayload input, IReadOnlyDictionary<string, JsonNode?>? otherSources = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(input);

        // Both sides are checked before any data is read, so a wrong setting fails the same way on every input.
        var reader = Prepare(definition.Formats?.Input, FormatDirection.Decode);
        var writer = Prepare(definition.Formats?.Output, FormatDirection.Encode);

        var size = input.ByteCount(reader.Context.Encoding);
        if (size > _limits.MaxInputBytes)
            throw reader.Context.Fail($"The input is {size:N0} bytes, more than the limit of {_limits.MaxInputBytes:N0}.");

        var decoded = reader.Codec.Decode(input, reader.Context);
        return Finish(definition, reader, writer, decoded, otherSources);
    }

    /// <summary>
    /// The input is already JSON (a pipeline payload, a parsed request body): the input format is skipped, the output
    /// format still applies.
    /// </summary>
    public MappingRunResult RunDecoded(MappingDefinition definition, JsonNode? input, IReadOnlyDictionary<string, JsonNode?>? otherSources = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var reader = Prepare(null, FormatDirection.Decode);
        var writer = Prepare(definition.Formats?.Output, FormatDirection.Encode);
        return Finish(definition, reader, writer, input, otherSources);
    }

    /// <summary>Only the decode step: what the designer shows as the input "seen as JSON".</summary>
    public JsonNode? Decode(FormatSpec? spec, FormatPayload raw)
    {
        var side = Prepare(spec, FormatDirection.Decode);
        return side.Codec.Decode(raw, side.Context);
    }

    /// <summary>Only the encode step.</summary>
    public FormatPayload Encode(FormatSpec? spec, JsonNode? value)
    {
        var side = Prepare(spec, FormatDirection.Encode);
        return side.Codec.Encode(value, side.Context);
    }

    private MappingRunResult Finish(MappingDefinition definition, Side reader, Side writer, JsonNode? decoded, IReadOnlyDictionary<string, JsonNode?>? otherSources)
    {
        var sources = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        if (otherSources is not null)
        {
            foreach (var pair in otherSources)
            {
                if (string.Equals(pair.Key, InputSource, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"'{InputSource}' is the decoded input; pass the other sources under other names.", nameof(otherSources));
                sources[pair.Key] = pair.Value;
            }
        }
        // The engine may move nodes of its sources into the result; the caller keeps its own copy of the decoded input.
        sources[InputSource] = decoded?.DeepClone();

        var mapped = _engine.Execute(definition, sources);
        var output = writer.Codec.Encode(mapped, writer.Context);

        return new MappingRunResult(reader.Codec.Type, writer.Codec.Type, decoded, mapped, output, writer.Codec.ContentType, writer.Context.Encoding);
    }

    private Side Prepare(FormatSpec? spec, FormatDirection direction)
    {
        var codec = _formats.Get(spec?.Type, direction);
        return new Side(codec, FormatContext.Create(codec, spec?.Options, direction, _limits));
    }

    private sealed class Side(IFormatCodec codec, FormatContext context)
    {
        public IFormatCodec Codec { get; } = codec;

        public FormatContext Context { get; } = context;
    }
}

/// <summary>What a run produced, with the JSON in the middle kept for preview and trace.</summary>
public sealed class MappingRunResult
{
    internal MappingRunResult(string inputFormat, string outputFormat, JsonNode? decoded, JsonNode mapped, FormatPayload output, string contentType, Encoding encoding)
    {
        InputFormat = inputFormat;
        OutputFormat = outputFormat;
        Decoded = decoded;
        Mapped = mapped;
        Output = output;
        ContentType = contentType;
        Encoding = encoding;
    }

    public string InputFormat { get; }

    public string OutputFormat { get; }

    /// <summary>The input as JSON, before mapping.</summary>
    public JsonNode? Decoded { get; }

    /// <summary>The mapping's JSON result, before encoding.</summary>
    public JsonNode Mapped { get; }

    /// <summary>The encoded output.</summary>
    public FormatPayload Output { get; }

    /// <summary>The output's media type, e.g. <c>application/xml</c>.</summary>
    public string ContentType { get; }

    /// <summary>The output's text encoding (the output format's <c>encoding</c> option, UTF-8 by default).</summary>
    public Encoding Encoding { get; }

    /// <summary>The output as text.</summary>
    public string OutputText => Output.AsText(Encoding);

    /// <summary>The output as bytes in <see cref="Encoding"/>.</summary>
    public byte[] OutputBytes => Output.AsBytes(Encoding);
}
