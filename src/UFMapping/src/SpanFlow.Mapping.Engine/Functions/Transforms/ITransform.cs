using System.Text.Json.Nodes;

namespace SpanFlow.Mapping.Engine;

/// <summary>
/// Transforms a list of already-evaluated arguments into a single value. Add a
/// new transform by implementing this and registering it in
/// <see cref="TransformRegistry.CreateDefault"/>.
/// </summary>
public interface ITransform
{
    /// <summary>The DSL function name (e.g. "concat"). Matched case-insensitively.</summary>
    string Name { get; }

    JsonNode? Apply(IReadOnlyList<JsonNode?> args);
}
