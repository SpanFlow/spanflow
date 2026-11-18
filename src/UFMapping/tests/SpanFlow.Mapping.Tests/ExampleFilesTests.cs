using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace SpanFlow.Mapping.Tests;

/// <summary>
/// Every example under <c>examples/</c> that has a recorded <c>&lt;name&gt;.output.json</c>
/// is run through the engine and must still produce exactly that output — so the
/// examples in the README can't silently rot.
/// </summary>
public class ExampleFilesTests
{
    public static TheoryData<string> Examples()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(ExamplesDir, "*.output.json"))
        {
            var name = Path.GetFileName(file)[..^".output.json".Length];
            if (!IsFormatExample(name)) data.Add(name);
        }
        return data;
    }

    /// <summary>
    /// E73: an example runs through MappingRunner when its mapping has a formats block or its input is not JSON;
    /// otherwise it is a plain engine example (JSON in, JSON out, compared as JSON).
    /// </summary>
    private static bool IsFormatExample(string name) =>
        !File.Exists(Path.Combine(ExamplesDir, $"{name}.input.json"))
        || JsonNode.Parse(File.ReadAllText(Path.Combine(ExamplesDir, $"{name}.mapping.json")))?["formats"] is not null;

    [Theory]
    [MemberData(nameof(Examples))]
    public void Example_ProducesItsRecordedOutput(string name)
    {
        var mapping = File.ReadAllText(Path.Combine(ExamplesDir, $"{name}.mapping.json"));
        var input = File.ReadAllText(Path.Combine(ExamplesDir, $"{name}.input.json"));
        var expected = TestSupport.Input(File.ReadAllText(Path.Combine(ExamplesDir, $"{name}.output.json")))!;

        var actual = TestSupport.Run(mapping, input);

#if NETFRAMEWORK
        // .NET Framework's System.Text.Json writes doubles with 17 digits (87.6 -> 87.599999999999994):
        // the same value as different text, so compare the trees by value there.
        Assert.True(SameValue(expected, actual), $"Expected: {expected.ToJsonString()}\nActual:   {actual.ToJsonString()}");
#else
        Assert.Equal(expected.ToJsonString(), actual.ToJsonString());
#endif
    }

    /// <summary>E73: examples whose output is another format (an EDI 997 ...): raw input in, through MappingRunner, raw output out.</summary>
    public static TheoryData<string> FormatExamples()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(ExamplesDir, "*.output.*"))
        {
            var fileName = Path.GetFileName(file);
            var name = fileName.Substring(0, fileName.IndexOf(".output.", StringComparison.Ordinal));
            if (IsFormatExample(name)) data.Add(name);
        }
        return data;
    }

    /// <summary>
    /// Raw input in, through MappingRunner, raw output out, compared as text. <c>EXAMPLES_REGEN=1</c> records the
    /// output instead (then read the diff: the recorded file is what the docs quote).
    /// </summary>
    [Theory]
    [MemberData(nameof(FormatExamples))]
    public void FormatExample_ProducesItsRecordedOutput(string name)
    {
        var mapping = TestSupport.Mapping(File.ReadAllText(Path.Combine(ExamplesDir, $"{name}.mapping.json")));
        var input = File.ReadAllBytes(Directory.GetFiles(ExamplesDir, $"{name}.input.*").Single());
        var outputFile = Directory.GetFiles(ExamplesDir, $"{name}.output.*").Single();

        var result = new SpanFlow.Mapping.Formats.MappingRunner().Run(mapping, SpanFlow.Mapping.Formats.FormatPayload.FromBytes(input));

        if (Environment.GetEnvironmentVariable("EXAMPLES_REGEN") == "1")
        {
            File.WriteAllText(outputFile, result.OutputText);
            return;
        }
        Assert.Equal(File.ReadAllText(outputFile), result.OutputText);
    }

#if NETFRAMEWORK
    private static bool SameValue(JsonNode? a, JsonNode? b) => (a, b) switch
    {
        (null, null) => true,
        (JsonObject x, JsonObject y) => x.Count == y.Count
            && x.All(p => y.TryGetPropertyValue(p.Key, out var v) && SameValue(p.Value, v)),
        (JsonArray x, JsonArray y) => x.Count == y.Count && x.Zip(y, SameValue).All(same => same),
        (JsonValue x, JsonValue y) when x.GetValueKind() == JsonValueKind.Number && y.GetValueKind() == JsonValueKind.Number
            => double.Parse(x.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture)
               == double.Parse(y.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture),
        (JsonValue x, JsonValue y) => x.ToJsonString() == y.ToJsonString(),
        _ => false,
    };
#endif

    private static string ExamplesDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "examples")))
            {
                dir = dir.Parent;
            }
            return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("examples/ not found above the test output."), "examples");
        }
    }
}
