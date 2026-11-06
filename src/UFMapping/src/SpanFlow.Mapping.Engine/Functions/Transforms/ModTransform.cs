using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class ModTransform : ITransform
{
    // Remainder of arg0 / arg1. A zero divisor yields 0.
    public string Name => "mod";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
    {
        var a = JsonScalarConverter.ToDecimal(args.ElementAtOrDefault(0));
        var b = JsonScalarConverter.ToDecimal(args.ElementAtOrDefault(1));
        return JsonValue.Create(b == 0m ? 0m : a % b);
    }
}
