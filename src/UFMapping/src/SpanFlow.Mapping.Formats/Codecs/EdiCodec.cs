using System.Text;
using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Formats.Codecs;

/// <summary>
/// EDI, level 1 (generic, no guide needed, so every message type works): X12 and EDIFACT read into envelopes and
/// segments whose elements are numbered. An element with components is a list.
/// <code>
/// X12:     { "interchange": { "ISA": [16 elements], "groups": [ { "GS": [...], "transactions": [ { "ST": [...], "segments": [ { "id": "BEG", "e": [...] } ] } ] } ] } }
/// EDIFACT: { "interchange": { "una": true, "UNB": [...], "messages": [ { "UNH": [...], "segments": [ ... ] } ] } }   (or "groups": [ { "UNG": [...], "messages": [...] } ])
/// </code>
/// Trailers (SE, GE, IEA, UNT, UNE, UNZ) are not in the JSON: writing computes their counts and control numbers.
/// Separators are read from the envelope; when they differ from the standard ones they are kept in
/// <c>interchange.separators</c> so writing gives them back. Options override both.
/// Level 2 (E73-T11): with the <c>guides</c> option, envelopes are named and each transaction with a guide reads
/// into named segments and loops (<c>body</c>), with a <c>problems</c> report; see <see cref="EdiLevel2"/>.
/// </summary>
public sealed class EdiCodec : IFormatCodec
{
    private static readonly IReadOnlyList<FormatOptionInfo> OptionList =
    [
        new("standard", FormatOptionKind.String, FormatOptionUse.Both, "x12 or edifact; found from the data when left out."),
        new("elementSeparator", FormatOptionKind.String, FormatOptionUse.EncodeOnly, "Element separator (X12 * , EDIFACT +)."),
        new("componentSeparator", FormatOptionKind.String, FormatOptionUse.EncodeOnly, "Component separator (both :)."),
        new("segmentTerminator", FormatOptionKind.String, FormatOptionUse.EncodeOnly, "Segment terminator (X12 ~ , EDIFACT ')."),
        new("releaseCharacter", FormatOptionKind.String, FormatOptionUse.EncodeOnly, "EDIFACT escape character (default ?)."),
        new("newline", FormatOptionKind.String, FormatOptionUse.EncodeOnly, "Written after each segment terminator, e.g. \\n (default none)."),
        new("guides", FormatOptionKind.Any, FormatOptionUse.Both, "Level 2: \"bundled\" (850, 810, 856, 997, ORDERS, INVOIC, DESADV), bundled guide names, your own guide objects, or a list of those. Envelopes get names and guided messages read into named segments and loops."),
        new("validate", FormatOptionKind.Bool, FormatOptionUse.Both, "With guides: stop at the first problem instead of listing problems beside the message (reading), and check what is written (writing)."),
    ];

    public string Type => "edi";

    public string ContentType => "text/plain";

    public IReadOnlyList<FormatOptionInfo> Options => OptionList;

    private sealed class Separators
    {
        public char Element { get; set; }
        public char Component { get; set; }
        public char Segment { get; set; }
        public char? Release { get; set; }
        public char Decimal { get; set; } = '.';

        public static Separators X12() => new() { Element = '*', Component = ':', Segment = '~' };

        public static Separators Edifact() => new() { Element = '+', Component = ':', Segment = '\'', Release = '?' };
    }

    private sealed class Segment(int number, string id, List<List<string>> elements)
    {
        public int Number { get; } = number;
        public string Id { get; } = id;
        public List<List<string>> Elements { get; } = elements;
    }

    // ---------------------------------------------------------------- decode

    public JsonNode? Decode(FormatPayload raw, FormatContext context)
    {
        var text = raw.AsText(context.Encoding).TrimStart();
        if (text.Length == 0) return null;

        var standard = context.GetString("standard")
            ?? (text.StartsWith("ISA", StringComparison.Ordinal) ? "x12"
                : text.StartsWith("UNA", StringComparison.Ordinal) || text.StartsWith("UNB", StringComparison.Ordinal) ? "edifact"
                : throw context.Fail("This does not look like EDI: an X12 file starts with ISA, an EDIFACT file with UNA or UNB.", new FormatLocation { Offset = 0 }));

        var level1 = standard switch
        {
            "x12" => DecodeX12(text, context),
            "edifact" => DecodeEdifact(text, context),
            _ => throw context.Fail($"Option 'standard' must be x12 or edifact, not '{standard}'."),
        };
        var level2 = EdiLevel2.From(context);
        return level2 is null ? level1 : level2.Name(level1);
    }

    private static JsonNode DecodeX12(string text, FormatContext context)
    {
        if (!text.StartsWith("ISA", StringComparison.Ordinal) || text.Length < 106)
            throw context.Fail("An X12 interchange starts with a 106-character ISA segment.", new FormatLocation { Segment = 1 });

        var separators = new Separators { Element = text[3], Component = text[104], Segment = text[105] };
        var interchange = new JsonObject();
        AddSeparators(interchange, separators, Separators.X12());

        // ISA is fixed width and its last element IS the component separator, so it is split by position, not tokenized.
        var isaParts = text.Substring(0, 105).Split(separators.Element);
        if (isaParts.Length != 17) throw context.Fail($"ISA has {isaParts.Length - 1} elements, X12 needs 16.", new FormatLocation { Segment = 1 });
        interchange["ISA"] = new JsonArray(isaParts.Skip(1).Select(p => (JsonNode?)JsonValue.Create(p)).ToArray());

        var segments = Tokenize(text.Substring(106), separators, context, firstNumber: 2);
        var index = 0;

        var groups = new JsonArray();
        interchange["groups"] = groups;
        while (index < segments.Count && segments[index].Id == "GS")
        {
            var gs = segments[index++];
            var transactions = new JsonArray();
            groups.Add(new JsonObject { ["GS"] = Elements(gs), ["transactions"] = transactions });

            while (index < segments.Count && segments[index].Id == "ST")
            {
                var st = segments[index++];
                var body = new JsonArray();
                while (index < segments.Count && segments[index].Id != "SE")
                {
                    if (segments[index].Id is "ST" or "GE" or "GS" or "IEA" or "ISA")
                        throw context.Fail($"ST at segment {st.Number} is not closed by SE before {segments[index].Id}.", new FormatLocation { Segment = segments[index].Number });
                    body.Add(SegmentJson(segments[index++]));
                }
                Expect(segments, ref index, "SE", context);
                transactions.Add(new JsonObject { ["ST"] = Elements(st), ["segments"] = body });
            }
            Expect(segments, ref index, "GE", context);
        }
        Expect(segments, ref index, "IEA", context);
        if (index < segments.Count)
            throw context.Fail($"Found {segments[index].Id} after IEA: one interchange per file.", new FormatLocation { Segment = segments[index].Number });

        return new JsonObject { ["interchange"] = interchange };
    }

    private static JsonNode DecodeEdifact(string text, FormatContext context)
    {
        var separators = Separators.Edifact();
        var interchange = new JsonObject();
        var una = text.StartsWith("UNA", StringComparison.Ordinal);
        if (una)
        {
            if (text.Length < 9) throw context.Fail("UNA must be followed by its six service characters.", new FormatLocation { Segment = 1 });
            separators.Component = text[3];
            separators.Element = text[4];
            separators.Decimal = text[5];
            separators.Release = text[6] == ' ' ? null : text[6];
            separators.Segment = text[8];
            text = text.Substring(9);
            interchange["una"] = true;
        }
        AddSeparators(interchange, separators, Separators.Edifact());

        var segments = Tokenize(text, separators, context, firstNumber: una ? 2 : 1);
        var index = 0;
        interchange["UNB"] = Elements(Expect(segments, ref index, "UNB", context));

        if (index < segments.Count && segments[index].Id == "UNG")
        {
            var groups = new JsonArray();
            interchange["groups"] = groups;
            while (index < segments.Count && segments[index].Id == "UNG")
            {
                var ung = segments[index++];
                groups.Add(new JsonObject { ["UNG"] = Elements(ung), ["messages"] = Messages(segments, ref index, context) });
                Expect(segments, ref index, "UNE", context);
            }
        }
        else
        {
            interchange["messages"] = Messages(segments, ref index, context);
        }

        Expect(segments, ref index, "UNZ", context);
        if (index < segments.Count)
            throw context.Fail($"Found {segments[index].Id} after UNZ: one interchange per file.", new FormatLocation { Segment = segments[index].Number });
        return new JsonObject { ["interchange"] = interchange };
    }

    private static JsonArray Messages(List<Segment> segments, ref int index, FormatContext context)
    {
        var messages = new JsonArray();
        while (index < segments.Count && segments[index].Id == "UNH")
        {
            var unh = segments[index++];
            var body = new JsonArray();
            while (index < segments.Count && segments[index].Id != "UNT")
            {
                if (segments[index].Id is "UNH" or "UNE" or "UNZ" or "UNG")
                    throw context.Fail($"UNH at segment {unh.Number} is not closed by UNT before {segments[index].Id}.", new FormatLocation { Segment = segments[index].Number });
                body.Add(SegmentJson(segments[index++]));
            }
            Expect(segments, ref index, "UNT", context);
            messages.Add(new JsonObject { ["UNH"] = Elements(unh), ["segments"] = body });
        }
        return messages;
    }

    private static Segment Expect(List<Segment> segments, ref int index, string id, FormatContext context)
    {
        if (index >= segments.Count) throw context.Fail($"The file ends where {id} was expected.", new FormatLocation { Segment = segments.Count + 1 });
        var segment = segments[index];
        if (segment.Id != id) throw context.Fail($"Expected {id} here, found {segment.Id}.", new FormatLocation { Segment = segment.Number });
        index++;
        return segment;
    }

    /// <summary>Segments, elements and components; the release character (EDIFACT) escapes the next character.</summary>
    private static List<Segment> Tokenize(string text, Separators separators, FormatContext context, int firstNumber = 1)
    {
        var segments = new List<Segment>();
        var elements = new List<List<string>>();
        var components = new List<string>();
        var current = new StringBuilder();
        var number = firstNumber;
        var atSegmentStart = true;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (atSegmentStart && c is '\r' or '\n' or ' ' or '\t') continue;
            atSegmentStart = false;

            if (separators.Release is { } release && c == release)
            {
                if (i + 1 >= text.Length) throw context.Fail("The file ends with a lone release character.", new FormatLocation { Segment = number });
                current.Append(text[++i]);
                continue;
            }
            if (c == separators.Component)
            {
                components.Add(current.ToString());
                current.Clear();
                continue;
            }
            if (c == separators.Element)
            {
                components.Add(current.ToString());
                current.Clear();
                elements.Add(components);
                components = new List<string>();
                continue;
            }
            if (c == separators.Segment)
            {
                components.Add(current.ToString());
                current.Clear();
                elements.Add(components);
                components = new List<string>();
                segments.Add(ToSegment(elements, number++, context));
                elements = new List<List<string>>();
                atSegmentStart = true;
                continue;
            }
            current.Append(c);
        }

        if (current.ToString().Trim().Length > 0 || elements.Count > 0)
            throw context.Fail($"The last segment is not ended by '{separators.Segment}'.", new FormatLocation { Segment = number });
        return segments;
    }

    private static Segment ToSegment(List<List<string>> elements, int number, FormatContext context)
    {
        var id = elements[0];
        if (id.Count != 1 || id[0].Length == 0) throw context.Fail("A segment must start with its id.", new FormatLocation { Segment = number });
        return new Segment(number, id[0], elements.Skip(1).ToList());
    }

    private static JsonArray Elements(Segment segment) =>
        new(segment.Elements.Select(e => e.Count == 1
            ? (JsonNode?)JsonValue.Create(e[0])
            : new JsonArray(e.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray())).ToArray());

    private static JsonObject SegmentJson(Segment segment) => new() { ["id"] = segment.Id, ["e"] = Elements(segment) };

    private static void AddSeparators(JsonObject interchange, Separators found, Separators standard)
    {
        var differ = new JsonObject();
        if (found.Element != standard.Element) differ["element"] = found.Element.ToString();
        if (found.Component != standard.Component) differ["component"] = found.Component.ToString();
        if (found.Segment != standard.Segment) differ["segment"] = found.Segment.ToString();
        if (found.Release != standard.Release) differ["release"] = found.Release?.ToString() ?? "";
        if (found.Decimal != standard.Decimal) differ["decimal"] = found.Decimal.ToString();
        if (differ.Count > 0) interchange["separators"] = differ;
    }

    // ---------------------------------------------------------------- encode

    private static readonly int[] IsaWidths = [2, 10, 2, 10, 2, 15, 2, 15, 6, 4, 1, 5, 9, 1, 1, 1];

    public FormatPayload Encode(JsonNode? value, FormatContext context)
    {
        var interchange = (value as JsonObject)?["interchange"] as JsonObject
            ?? throw context.Fail("EDI output needs { \"interchange\": { ... } } (the shape reading EDI gives).");

        var standard = context.GetString("standard")
            ?? (interchange.ContainsKey("ISA") ? "x12" : interchange.ContainsKey("UNB") ? "edifact"
                : throw context.Fail("Set the standard option, or give interchange an ISA (X12) or UNB (EDIFACT)."));
        if (standard is not ("x12" or "edifact")) throw context.Fail($"Option 'standard' must be x12 or edifact, not '{standard}'.");

        var level2 = EdiLevel2.From(context);
        if (level2 is not null) interchange = level2.Unname(interchange, standard);
        else if (interchange["ISA"] is JsonObject || interchange["UNB"] is JsonObject)
            throw context.Fail("The envelope has named elements (level 2): set the guides option to write it.");

        var writer = standard switch
        {
            "x12" => new Writer(context, Resolve(Separators.X12(), interchange, context)),
            "edifact" => new Writer(context, Resolve(Separators.Edifact(), interchange, context)),
            _ => throw context.Fail($"Option 'standard' must be x12 or edifact, not '{standard}'."),
        };
        return FormatPayload.FromText(standard == "x12" ? writer.X12(interchange) : writer.Edifact(interchange));
    }

    private static Separators Resolve(Separators separators, JsonObject interchange, FormatContext context)
    {
        if (interchange["separators"] is JsonObject kept)
        {
            foreach (var pair in kept)
            {
                if (!CodecText.TryScalar(pair.Value, out var text)) throw context.Fail($"separators.{pair.Key} must be one character.");
                Apply(separators, pair.Key, text ?? "", context);
            }
        }
        Apply(separators, "element", context.GetString("elementSeparator"), context);
        Apply(separators, "component", context.GetString("componentSeparator"), context);
        Apply(separators, "segment", context.GetString("segmentTerminator"), context);
        Apply(separators, "release", context.GetString("releaseCharacter"), context);

        var used = new[] { separators.Element, separators.Component, separators.Segment };
        if (used.Distinct().Count() != used.Length || (separators.Release is { } r && used.Contains(r)))
            throw context.Fail("The element, component and segment separators and the release character must all differ.");
        return separators;
    }

    private static void Apply(Separators separators, string key, string? text, FormatContext context)
    {
        if (text is null) return;
        if (key == "release" && text.Length == 0)
        {
            separators.Release = null;
            return;
        }
        if (text.Length != 1) throw context.Fail($"The {key} separator must be one character, not '{text}'.");
        var c = text[0];
        switch (key)
        {
            case "element": separators.Element = c; break;
            case "component": separators.Component = c; break;
            case "segment": separators.Segment = c; break;
            case "release": separators.Release = c; break;
            case "decimal": separators.Decimal = c; break;
            default: throw context.Fail($"Unknown separator '{key}'. Use element, component, segment, release or decimal.");
        }
    }

    private sealed class Writer(FormatContext context, Separators separators)
    {
        private readonly StringBuilder _out = new();
        private readonly string _newline = CodecText.Separator(context.GetString("newline", "")!);
        private int _segmentNumber;

        public string X12(JsonObject interchange)
        {
            var isa = List(interchange["ISA"], "ISA");
            if (isa.Count != 16) throw Fail($"ISA needs 16 elements, the mapping gave {isa.Count}.", "ISA");
            var isaText = new List<string>();
            for (var i = 0; i < 16; i++)
            {
                var text = i == 15 ? separators.Component.ToString() : Scalar(isa[i], $"ISA{i + 1:00}");
                if (text.Length > IsaWidths[i]) throw Fail($"ISA{i + 1:00} is {text.Length} characters, it holds {IsaWidths[i]}.", "ISA");
                isaText.Add(i == 12 ? text.PadLeft(IsaWidths[i], '0') : text.PadRight(IsaWidths[i]));
            }
            WriteRaw("ISA", isaText);

            var groups = List(interchange["groups"] ?? new JsonArray(), "groups");
            foreach (var groupNode in groups)
            {
                var group = Obj(groupNode, "groups[]");
                var gs = List(group["GS"], "GS");
                Write("GS", gs);
                var transactions = List(group["transactions"] ?? new JsonArray(), "transactions");
                foreach (var txNode in transactions)
                {
                    var tx = Obj(txNode, "transactions[]");
                    var st = List(tx["ST"], "ST");
                    Write("ST", st);
                    var body = List(tx["segments"] ?? new JsonArray(), "segments");
                    foreach (var segment in body) WriteSegment(segment);
                    WriteRaw("SE", [(body.Count + 2).ToString(), Element(st, 1, "ST02")]);
                }
                WriteRaw("GE", [transactions.Count.ToString(), Element(gs, 5, "GS06")]);
            }
            WriteRaw("IEA", [groups.Count.ToString(), isaText[12]]);
            return _out.ToString();
        }

        public string Edifact(JsonObject interchange)
        {
            var standard = Separators.Edifact();
            var custom = separators.Element != standard.Element || separators.Component != standard.Component || separators.Segment != standard.Segment
                || separators.Release != standard.Release || separators.Decimal != standard.Decimal;
            if (interchange["una"] is JsonValue una && una.TryGetValue<bool>(out var wanted) && wanted || custom)
            {
                _out.Append("UNA").Append(separators.Component).Append(separators.Element).Append(separators.Decimal)
                    .Append(separators.Release ?? ' ').Append(' ').Append(separators.Segment).Append(_newline);
            }

            var unb = List(interchange["UNB"], "UNB");
            Write("UNB", unb);
            int count;
            if (interchange["groups"] is JsonArray groups)
            {
                foreach (var groupNode in groups)
                {
                    var group = Obj(groupNode, "groups[]");
                    var ung = List(group["UNG"], "UNG");
                    Write("UNG", ung);
                    var messages = WriteMessages(group["messages"]);
                    WriteRaw("UNE", [messages.ToString(), Element(ung, 4, "UNG05")]);
                }
                count = groups.Count;
            }
            else
            {
                count = WriteMessages(interchange["messages"]);
            }
            WriteRaw("UNZ", [count.ToString(), Element(unb, 4, "UNB05")]);
            return _out.ToString();
        }

        private int WriteMessages(JsonNode? node)
        {
            var messages = List(node ?? new JsonArray(), "messages");
            foreach (var messageNode in messages)
            {
                var message = Obj(messageNode, "messages[]");
                var unh = List(message["UNH"], "UNH");
                Write("UNH", unh);
                var body = List(message["segments"] ?? new JsonArray(), "segments");
                foreach (var segment in body) WriteSegment(segment);
                WriteRaw("UNT", [(body.Count + 2).ToString(), Element(unh, 0, "UNH01")]);
            }
            return messages.Count;
        }

        private void WriteSegment(JsonNode? node)
        {
            var segment = Obj(node, "segments[]");
            if (!CodecText.TryScalar(segment["id"], out var id) || string.IsNullOrEmpty(id)) throw Fail("Each segment needs an \"id\", e.g. \"BEG\".", "segments[]");
            Write(id!, segment["e"] is null ? new JsonArray() : List(segment["e"], id!));
        }

        private void Write(string id, JsonArray elements)
        {
            var texts = new List<string>();
            for (var i = 0; i < elements.Count; i++)
            {
                var where = $"{id}{i + 1:00}";
                texts.Add(elements[i] is JsonArray components
                    ? string.Join(separators.Component.ToString(), components.Select((c, k) => Escape(Scalar(c, $"{where}-{k + 1}"), where)))
                    : Escape(Scalar(elements[i], where), where));
            }
            WriteRaw(id, texts);
        }

        private void WriteRaw(string id, IEnumerable<string> elements)
        {
            _segmentNumber++;
            _out.Append(id);
            foreach (var element in elements) _out.Append(separators.Element).Append(element);
            _out.Append(separators.Segment).Append(_newline);
        }

        private string Escape(string text, string where)
        {
            var special = new[] { separators.Element, separators.Component, separators.Segment };
            if (text.IndexOfAny(special) < 0 && (separators.Release is not { } r0 || text.IndexOf(r0) < 0)) return text;
            if (separators.Release is not { } release)
                throw Fail($"{where} holds a separator character ('{text}') and X12 has no way to escape it.", where);
            var escaped = new StringBuilder();
            foreach (var c in text)
            {
                if (c == release || special.Contains(c)) escaped.Append(release);
                escaped.Append(c);
            }
            return escaped.ToString();
        }

        private string Scalar(JsonNode? node, string where) =>
            CodecText.TryScalar(node, out var text) ? text ?? "" : throw Fail($"{where} must be a single value (or a list of components for an element).", where);

        private string Element(JsonArray elements, int index, string where) =>
            index < elements.Count ? (elements[index] is JsonArray c ? Scalar(c.FirstOrDefault(), where) : Scalar(elements[index], where))
                : throw Fail($"{where} is needed to write the trailer's control reference.", where);

        private JsonArray List(JsonNode? node, string where) =>
            node as JsonArray ?? throw Fail($"{where} must be a list.", where);

        private JsonObject Obj(JsonNode? node, string where) =>
            node as JsonObject ?? throw Fail($"Each item of {where.TrimEnd('[', ']')} must be an object.", where);

        private MappingFormatException Fail(string reason, string where) =>
            context.Fail(reason, new FormatLocation { Segment = _segmentNumber + 1, Field = where });
    }
}
