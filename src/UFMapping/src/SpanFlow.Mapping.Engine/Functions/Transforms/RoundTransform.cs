using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

public sealed class RoundTransform : ITransform
{
    // round(number, [decimals]); decimals defaults to 0.
    public string Name => "round";

    public JsonNode? Apply(IReadOnlyList<JsonNode?> args)
    {
        var value = JsonScalarConverter.ToDecimal(args.FirstOrDefault());
        var decimals = args.Count > 1 ? (int)JsonScalarConverter.ToDecimal(args.ElementAtOrDefault(1)) : 0;
        decimals = Math.Clamp(decimals, 0, 28);

        return JsonValue.Create(Math.Round(value, decimals, MidpointRounding.AwayFromZero));
    }
}
