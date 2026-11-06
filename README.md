# SpanFlow

SpanFlow is a self-hosted platform for integrations, data pipelines and small apps, published as one API.
This repository will hold its full source. For now it holds the mapping engine libraries, which are published
to NuGet; the rest of the product follows in a later release.

## SpanFlow mapping libraries

| Package | What it is |
|---|---|
| [SpanFlow.Mapping.Core](https://www.nuget.org/packages/SpanFlow.Mapping.Core) | The mapping model and its validation |
| [SpanFlow.Mapping.Engine](https://www.nuget.org/packages/SpanFlow.Mapping.Engine) | Runs a mapping: turns one shape of data into another |

Both target netstandard2.0, net8.0, net9.0 and net10.0.

```bash
dotnet add package SpanFlow.Mapping.Engine
```

## Repository layout

| Path | What it is |
|---|---|
| `src/UFMapping/src/SpanFlow.Mapping.Core` | Core library source |
| `src/UFMapping/src/SpanFlow.Mapping.Engine` | Engine library source |
| `src/UFMapping/tests/SpanFlow.Mapping.Tests` | Their tests |

## Building and testing

You need the .NET 10 SDK, plus the .NET 8 and 9 runtimes to run the tests on every target.

```bash
dotnet test src/UFMapping/tests/SpanFlow.Mapping.Tests/SpanFlow.Mapping.Tests.csproj
```

## Licence

The two mapping libraries are MIT-licensed (see the `LICENSE.md` in each folder). Everything else in this
repository is under the [Functional Source License 1.1, Apache 2.0 future licence](LICENSE.md) (FSL-1.1-ALv2).

## Contributing and security

See [CONTRIBUTING.md](CONTRIBUTING.md). Report vulnerabilities privately as described in [SECURITY.md](SECURITY.md),
not in public issues.
