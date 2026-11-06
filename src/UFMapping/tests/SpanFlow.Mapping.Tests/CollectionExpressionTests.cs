using Xunit;

namespace SpanFlow.Mapping.Tests;

/// <summary>
/// A path ending in "[]" is a collection expression: it evaluates to the whole
/// list, not just its first element. Regression cover for a bug where wiring a
/// list into a node via `"input": { "from": "...[]" }` silently kept only row 1,
/// so the node returned a plausible but wrong result instead of failing.
/// </summary>
public class CollectionExpressionTests
{
    private const string Orders = """
        { "orders": [
          { "id": 1, "status": "Completed", "amount": 100 },
          { "id": 2, "status": "Pending",   "amount": 250 },
          { "id": 3, "status": "Completed", "amount": 50 }
        ] }
        """;

    [Fact]
    public void Filter_WiredViaInputExpression_SeesEveryRow()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "completed", "filter": {
              "input": { "from": "input.orders[]" },
              "where": { "field": "status", "op": "eq", "value": "Completed" } } }
            """);

        // Row 3 matches too: before the fix only row 1 was ever considered.
        var completed = TestSupport.Run(mapping, Orders)["completed"]!.AsArray();
        Assert.Equal([1, 3], completed.Select(row => row!["id"]!.GetValue<int>()));
    }

    [Fact]
    public void Filter_WiredViaInputExpression_MissingFirstRowMatch_StillFindsLaterRows()
    {
        // The old behavior's worst case: the first row does NOT match, so the
        // whole filter came back empty even though rows 2..n did match.
        var mapping = TestSupport.Rule(
            """
            { "target": "pending", "filter": {
              "input": { "from": "input.orders[]" },
              "where": { "field": "status", "op": "eq", "value": "Pending" } } }
            """);

        var pending = Assert.Single(TestSupport.Run(mapping, Orders)["pending"]!.AsArray());
        Assert.Equal(2, pending!["id"]!.GetValue<int>());
    }

    [Fact]
    public void Sort_WiredViaInputExpression_SortsEveryRow()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "byAmountDesc", "sort": {
              "input": { "from": "input.orders[]" },
              "keys": [ { "by": "amount", "order": "desc" } ] } }
            """);

        var sorted = TestSupport.Run(mapping, Orders)["byAmountDesc"]!.AsArray();
        Assert.Equal([250m, 100m, 50m], sorted.Select(row => row!["amount"]!.GetValue<decimal>()));
    }

    [Fact]
    public void Aggregate_WiredViaInputExpression_FoldsEveryRow()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "total", "aggregate": {
              "function": "sum",
              "input": { "from": "input.orders[]" },
              "select": "amount" } }
            """);

        // 100 + 250 + 50; the old behavior returned just 100.
        Assert.Equal(400m, TestSupport.Run(mapping, Orders)["total"]!.GetValue<decimal>());
    }

    [Fact]
    public void PlainStringCollectionExpression_EvaluatesToTheWholeList()
    {
        var mapping = TestSupport.Rule("""{ "target": "all", "from": "input.orders[]" }""");

        Assert.Equal(3, TestSupport.Run(mapping, Orders)["all"]!.AsArray().Count);
    }

    [Fact]
    public void ScalarPath_IsUnaffected()
    {
        var mapping = TestSupport.Rule("""{ "target": "first", "from": "input.orders.id" }""");

        // No "[]" marker: still resolves to a single value, not an array.
        Assert.Equal(1, TestSupport.Run(mapping, Orders)["first"]!.GetValue<int>());
    }

    [Fact]
    public void MidPathMarker_ProjectsTheFieldAcrossEveryElement()
    {
        // "[]" means "fan out" wherever it appears, not only at the end.
        var mapping = TestSupport.Rule("""{ "target": "amounts", "from": "input.orders[].amount" }""");

        var amounts = TestSupport.Run(mapping, Orders)["amounts"]!.AsArray();
        Assert.Equal([100m, 250m, 50m], amounts.Select(a => a!.GetValue<decimal>()));
    }

    [Fact]
    public void MidPathMarker_WorksAsATransformArgument()
    {
        var mapping = TestSupport.Rule(
            """
            { "target": "total", "aggregate": {
              "function": "sum", "input": { "from": "input.orders[].amount" } } }
            """);

        Assert.Equal(400m, TestSupport.Run(mapping, Orders)["total"]!.GetValue<decimal>());
    }

    [Fact]
    public void Marker_WorksOnACollectionOfScalars_AsWellAsObjects()
    {
        // The marker doesn't care what the array holds: it yields the elements
        // as they are — scalars stay scalars, objects stay objects.
        var mapping = TestSupport.Rule("""{ "target": "tags", "from": "input.tags[]" }""");

        var tags = TestSupport.Run(mapping, """{ "tags": ["a", "b", "c"] }""")["tags"]!.AsArray();
        Assert.Equal(["a", "b", "c"], tags.Select(t => t!.GetValue<string>()));
    }

    [Fact]
    public void Marker_OnASingleElementArray_StillYieldsAnArray()
    {
        // A one-row list is a list, not a bare object — the shape follows the
        // marker, not the row count, so downstream nodes stay predictable.
        var mapping = TestSupport.Rule("""{ "target": "rows", "from": "input.orders[]" }""");

        var rows = TestSupport.Run(mapping, """{ "orders": [ { "id": 1 } ] }""")["rows"]!.AsArray();
        Assert.Equal(1, Assert.Single(rows)!["id"]!.GetValue<int>());
    }

    [Fact]
    public void Marker_OnANonArray_YieldsEmpty_NotTheObject()
    {
        // Trap worth knowing: "[]" asks for array elements. Point it at a plain
        // object and there are none, so you get [] — use a bare path for objects.
        var mapping = TestSupport.Rule("""{ "target": "person", "from": "input.person[]" }""");

        Assert.Empty(TestSupport.Run(mapping, """{ "person": { "name": "Ada" } }""")["person"]!.AsArray());
    }
}
