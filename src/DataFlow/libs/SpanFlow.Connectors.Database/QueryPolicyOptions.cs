namespace SpanFlow.Connectors.Database;

/// <summary>
/// The deployment's ceiling on a db read (E22-T3), the companion to
/// <c>db.query-policy</c>: the feature key says <i>whether</i> a project is policed, this says
/// <i>how much</i> a policed read may bring back.
/// <para>
/// A number, not a per-project row, for the same reason the pipeline payload limit is: it
/// is an operator's limit on how much memory one run may take, and the operator is the person who
/// knows how much the box has. A hosted multi-tenant deployment sets it low; a self-hosted box moving
/// warehouse extracts raises it. Which *projects* are held to it is the per-project decision, and that
/// is the feature grant.
/// </para>
/// </summary>
public class QueryPolicyOptions
{
    public const string SectionName = "QueryPolicy";

    /// <summary>
    /// Most rows a db retrieve may return when the policy is enforced. The reader stops there and the
    /// step fails, rather than truncating: half a table silently delivered as if it were the whole one
    /// is a data-corruption bug, and a loud failure is the only honest answer.
    /// </summary>
    public int MaxRows { get; set; } = 100_000;
}
