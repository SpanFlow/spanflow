using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

internal sealed record EvaluationContext(
    IReadOnlyDictionary<string, JsonNode?> Sources,
    JsonNode? Current,
    IReadOnlyList<JsonNode?>? GroupItems,
    string? GroupKey)
{
    /// <summary>
    /// The group's members as an addressable array, so a groupBy map can nest
    /// nodes over the rows of its own group (<c>group[]</c>). Null outside a group.
    /// Detached clones: the originals still belong to the input document.
    /// </summary>
    public JsonArray? GroupArray { get; private init; }

    public EvaluationContext WithCurrent(JsonNode? current) => this with { Current = current };

    public EvaluationContext WithGroup(IReadOnlyList<JsonNode?> items, string? key) => this with
    {
        GroupItems = items,
        GroupKey = key,
        GroupArray = new JsonArray(items.Select(item => item?.DeepClone()).ToArray())
    };
}

internal static class JsonSerializerOptionsProvider
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
