using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class DivideTransform : ITransform
{
    // First argument divided by the rest. Division by zero yields 0.
    public string Name => "divide";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
    {
        if (args.Count == 0)
        {
            return JsonValue.Create(0m);
        }

        var result = JsonScalarConverter.ToDecimal(args[0]);
        for (var i = 1; i < args.Count; i++)
        {
            var divisor = JsonScalarConverter.ToDecimal(args[i]);
            if (divisor == 0m)
            {
                return JsonValue.Create(0m);
            }

            result /= divisor;
        }

        return JsonValue.Create(result);
    }
}
