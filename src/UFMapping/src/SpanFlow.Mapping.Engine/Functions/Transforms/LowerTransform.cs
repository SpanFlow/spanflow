using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class LowerTransform : ITransform
{
    public string Name => "lower";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
        => JsonValue.Create((JsonScalarConverter.ToText(args.FirstOrDefault()) ?? string.Empty).ToLowerInvariant());
}
