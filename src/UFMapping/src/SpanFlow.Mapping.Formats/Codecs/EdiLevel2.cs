using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SpanFlow.Mapping.Formats.Codecs;

/// <summary>
/// E73-T11: EDI level 2, a JSON-to-JSON layer over level 1 so the tokenizer and writer stay as they are.
/// Reading: the envelope segments get names (ISA.senderId, UNB.sender.id ...) and each transaction / message with a
/// guide becomes <c>{ "ST": {...}, "guide": "x12-850", "body": { "BEG": {...}, "PO1Loop": [ ... ] }, "problems": [...] }</c>.
/// Problems (missing mandatory segment, element too long, wrong code ...) are listed, not thrown, so a bad partner
/// file still maps; <c>validate</c> stops at the first instead. Writing turns the named JSON back into level 1. A
/// transaction with no guide keeps its numbered <c>segments</c>.
/// </summary>
internal sealed class EdiLevel2
{
    private const string BundledPrefix = "SpanFlow.Mapping.Formats.Guides.";
    private static readonly object BundledLock = new();
    private static List<EdiGuide>? _bundled;

    private readonly List<EdiGuide> _guides;
    private readonly FormatContext _context;
    private readonly bool _validate;

    private EdiLevel2(List<EdiGuide> guides, FormatContext context, bool validate)
    {
        _guides = guides;
        _context = context;
        _validate = validate;
    }

    /// <summary>The level-2 layer when the <c>guides</c> option is set; null means level 1 only.</summary>
    public static EdiLevel2? From(FormatContext context)
    {
        var option = context.Get("guides");
        if (option is null) return null;
        var guides = new List<EdiGuide>();
        var items = option is JsonArray list ? list.ToList() : [option];
        for (var i = 0; i < items.Count; i++)
        {
            switch (items[i])
            {
                case JsonObject own:
                    guides.Add(EdiGuide.Parse(own, context, $"guides[{i}]"));
                    break;
                case JsonValue v when v.TryGetValue<string>(out var name) && name == "bundled":
                    guides.AddRange(Bundled(context));
                    break;
                case JsonValue v when v.TryGetValue<string>(out var name):
                    guides.Add(Bundled(context).FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase))
                        ?? throw context.Fail($"There is no bundled guide '{name}'. Bundled: {string.Join(", ", Bundled(context).Select(g => g.Name))}; or give a guide object."));
                    break;
                default:
                    throw context.Fail("Option 'guides' takes \"bundled\", a bundled guide's name, a guide object, or a list of those.");
            }
        }
        return new EdiLevel2(guides, context, context.GetBool("validate"));
    }

    /// <summary>The guides shipped in the assembly (Guides/*.json), read once.</summary>
    public static IReadOnlyList<EdiGuide> Bundled(FormatContext context)
    {
        lock (BundledLock)
        {
            if (_bundled is not null) return _bundled;
            var assembly = typeof(EdiLevel2).GetTypeInfo().Assembly;
            var list = new List<EdiGuide>();
            foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(BundledPrefix, StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal))
            {
                using var stream = assembly.GetManifestResourceStream(resource)!;
                list.Add(EdiGuide.Parse(JsonNode.Parse(stream)!.AsObject(), context, resource.Substring(BundledPrefix.Length)));
            }
            return _bundled = list;
        }
    }

    private EdiGuide? Find(string standard, string? message) =>
        message is null ? null : _guides.FirstOrDefault(g => g.Standard == standard && g.Message == message);

    // ================================================================ reading

    private sealed class Reading(EdiLevel2 owner)
    {
        public int Number;
        public readonly List<JsonObject> Problems = [];

        public void Problem(int segment, string id, string? element, string problem)
        {
            var entry = new JsonObject { ["segment"] = segment, ["id"] = id };
            if (element is not null) entry["element"] = element;
            entry["problem"] = problem;
            if (owner._validate)
                throw owner._context.Fail($"Segment {segment} ({id}){(element is null ? "" : " " + element)}: {problem}", new FormatLocation { Segment = segment, Field = element ?? id });
            Problems.Add(entry);
        }
    }

    /// <summary>Level-1 JSON in, named JSON out.</summary>
    public JsonNode Name(JsonNode level1)
    {
        var interchange = (JsonObject)level1["interchange"]!;
        var reading = new Reading(this);
        var x12 = interchange.ContainsKey("ISA");
        if (x12)
        {
            reading.Number++;
            interchange["ISA"] = Named((JsonArray)interchange["ISA"]!, EdiEnvelope.Isa, trim: true);
            foreach (var group in (JsonArray)interchange["groups"]!)
            {
                reading.Number++;
                group!["GS"] = Named((JsonArray)group["GS"]!, EdiEnvelope.Gs);
                var transactions = (JsonArray)group["transactions"]!;
                for (var t = 0; t < transactions.Count; t++) transactions[t] = Transaction((JsonObject)transactions[t]!, "ST", EdiEnvelope.St, "x12", reading);
                reading.Number++; // GE
            }
        }
        else
        {
            if (interchange["una"] is not null) reading.Number++;
            reading.Number++;
            interchange["UNB"] = Named((JsonArray)interchange["UNB"]!, EdiEnvelope.Unb);
            if (interchange["groups"] is JsonArray groups)
            {
                foreach (var group in groups)
                {
                    reading.Number++;
                    group!["UNG"] = Named((JsonArray)group["UNG"]!, EdiEnvelope.Ung);
                    Messages((JsonArray)group["messages"]!, reading);
                    reading.Number++; // UNE
                }
            }
            else
            {
                Messages((JsonArray)interchange["messages"]!, reading);
            }
        }
        return level1;
    }

    private void Messages(JsonArray messages, Reading reading)
    {
        for (var m = 0; m < messages.Count; m++) messages[m] = Transaction((JsonObject)messages[m]!, "UNH", EdiEnvelope.Unh, "edifact", reading);
    }

    private JsonObject Transaction(JsonObject tx, string header, EdiSegmentDef headerDef, string standard, Reading reading)
    {
        reading.Number++;
        var headerElements = (JsonArray)tx[header]!;
        var segments = (JsonArray)tx["segments"]!;
        var first = reading.Number + 1;
        reading.Number += segments.Count + 1; // the body, then SE / UNT

        var guide = Find(standard, MessageOf(headerElements, standard));
        var named = new JsonObject { [header] = Named(headerElements, headerDef) };
        if (guide is null)
        {
            named["segments"] = segments.DeepClone();
            return named;
        }

        var problems = new Reading(this);
        var list = segments.Select((s, i) => (Segment: (JsonObject)s!, Number: first + i)).ToList();
        var pos = 0;
        var body = Level(guide.Items, list, ref pos, top: true, problems, trailer: reading.Number);
        named["guide"] = guide.Name;
        named["body"] = body;
        if (problems.Problems.Count > 0) named["problems"] = new JsonArray(problems.Problems.ToArray<JsonNode?>());
        return named;
    }

    private static string? MessageOf(JsonArray header, string standard)
    {
        var node = standard == "x12" ? header.ElementAtOrDefault(0) : header.ElementAtOrDefault(1);
        if (node is JsonArray components) node = components.FirstOrDefault();
        return CodecText.TryScalar(node, out var text) ? text : null;
    }

    /// <summary>
    /// Reads one level of a guide: each segment is matched to the first item at or after the current one that it
    /// starts (and that has repeats left). A segment no item here takes ends a loop's level; at the top it is kept
    /// in <c>unexpected</c> and reported.
    /// </summary>
    private JsonObject Level(List<EdiItemDef> items, List<(JsonObject Segment, int Number)> segments, ref int pos, bool top, Reading reading, int trailer)
    {
        var obj = new JsonObject();
        var counts = new int[items.Count];
        // Where the reading moved past each item: a missing one is reported at the first segment after its place.
        var passed = new int?[items.Count];
        var at = 0;
        while (pos < segments.Count)
        {
            var (segment, number) = segments[pos];
            var id = segment["id"]!.GetValue<string>();
            var k = -1;
            for (var j = at; j < items.Count; j++)
            {
                if (items[j].Trigger == id && counts[j] < items[j].Max)
                {
                    k = j;
                    break;
                }
            }

            if (k < 0)
            {
                if (!top) break;
                reading.Problem(number, id, null, "This segment is not where the guide expects it: unknown here, out of order, or repeated more often than allowed. It is kept in 'unexpected'.");
                if (obj["unexpected"] is not JsonArray unexpected) obj["unexpected"] = unexpected = new JsonArray();
                unexpected.Add(segment.DeepClone());
                pos++;
                continue;
            }

            for (var j = at; j < k; j++) passed[j] ??= number;
            at = k;
            JsonNode value;
            if (items[k] is EdiSegmentDef def)
            {
                var elements = segment["e"] as JsonArray ?? new JsonArray();
                Check(elements, def, number, reading);
                value = Named(elements, def);
                pos++;
            }
            else
            {
                value = Level(((EdiLoopDef)items[k]).Items, segments, ref pos, top: false, reading, trailer);
            }
            Add(obj, items[k], value);
            counts[k]++;
        }

        var next = pos < segments.Count ? segments[pos].Number : trailer;
        for (var j = 0; j < items.Count; j++)
        {
            if (counts[j] < items[j].Min)
                reading.Problem(passed[j] ?? next, items[j].Trigger, null, $"Missing {items[j].Key}: the guide needs {(items[j].Min == 1 ? "it" : $"at least {items[j].Min}")}, found {counts[j]}.");
        }
        return obj;
    }

    private static void Add(JsonObject obj, EdiItemDef item, JsonNode value)
    {
        if (item.Max == 1)
        {
            obj[item.Key] = value;
            return;
        }
        if (obj[item.Key] is not JsonArray list) obj[item.Key] = list = new JsonArray();
        list.Add(value);
    }

    /// <summary>Numbered elements to named ones. Unnamed or extra ones are called by place: BEG06, PID05-2.</summary>
    private static JsonObject Named(JsonArray elements, EdiSegmentDef def, bool trim = false)
    {
        var obj = new JsonObject();
        for (var i = 0; i < elements.Count; i++)
        {
            var element = i < def.Elements.Count ? def.Elements[i] : null;
            var place = $"{def.Id}{i + 1:00}";
            var key = element?.Name ?? place;
            var raw = elements[i];
            if (element?.Components is { } parts)
            {
                var values = raw is JsonArray list ? list.Select(Text).ToList() : [Text(raw)];
                var composite = new JsonObject();
                for (var c = 0; c < values.Count; c++)
                {
                    if (values[c].Length > 0) composite[c < parts.Count && parts[c].Name is { } n ? n : $"{place}-{c + 1}"] = values[c];
                }
                if (composite.Count > 0) obj[key] = composite;
            }
            else if (raw is JsonArray components)
            {
                obj[key] = components.DeepClone();
            }
            else
            {
                var text = Text(raw);
                if (trim) text = text.TrimEnd();
                if (text.Length > 0) obj[key] = text;
            }
        }
        return obj;
    }

    private static string Text(JsonNode? node) => CodecText.TryScalar(node, out var text) ? text ?? "" : "";

    // ---------------------------------------------------------------- element checks

    private static readonly Regex NumericN = new(@"^-?[0-9]+$", RegexOptions.CultureInvariant);
    private static readonly Regex DateDt = new(@"^([0-9]{6}|[0-9]{8})$", RegexOptions.CultureInvariant);
    private static readonly Regex TimeTm = new(@"^[0-9]{4}([0-9]{2}([0-9]{1,2})?)?$", RegexOptions.CultureInvariant);

    private static void Check(JsonArray elements, EdiSegmentDef def, int number, Reading reading)
    {
        for (var i = 0; i < def.Elements.Count; i++)
        {
            var element = def.Elements[i];
            var place = $"{def.Id}{i + 1:00}";
            var raw = i < elements.Count ? elements[i] : null;
            if (element.Components is { } parts)
            {
                var values = raw is JsonArray list ? list.Select(Text).ToList() : [Text(raw)];
                if (values.All(v => v.Length == 0))
                {
                    if (element.Required) reading.Problem(number, def.Id, place, $"{Label(place, element)} is required.");
                    continue;
                }
                for (var c = 0; c < parts.Count; c++) CheckValue(c < values.Count ? values[c] : "", parts[c], $"{place}-{c + 1}", def.Id, number, reading);
                continue;
            }
            CheckValue(raw is JsonArray components ? Text(components.FirstOrDefault()) : Text(raw), element, place, def.Id, number, reading);
        }
    }

    private static void CheckValue(string value, EdiElementDef element, string place, string id, int number, Reading reading)
    {
        if (value.Length == 0)
        {
            if (element.Required) reading.Problem(number, id, place, $"{Label(place, element)} is required.");
            return;
        }
        var label = Label(place, element);
        if (element.MaxLength is { } max && value.Length > max) reading.Problem(number, id, place, $"{label} is {value.Length} characters, more than {max}.");
        if (element.MinLength is { } min && value.Length < min) reading.Problem(number, id, place, $"{label} is {value.Length} characters, fewer than {min}.");
        var typeOk = element.Type switch
        {
            "n" => NumericN.IsMatch(value),
            "r" => decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _),
            "dt" => DateDt.IsMatch(value),
            "tm" => TimeTm.IsMatch(value),
            _ => true,
        };
        if (!typeOk) reading.Problem(number, id, place, $"{label} '{value}' is not {TypeName(element.Type!)}.");
        if (element.Codes is { Length: > 0 } codes && Array.IndexOf(codes, value) < 0)
            reading.Problem(number, id, place, $"{label} '{value}' is not one of the guide's codes ({string.Join(", ", codes.Take(12))}{(codes.Length > 12 ? ", ..." : "")}).");
    }

    private static string Label(string place, EdiElementDef element) => element.Name is null ? place : $"{place} ({element.Name})";

    private static string TypeName(string type) => type switch
    {
        "n" => "a whole number",
        "r" => "a number",
        "dt" => "a date (YYMMDD or CCYYMMDD)",
        "tm" => "a time (HHMM, HHMMSS ...)",
        _ => type,
    };

    // ================================================================ writing

    /// <summary>Named JSON (or a mix, or level 1) in, level-1 JSON out for the writer. One group or transaction may be an object.</summary>
    public JsonObject Unname(JsonObject interchange, string standard)
    {
        var level1 = new JsonObject();
        foreach (var pair in interchange) level1[pair.Key] = pair.Value?.DeepClone();

        var number = 0;
        if (standard == "x12")
        {
            level1["ISA"] = Envelope(interchange["ISA"], EdiEnvelope.Isa, "ISA", pad: 16);
            number++;
            var groups = new JsonArray();
            foreach (var groupNode in Many(interchange["groups"], "groups"))
            {
                var group = Obj(groupNode, "groups[]");
                number++;
                var transactions = new JsonArray();
                foreach (var tx in Many(group["transactions"], "transactions")) transactions.Add(WriteTransaction(Obj(tx, "transactions[]"), "ST", EdiEnvelope.St, "x12", ref number));
                number++;
                groups.Add(new JsonObject { ["GS"] = Envelope(group["GS"], EdiEnvelope.Gs, "GS"), ["transactions"] = transactions });
            }
            level1["groups"] = groups;
        }
        else
        {
            level1["UNB"] = Envelope(interchange["UNB"], EdiEnvelope.Unb, "UNB");
            number += interchange["una"] is null ? 1 : 2;
            if (interchange["groups"] is not null)
            {
                var groups = new JsonArray();
                foreach (var groupNode in Many(interchange["groups"], "groups"))
                {
                    var group = Obj(groupNode, "groups[]");
                    number++;
                    var messages = new JsonArray();
                    foreach (var m in Many(group["messages"], "messages")) messages.Add(WriteTransaction(Obj(m, "messages[]"), "UNH", EdiEnvelope.Unh, "edifact", ref number));
                    number++;
                    groups.Add(new JsonObject { ["UNG"] = Envelope(group["UNG"], EdiEnvelope.Ung, "UNG"), ["messages"] = messages });
                }
                level1["groups"] = groups;
            }
            else
            {
                var messages = new JsonArray();
                foreach (var m in Many(interchange["messages"], "messages")) messages.Add(WriteTransaction(Obj(m, "messages[]"), "UNH", EdiEnvelope.Unh, "edifact", ref number));
                level1["messages"] = messages;
            }
        }
        return level1;
    }

    private JsonObject WriteTransaction(JsonObject tx, string header, EdiSegmentDef headerDef, string standard, ref int number)
    {
        var headerElements = Envelope(tx[header], headerDef, header);
        number++;
        var result = new JsonObject { [header] = headerElements };
        if (tx["body"] is null)
        {
            result["segments"] = tx["segments"]?.DeepClone() ?? new JsonArray();
            number += ((JsonArray)result["segments"]!).Count + 1;
            return result;
        }

        var guideName = CodecText.TryScalar(tx["guide"], out var g) ? g : null;
        var message = MessageOf(headerElements, standard);
        var guide = guideName is not null
            ? _guides.FirstOrDefault(x => string.Equals(x.Name, guideName, StringComparison.OrdinalIgnoreCase))
                ?? throw _context.Fail($"The transaction names guide '{guideName}', which the guides option does not include.")
            : Find(standard, message) ?? throw _context.Fail($"No guide for {standard} message '{message}': add one to the guides option, or write numbered 'segments' instead of 'body'.");

        var segments = new JsonArray();
        var reading = new Reading(this);
        Flatten(Obj(tx["body"], "body"), guide.Items, segments, top: true, ref number, reading, guide.Name);
        number++;
        result["segments"] = segments;
        return result;
    }

    private void Flatten(JsonObject obj, List<EdiItemDef> items, JsonArray output, bool top, ref int number, Reading reading, string where)
    {
        foreach (var pair in obj)
        {
            if (top && pair.Key == "unexpected") continue;
            if (!items.Any(i => i.Key == pair.Key))
                throw _context.Fail($"{where} has no '{pair.Key}' at this level. Here: {string.Join(", ", items.Select(i => i.Key))}.", new FormatLocation { Field = $"{where}.{pair.Key}" });
        }

        foreach (var item in items)
        {
            var instances = obj[item.Key] is null ? [] : Many(obj[item.Key], $"{where}.{item.Key}");
            if (instances.Count < item.Min) reading.Problem(number + 1, item.Trigger, null, $"Missing {item.Key}: the guide needs {(item.Min == 1 ? "it" : $"at least {item.Min}")}, the mapping gave {instances.Count}.");
            if (instances.Count > item.Max) reading.Problem(number + 1, item.Trigger, null, $"{item.Key} repeats {instances.Count} times, the guide allows {item.Max}.");
            foreach (var instance in instances)
            {
                if (item is EdiSegmentDef def)
                {
                    var elements = Unnamed(Obj(instance, $"{where}.{item.Key}"), def, $"{where}.{item.Key}");
                    number++;
                    Check(elements, def, number, reading);
                    output.Add(new JsonObject { ["id"] = def.Id, ["e"] = elements });
                }
                else
                {
                    Flatten(Obj(instance, $"{where}.{item.Key}"), ((EdiLoopDef)item).Items, output, top: false, ref number, reading, $"{where}.{item.Key}");
                }
            }
        }

        // Kept from reading: no place is known for them, so they go after the rest.
        if (top && obj["unexpected"] is JsonArray unexpected)
        {
            foreach (var segment in unexpected)
            {
                output.Add(segment?.DeepClone());
                number++;
            }
        }
    }

    private JsonArray Envelope(JsonNode? node, EdiSegmentDef def, string where, int pad = 0) => node switch
    {
        JsonArray array => (JsonArray)array.DeepClone(),
        JsonObject named => Unnamed(named, def, where, pad),
        null => throw _context.Fail($"{where} is missing.", new FormatLocation { Field = where }),
        _ => throw _context.Fail($"{where} must be a list of elements or an object of named ones.", new FormatLocation { Field = where }),
    };

    /// <summary>Named elements to numbered ones; a key that is neither a guide name nor a place (BEG06, PID05-2) is refused.</summary>
    private JsonArray Unnamed(JsonObject named, EdiSegmentDef def, string where, int pad = 0)
    {
        var values = new SortedDictionary<int, List<string>>();
        var place = new Regex("^" + Regex.Escape(def.Id) + @"([0-9]{2})(?:-([0-9]+))?$", RegexOptions.CultureInvariant);

        void Set(int index, int component, string text)
        {
            if (!values.TryGetValue(index, out var parts)) values[index] = parts = [];
            while (parts.Count <= component) parts.Add("");
            parts[component] = text;
        }

        foreach (var pair in named)
        {
            var index = def.Elements.FindIndex(e => e.Name == pair.Key);
            var component = -1;
            if (index < 0)
            {
                var match = place.Match(pair.Key);
                if (!match.Success)
                {
                    var names = def.Elements.Select((e, i) => e.Name ?? $"{def.Id}{i + 1:00}");
                    throw _context.Fail($"{def.Id} has no element '{pair.Key}'. Elements: {string.Join(", ", names)}.", new FormatLocation { Field = $"{where}.{pair.Key}" });
                }
                index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) - 1;
                if (match.Groups[2].Success) component = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) - 1;
                if (index < 0 || component < -1) throw _context.Fail($"'{pair.Key}' is not a place in {def.Id}: positions start at 01.", new FormatLocation { Field = $"{where}.{pair.Key}" });
            }

            var parts = index < def.Elements.Count ? def.Elements[index].Components : null;
            switch (pair.Value)
            {
                case null:
                    break;
                case JsonObject composite:
                    foreach (var part in composite)
                    {
                        var c = parts?.FindIndex(p => p.Name == part.Key) ?? -1;
                        if (c < 0)
                        {
                            var m = place.Match(part.Key);
                            if (!m.Success || !m.Groups[2].Success)
                                throw _context.Fail($"{def.Id}.{pair.Key} has no component '{part.Key}'.", new FormatLocation { Field = $"{where}.{pair.Key}.{part.Key}" });
                            c = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) - 1;
                        }
                        Set(index, c, Scalar(part.Value, $"{where}.{pair.Key}.{part.Key}"));
                    }
                    break;
                case JsonArray list:
                    for (var c = 0; c < list.Count; c++) Set(index, c, Scalar(list[c], $"{where}.{pair.Key}[{c}]"));
                    break;
                default:
                    Set(index, Math.Max(component, 0), Scalar(pair.Value, $"{where}.{pair.Key}"));
                    break;
            }
        }

        var count = Math.Max(pad, values.Count == 0 ? 0 : values.Keys.Max() + 1);
        var elements = new JsonArray();
        for (var i = 0; i < count; i++)
        {
            if (!values.TryGetValue(i, out var parts))
            {
                elements.Add("");
                continue;
            }
            while (parts.Count > 1 && parts[parts.Count - 1].Length == 0) parts.RemoveAt(parts.Count - 1);
            elements.Add(parts.Count == 1 ? JsonValue.Create(parts[0]) : new JsonArray(parts.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()));
        }
        while (elements.Count > pad && elements.Count > 0 && elements[elements.Count - 1] is JsonValue last && last.GetValue<string>().Length == 0) elements.RemoveAt(elements.Count - 1);
        return elements;
    }

    private string Scalar(JsonNode? node, string where) =>
        CodecText.TryScalar(node, out var text) ? text ?? "" : throw _context.Fail($"{where} must be a single value.", new FormatLocation { Field = where });

    private List<JsonNode?> Many(JsonNode? node, string where) => node switch
    {
        null => [],
        JsonArray list => list.ToList(),
        JsonObject one => [one],
        _ => throw _context.Fail($"{where} must be an object or a list of objects.", new FormatLocation { Field = where }),
    };

    private JsonObject Obj(JsonNode? node, string where) =>
        node as JsonObject ?? throw _context.Fail($"{where} must be an object.", new FormatLocation { Field = where });
}

/// <summary>Names for the envelope segments, the same for every message type.</summary>
internal static class EdiEnvelope
{
    private static EdiElementDef E(string name, params string[] components) =>
        EdiElementDef.Named(name, components: components.Length == 0 ? null : components.Select(c => EdiElementDef.Named(c)).ToList());

    private static EdiSegmentDef Def(string id, params EdiElementDef[] elements) => new(id, id, 1, 1, elements.ToList());

    public static readonly EdiSegmentDef Isa = Def("ISA",
        E("authorizationQualifier"), E("authorizationInfo"), E("securityQualifier"), E("securityInfo"),
        E("senderQualifier"), E("senderId"), E("receiverQualifier"), E("receiverId"), E("date"), E("time"),
        E("standardsId"), E("version"), E("controlNumber"), E("ackRequested"), E("usage"), E("componentSeparator"));

    public static readonly EdiSegmentDef Gs = Def("GS",
        E("functionalId"), E("senderCode"), E("receiverCode"), E("date"), E("time"), E("controlNumber"), E("agency"), E("version"));

    public static readonly EdiSegmentDef St = Def("ST", E("code"), E("controlNumber"), E("implementationReference"));

    public static readonly EdiSegmentDef Unb = Def("UNB",
        E("syntax", "id", "version"), E("sender", "id", "qualifier", "routing"), E("recipient", "id", "qualifier", "routing"),
        E("preparation", "date", "time"), E("controlReference"), E("recipientPassword", "password", "qualifier"),
        E("applicationReference"), E("priority"), E("ackRequest"), E("agreementId"), E("testIndicator"));

    public static readonly EdiSegmentDef Ung = Def("UNG",
        E("groupId"), E("applicationSender", "id", "qualifier"), E("applicationRecipient", "id", "qualifier"),
        E("preparation", "date", "time"), E("groupReference"), E("agency"), E("messageVersion", "version", "release", "association"), E("password"));

    public static readonly EdiSegmentDef Unh = Def("UNH",
        E("reference"), E("type", "type", "version", "release", "agency", "association"), E("commonAccessReference"), E("transferStatus", "sequence", "firstLast"));
}
