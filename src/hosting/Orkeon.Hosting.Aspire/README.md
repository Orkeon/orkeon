# Orkeon.Hosting.Aspire

Part of [Orkeon](https://github.com/Orkeon/orkeon) — AI agent teams that stay inside the lines.

The .NET Aspire hosting integration: describe Orkeon processes in an AppHost and read their
runs in the Aspire dashboard — every `invoke_agent`, `chat` and `execute_tool` span
(OpenTelemetry GenAI conventions), the token metrics and the structured logs, with no
configuration: Aspire injects `OTEL_EXPORTER_OTLP_ENDPOINT`, the runners honour it.

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// the service daemon, with its crews registered in its settings file
builder.AddOrkeonHost("orkeon-host", settingsPath: "host.appsettings.json");

// one crew run, files under ./out, on a local model
builder.AddOrkeonCrewRun("quickstart", crewPath: "../../quickstart/crew.yaml")
       .WithOrkeonModel(new Uri("http://localhost:11434"), "llama3.2:1b");

builder.Build().Run();
```

Both resources launch the shipped executables (`orkeon-host`, `orkeon`) found on the PATH —
or the `command:` you pass. `WithOrkeonSetting("Llm:Model", …)` sets any Orkeon
configuration key through the `ORKEON_` environment the runners read.

The assembly itself references nothing of Orkeon but a constants satellite — it describes
processes, it does not run crews; the package depends on `Aspire.Hosting` and on the
`Orkeon` umbrella, which is where that satellite ships.

MIT © Orkeon Contributors
