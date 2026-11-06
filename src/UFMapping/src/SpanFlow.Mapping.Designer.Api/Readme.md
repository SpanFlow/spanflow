# SpanFlow.Mapping.Designer.Api

The mapping designer's API: runs mapping definitions with `SpanFlow.Mapping.Engine`. It runs two ways.

| Mode | What it is | Needs |
|---|---|---|
| **Platform** | One service of the SpanFlow stack. Sign-in through Platform (JWT, proxied login/refresh/logout), Platform API keys, dynamic CORS, queue-backed logging, consumption recording. The UI is the Studio's Mapping tab. | Platform, and whatever Platform's messaging is set up for |
| **Standalone** | The designer on its own for local or self-hosted use. No sign-in: every endpoint is open. Serves the standalone designer page from `wwwroot`. | Nothing |

`Platform:Enabled` picks the mode. Unset, it is Platform when the build has the integration and
`Platform:BaseUrl` is set (how every stack install runs), Standalone otherwise.

## Builds

`PlatformIntegration.props` decides whether `Platform.Client` is compiled in. It is on when
`../../../Platform/Platform.Client` exists (the private repos side by side, or the full public tree) and off
otherwise, so the public mapping release builds the standalone designer from Core and Engine alone. A
standalone build has no Platform code at all (`Platform/` is left out) and refuses `Platform:Enabled=true`.

```bash
dotnet build -p:PlatformIntegration=false   # force the standalone-only build
```

## Endpoints

| Endpoint | Platform mode | Standalone |
|---|---|---|
| `POST /api/mapping/execute` | JWT or API key | open |
| `POST /api/mapping/trace` | JWT or API key | open |
| `POST /api/auth/login`, `/refresh`, `/logout` | proxied to Platform | 404 |
| `GET /api/designer/info` | 404 | `{ "mode": "standalone", ... }` |
| `GET /healthz`, `/readyz` | open | open |

`execute` takes `{ "mapping": {...}, "inputObject": {...} }` and returns the output, or `400` with
`{ "error": "..." }` (a stack trace only in Development).

## Run

Platform mode (port 5215, needs Platform running; see the `platform-local-run` skill in the Platform repo):

```bash
dotnet run
```

Standalone, with the designer UI from platform-admin's dev server:

```bash
dotnet run -- --Platform:Enabled=false
```

```bash
cd ../../../Platform/platform-admin && npm run dev:mapping   # http://localhost:5187, /api proxied to :5215
```

Standalone image (API + UI, see `Dockerfile.designer` at the repo root):

```bash
docker build -f Dockerfile.designer --build-context admin=../Platform/platform-admin -t spanflow/mapping-designer .
docker run -p 8080:8080 spanflow/mapping-designer
```
