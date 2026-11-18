using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Formats;

/// <summary>
/// A ready-made format setup to copy and edit (D73.7: a layout is data, never code): the ISO 8583:1987 field table
/// with a hex or a binary bitmap, and the positional string example. Picking one gives a mapping side its options;
/// a partner's own variant is an edit of those options, not a release.
/// </summary>
public sealed class StarterLayout
{
    internal StarterLayout(string name, string title, string format, string description, JsonObject options)
    {
        Name = name;
        Title = title;
        Format = format;
        Description = description;
        _options = options;
    }

    private readonly JsonObject _options;

    public string Name { get; }

    public string Title { get; }

    /// <summary>The format type the options are for (positional for all the bundled ones).</summary>
    public string Format { get; }

    public string Description { get; }

    /// <summary>A fresh copy of the options, safe to change.</summary>
    public JsonObject Options => (JsonObject)_options.DeepClone();
}

/// <summary>The starter layouts bundled with this library.</summary>
public static class StarterLayouts
{
    private const string Prefix = "SpanFlow.Mapping.Formats.Starters.";

    private static readonly Lazy<IReadOnlyList<StarterLayout>> Loaded = new(Load);

    public static IReadOnlyList<StarterLayout> All => Loaded.Value;

    public static StarterLayout? Find(string name) => All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<StarterLayout> Load()
    {
        var assembly = typeof(StarterLayouts).Assembly;
        var list = new List<StarterLayout>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            var node = JsonNode.Parse(stream)!.AsObject();
            list.Add(new StarterLayout(
                node["name"]!.GetValue<string>(),
                node["title"]!.GetValue<string>(),
                node["format"]!.GetValue<string>(),
                node["description"]!.GetValue<string>(),
                node["options"]!.AsObject()));
        }
        return list.OrderBy(s => s.Name, StringComparer.Ordinal).ToList();
    }
}
