using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class MultiplyTransform : ITransform
{
    public string Name => "multiply";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
    {
        decimal total = 1m;
        foreach (var arg in args)
        {
            total *= JsonScalarConverter.ToDecimal(arg);
        }

        return JsonValue.Create(total);
    }
}
