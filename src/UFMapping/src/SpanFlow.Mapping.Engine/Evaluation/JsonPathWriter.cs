using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

internal static class JsonPathWriter
{
    public static void SetValue(JsonObject target, string path, JsonNode? value)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            throw new InvalidOperationException("Target path cannot be empty.");
        }

        JsonObject current = target;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            if (current[parts[index]] is not JsonObject next)
            {
                next = new JsonObject();
                current[parts[index]] = next;
            }

            current = next;
        }

        current[parts[^1]] = value?.DeepClone();
    }
}
