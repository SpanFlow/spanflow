# SpanFlow Mapping

Turn one shape of JSON into another with a declarative mapping definition: paths, functions, conditions,
grouping, joins and aggregates. It runs in-process with no server, and it is the engine behind SpanFlow's
transform node and visual mapping designer.

| Package | What it holds |
|---|---|
| `SpanFlow.Mapping.Core` | The mapping definition model and its validator |
| `SpanFlow.Mapping.Engine` | `MappingEngine`, which runs a definition over JSON input |
| `SpanFlow.Mapping.Formats` | `MappingRunner`: reads the raw input into JSON, runs the engine, writes the output format: json, xml (optionally shaped and typed by an XSD, with validation), text (whole, lines, CSV/delimited, regex pattern, fixed width), positional layouts (fixed, LL/LLL prefixes, records, ISO 8583 bitmaps; bundled ISO 8583:1987 starter layouts) and EDI (X12, EDIFACT; level 2 guides with named segments, loops and a problem report; bundled 850, 810, 856, 997, ORDERS, INVOIC, DESADV); any code page incl. EBCDIC |

Targets .NET 8, 9 and 10, plus .NET Standard 2.0 (which includes .NET Framework 4.6.2+).

## Example

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using SpanFlow.Mapping.Core.Models;
using SpanFlow.Mapping.Engine;

var mapping = JsonSerializer.Deserialize<MappingDefinition>("""
{
  "target": {
    "type": "object",
    "mappings": [
      { "target": "profile.fullName",
        "transform": { "function": "concat", "args": [ "input.user.firstName", "' '", "input.user.lastName" ] } },
      { "target": "summary.completedAmount",
        "aggregate": { "function": "sum", "from": "input.orders[]",
                       "where": { "field": "status", "op": "eq", "value": "Completed" }, "select": "amount" } }
    ]
  }
}
""", new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

var input = JsonNode.Parse("""
{ "user": { "firstName": "Ada", "lastName": "Lovelace" },
  "orders": [ { "status": "Completed", "amount": 100 }, { "status": "Pending", "amount": 50 } ] }
""");

JsonNode output = new MappingEngine().Execute(mapping, input);
// {"profile":{"fullName":"Ada Lovelace"},"summary":{"completedAmount":100}}
```

## Notes for .NET Framework / .NET Standard 2.0

- IANA time zone ids (`America/New_York`) and Windows ids both work on every target; on .NET Standard 2.0
  they are resolved through the TimeZoneConverter package.
- .NET Framework's `double.Parse` is not correctly rounded, so a few extreme values (such as `1e300`) can come
  back one unit in the last place off from what .NET 8+ produces. Integers and decimals are unaffected.

## Licence

MIT. These two libraries are MIT-licensed; the rest of SpanFlow is published under FSL-1.1-ALv2.
