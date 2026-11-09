# SpanFlow.Connectors.Http

Builds HTTP requests from a JSON description, the way an API tool does, and signs them with common API
authentication schemes. It comes from [SpanFlow](https://github.com/SpanFlow/spanflow), where it handles every HTTP
call a pipeline makes.

**Authentication schemes** (`IAuthStrategy`, chosen by `auth.type`):

| `type` | Scheme |
|---|---|
| `none`, `apiKey` (in a header, query or cookie), `basic`, `bearer` | Static credentials |
| `oauth2ClientCredentials`, `tokenEndpoint`, `loginRequest` | A token that is fetched, cached and renewed before it expires |
| `digest` | HTTP Digest (RFC 7616: MD5 or SHA-256, `qop=auth`) |
| `hawk` | Hawk, with payload hashing |
| `oauth1` | OAuth 1.0a (HMAC-SHA1/256/512, RSA-SHA1/256/512 or PLAINTEXT; in the header, query or form body) |
| `jwtBearer` | A JWT you sign (HS, RS, PS or ES with 256, 384 or 512), sent as a bearer token |

```csharp
using System.Text.Json.Nodes;
using SpanFlow.Connectors.Http;
using SpanFlow.Connectors.Http.Auth;

var request = JsonNode.Parse("""
{
  "method": "POST",
  "path": "/v1/orders",
  "query": { "dryRun": "true" },
  "headers": { "X-Request-Id": "abc-123" },
  "bodyType": "json",
  "body": { "sku": "A-1", "qty": 2 }
}
""")!.AsObject();

var settings = HttpRequestSettings.Default;
using var message = new HttpRequestMessage(HttpMethod.Post, HttpRequestBuilder.BuildUri("https://api.example.com", request, settings))
{
    Content = HttpRequestBuilder.BuildContent(request, explicitBody: null),
};
HttpRequestBuilder.ApplyHeaders(message, new Dictionary<string, string>(), request, settings);

// Sign it. The static registry holds the schemes that need no token store or IHttpClientFactory.
var auth = JsonNode.Parse("""{ "type": "hawk", "authId": "id1", "authKey": "a-shared-key" }""")!.AsObject();
await AuthStrategyRegistry.CreateStatic().Get("hawk").ApplyAsync(message, auth, CancellationToken.None);
```

The token-based schemes take an `IHttpClientFactory` and a `TokenCache`. To share tokens across processes, give
them your own `ISharedTokenStore`. `HttpThrottleRegistry` spaces out calls to each host.

Targets .NET 8, 9 and 10. MIT licensed.
