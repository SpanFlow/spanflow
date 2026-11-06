using System.Linq;
using SpanFlow.Mapping.Engine;
using Xunit;

namespace SpanFlow.Mapping.Tests;

public class ArrayTargetTests
{
    private const string Input = """
        {
          "person": [
            { "id": 101, "fullName": "Alice Smith", "salary": 75000, "department": "Engineering" },
            { "id": 102, "fullName": "Bob Johnson", "salary": 92000, "department": "Marketing" },
            { "id": 103, "fullName": "Carol White", "salary": 110000, "department": "Finance" }
          ]
        }
        """;

    [Fact]
    public void ArrayRoot_ProjectsEveryItem()
    {
        var mapping = TestSupport.Mapping(
            """
            { "target": { "type": "array", "from": "input.person[]", "map": {
              "employeeId": "id",
              "name": "fullName",
              "annualSalary": "salary",
              "displayName": { "function": "concat", "args": [ "fullName", "' - '", "department" ] }
            } } }
            """);

        var result = new MappingEngine().Execute(mapping, TestSupport.Input(Input)).AsArray();

        Assert.Equal(3, result.Count);
        Assert.Equal(101, result[0]!["employeeId"]!.GetValue<int>());
        Assert.Equal("Alice Smith", result[0]!["name"]!.GetValue<string>());
        Assert.Equal(75000, result[0]!["annualSalary"]!.GetValue<int>());
        Assert.Equal("Alice Smith - Engineering", result[0]!["displayName"]!.GetValue<string>());
        Assert.Equal("Carol White - Finance", result[2]!["displayName"]!.GetValue<string>());
    }

    [Fact]
    public void ArrayRoot_FromRootArraySource()
    {
        // Source is itself a top-level array: iterate "input[]".
        var mapping = TestSupport.Mapping(
            """
            { "target": { "type": "array", "from": "input[]", "map": {
              "employeeId": "id",
              "label": { "function": "concat", "args": [ "fullName", "' - '", "department" ] }
            } } }
            """);
        var input = """
            [ { "id": 1, "fullName": "Alice Smith", "department": "Engineering" },
              { "id": 2, "fullName": "Bob Johnson", "department": "Marketing" } ]
            """;

        var result = new MappingEngine().Execute(mapping, TestSupport.Input(input)).AsArray();

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0]!["employeeId"]!.GetValue<int>());
        Assert.Equal("Bob Johnson - Marketing", result[1]!["label"]!.GetValue<string>());
    }

    [Fact]
    public void ArrayTarget_RequiresFromAndMap()
    {
        var mapping = TestSupport.Mapping("""{ "target": { "type": "array" } }""");
        Assert.ThrowsAny<System.Exception>(() => new MappingEngine().Execute(mapping, TestSupport.Input("{}")));
    }
}
