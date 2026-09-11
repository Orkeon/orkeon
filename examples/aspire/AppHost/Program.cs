using Orkeon.Hosting.Aspire;

// An Aspire AppHost that runs the README quickstart crew and shows the run in the
// dashboard: the invoke_agent / chat / execute_tool spans (gen_ai.* conventions), the
// token metrics and the structured logs -- Aspire injects OTEL_EXPORTER_OTLP_ENDPOINT
// into the process, the orkeon runner honours it, nothing else is configured.
//
//   dotnet run --project examples/aspire/AppHost/OrkeonAppHost.csproj
//
// `orkeon` must be on the PATH (dotnet tool install -g Orkeon.Scripting.Cli --prerelease)
// or named through ORKEON_CLI (e.g. a source build: ORKEON_CLI=/path/to/orkeon). The model
// is the quickstart's: Ollama on localhost with qwen2.5:1.5b.
var builder = DistributedApplication.CreateBuilder(args);

var cli = Environment.GetEnvironmentVariable("ORKEON_CLI") ?? "orkeon";
var crew = Path.Combine(builder.AppHostDirectory, "..", "..", "quickstart", "crew.yaml");
// Ollama on this machine by default; ORKEON_Llm__BaseUrl in the AppHost's environment
// points the run elsewhere (a container, another host) without touching the code.
var model = new Uri(Environment.GetEnvironmentVariable("ORKEON_Llm__BaseUrl") ?? "http://localhost:11434");

builder.AddOrkeonCrewRun("quickstart", crew, command: cli)
    .WithOrkeonModel(model, "qwen2.5:1.5b");

builder.Build().Run();
