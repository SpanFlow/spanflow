using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class CoalesceTransform : ITransform
{
    public string Name => "coalesce";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
        => args.FirstOrDefault(arg => JsonScalarConverter.ToObject(arg) is not null)?.DeepClone();
}
