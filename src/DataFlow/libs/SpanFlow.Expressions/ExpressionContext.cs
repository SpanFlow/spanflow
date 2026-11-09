using System.Text.Json.Nodes;

namespace SpanFlow.Expressions;

/// <summary>
/// What `{{ ... }}` expressions can see: the node's default input, the run's trigger payload,
/// and prior node outputs (`nodes.&lt;id&gt;.output...`).
/// </summary>
// A record, not a class, for `with`: a node that adds one root (a retrieve adding `cursor`) copies
// the rest rather than restating it, so a root added later cannot be silently dropped on that path.
public record ExpressionContext
{
    public JsonNode? Input { get; init; }
    public JsonNode? Trigger { get; init; }

    /// <summary>Named operation parameters, exposed as `params.*` (app-manifest operations, E10).</summary>
    public JsonNode? Params { get; init; }

    /// <summary>The pipeline's persistent state, exposed as `state.*` (watermarks, E14-T4).</summary>
    public JsonNode? State { get; init; }

    /// <summary>A chat session's captured variables, exposed as `vars.*` (E25 flow runtime).</summary>
    public JsonNode? Vars { get; init; }

    /// <summary>A chat session's identity claims, exposed as `user.*` (E25 flow runtime; set by a login node).</summary>
    public JsonNode? User { get; init; }

    /// <summary>Inbound request headers, exposed as `headers.*` / `headers['X-...']` (E06-T6 webhook routing).</summary>
    public JsonNode? Headers { get; init; }

    /// <summary>An inbound request body, exposed as `body.*` (E06-T6 webhook routing).</summary>
    public JsonNode? Body { get; init; }

    /// <summary>
    /// This retrieve node's cursor, exposed as `cursor.from` / `cursor.first` (E44-T2). Node-specific
    /// like `input`: two retrieves in one pipeline carry two independent watermarks, and a node
    /// without a cursor has none, so `{{cursor.from}}` there resolves to null rather than to a
    /// neighbour's value.
    /// </summary>
    public JsonNode? Cursor { get; init; }

    /// <summary>
    /// Facts about the run itself, exposed as `run.source` / `run.triggerType` / `run.runId`
    /// (E44-T7). Separate from `trigger`, which is the caller's own payload — putting a `source` key
    /// in there would overwrite a field of that name in somebody's webhook body.
    /// </summary>
    public JsonNode? Run { get; init; }

    public IReadOnlyDictionary<string, JsonNode?> NodeOutputs { get; init; } =
        new Dictionary<string, JsonNode?>();

    /// <summary>
    /// Every identifier an expression may start with. A name outside this set is a mistake — a typo,
    /// or a root this surface doesn't offer — and the evaluator rejects it rather than quietly
    /// resolving to null, which is indistinguishable from "the value was absent" and turns a typo
    /// into an empty result set with no error anywhere.
    /// </summary>
    internal static readonly IReadOnlySet<string> KnownRoots = new HashSet<string>(StringComparer.Ordinal)
    {
        "input", "trigger", "params", "state", "vars", "user", "headers", "body", "nodes", "cursor", "run",
    };

    /// <summary>Resolves a root identifier (`input`, `trigger`, `params`, `state`, `vars`, `user`,
    /// `headers`, `body`, `nodes`) or null when absent.</summary>
    internal JsonNode? ResolveRoot(string name) => name switch
    {
        "input" => Input,
        "trigger" => Trigger,
        "params" => Params,
        "state" => State,
        "vars" => Vars,
        "user" => User,
        "headers" => Headers,
        "body" => Body,
        "cursor" => Cursor,
        "run" => Run,
        _ => null,
    };
}
