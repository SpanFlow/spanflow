using System.Text;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace SpanFlow.Mapping.Formats.Codecs;

/// <summary>
/// XML both ways, with one fixed convention: <c>{ "Root": { ... } }</c>; attributes are <c>@name</c>; text beside
/// attributes or children is <c>#text</c>; an element with only text is a plain string; an element seen twice is a
/// list. Prefixes stay in names (<c>x:Note</c>) and <c>@xmlns:x</c> keeps the declaration, so writing restores them.
/// Options: <c>arrays</c> (elements that are always lists, so one item and two give the same shape), <c>types</c>,
/// <c>root</c>, <c>xsd</c> + <c>validate</c> (E73-T10: lists and value types from a schema), and for writing
/// <c>declaration</c>, <c>indent</c>, <c>cdata</c>, <c>namespaces</c>. DTDs and external entities are refused.
/// </summary>
public sealed class XmlCodec : IFormatCodec
{
    private const string XmlnsUri = "http://www.w3.org/2000/xmlns/";
    private const string XmlUri = "http://www.w3.org/XML/1998/namespace";

    private static readonly IReadOnlyList<FormatOptionInfo> OptionList =
    [
        new("arrays", FormatOptionKind.StringList, FormatOptionUse.DecodeOnly, "Elements that are always lists: a name (Line) or a path from the root (/Order/Line)."),
        new("types", FormatOptionKind.String, FormatOptionUse.DecodeOnly, "string (default) or infer: read plain numbers and true/false as such."),
        new("xsd", FormatOptionKind.String, FormatOptionUse.DecodeOnly, "An XSD (the schema text): elements it lets repeat are always lists, and its number and boolean types are read as such."),
        new("validate", FormatOptionKind.Bool, FormatOptionUse.DecodeOnly, "Check the XML against the xsd and stop at the first place it breaks the schema."),
        new("root", FormatOptionKind.String, FormatOptionUse.Both, "The root element's name: reading leaves it out of the JSON, writing wraps the JSON in it."),
        new("declaration", FormatOptionKind.Bool, FormatOptionUse.EncodeOnly, "Start with <?xml version=\"1.0\" ...?> (default true)."),
        new("indent", FormatOptionKind.Bool, FormatOptionUse.EncodeOnly, "Write indented, readable XML."),
        new("cdata", FormatOptionKind.StringList, FormatOptionUse.EncodeOnly, "Elements whose text is written as CDATA: a name or a path from the root."),
        new("namespaces", FormatOptionKind.Object, FormatOptionUse.EncodeOnly, "Prefix to namespace URI, for prefixes the JSON uses but does not declare; \"\" is the default namespace."),
    ];

    public string Type => "xml";

    public string ContentType => "application/xml";

    public IReadOnlyList<FormatOptionInfo> Options => OptionList;

    // ---------------------------------------------------------------- decode

    public JsonNode? Decode(FormatPayload raw, FormatContext context)
    {
        var text = raw.AsText(context.Encoding);
        if (string.IsNullOrWhiteSpace(text)) return null;

        var xsd = context.GetString("xsd");
        var hints = string.IsNullOrWhiteSpace(xsd) ? null : XsdHints.For(xsd!, context);
        var validate = context.GetBool("validate");
        if (validate && hints is null) throw context.Fail("Option 'validate' needs the schema in the xsd option.");

        XDocument document;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            };
            if (validate)
            {
                settings.Schemas = hints!.Schemas;
                settings.ValidationType = ValidationType.Schema;
                settings.ValidationEventHandler += (_, e) =>
                {
                    if (e.Severity == XmlSeverityType.Error) throw e.Exception;
                };
            }
            using var reader = XmlReader.Create(new StringReader(text), settings);
            document = XDocument.Load(reader, LoadOptions.SetLineInfo);
        }
        catch (XmlException ex)
        {
            throw context.Fail(ex.Message.Split(new[] { " Line " }, StringSplitOptions.None)[0].TrimEnd('.', ' ') + ".",
                new FormatLocation { Line = ex.LineNumber, Column = ex.LinePosition }, ex);
        }
        catch (XmlSchemaException ex)
        {
            throw context.Fail($"The XML does not follow the xsd: {ex.Message.TrimEnd('.', ' ')}.",
                new FormatLocation { Line = ex.LineNumber, Column = ex.LinePosition }, ex);
        }

        var arrays = context.GetStrings("arrays");
        var infer = context.GetString("types", "string") switch
        {
            "string" => false,
            "infer" => true,
            var other => throw context.Fail($"Option 'types' must be string or infer, not '{other}'."),
        };
        var reading = new Reading(context, new HashSet<string>(arrays, StringComparer.Ordinal), infer, hints);

        var root = document.Root!;
        var rootName = QualifiedName(root);
        var content = reading.Element(root, "/" + rootName, "/" + root.Name.LocalName, 1);

        var expectedRoot = context.GetString("root");
        if (expectedRoot is null) return new JsonObject { [rootName] = content };
        if (expectedRoot != rootName) throw context.Fail($"The root element is '{rootName}', not '{expectedRoot}' as the root option says.", Where(root));
        return content;
    }

    /// <summary>Reads elements into the convention. <c>local</c> is the path of local names the XSD hints are keyed by.</summary>
    private sealed class Reading(FormatContext context, HashSet<string> arrays, bool infer, XsdHints? hints)
    {
        public JsonNode? Element(XElement element, string path, string local, int depth)
        {
            if (depth > context.Limits.MaxDepth) throw context.Fail($"The XML is nested more than {context.Limits.MaxDepth} levels deep.", Where(element));

            var text = string.Concat(element.Nodes().OfType<XText>().Select(t => t.Value));
            if (!element.HasAttributes && !element.HasElements) return Leaf(text, local);

            var obj = new JsonObject();
            foreach (var attribute in element.Attributes())
            {
                var name = "@" + AttributeName(attribute);
                obj[name] = attribute.IsNamespaceDeclaration ? JsonValue.Create(attribute.Value) : Leaf(attribute.Value, local + "/@" + attribute.Name.LocalName);
            }

            var lists = new HashSet<string>(StringComparer.Ordinal);
            foreach (var child in element.Elements())
            {
                var name = QualifiedName(child);
                var childPath = path + "/" + name;
                var childLocal = local + "/" + child.Name.LocalName;
                var value = Element(child, childPath, childLocal, depth + 1);
                if (!obj.ContainsKey(name))
                {
                    if (arrays.Contains(name) || arrays.Contains(childPath) || hints?.Arrays.Contains(childLocal) == true)
                    {
                        obj[name] = new JsonArray(value);
                        lists.Add(name);
                    }
                    else
                    {
                        obj[name] = value;
                    }
                }
                else if (lists.Contains(name))
                {
                    ((JsonArray)obj[name]!).Add(value);
                }
                else
                {
                    // Seen a second time: the single value becomes a list, in place (key order kept).
                    var first = obj[name];
                    obj[name] = null;
                    obj[name] = new JsonArray(first, value);
                    lists.Add(name);
                }
            }

            if (text.Trim().Length > 0) obj["#text"] = Leaf(text, local);
            return obj;
        }

        /// <summary>The XSD's type wins where it gives one; a value that does not fit it stays text (validate reports it).</summary>
        private JsonNode? Leaf(string text, string local)
        {
            if (hints is not null && hints.Values.TryGetValue(local, out var kind))
            {
                var trimmed = text.Trim();
                switch (kind)
                {
                    case XsdValueKind.Boolean when trimmed is "true" or "1":
                        return JsonValue.Create(true);
                    case XsdValueKind.Boolean when trimmed is "false" or "0":
                        return JsonValue.Create(false);
                    case XsdValueKind.Integer or XsdValueKind.Number when CodecText.TryNumber(trimmed, kind == XsdValueKind.Integer, out var number):
                        return number;
                }
                return JsonValue.Create(text);
            }
            return infer ? CodecText.Infer(text) : JsonValue.Create(text);
        }
    }

    private static string QualifiedName(XElement element)
    {
        var ns = element.Name.Namespace;
        if (ns == XNamespace.None) return element.Name.LocalName;
        var prefix = element.GetPrefixOfNamespace(ns);
        return string.IsNullOrEmpty(prefix) ? element.Name.LocalName : prefix + ":" + element.Name.LocalName;
    }

    private static string AttributeName(XAttribute attribute)
    {
        if (attribute.IsNamespaceDeclaration)
            return attribute.Name.Namespace == XNamespace.None ? "xmlns" : "xmlns:" + attribute.Name.LocalName;
        var ns = attribute.Name.Namespace;
        if (ns == XNamespace.None) return attribute.Name.LocalName;
        if (ns == XNamespace.Xml) return "xml:" + attribute.Name.LocalName;
        var prefix = attribute.Parent?.GetPrefixOfNamespace(ns);
        return string.IsNullOrEmpty(prefix) ? attribute.Name.LocalName : prefix + ":" + attribute.Name.LocalName;
    }

    private static FormatLocation? Where(XObject node) =>
        node is IXmlLineInfo info && info.HasLineInfo() ? new FormatLocation { Line = info.LineNumber, Column = info.LinePosition } : null;

    // ---------------------------------------------------------------- encode

    public FormatPayload Encode(JsonNode? value, FormatContext context)
    {
        string rootName;
        JsonNode? content;
        var root = context.GetString("root");
        if (root is not null)
        {
            rootName = root;
            content = value;
        }
        else if (value is JsonObject obj && obj.Count == 1 && !obj.First().Key.StartsWith("@", StringComparison.Ordinal) && obj.First().Key != "#text")
        {
            rootName = obj.First().Key;
            content = obj.First().Value;
        }
        else
        {
            var keys = value is JsonObject many ? string.Join(", ", many.Select(p => p.Key)) : "none";
            throw context.Fail($"XML needs one root element, but the mapping's result has these top-level keys: {keys}. Map everything under one key, or set the root option.");
        }
        if (content is JsonArray) throw context.Fail($"The root element '{rootName}' cannot be a list: XML has exactly one root.");

        var namespaces = new Dictionary<string, string>(StringComparer.Ordinal);
        if (context.GetObject("namespaces") is { } declared)
        {
            foreach (var pair in declared)
            {
                if (!CodecText.TryScalar(pair.Value, out var uri) || uri is null) throw context.Fail($"namespaces.{pair.Key} must be a URI text.");
                namespaces[pair.Key] = uri;
            }
        }

        var builder = new StringBuilder();
        var indent = context.GetBool("indent");
        if (context.GetBool("declaration", true))
        {
            builder.Append("<?xml version=\"1.0\" encoding=\"").Append(context.Encoding.WebName).Append("\"?>");
            if (indent) builder.Append('\n');
        }

        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = indent,
            IndentChars = "  ",
            NewLineChars = "\n",
            ConformanceLevel = ConformanceLevel.Document,
        };
        var writing = new Writing(context, new HashSet<string>(context.GetStrings("cdata"), StringComparer.Ordinal), namespaces);
        using (var writer = XmlWriter.Create(builder, settings))
        {
            try
            {
                // Declarations from the namespaces option go on the root, unless the JSON declares the prefix itself.
                writing.Element(writer, rootName, content, new Dictionary<string, string>(StringComparer.Ordinal), "/" + rootName, 1, isRoot: true);
            }
            catch (XmlException ex)
            {
                throw context.Fail(ex.Message, inner: ex);
            }
            catch (ArgumentException ex)
            {
                throw context.Fail(ex.Message, inner: ex);
            }
        }
        return FormatPayload.FromText(builder.ToString());
    }

    private sealed class Writing(FormatContext context, HashSet<string> cdata, Dictionary<string, string> optionNamespaces)
    {
        public void Element(XmlWriter writer, string name, JsonNode? value, Dictionary<string, string> scope, string path, int depth, bool isRoot = false)
        {
            if (depth > context.Limits.MaxDepth) throw context.Fail($"The JSON is nested more than {context.Limits.MaxDepth} levels deep.", new FormatLocation { Field = path });

            if (value is JsonArray items)
            {
                foreach (var item in items)
                {
                    if (item is JsonArray) throw context.Fail("A list inside a list has no XML form; wrap the inner list in an object.", new FormatLocation { Field = path });
                    Element(writer, name, item, scope, path, depth);
                }
                return;
            }

            var (prefix, local) = Split(name, path);
            var inner = new Dictionary<string, string>(scope, StringComparer.Ordinal);
            var obj = value as JsonObject;
            if (obj is not null)
            {
                foreach (var pair in obj)
                {
                    if (pair.Key == "@xmlns") inner[""] = Text(pair.Value, path + "/@xmlns");
                    else if (pair.Key.StartsWith("@xmlns:", StringComparison.Ordinal)) inner[pair.Key.Substring(7)] = Text(pair.Value, path + "/" + pair.Key);
                }
            }

            var extraDeclarations = new List<KeyValuePair<string, string>>();
            if (isRoot)
            {
                foreach (var pair in optionNamespaces)
                {
                    if (!inner.ContainsKey(pair.Key))
                    {
                        inner[pair.Key] = pair.Value;
                        extraDeclarations.Add(pair);
                    }
                }
            }

            writer.WriteStartElement(prefix ?? "", local, Resolve(prefix, inner, path, element: true));

            foreach (var declaration in extraDeclarations)
            {
                if (declaration.Key.Length == 0) writer.WriteAttributeString("xmlns", declaration.Value);
                else writer.WriteAttributeString("xmlns", declaration.Key, XmlnsUri, declaration.Value);
            }

            if (obj is null)
            {
                if (!CodecText.TryScalar(value, out var text)) throw new InvalidOperationException();
                WriteText(writer, text, path, name);
                writer.WriteEndElement();
                return;
            }

            foreach (var pair in obj.Where(p => p.Key.StartsWith("@", StringComparison.Ordinal)))
            {
                var attributeName = pair.Key.Substring(1);
                var attributePath = path + "/" + pair.Key;
                var text = Text(pair.Value, attributePath);
                if (attributeName == "xmlns")
                {
                    writer.WriteAttributeString("xmlns", text);
                    continue;
                }
                var (attributePrefix, attributeLocal) = Split(attributeName, attributePath);
                if (attributePrefix == "xmlns")
                {
                    writer.WriteAttributeString("xmlns", attributeLocal, XmlnsUri, text);
                    continue;
                }
                writer.WriteAttributeString(attributePrefix ?? "", attributeLocal, attributePrefix is null ? "" : Resolve(attributePrefix, inner, attributePath, element: false), text);
            }

            if (obj["#text"] is { } own)
            {
                if (!CodecText.TryScalar(own, out var text)) throw context.Fail("#text must be a single value.", new FormatLocation { Field = path + "/#text" });
                WriteText(writer, text, path, name);
            }

            foreach (var pair in obj.Where(p => !p.Key.StartsWith("@", StringComparison.Ordinal) && p.Key != "#text"))
            {
                Element(writer, pair.Key, pair.Value, inner, path + "/" + pair.Key, depth + 1);
            }
            writer.WriteEndElement();
        }

        private void WriteText(XmlWriter writer, string? text, string path, string name)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (cdata.Contains(name) || cdata.Contains(path)) writer.WriteCData(text);
            else writer.WriteString(text);
        }

        private string Text(JsonNode? node, string path) =>
            CodecText.TryScalar(node, out var text) ? text ?? "" : throw context.Fail("An attribute must be a single value, not an object or a list.", new FormatLocation { Field = path });

        private (string? Prefix, string Local) Split(string name, string path)
        {
            var colon = name.IndexOf(':');
            var prefix = colon > 0 ? name.Substring(0, colon) : null;
            var local = colon > 0 ? name.Substring(colon + 1) : name;
            try
            {
                XmlConvert.VerifyNCName(local);
                if (prefix is not null) XmlConvert.VerifyNCName(prefix);
            }
            catch (XmlException)
            {
                throw context.Fail($"'{name}' is not a valid XML name.", new FormatLocation { Field = path });
            }
            return (prefix, local);
        }

        private string Resolve(string? prefix, Dictionary<string, string> scope, string path, bool element)
        {
            if (prefix is null) return element && scope.TryGetValue("", out var defaultNs) ? defaultNs : "";
            if (prefix == "xml") return XmlUri;
            if (scope.TryGetValue(prefix, out var uri)) return uri;
            throw context.Fail($"The prefix '{prefix}' is not declared: add \"@xmlns:{prefix}\" to an enclosing element or set it in the namespaces option.", new FormatLocation { Field = path });
        }
    }
}
