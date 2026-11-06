using System.Text.Json;
using System.Text.Json.Nodes;
using SpanFlow.Mapping.Core.Models;
using SpanFlow.Mapping.Engine;

namespace SpanFlow.Mapping.Tests;

internal static class TestSupport
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static MappingDefinition Mapping(string json) =>
        JsonSerializer.Deserialize<MappingDefinition>(json, Options)
        ?? throw new InvalidOperationException("Failed to deserialize mapping.");

    public static JsonNode? Input(string json) => JsonNode.Parse(json);

    public static JsonNode Run(string mappingJson, string inputJson) =>
        new MappingEngine().Execute(Mapping(mappingJson), Input(inputJson));

    /// <summary>Wraps a single mapping rule in a complete object-target definition.</summary>
    public static string Rule(string ruleJson) =>
        $$"""{ "target": { "type": "object", "mappings": [ {{ruleJson}} ] } }""";
}
