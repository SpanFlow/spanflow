using System.Text.Json.Nodes;
using SpanFlow.Mapping.Core.Models;
using SpanFlow.Mapping.Formats;
using Xunit;

namespace SpanFlow.Mapping.Tests;

/// <summary>
/// The round-trip harness every codec is held to (E73, D73.5). Each sample under <c>FormatSamples/&lt;type&gt;/</c>
/// (see the README there) must decode to its expected JSON, write back to its recorded text when there is one, and
/// read back unchanged. A new codec only adds sample files; a codec without samples fails
/// <see cref="EveryCodec_HasSamples"/>.
/// </summary>
public class FormatSamplesTests
{
    private const string RawMarker = ".raw.";

    public static TheoryData<string, string> Samples()
    {
        var data = new TheoryData<string, string>();
        foreach (var dir in Directory.GetDirectories(SamplesDir))
        {
            foreach (var file in Directory.GetFiles(dir, $"*{RawMarker}*"))
            {
                var name = Path.GetFileName(file);
                data.Add(Path.GetFileName(dir), name.Substring(0, name.IndexOf(RawMarker, StringComparison.Ordinal)));
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Sample_Decodes_Encodes_AndRoundTrips(string type, string name)
    {
        var dir = Path.Combine(SamplesDir, type);
        var runner = new MappingRunner();
        var options = ReadObject(dir, $"{name}.options.json");
        var decodeOptions = Merge(options, ReadObject(dir, $"{name}.decode-options.json"));
        var schema = Path.Combine(dir, $"{name}.schema.xsd");
        if (File.Exists(schema)) (decodeOptions ??= new JsonObject())["xsd"] = File.ReadAllText(schema);
        var encodeOptions = Merge(options, ReadObject(dir, $"{name}.encode-options.json"));
        var decodeSpec = new FormatSpec { Type = type, Options = decodeOptions };
        var encodeSpec = new FormatSpec { Type = type, Options = encodeOptions };

        var raw = File.ReadAllBytes(Single(dir, $"{name}{RawMarker}*"));
        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, $"{name}.expected.json")));

        var decoded = runner.Decode(decodeSpec, FormatPayload.FromBytes(raw));
        AssertSameJson(expected, decoded, "decode");

        var encoded = runner.Encode(encodeSpec, expected?.DeepClone());
        var encodedFile = Directory.GetFiles(dir, $"{name}.encoded.*").SingleOrDefault();
        if (encodedFile is not null)
        {
            var context = FormatContext.Create(runner.Formats.Get(type, FormatDirection.Encode), encodeOptions, FormatDirection.Encode);
            Assert.Equal(FormatPayload.FromBytes(File.ReadAllBytes(encodedFile)).AsText(context.Encoding), encoded.AsText(context.Encoding));
        }

        var again = runner.Decode(decodeSpec, encoded);
        AssertSameJson(expected, again, "round trip");
    }

    [Fact]
    public void EveryCodec_HasSamples()
    {
        foreach (var codec in FormatRegistry.CreateDefault().Codecs)
        {
            var dir = Path.Combine(SamplesDir, codec.Type);
            Assert.True(Directory.Exists(dir) && Directory.GetFiles(dir, $"*{RawMarker}*").Length > 0,
                $"The '{codec.Type}' codec has no samples: add FormatSamples/{codec.Type}/<name>.raw.<ext> + <name>.expected.json.");
        }
    }

    internal static void AssertSameJson(JsonNode? expected, JsonNode? actual, string step) =>
        Assert.True(JsonNode.DeepEquals(expected, actual),
            $"{step}: expected {expected?.ToJsonString() ?? "null"}\nactual   {actual?.ToJsonString() ?? "null"}");

    private static string Single(string dir, string pattern) => Directory.GetFiles(dir, pattern).Single();

    private static JsonObject? ReadObject(string dir, string file)
    {
        var path = Path.Combine(dir, file);
        return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject() : null;
    }

    private static JsonObject? Merge(JsonObject? first, JsonObject? second)
    {
        if (second is null) return first;
        var merged = first?.DeepClone().AsObject() ?? new JsonObject();
        foreach (var pair in second) merged[pair.Key] = pair.Value?.DeepClone();
        return merged;
    }

    private static string SamplesDir => Path.Combine(AppContext.BaseDirectory, "FormatSamples");
}
