# SpanFlow

SpanFlow is a self-hosted platform for integrations, data pipelines and small apps, published as one API.
This repository will hold its full source. For now it holds the mapping engine libraries, which are published
to NuGet, and the standalone mapping designer; the rest of the product follows in a later release.

## SpanFlow mapping libraries

| Package | What it is |
|---|---|
| [SpanFlow.Mapping.Core](https://www.nuget.org/packages/SpanFlow.Mapping.Core) | The mapping model and its validation |
| [SpanFlow.Mapping.Engine](https://www.nuget.org/packages/SpanFlow.Mapping.Engine) | Runs a mapping: turns one shape of data into another |

Both target netstandard2.0, net8.0, net9.0 and net10.0.

```bash
dotnet add package SpanFlow.Mapping.Engine
```

## Mapping designer

A visual editor for mappings: drag Source, Target, Transform, Filter, Join, For Each, Group By and the other
built-in nodes onto a canvas, wire them up, paste sample input and see the output as you go. The mapping it
produces is the same JSON the engine runs, so a mapping designed here runs unchanged in your own code.

Try it without installing anything at <https://mapping.spanflow.app>: the designer in your browser, no sign-in. The
same host is a free mapping API, limited per caller:

```bash
curl -X POST https://mapping.spanflow.app/ -H "Content-Type: application/json" \
  -d '{ "mapping": { "target": { "type": "object", "mappings": [ { "target": "name", "from": "input.fullName" } ] } },
        "inputObject": { "fullName": "Alice Smith" } }'
```

Run it yourself with Docker:

```bash
docker run -p 8080:8080 ghcr.io/spanflow/mapping-designer
```

Then open <http://localhost:8080>. Your work is saved in the browser; **Save Pipeline** writes it to a file.

This is the designer on its own: no sign-in, no database, nothing else to install. Every endpoint is open, so
keep it on your machine or put it behind your own authentication (a reverse proxy, a VPN) before you expose it.

The API it runs on answers on the same port:

| Endpoint | What it does |
|---|---|
| `POST /api/mapping/execute` | Runs `{ "mapping": {...}, "inputObject": {...} }` and returns the output, or `400` with `{ "error": "..." }` |
| `POST /api/mapping/trace` | Runs one probe mapping per node and times each (what the designer's debug view shows) |
| `GET /api/designer/info` | Which mode answered (`standalone`) and the engine version |
| `GET /healthz`, `GET /readyz` | Liveness and readiness probes |

Settings are environment variables. The ones you are likely to want:

| Variable | Default | What it does |
|---|---|---|
| `Kestrel__Limits__MaxRequestBodySize` | `31457280` (30 MB) | Largest request body, so the largest sample input |
| `Cors__Origins__0` | none | An origin allowed to call the API from a page served elsewhere |

### Building it yourself

The image needs only this repository:

```bash
docker build src/UFMapping -f src/UFMapping/Dockerfile.designer --build-context admin=src/Platform/platform-admin -t mapping-designer
```

Or run it without Docker (the .NET 10 SDK and Node.js 22):

```bash
dotnet run --project src/UFMapping/src/SpanFlow.Mapping.Designer.Api --urls http://localhost:5215
```

```bash
cd src/Platform/platform-admin && npm ci && npm run dev:mapping
```

The second command serves the designer UI at <http://localhost:5187> and sends its API calls to the first.

## Repository layout

| Path | What it is |
|---|---|
| `src/UFMapping/src/SpanFlow.Mapping.Core` | Core library source |
| `src/UFMapping/src/SpanFlow.Mapping.Engine` | Engine library source |
| `src/UFMapping/tests/SpanFlow.Mapping.Tests` | Their tests |
| `src/UFMapping/src/SpanFlow.Mapping.Designer.Api` | The designer's API |
| `src/UFMapping/tests/SpanFlow.Mapping.Designer.Tests` | Its tests |
| `src/UFMapping/Dockerfile.designer` | The designer image |
| `src/Platform/platform-admin` | The designer UI: `src/mapping` is the designer, `src/standalone` its page. Only the files that page needs are here; the rest of this app follows with the full release |

## Building and testing

You need the .NET 10 SDK, plus the .NET 8 and 9 runtimes to run the library tests on every target.

```bash
dotnet test src/UFMapping/tests/SpanFlow.Mapping.Tests/SpanFlow.Mapping.Tests.csproj
```

```bash
dotnet test src/UFMapping/tests/SpanFlow.Mapping.Designer.Tests/SpanFlow.Mapping.Designer.Tests.csproj
```

## Licence

The two mapping libraries are MIT-licensed (see the `LICENSE.md` in each folder). Everything else in this
repository, the designer included, is under the [Functional Source License 1.1, Apache 2.0 future licence](LICENSE.md)
(FSL-1.1-ALv2).

## Contributing and security

See [CONTRIBUTING.md](CONTRIBUTING.md). Report vulnerabilities privately as described in [SECURITY.md](SECURITY.md),
not in public issues.
