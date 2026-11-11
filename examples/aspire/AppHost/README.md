# Aspire AppHost example

A .NET Aspire AppHost (`Orkeon.Hosting.Aspire`) that runs the README quickstart crew as a
resource and shows the run in the Aspire dashboard: the `invoke_agent`, `chat` and
`execute_tool` spans (OpenTelemetry GenAI conventions), the `gen_ai.client.*` metrics and
the structured logs. Nothing is configured for that: Aspire injects
`OTEL_EXPORTER_OTLP_ENDPOINT` into the process and the `orkeon` runner honours it.

## Run

```bash
ollama pull qwen2.5:1.5b
dotnet tool install -g Orkeon.Scripting.Cli --prerelease   # or ORKEON_CLI=/path/to/a/source-built orkeon
dotnet run --project examples/aspire/AppHost/OrkeonAppHost.csproj
```

The console prints the dashboard URL (with its login token). The `quickstart` resource
runs `orkeon run ../../quickstart/crew.yaml --mount <AppHost>/out:/output:rw` and exits;
its traces and logs stay in the dashboard. `out/hello.md` is the crew's file.

`AddOrkeonHost("orkeon-host", settingsPath: …)` adds the service daemon the same way — see
the [Orkeon.Hosting.Aspire README](../../../src/hosting/Orkeon.Hosting.Aspire/README.md).
