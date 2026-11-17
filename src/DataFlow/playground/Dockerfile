# The libraries playground (E71), for anyone to run on their own server. Build from the repository root:
#
#   docker build -f playground/Dockerfile -t libraries-playground .
#   docker run -p 8080:8080 -v playground-data:/data -e Playground__AccessKey=choose-a-key libraries-playground
#
# Then open http://<server>:8080/?key=choose-a-key once. Self-hosted mode: the HTTP panel sends to any address this
# server reaches, databases are the connections you add, files are your stores (local folders under /data/files, S3,
# Azure Blob, FTP). Everything you save is in /data/playground.json, so mount a volume there. Without
# Playground__AccessKey a key is made at start and written to the log: the playground is never open by accident.
# Limits per panel: Playground__Limits__<Expressions|Http|Files|Database>__<PerMinute|PerHour|MaxBodyBytes|...>.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props NuGet.Config* ./
COPY libs/ libs/
COPY playground/ playground/
RUN dotnet publish playground/SpanFlow.Libraries.Playground/SpanFlow.Libraries.Playground.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

RUN mkdir -p /data/files && chown -R app:app /data
VOLUME /data

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0 \
    Playground__Mode=SelfHosted \
    Playground__DataFile=/data/playground.json \
    Playground__LocalFileRoots__0=/data/files
EXPOSE 8080

# The base image's unprivileged user.
USER app
ENTRYPOINT ["dotnet", "SpanFlow.Libraries.Playground.dll"]
