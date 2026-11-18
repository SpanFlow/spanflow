using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace SpanFlow.Mapping.Designer.Tests;

/// <summary>Hosts the designer API in memory with the given settings layered over appsettings.json.</summary>
internal sealed class DesignerFactory(
    IDictionary<string, string?> settings,
    Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? services = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        if (services is not null) builder.ConfigureServices(services);
        // Program reads the mode from builder.Configuration before Build(), so the settings have to be in the
        // host's own configuration rather than added by ConfigureAppConfiguration afterwards.
        foreach (var (key, value) in settings) builder.UseSetting(key, value);
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings));
    }
}

internal static class Samples
{
    public const string ExecuteBody = """
        {
          "mapping": { "target": { "type": "object", "mappings": [
            { "target": "fullName", "transform": { "function": "concat", "args": [ "input.first", "' '", "input.last" ] } },
            { "target": "city", "from": "input.address.city" }
          ] } },
          "inputObject": { "first": "Ada", "last": "Lovelace", "address": { "city": "London" } }
        }
        """;

    public const string TraceBody = """
        {
          "inputObject": { "first": "Ada" },
          "probes": [
            { "id": "n1", "mapping": { "target": { "type": "object", "mappings": [ { "target": "v", "from": "input.first" } ] } } },
            { "id": "n2", "mapping": { "target": { "type": "object", "mappings": [ { "target": "v", "transform": { "function": "noSuchFunction", "args": [] } } ] } } }
          ]
        }
        """;

    /// <summary>E73: XML in, CSV out, through /api/mapping/run.</summary>
    public const string XmlToCsvRunBody = """
        {
          "mapping": {
            "formats": { "input": { "type": "xml", "options": { "arrays": ["Line"] } }, "output": { "type": "text", "options": { "mode": "delimited" } } },
            "target": { "type": "array", "from": "input.Order.Line[]", "map": { "sku": "@sku", "qty": "#text" } }
          },
          "rawInput": "<Order><Line sku=\"A\">2</Line><Line sku=\"B\">1</Line></Order>"
        }
        """;

    public static StringContent Json(string body) => new(body, System.Text.Encoding.UTF8, "application/json");
}
