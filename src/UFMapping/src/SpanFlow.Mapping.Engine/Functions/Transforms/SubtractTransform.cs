using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class SubtractTransform : ITransform
{
    // First argument minus the rest.
    public string Name => "subtract";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
    {
        if (args.Count == 0)
        {
            return JsonValue.Create(0m);
        }

        var result = JsonScalarConverter.ToDecimal(args[0]);
        for (var i = 1; i < args.Count; i++)
        {
            result -= JsonScalarConverter.ToDecimal(args[i]);
        }

        return JsonValue.Create(result);
    }
}
