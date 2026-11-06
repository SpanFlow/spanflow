using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

internal static class JsonPathResolver
{
    /// <summary>Reserved root naming the members of the enclosing groupBy group.</summary>
    public const string GroupSourceName = "group";

    public static IEnumerable<JsonNode?> ResolveMany(EvaluationContext context, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        var (root, remainingPath) = ResolveRoot(context, path);
        if (root is null)
        {
            return [];
        }

        IEnumerable<JsonNode?> current = [root];
        if (string.IsNullOrWhiteSpace(remainingPath))
        {
            return current;
        }

        foreach (var segment in remainingPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var isCollection = segment.EndsWith("[]", StringComparison.Ordinal);
            var property = isCollection ? segment[..^2] : segment;

            current = current.SelectMany(node =>
            {
                // Read `property` off a single node, fanning a collection segment out.
                static IEnumerable<JsonNode?> ResolveProperty(JsonNode? node, string property, bool isCollection)
                {
                    var next = string.IsNullOrWhiteSpace(property)
                        ? node
                        : node is JsonObject obj ? obj[property] : null;

                    if (isCollection)
                    {
                        return next is JsonArray array ? array.AsEnumerable() : [];
                    }

                    return next is null ? [] : [next];
                }

                // If the current node is an array and we're reading a property,
                // implicitly map across its elements (e.g. `orders.amount`).
                if (node is JsonArray arrayNode && !string.IsNullOrWhiteSpace(property))
                {
                    return arrayNode.SelectMany(element => ResolveProperty(element, property, isCollection));
                }

                return ResolveProperty(node, property, isCollection);
            });
        }

        return current;
    }

    public static JsonNode? ResolveSingle(EvaluationContext context, string path)
    {
        return ResolveMany(context, path).FirstOrDefault();
    }

    private static (JsonNode? Root, string? RemainingPath) ResolveRoot(EvaluationContext context, string path)
    {
        if (path == "$")
        {
            if (context.Current is not null)
            {
                return (context.Current, null);
            }

            return context.Sources.TryGetValue("input", out var root)
                ? (root, null)
                : (null, null);
        }

        var parts = path.Split('.', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0)
        {
            // The first segment may itself request a collection (e.g. "input[]"),
            // which fans the root array out into its elements.
            var first = parts[0];
            var firstIsCollection = first.EndsWith("[]", StringComparison.Ordinal);
            var key = firstIsCollection ? first[..^2] : first;

            // Inside a groupBy map, `group` names the group's own members. It takes
            // precedence over a same-named source, which the engine rejects up front.
            if (context.GroupArray is not null &&
                string.Equals(key, GroupSourceName, StringComparison.OrdinalIgnoreCase))
            {
                return (context.GroupArray, Remainder(parts, firstIsCollection));
            }

            if (context.Sources.TryGetValue(key, out var sourceRoot))
            {
                return (sourceRoot, Remainder(parts, firstIsCollection));
            }
        }

        if (context.Current is not null)
        {
            return (context.Current, path);
        }

        return context.Sources.TryGetValue("input", out var inputRoot)
            ? (inputRoot, path)
            : (null, null);
    }

    private static string? Remainder(string[] parts, bool firstIsCollection)
    {
        // "[]" is a root-level collection marker, fanned out by the segment loop.
        return parts.Length == 2 ? parts[1] : firstIsCollection ? "[]" : null;
    }
}
