using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class AddTransform : ITransform
{
    public string Name => "add";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
        => JsonValue.Create(args.Sum(JsonScalarConverter.ToDecimal));
}
