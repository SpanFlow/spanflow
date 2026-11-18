using System.Xml;
using System.Xml.Schema;

namespace SpanFlow.Mapping.Formats.Codecs;

/// <summary>What a value's XSD type turns into in JSON.</summary>
internal enum XsdValueKind
{
    Text,
    Integer,
    Number,
    Boolean,
}

/// <summary>
/// E73-T10: what an XSD says about the JSON shape of a document: which elements are lists (<c>maxOccurs</c> above 1,
/// so one item and two give the same shape) and which values are numbers or booleans. Keyed by paths of local names
/// from the root (<c>/Document/CstmrCdtTrfInitn/PmtInf</c>; an attribute is <c>.../InstdAmt/@Ccy</c>, an element's
/// own text beside attributes is the element's path), so prefixes in the document do not matter. The schema is
/// compiled with no resolver (includes and imports are not fetched) and DTDs prohibited.
/// </summary>
internal sealed class XsdHints
{
    private static readonly Dictionary<string, XsdHints> Cache = new(StringComparer.Ordinal);
    private const int CacheSize = 16;

    private XsdHints(XmlSchemaSet schemas) => Schemas = schemas;

    public XmlSchemaSet Schemas { get; }

    public HashSet<string> Arrays { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, XsdValueKind> Values { get; } = new(StringComparer.Ordinal);

    /// <summary>The hints for an XSD text, compiled once per distinct text (the designer re-reads the same one often).</summary>
    public static XsdHints For(string xsd, FormatContext context)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(xsd, out var cached)) return cached;
        }

        var hints = Build(xsd, context);
        lock (Cache)
        {
            if (Cache.Count >= CacheSize) Cache.Clear();
            Cache[xsd] = hints;
        }
        return hints;
    }

    private static XsdHints Build(string xsd, FormatContext context)
    {
        var schemas = new XmlSchemaSet { XmlResolver = null };
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(xsd), settings);
            var schema = XmlSchema.Read(reader, (_, e) => throw e.Exception) ?? throw new XmlSchemaException("The text is not an XSD.");
            schemas.Add(schema);
            schemas.Compile();
        }
        catch (Exception ex) when (ex is XmlException or XmlSchemaException)
        {
            var (line, column) = ex switch
            {
                XmlSchemaException s => (s.LineNumber, s.LinePosition),
                XmlException x => (x.LineNumber, x.LinePosition),
                _ => (0, 0),
            };
            throw context.Fail($"The xsd option is not a schema that compiles: {ex.Message.Split(new[] { " Line " }, StringSplitOptions.None)[0].TrimEnd('.', ' ')}.",
                line > 0 ? new FormatLocation { Line = line, Column = column, Field = "xsd" } : new FormatLocation { Field = "xsd" }, ex);
        }

        var hints = new XsdHints(schemas);
        foreach (XmlSchemaElement element in schemas.GlobalElements.Values)
        {
            hints.Walk(element, "/" + element.QualifiedName.Name, 1, new HashSet<XmlSchemaType>(), context.Limits.MaxDepth);
        }
        return hints;
    }

    private void Walk(XmlSchemaElement element, string path, int depth, HashSet<XmlSchemaType> stack, int maxDepth)
    {
        if (depth > maxDepth) return;
        var type = element.ElementSchemaType;
        if (type is XmlSchemaSimpleType simple)
        {
            Note(path, simple.Datatype);
            return;
        }
        if (type is not XmlSchemaComplexType complex) return;

        // A type that contains itself (a tree of parts) is followed once per branch, not forever.
        if (!stack.Add(complex)) return;
        try
        {
            foreach (XmlSchemaAttribute attribute in complex.AttributeUses.Values)
            {
                Note($"{path}/@{attribute.QualifiedName.Name}", attribute.AttributeSchemaType?.Datatype);
            }
            if (complex.ContentType is XmlSchemaContentType.TextOnly) Note(path, complex.Datatype);
            Particle(complex.ContentTypeParticle, path, depth, repeated: false, stack, maxDepth);
        }
        finally
        {
            stack.Remove(complex);
        }
    }

    /// <summary>A repeat on an enclosing sequence or choice repeats every element inside it too.</summary>
    private void Particle(XmlSchemaParticle? particle, string path, int depth, bool repeated, HashSet<XmlSchemaType> stack, int maxDepth)
    {
        if (particle is null) return;
        var many = repeated || particle.MaxOccurs > 1;
        switch (particle)
        {
            case XmlSchemaElement child:
                var childPath = path + "/" + child.QualifiedName.Name;
                if (many) Arrays.Add(childPath);
                Walk(child, childPath, depth + 1, stack, maxDepth);
                break;
            case XmlSchemaGroupBase group:
                foreach (XmlSchemaObject item in group.Items)
                {
                    if (item is XmlSchemaParticle inner) Particle(inner, path, depth, many, stack, maxDepth);
                }
                break;
        }
    }

    private void Note(string path, XmlSchemaDatatype? datatype)
    {
        var kind = KindOf(datatype);
        if (kind != XsdValueKind.Text) Values[path] = kind;
    }

    private static XsdValueKind KindOf(XmlSchemaDatatype? datatype)
    {
        if (datatype is null || datatype.Variety != XmlSchemaDatatypeVariety.Atomic) return XsdValueKind.Text;
        return datatype.TypeCode switch
        {
            XmlTypeCode.Boolean => XsdValueKind.Boolean,
            XmlTypeCode.Decimal or XmlTypeCode.Float or XmlTypeCode.Double => XsdValueKind.Number,
            XmlTypeCode.Integer or XmlTypeCode.NonPositiveInteger or XmlTypeCode.NegativeInteger or XmlTypeCode.Long or XmlTypeCode.Int
                or XmlTypeCode.Short or XmlTypeCode.Byte or XmlTypeCode.NonNegativeInteger or XmlTypeCode.UnsignedLong
                or XmlTypeCode.UnsignedInt or XmlTypeCode.UnsignedShort or XmlTypeCode.UnsignedByte or XmlTypeCode.PositiveInteger => XsdValueKind.Integer,
            _ => XsdValueKind.Text,
        };
    }
}
