using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Formats.Codecs;

/// <summary>
/// E73-T11: an EDI guide (D73.8: data, never code) for one message type: the segments and loops of a transaction
/// (X12) or message (EDIFACT) body in order, with element names and rules. Read from JSON:
/// <code>
/// { "name": "x12-850", "standard": "x12", "message": "850", "version": "004010", "title": "Purchase order",
///   "segments": [
///     { "id": "BEG", "min": 1, "elements": [ { "name": "purposeCode", "required": true, "min": 2, "max": 2, "type": "id", "codes": ["00"] } ] },
///     { "loop": "PO1Loop", "min": 1, "max": 100000, "segments": [ { "id": "PO1", "min": 1, "elements": [ ... ] } ] } ] }
/// </code>
/// A segment or loop with <c>max</c> above 1 is a list in the JSON; the first segment of a loop starts each
/// repeat. An element with <c>components</c> is a composite (an object of its components).
/// </summary>
internal sealed class EdiGuide
{
    private static readonly HashSet<string> GuideKeys = new(StringComparer.Ordinal) { "name", "standard", "message", "version", "title", "description", "segments" };
    private static readonly HashSet<string> SegmentKeys = new(StringComparer.Ordinal) { "id", "name", "min", "max", "elements", "description" };
    private static readonly HashSet<string> LoopKeys = new(StringComparer.Ordinal) { "loop", "min", "max", "segments", "description" };
    private static readonly HashSet<string> ElementKeys = new(StringComparer.Ordinal) { "name", "required", "min", "max", "type", "codes", "components", "description" };
    private static readonly HashSet<string> ElementTypes = new(StringComparer.Ordinal) { "an", "id", "n", "r", "dt", "tm", "a" };

    public required string Name { get; init; }
    public required string Standard { get; init; }
    public required string Message { get; init; }
    public string? Version { get; init; }
    public string? Title { get; init; }
    public required List<EdiItemDef> Items { get; init; }

    public static EdiGuide Parse(JsonObject json, FormatContext context, string where)
    {
        Keys(json, GuideKeys, where, context);
        var name = Text(json, "name", where, context) ?? throw context.Fail($"{where} needs a 'name'.");
        where = $"guide '{name}'";
        var standard = Text(json, "standard", where, context);
        if (standard is not ("x12" or "edifact")) throw context.Fail($"{where}: 'standard' must be x12 or edifact.");
        return new EdiGuide
        {
            Name = name,
            Standard = standard,
            Message = Text(json, "message", where, context) ?? throw context.Fail($"{where} needs 'message' (e.g. 850 or ORDERS)."),
            Version = Text(json, "version", where, context),
            Title = Text(json, "title", where, context),
            Items = ReadItems(json["segments"], $"{where}.segments", context),
        };
    }

    private static List<EdiItemDef> ReadItems(JsonNode? node, string where, FormatContext context)
    {
        if (node is not JsonArray array || array.Count == 0) throw context.Fail($"{where} must be a non-empty list of segments and loops.");
        var items = new List<EdiItemDef>();
        for (var i = 0; i < array.Count; i++)
        {
            var at = $"{where}[{i}]";
            if (array[i] is not JsonObject item) throw context.Fail($"{at} must be an object.");
            if (item.ContainsKey("loop"))
            {
                Keys(item, LoopKeys, at, context);
                var loopName = Text(item, "loop", at, context)!;
                var inner = ReadItems(item["segments"], $"{at}.segments", context);
                if (inner[0] is not EdiSegmentDef first) throw context.Fail($"Loop '{loopName}' must start with a segment, which marks each repeat.");
                items.Add(new EdiLoopDef(loopName, Int(item, "min", at, context) ?? 0, Int(item, "max", at, context) ?? 1, inner, first.Id));
            }
            else
            {
                Keys(item, SegmentKeys, at, context);
                var id = Text(item, "id", at, context) ?? throw context.Fail($"{at} needs an 'id' (a segment) or 'loop'.");
                var elements = new List<EdiElementDef>();
                if (item["elements"] is JsonArray list)
                {
                    for (var e = 0; e < list.Count; e++) elements.Add(Element(list[e], $"{at}.elements[{e}]", context));
                }
                else if (item["elements"] is not null)
                {
                    throw context.Fail($"{at}.elements must be a list.");
                }
                items.Add(new EdiSegmentDef(id, Text(item, "name", at, context) ?? id, Int(item, "min", at, context) ?? 0, Int(item, "max", at, context) ?? 1, elements));
            }
        }

        var keys = items.GroupBy(i => i.Key).FirstOrDefault(g => g.Count() > 1);
        if (keys is not null) throw context.Fail($"{where} has '{keys.Key}' twice at one level; give one of them a different 'name'.");
        foreach (var item in items.Where(i => i.Max < 1 || i.Min < 0 || i.Min > i.Max))
            throw context.Fail($"{where}: '{item.Key}' needs 0 <= min <= max and max >= 1.");
        return items;
    }

    private static EdiElementDef Element(JsonNode? node, string at, FormatContext context)
    {
        if (node is not JsonObject e) throw context.Fail($"{at} must be an object.");
        Keys(e, ElementKeys, at, context);
        var type = Text(e, "type", at, context);
        if (type is not null && !ElementTypes.Contains(type)) throw context.Fail($"{at}.type '{type}' is unknown. Types: {string.Join(", ", ElementTypes)}.");
        List<EdiElementDef>? components = null;
        if (e["components"] is JsonArray parts)
        {
            components = [];
            for (var c = 0; c < parts.Count; c++) components.Add(Element(parts[c], $"{at}.components[{c}]", context));
        }
        string[]? codes = null;
        if (e["codes"] is JsonArray codeList) codes = codeList.Select(c => c?.GetValue<string>() ?? "").ToArray();
        else if (e["codes"] is not null) throw context.Fail($"{at}.codes must be a list of texts.");
        return new EdiElementDef(Text(e, "name", at, context), Bool(e, "required", at, context) ?? false, Int(e, "min", at, context), Int(e, "max", at, context), type, codes, components);
    }

    private static void Keys(JsonObject obj, HashSet<string> allowed, string at, FormatContext context)
    {
        foreach (var pair in obj)
        {
            if (!allowed.Contains(pair.Key))
                throw context.Fail($"Unknown key '{pair.Key}' in {at}. Keys: {string.Join(", ", allowed.OrderBy(k => k, StringComparer.Ordinal))}.");
        }
    }

    private static string? Text(JsonObject obj, string key, string at, FormatContext context) => obj[key] switch
    {
        null => null,
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        _ => throw context.Fail($"{at}.{key} must be text."),
    };

    private static int? Int(JsonObject obj, string key, string at, FormatContext context) => obj[key] switch
    {
        null => null,
        JsonValue v when v.TryGetValue<int>(out var n) => n,
        _ => throw context.Fail($"{at}.{key} must be a whole number."),
    };

    private static bool? Bool(JsonObject obj, string key, string at, FormatContext context) => obj[key] switch
    {
        null => null,
        JsonValue v when v.GetValueKind() is JsonValueKind.True or JsonValueKind.False => v.GetValue<bool>(),
        _ => throw context.Fail($"{at}.{key} must be true or false."),
    };
}

/// <summary>A segment or a loop in a guide. <see cref="Key"/> is its name in the JSON.</summary>
internal abstract class EdiItemDef(string key, int min, int max)
{
    public string Key { get; } = key;
    public int Min { get; } = min;
    public int Max { get; } = max;

    /// <summary>The segment id that starts this item: the segment's own, or a loop's first.</summary>
    public abstract string Trigger { get; }
}

internal sealed class EdiSegmentDef(string id, string key, int min, int max, List<EdiElementDef> elements) : EdiItemDef(key, min, max)
{
    public string Id { get; } = id;
    public List<EdiElementDef> Elements { get; } = elements;
    public override string Trigger => Id;
}

internal sealed class EdiLoopDef(string name, int min, int max, List<EdiItemDef> items, string trigger) : EdiItemDef(name, min, max)
{
    public List<EdiItemDef> Items { get; } = items;
    public override string Trigger { get; } = trigger;
}

/// <summary>One element: its JSON name, whether it must have a value, its length range, type and allowed codes.</summary>
internal sealed record EdiElementDef(string? Name, bool Required, int? MinLength, int? MaxLength, string? Type, string[]? Codes, List<EdiElementDef>? Components)
{
    public static EdiElementDef Named(string name, bool required = false, List<EdiElementDef>? components = null) => new(name, required, null, null, null, null, components);
}
