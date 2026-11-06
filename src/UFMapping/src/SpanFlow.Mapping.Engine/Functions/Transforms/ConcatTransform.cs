using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class ConcatTransform : ITransform
{
    public string Name => "concat";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
        => JsonValue.Create(string.Concat(args.Select(JsonScalarConverter.ToText)));
}
