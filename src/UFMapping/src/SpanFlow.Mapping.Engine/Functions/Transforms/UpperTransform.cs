using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class UpperTransform : ITransform
{
    public string Name => "upper";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
        => JsonValue.Create((JsonScalarConverter.ToText(args.FirstOrDefault()) ?? string.Empty).ToUpperInvariant());
}
