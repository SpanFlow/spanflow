using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Formats.Codecs;

/// <summary>
/// A positional layout read from the <c>layout</c> option (D73.7: a layout is data, never code). Either
/// <c>fields</c> (the whole payload is one record) or <c>records</c> (one record per line, each line's kind chosen by
/// <c>when</c>). Every key is checked, so a typo fails with the place it is in.
/// </summary>
internal sealed class PositionalLayout
{
    private static readonly HashSet<string> Types = new(StringComparer.Ordinal)
    {
        "text", "alpha", "alphanumeric", "numeric", "integer", "decimal", "date", "bool",
    };

    private static readonly HashSet<string> FieldKeys = new(StringComparer.Ordinal)
    {
        "name", "length", "prefix", "rest", "type", "scale", "sign", "pad", "align", "trim", "format", "values",
        "const", "filler", "max", "fields", "repeat", "bitmap", "bit",
    };

    private static readonly HashSet<string> RecordKeys = new(StringComparer.Ordinal) { "name", "when", "many", "fields" };

    private PositionalLayout(List<PositionalField>? fields, List<PositionalRecord>? records)
    {
        Fields = fields;
        Records = records;
    }

    public List<PositionalField>? Fields { get; }

    public List<PositionalRecord>? Records { get; }

    public static PositionalLayout Parse(JsonObject? layout, FormatContext context)
    {
        if (layout is null) throw context.Fail("Option 'layout' is required: it lists the fields (or records) and their lengths.");
        foreach (var pair in layout)
        {
            if (pair.Key is not ("fields" or "records"))
                throw context.Fail($"Unknown layout key '{pair.Key}'. A layout has 'fields' or 'records'.", new FormatLocation { Field = "layout" });
        }

        var hasFields = layout.TryGetPropertyValue("fields", out var fields);
        var hasRecords = layout.TryGetPropertyValue("records", out var records);
        if (hasFields == hasRecords) throw context.Fail("A layout has either 'fields' (one record) or 'records' (one record per line), not both and not neither.");

        if (hasFields) return new PositionalLayout(ParseFields(fields, "layout.fields", context), null);

        if (records is not JsonArray recordArray || recordArray.Count == 0) throw context.Fail("'layout.records' must be a non-empty list.");
        var list = new List<PositionalRecord>();
        for (var i = 0; i < recordArray.Count; i++)
        {
            var path = $"layout.records[{i}]";
            if (recordArray[i] is not JsonObject record) throw context.Fail($"{path} must be an object.");
            foreach (var pair in record)
            {
                if (!RecordKeys.Contains(pair.Key)) throw context.Fail($"Unknown key '{pair.Key}' in {path}. Record keys: name, when, many, fields.");
            }

            var name = Text(record, "name", path, context) ?? throw context.Fail($"{path} needs a 'name'.");
            int whenStart = 0;
            string? whenEquals = null;
            if (record["when"] is JsonObject when)
            {
                foreach (var pair in when)
                {
                    if (pair.Key is not ("start" or "equals")) throw context.Fail($"Unknown key '{pair.Key}' in {path}.when. Use start and equals.");
                }
                whenStart = Int(when, "start", $"{path}.when", context) ?? 0;
                whenEquals = Text(when, "equals", $"{path}.when", context) ?? throw context.Fail($"{path}.when needs 'equals'.");
            }
            else if (record["when"] is not null)
            {
                throw context.Fail($"{path}.when must be an object such as {{ \"start\": 0, \"equals\": \"01\" }}.");
            }

            list.Add(new PositionalRecord(name, whenStart, whenEquals, Bool(record, "many", path, context) ?? false,
                ParseFields(record["fields"], $"{path}.fields", context)));
        }

        if (list.Select(r => r.Name).Distinct(StringComparer.Ordinal).Count() != list.Count) throw context.Fail("Two records in the layout have the same name.");
        return new PositionalLayout(null, list);
    }

    private static List<PositionalField> ParseFields(JsonNode? node, string path, FormatContext context, bool inBitmap = false)
    {
        if (node is not JsonArray array || array.Count == 0) throw context.Fail($"'{path}' must be a non-empty list of fields.");
        var fields = new List<PositionalField>();
        for (var i = 0; i < array.Count; i++)
        {
            fields.Add(ParseField(array[i], $"{path}[{i}]", context, inBitmap));
        }

        // An unnamed bitmap puts its fields beside the bitmap's siblings, so their names share one object.
        var names = fields.Where(f => f.Name is not null).Select(f => f.Name!)
            .Concat(fields.Where(f => f.IsBitmap && f.Name is null).SelectMany(f => f.Fields!).Where(f => f.Name is not null).Select(f => f.Name!))
            .ToList();
        var duplicate = names.GroupBy(n => n, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) throw context.Fail($"Two fields in '{path}' are named '{duplicate.Key}'.");

        for (var i = 0; i < fields.Count; i++)
        {
            var f = fields[i];
            if (f.Rest && i != fields.Count - 1) throw context.Fail($"'{f.Path}' uses rest, so it must be the last field.");
            if (f.CountField is not null)
            {
                var source = fields.Take(i).FirstOrDefault(other => other.Name == f.CountField);
                if (source is null || source.IsGroup)
                    throw context.Fail($"'{f.Path}' repeats by '{f.CountField}', which must be a field before it in the same list.");
            }
        }
        return fields;
    }

    private static PositionalField ParseField(JsonNode? node, string path, FormatContext context, bool inBitmap = false)
    {
        if (node is not JsonObject field) throw context.Fail($"'{path}' must be an object.");
        foreach (var pair in field)
        {
            if (!FieldKeys.Contains(pair.Key))
                throw context.Fail($"Unknown key '{pair.Key}' in '{path}'. Field keys: {string.Join(", ", FieldKeys.OrderBy(k => k, StringComparer.Ordinal))}.");
        }

        var name = Text(field, "name", path, context);
        var label = name is null ? path : $"{path} ({name})";

        // E73-T9: a bitmap (ISO 8583) says which of its fields are present; each field names its bit.
        var bit = Int(field, "bit", path, context);
        if (inBitmap)
        {
            if (bit is null or < 2 or > 128) throw context.Fail($"'{label}' is inside a bitmap, so it needs a 'bit' from 2 to 128 (bit 1 marks the secondary bitmap).");
            if (name is null) throw context.Fail($"'{path}' (bit {bit}) is inside a bitmap, so it needs a 'name'.");
            if (field["const"] is not null || field["filler"] is not null) throw context.Fail($"'{label}' is inside a bitmap, where every field is data: no const or filler.");
            if (field["bitmap"] is not null) throw context.Fail($"'{label}': a bitmap cannot hold another bitmap.");
        }
        else if (bit is not null)
        {
            throw context.Fail($"'{label}' has a 'bit' but is not inside a bitmap.");
        }

        if (field["bitmap"] is not null)
        {
            var mode = Text(field, "bitmap", path, context);
            if (mode is not ("hex" or "binary"))
                throw context.Fail($"'{label}'.bitmap must be hex (16 hex characters per 64 bits) or binary (8 bytes per 64 bits).");
            var others = field.Select(p => p.Key).Where(k => k is not ("name" or "bitmap" or "fields")).ToList();
            if (others.Count > 0) throw context.Fail($"'{label}' is a bitmap: it takes name, bitmap and fields only, not {string.Join(", ", others)}.");
            var members = ParseFields(field["fields"], $"{path}.fields", context, inBitmap: true);
            var twice = members.GroupBy(f => f.Bit).FirstOrDefault(g => g.Count() > 1);
            if (twice is not null) throw context.Fail($"Two fields in '{path}' use bit {twice.Key}.");
            if (members.Any(f => f.CountField is not null)) throw context.Fail($"'{label}': a group inside a bitmap repeats by a fixed count, not by a count field.");
            // Present fields are read and written in bit order, whatever order the layout lists them in.
            return new PositionalField(path, name) { Bitmap = mode, Fields = members.OrderBy(f => f.Bit).ToList() };
        }

        var constant = Text(field, "const", path, context);
        var filler = Bool(field, "filler", path, context) ?? false;
        if (name is null && constant is null && !filler) throw context.Fail($"'{path}' needs a 'name' (or 'const' / 'filler' for a part that is not data).");

        if (field["fields"] is not null)
        {
            int? count = null;
            string? countField = null;
            if (field["repeat"] is JsonValue repeat)
            {
                if (repeat.TryGetValue<int>(out var fixedCount) && fixedCount > 0) count = fixedCount;
                else if (repeat.TryGetValue<string>(out var byField) && byField.Length > 0) countField = byField;
                else throw context.Fail($"'{label}'.repeat must be a positive number or the name of an earlier count field.");
            }
            if (name is null) throw context.Fail($"'{path}' is a group, so it needs a 'name'.");
            var groupKeys = field.Select(p => p.Key).Where(k => k is not ("name" or "fields" or "repeat" or "bit")).ToList();
            if (groupKeys.Count > 0) throw context.Fail($"'{label}' is a group: it takes name, fields and repeat only, not {string.Join(", ", groupKeys)}.");
            return new PositionalField(path, name) { Fields = ParseFields(field["fields"], $"{path}.fields", context), RepeatCount = count, CountField = countField, Bit = bit };
        }
        if (field["repeat"] is not null) throw context.Fail($"'{label}' has 'repeat' but no 'fields': only a group repeats.");

        var length = Int(field, "length", path, context);
        var prefix = Int(field, "prefix", path, context);
        var rest = Bool(field, "rest", path, context) ?? false;
        if ((length is not null ? 1 : 0) + (prefix is not null ? 1 : 0) + (rest ? 1 : 0) != 1)
            throw context.Fail($"'{label}' needs exactly one of 'length' (fixed width), 'prefix' (a length written before it, e.g. 2 for LL) or 'rest'.");
        if (length is <= 0) throw context.Fail($"'{label}'.length must be more than 0.");
        if (prefix is not null && (prefix < 1 || prefix > 6)) throw context.Fail($"'{label}'.prefix must be 1 to 6 digits.");

        var type = Text(field, "type", path, context) ?? "text";
        if (!Types.Contains(type)) throw context.Fail($"'{label}'.type '{type}' is unknown. Types: {string.Join(", ", Types)}.");
        var numeric = type is "numeric" or "integer" or "decimal";

        var padText = Text(field, "pad", path, context);
        if (padText is not null && padText.Length != 1) throw context.Fail($"'{label}'.pad must be one character.");
        var align = Text(field, "align", path, context);
        if (align is not null and not ("left" or "right")) throw context.Fail($"'{label}'.align must be left or right.");
        var sign = Text(field, "sign", path, context);
        if (sign is not null and not ("leading" or "trailing")) throw context.Fail($"'{label}'.sign must be leading or trailing.");
        if (sign is not null && type is not ("integer" or "decimal")) throw context.Fail($"'{label}'.sign only applies to integer and decimal fields.");

        var values = field["values"] is JsonArray v ? v.Select(x => x?.GetValue<string>() ?? "").ToArray() : null;
        if (values is not null && (type != "bool" || values.Length != 2)) throw context.Fail($"'{label}'.values is for bool fields: [\"true text\", \"false text\"].");
        var format = Text(field, "format", path, context);
        if (type == "date" && format is null) throw context.Fail($"'{label}' is a date, so it needs a 'format' such as yyyyMMdd.");

        var result = new PositionalField(path, name)
        {
            Length = length,
            Prefix = prefix,
            Rest = rest,
            Type = type,
            Scale = Int(field, "scale", path, context) ?? 0,
            Sign = sign,
            Pad = padText?[0] ?? (numeric ? '0' : ' '),
            AlignRight = align is null ? numeric : align == "right",
            Trim = Bool(field, "trim", path, context) ?? type is not "numeric",
            Format = format,
            BoolValues = values ?? ["Y", "N"],
            Const = constant,
            Filler = filler,
            Max = Int(field, "max", path, context),
            Bit = bit,
        };
        if (constant is not null && length is not null && constant.Length != length)
            throw context.Fail($"'{label}'.const is {constant.Length} characters but the field is {length} long.");
        return result;
    }

    private static string? Text(JsonObject obj, string key, string path, FormatContext context) =>
        obj[key] switch
        {
            null => null,
            JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
            _ => throw context.Fail($"'{path}'.{key} must be text."),
        };

    private static int? Int(JsonObject obj, string key, string path, FormatContext context) =>
        obj[key] switch
        {
            null => null,
            JsonValue v when v.TryGetValue<int>(out var n) => n,
            _ => throw context.Fail($"'{path}'.{key} must be a whole number."),
        };

    private static bool? Bool(JsonObject obj, string key, string path, FormatContext context) =>
        obj[key] switch
        {
            null => null,
            JsonValue v when v.GetValueKind() is JsonValueKind.True or JsonValueKind.False => v.GetValue<bool>(),
            _ => throw context.Fail($"'{path}'.{key} must be true or false."),
        };
}

internal sealed class PositionalRecord(string name, int whenStart, string? whenEquals, bool many, List<PositionalField> fields)
{
    public string Name { get; } = name;
    public int WhenStart { get; } = whenStart;
    public string? WhenEquals { get; } = whenEquals;
    public bool Many { get; } = many;
    public List<PositionalField> Fields { get; } = fields;

    public bool Matches(string line) =>
        WhenEquals is null || (line.Length >= WhenStart + WhenEquals.Length && string.CompareOrdinal(line, WhenStart, WhenEquals, 0, WhenEquals.Length) == 0);
}

internal sealed class PositionalField(string path, string? name)
{
    public string Path { get; } = path;
    public string? Name { get; } = name;
    public int? Length { get; init; }
    public int? Prefix { get; init; }
    public bool Rest { get; init; }
    public string Type { get; init; } = "text";
    public int Scale { get; init; }
    public string? Sign { get; init; }
    public char Pad { get; init; } = ' ';
    public bool AlignRight { get; init; }
    public bool Trim { get; init; } = true;
    public string? Format { get; init; }
    public string[] BoolValues { get; init; } = ["Y", "N"];
    public string? Const { get; init; }
    public bool Filler { get; init; }
    public int? Max { get; init; }
    public List<PositionalField>? Fields { get; init; }
    public int? RepeatCount { get; init; }
    public string? CountField { get; init; }

    /// <summary>hex or binary for a bitmap field (its <see cref="Fields"/> are the members, by bit); otherwise null.</summary>
    public string? Bitmap { get; init; }

    /// <summary>The bit that marks this field present, for a field inside a bitmap.</summary>
    public int? Bit { get; init; }

    public bool IsBitmap => Bitmap is not null;

    public bool IsGroup => Fields is not null && Bitmap is null;

    /// <summary>The name used in messages: the field's name, or its place in the layout.</summary>
    public string Label => Name ?? Path;
}

/// <summary>Reads and writes records with a layout, counting in characters or in encoded bytes (D73.9).</summary>
internal sealed class PositionalEngine
{
    private readonly FormatContext _context;
    private readonly bool _bytes;

    public PositionalEngine(FormatContext context, string? unit)
    {
        _context = context;
        _bytes = unit switch
        {
            null or "chars" => false,
            "bytes" => true,
            _ => throw context.Fail($"Option 'unit' must be chars or bytes, not '{unit}'."),
        };
    }

    // ---------------------------------------------------------------- decode

    public JsonNode Decode(string text, PositionalLayout layout, bool strict)
    {
        if (layout.Fields is not null)
        {
            // One record: the whole payload, without the single line break an editor leaves at the end.
            if (text.EndsWith("\r\n", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 2);
            else if (text.EndsWith("\n", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 1);
            return DecodeRecord(text, layout.Fields, line: null, strict);
        }

        var result = new JsonObject();
        foreach (var record in layout.Records!.Where(r => r.Many)) result[record.Name] = new JsonArray();
        var lines = CodecText.SplitLines(text);
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Length == 0) continue;
            var record = layout.Records!.FirstOrDefault(r => r.Matches(lines[i]))
                ?? throw _context.Fail("No record in the layout matches this line.", new FormatLocation { Line = i + 1 });
            var value = DecodeRecord(lines[i], record.Fields, i + 1, strict);
            if (record.Many)
            {
                ((JsonArray)result[record.Name]!).Add(value);
            }
            else if (result.ContainsKey(record.Name))
            {
                throw _context.Fail($"Record '{record.Name}' appears a second time; set \"many\": true on it if it repeats.", new FormatLocation { Line = i + 1 });
            }
            else
            {
                result[record.Name] = value;
            }
        }
        return result;
    }

    private JsonObject DecodeRecord(string record, List<PositionalField> fields, int? line, bool strict)
    {
        var cursor = new Cursor(record, _bytes, _context.Encoding);
        var obj = DecodeFields(cursor, fields, line);
        if (strict && cursor.Remaining > 0)
        {
            throw _context.Fail($"{cursor.Remaining} {Unit} left after the last field; add fields for them, or set strict to false.",
                new FormatLocation { Line = line, Offset = cursor.Position });
        }
        return obj;
    }

    private JsonObject DecodeFields(Cursor cursor, List<PositionalField> fields, int? line) => DecodeFields(cursor, fields, line, new JsonObject());

    private JsonObject DecodeFields(Cursor cursor, List<PositionalField> fields, int? line, JsonObject obj)
    {
        foreach (var field in fields)
        {
            if (field.IsBitmap)
            {
                // The bitmap itself is not data: it only says which members follow, in bit order.
                var bits = ReadBitmap(cursor, field, line);
                var target = obj;
                if (field.Name is not null) obj[field.Name] = target = new JsonObject();
                for (var bit = 2; bit < bits.Length; bit++)
                {
                    if (!bits[bit]) continue;
                    var member = field.Fields!.FirstOrDefault(f => f.Bit == bit)
                        ?? throw Fail($"has bit {bit} set but no field for it; add a field with \"bit\": {bit} to the layout", field, line, cursor.Position);
                    DecodeFields(cursor, [member], line, target);
                }
                continue;
            }

            if (field.IsGroup)
            {
                if (field.RepeatCount is null && field.CountField is null)
                {
                    obj[field.Name!] = DecodeFields(cursor, field.Fields!, line);
                    continue;
                }

                var count = field.RepeatCount ?? CountFrom(obj, field, line, cursor);
                var items = new JsonArray();
                for (var i = 0; i < count; i++) items.Add(DecodeFields(cursor, field.Fields!, line));
                obj[field.Name!] = items;
                continue;
            }

            var start = cursor.Position;
            string raw;
            if (field.Prefix is { } digits)
            {
                var lengthText = cursor.Take(digits);
                if (lengthText is null || !AllDigits(lengthText))
                    throw Fail($"needs a {digits}-digit length first, found '{lengthText ?? cursor.Rest()}'", field, line, start);
                var length = int.Parse(lengthText, CultureInfo.InvariantCulture);
                if (field.Max is { } max && length > max) throw Fail($"says it is {length} long, more than its max of {max}", field, line, start);
                raw = cursor.Take(length) ?? throw Fail($"says it is {length} long but only {cursor.Remaining} {Unit} are left", field, line, start);
            }
            else if (field.Rest)
            {
                raw = cursor.Rest();
            }
            else
            {
                var length = field.Length!.Value;
                var taken = cursor.Take(length);
                if (taken is null)
                {
                    // An editor may strip trailing spaces: a short text field at the very end is read as padded.
                    if (field.Type is "text" or "alpha" or "alphanumeric" && field.Const is null) taken = cursor.Rest();
                    else throw Fail($"needs {length} {Unit} but only {cursor.Remaining} are left", field, line, start);
                }
                raw = taken;
            }

            if (field.Const is not null)
            {
                if (raw != field.Const) throw Fail($"must be '{field.Const}', found '{raw}'", field, line, start);
                continue;
            }
            if (field.Filler) continue;

            obj[field.Name!] = DecodeValue(raw, field, line, start);
        }
        return obj;
    }

    /// <summary>
    /// The present bits (index 1 to 64, or to 128 when bit 1 says a secondary bitmap follows). Hex is 16 characters
    /// per 64 bits; binary is 8 bytes per 64 bits, read through a single-byte encoding so every byte value survives.
    /// </summary>
    private bool[] ReadBitmap(Cursor cursor, PositionalField field, int? line)
    {
        var primary = ReadBitmapPart(cursor, field, line);
        var secondary = primary[0] ? ReadBitmapPart(cursor, field, line) : null;
        var bits = new bool[secondary is null ? 65 : 129];
        for (var i = 0; i < 64; i++) bits[i + 1] = primary[i];
        if (secondary is not null) for (var i = 0; i < 64; i++) bits[i + 65] = secondary[i];
        return bits;
    }

    private bool[] ReadBitmapPart(Cursor cursor, PositionalField field, int? line)
    {
        var start = cursor.Position;
        byte[] bytes;
        if (field.Bitmap == "hex")
        {
            var hex = cursor.Take(16) ?? throw Fail($"needs 16 hex characters for its bitmap but only {cursor.Remaining} {Unit} are left", field, line, start);
            if (!hex.All(Uri.IsHexDigit)) throw Fail($"must be 16 hex characters (0-9, A-F), found '{hex}'", field, line, start);
            bytes = new byte[8];
            for (var i = 0; i < 8; i++) bytes[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        else
        {
            RequireSingleByte(field, line, start);
            var raw = cursor.Take(8) ?? throw Fail($"needs 8 bytes for its bitmap but only {cursor.Remaining} {Unit} are left", field, line, start);
            bytes = _context.Encoding.GetBytes(raw);
        }

        var bits = new bool[64];
        for (var i = 0; i < 64; i++) bits[i] = (bytes[i / 8] & (0x80 >> (i % 8))) != 0;
        return bits;
    }

    private void RequireSingleByte(PositionalField field, int? line, long start)
    {
        if (!_context.Encoding.IsSingleByte)
            throw Fail($"is a binary bitmap, which needs a single-byte encoding (such as iso-8859-1 or ebcdic) so every byte value is kept; this one is {_context.Encoding.WebName}", field, line, start);
    }

    private int CountFrom(JsonObject obj, PositionalField group, int? line, Cursor cursor)
    {
        var value = obj[group.CountField!];
        int count;
        if (value is JsonValue v && (v.TryGetValue<int>(out count) || (v.TryGetValue<string>(out var s) && int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out count))))
            return count;
        throw Fail($"repeats by '{group.CountField}', which is not a number", group, line, cursor.Position);
    }

    private JsonNode? DecodeValue(string raw, PositionalField field, int? line, long start)
    {
        var value = field.Trim ? TrimPad(raw, field) : raw;
        switch (field.Type)
        {
            case "text":
                return JsonValue.Create(value);
            case "alpha":
                if (value.Any(c => !char.IsLetter(c) && c != ' ')) throw Fail($"must be letters only, found '{value}'", field, line, start);
                return JsonValue.Create(value);
            case "alphanumeric":
                if (value.Any(c => !char.IsLetterOrDigit(c) && c != ' ')) throw Fail($"must be letters and digits only, found '{value}'", field, line, start);
                return JsonValue.Create(value);
            case "numeric":
                if (value.Trim().Length == 0) return JsonValue.Create("");
                if (!AllDigits(value)) throw Fail($"must be digits only, found '{value}'", field, line, start);
                return JsonValue.Create(value);
            case "integer":
            case "decimal":
            {
                // Zero padding is read by the number itself (000 is 0, not blank); only a non-zero pad is trimmed.
                var bare = raw.Trim();
                if (field.Pad is not ('0' or ' ')) bare = bare.Trim(field.Pad);
                var (negative, digits) = SplitSign(bare, field);
                if (digits.Length == 0) return null;
                if (field.Type == "integer" || field.Scale > 0 ? !AllDigits(digits) : !decimal.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _))
                    throw Fail($"must be a number, found '{raw}'", field, line, start);
                var number = decimal.Parse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
                for (var i = 0; i < field.Scale; i++) number /= 10;
                return CodecText.Number(negative ? -number : number);
            }
            case "date":
            {
                var text = raw.Trim();
                if (text.Trim(field.Pad).Length == 0) return null;
                if (!DateTime.TryParseExact(text, field.Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    throw Fail($"must be a date in the form {field.Format}, found '{raw}'", field, line, start);
                return JsonValue.Create(date.ToString(IsoFormat(field.Format!), CultureInfo.InvariantCulture));
            }
            case "bool":
                if (value == field.BoolValues[0]) return JsonValue.Create(true);
                if (value == field.BoolValues[1]) return JsonValue.Create(false);
                if (value.Trim().Length == 0) return null;
                throw Fail($"must be '{field.BoolValues[0]}' or '{field.BoolValues[1]}', found '{raw}'", field, line, start);
            default:
                throw new InvalidOperationException(field.Type);
        }
    }

    private (bool Negative, string Digits) SplitSign(string value, PositionalField field)
    {
        if (field.Sign == "leading" && value.Length > 0 && value[0] is '-' or '+') return (value[0] == '-', value.Substring(1));
        if (field.Sign == "trailing" && value.Length > 0 && value[value.Length - 1] is '-' or '+') return (value[value.Length - 1] == '-', value.Substring(0, value.Length - 1));
        return (false, value);
    }

    private static string TrimPad(string raw, PositionalField field) =>
        field.AlignRight ? raw.TrimStart(field.Pad) : raw.TrimEnd(field.Pad);

    // ---------------------------------------------------------------- encode

    public string Encode(JsonNode? value, PositionalLayout layout, string newline, bool trailing)
    {
        if (layout.Fields is not null)
        {
            if (value is not JsonObject obj) throw _context.Fail("A one-record layout writes an object; the mapping did not produce one.");
            return EncodeRecord(obj, layout.Fields, line: null);
        }

        if (value is not JsonObject root) throw _context.Fail("A records layout writes an object with one key per record name; the mapping did not produce one.");
        var known = new HashSet<string>(layout.Records!.Select(r => r.Name), StringComparer.Ordinal);
        var stray = root.Select(p => p.Key).FirstOrDefault(k => !known.Contains(k));
        if (stray is not null) throw _context.Fail($"'{stray}' is not a record in the layout. Records: {string.Join(", ", known)}.");

        var lines = new List<string>();
        foreach (var record in layout.Records!)
        {
            var node = root[record.Name];
            if (node is null) continue;
            if (record.Many)
            {
                if (node is not JsonArray items) throw _context.Fail($"Record '{record.Name}' repeats, so it must be a list.", new FormatLocation { Field = record.Name });
                foreach (var item in items) lines.Add(EncodeRecord(item as JsonObject ?? throw _context.Fail($"Each '{record.Name}' must be an object."), record.Fields, lines.Count + 1));
            }
            else
            {
                lines.Add(EncodeRecord(node as JsonObject ?? throw _context.Fail($"Record '{record.Name}' must be an object."), record.Fields, lines.Count + 1));
            }
        }
        return CodecText.Join(lines, newline, trailing);
    }

    private string EncodeRecord(JsonObject obj, List<PositionalField> fields, int? line)
    {
        var text = new StringBuilder();
        EncodeFields(text, obj, fields, line);
        return text.ToString();
    }

    private void EncodeFields(StringBuilder text, JsonObject? obj, List<PositionalField> fields, int? line)
    {
        // A count field always says how many items its group really has, whatever the JSON held.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var group in fields.Where(f => f.CountField is not null))
        {
            counts[group.CountField!] = (obj?[group.Name!] as JsonArray)?.Count ?? 0;
        }

        foreach (var field in fields)
        {
            var start = Units(text.ToString());
            if (field.IsBitmap)
            {
                // The bitmap is worked out from the members the mapping gave a value; it is never read from the JSON.
                var source = field.Name is null ? obj : obj?[field.Name] as JsonObject;
                var present = field.Fields!.Where(m => source?[m.Name!] is not null).ToList();
                var bits = new bool[129];
                foreach (var m in present) bits[m.Bit!.Value] = true;
                bits[1] = present.Any(m => m.Bit > 64);
                WriteBitmapPart(text, bits, 1, field, line, start);
                if (bits[1]) WriteBitmapPart(text, bits, 65, field, line, start);
                foreach (var m in present) EncodeFields(text, source, [m], line);
                continue;
            }

            if (field.IsGroup)
            {
                var node = obj?[field.Name!];
                if (field.RepeatCount is null && field.CountField is null)
                {
                    EncodeFields(text, node as JsonObject, field.Fields!, line);
                    continue;
                }

                var items = node as JsonArray ?? new JsonArray();
                if (field.RepeatCount is { } fixedCount && items.Count != fixedCount)
                    throw Fail($"holds exactly {fixedCount} items, the mapping gave {items.Count}", field, line, start);
                foreach (var item in items) EncodeFields(text, item as JsonObject, field.Fields!, line);
                continue;
            }

            if (field.Const is not null)
            {
                text.Append(field.Const);
                continue;
            }
            if (field.Filler)
            {
                text.Append(field.Pad, field.Length ?? 0);
                continue;
            }

            JsonNode? value = field.Name is not null && counts.TryGetValue(field.Name, out var count)
                ? JsonValue.Create(count)
                : obj?[field.Name!];
            var raw = EncodeValue(value, field, line, start);

            if (field.Prefix is { } digits)
            {
                var length = Units(raw);
                var limit = (int)Math.Pow(10, digits) - 1;
                if (length > limit || (field.Max is { } max && length > max))
                    throw Fail($"is {length} {Unit} long, more than it can hold ({Math.Min(limit, field.Max ?? limit)})", field, line, start);
                text.Append(length.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0')).Append(raw);
            }
            else if (field.Rest)
            {
                text.Append(raw);
            }
            else
            {
                text.Append(Fit(raw, field, line, start));
            }
        }
    }

    private void WriteBitmapPart(StringBuilder text, bool[] bits, int first, PositionalField field, int? line, long start)
    {
        var bytes = new byte[8];
        for (var i = 0; i < 64; i++)
        {
            if (bits[first + i]) bytes[i / 8] |= (byte)(0x80 >> (i % 8));
        }

        if (field.Bitmap == "hex")
        {
            foreach (var b in bytes) text.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            return;
        }
        RequireSingleByte(field, line, start);
        text.Append(_context.Encoding.GetString(bytes));
    }

    private string EncodeValue(JsonNode? value, PositionalField field, int? line, long start)
    {
        if (value is null) return "";
        if (!CodecText.TryScalar(value, out var text)) throw Fail("must be a single value, not an object or a list", field, line, start);
        var kind = ((JsonValue)value).GetValueKind();
        text ??= "";

        switch (field.Type)
        {
            case "text":
                return text!;
            case "alpha":
                if (text!.Any(c => !char.IsLetter(c) && c != ' ')) throw Fail($"must be letters only, got '{text}'", field, line, start);
                return text;
            case "alphanumeric":
                if (text!.Any(c => !char.IsLetterOrDigit(c) && c != ' ')) throw Fail($"must be letters and digits only, got '{text}'", field, line, start);
                return text;
            case "numeric":
                if (text!.Length > 0 && !AllDigits(text)) throw Fail($"must be digits only, got '{text}'", field, line, start);
                return text;
            case "integer":
            case "decimal":
            {
                if (kind == JsonValueKind.String && text!.Length == 0) return "";
                if (kind is not (JsonValueKind.Number or JsonValueKind.String) || !decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    throw Fail($"must be a number, got '{text}'", field, line, start);
                var scaled = number;
                for (var i = 0; i < field.Scale; i++) scaled *= 10;
                scaled = Math.Round(scaled, field.Type == "decimal" && field.Scale == 0 ? 10 : 0, MidpointRounding.AwayFromZero);
                if (field.Type == "integer" && scaled != decimal.Truncate(scaled)) throw Fail($"must be a whole number, got '{text}'", field, line, start);
                var negative = scaled < 0;
                if (negative && field.Sign is null) throw Fail($"is negative ({text}) but the layout has no 'sign' for it", field, line, start);
                var digits = CodecText.Normalize(Math.Abs(scaled)).ToString(CultureInfo.InvariantCulture);
                if (field.Sign is null) return digits;
                var width = (field.Length ?? digits.Length + 1) - 1;
                var body = field.Length is null ? digits : Pad(digits, width, field);
                var mark = negative ? "-" : "+";
                return field.Sign == "leading" ? mark + body : body + mark;
            }
            case "date":
            {
                if (kind != JsonValueKind.String || !DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
                    throw Fail($"must be a date such as 2026-11-17, got '{text}'", field, line, start);
                return date.ToString(field.Format, CultureInfo.InvariantCulture);
            }
            case "bool":
                return kind switch
                {
                    JsonValueKind.True => field.BoolValues[0],
                    JsonValueKind.False => field.BoolValues[1],
                    _ => throw Fail($"must be true or false, got '{text}'", field, line, start),
                };
            default:
                throw new InvalidOperationException(field.Type);
        }
    }

    private string Fit(string raw, PositionalField field, int? line, long start)
    {
        var length = field.Length!.Value;
        var size = Units(raw);
        if (size > length) throw Fail($"is {size} {Unit} ('{raw}'), the field holds {length}", field, line, start);
        return Pad(raw, length, field);
    }

    private string Pad(string raw, int length, PositionalField field)
    {
        var padding = new string(field.Pad, Math.Max(0, length - Units(raw)));
        return field.AlignRight ? padding + raw : raw + padding;
    }

    // ---------------------------------------------------------------- shared

    private string Unit => _bytes ? "bytes" : "characters";

    private int Units(string text) => _bytes ? _context.Encoding.GetByteCount(text) : text.Length;

    private MappingFormatException Fail(string problem, PositionalField field, int? line, long offset) =>
        _context.Fail($"Field '{field.Label}' {problem}.", new FormatLocation { Line = line, Offset = offset, Field = field.Label });

    private static bool AllDigits(string text) => text.Length > 0 && text.All(c => c is >= '0' and <= '9');

    private static string IsoFormat(string format)
    {
        var hasDate = format.IndexOfAny(['y', 'M', 'd']) >= 0;
        var hasTime = format.IndexOfAny(['H', 'h', 'm', 's']) >= 0;
        return hasDate && hasTime ? "yyyy-MM-dd'T'HH:mm:ss" : hasTime ? "HH:mm:ss" : "yyyy-MM-dd";
    }

    /// <summary>Reads a record in characters, or in bytes of the context's encoding.</summary>
    private sealed class Cursor
    {
        private readonly string _text;
        private readonly byte[]? _bytes;
        private readonly Encoding _encoding;
        private int _position;

        public Cursor(string text, bool bytes, Encoding encoding)
        {
            _text = text;
            _encoding = encoding;
            _bytes = bytes ? encoding.GetBytes(text) : null;
        }

        public int Position => _position;

        public int Remaining => (_bytes?.Length ?? _text.Length) - _position;

        /// <summary>The next <paramref name="count"/> units, or null when fewer are left (nothing is consumed then).</summary>
        public string? Take(int count)
        {
            if (count > Remaining) return null;
            var part = _bytes is null ? _text.Substring(_position, count) : _encoding.GetString(_bytes, _position, count);
            _position += count;
            return part;
        }

        public string Rest() => Take(Remaining)!;
    }
}
