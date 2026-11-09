# SpanFlow.Expressions

The expression and `{{ template }}` language that [SpanFlow](https://github.com/SpanFlow/spanflow) pipelines use,
packaged as a library. It reads JSON (`System.Text.Json.Nodes`) and is deliberately **not** a scripting engine:
an expression can read paths, compare values, combine them with `&&`, `||` and `!` and call `exists()`, and nothing
else. You can evaluate templates written by your users without running their code.

```csharp
using System.Text.Json.Nodes;
using SpanFlow.Expressions;

var context = new ExpressionContext
{
    Input = JsonNode.Parse("""{ "order": { "id": 42, "total": 120.5, "items": [1, 2, 3] } }"""),
};

ExpressionEvaluator.EvaluateAsBool("input.order.total > 100 && input.order.items.length == 3", context); // true

// A template that is one whole {{ }} keeps its JSON type; mixed text becomes a string.
TemplateRenderer.RenderString("{{ input.order.id }}", context);         // 42 (a number)
TemplateRenderer.RenderString("Order #{{ input.order.id }}", context); // "Order #42"

// Render every {{ }} inside a JSON document.
var body = TemplateRenderer.Render(
    JsonNode.Parse("""{ "ref": "{{ input.order.id }}", "big": "{{ input.order.total > 100 }}" }"""), context);
```

| Syntax | Meaning |
|---|---|
| `input.items[0].id` | A path. A missing path is `null`, never an error. |
| `==` `!=` `<` `<=` `>` `>=` | Comparisons (numbers compare as numbers). |
| `&&` `\|\|` `!` `( )` | Boolean logic. `false`, `null`, `0`, `""` and `[]` count as false. |
| `x.length` | Length of a string or array. |
| `exists(path)` | True when the path has a value (a missing path or a `null` is false). |

The roots a context can expose are `input`, `trigger`, `nodes.<id>.output`, `params`, `state`, `vars`, `user`,
`headers` and `body`.

Targets .NET 8, 9 and 10. MIT licensed.
