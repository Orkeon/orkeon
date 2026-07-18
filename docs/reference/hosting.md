# Orkeon.Hosting — runner & host bootstrap

`Orkeon.Hosting` is the shared bootstrap layer that turns the Orkeon libraries into a runnable host.
It owns the wiring order that a working runtime needs — LLM provider, core services, the standard tool
suites, the virtual file system, and the DI-backed tool registry — plus the end-to-end execution flows
(one-shot kickoff, interactive loop, tool listing) used by every Orkeon runner and CLI.

It is consumed by the runner harness and by external hosts that reference it as a package. This page documents its public bootstrap surface.

## Package

| | |
|---|---|
| PackageId | `Orkeon.Hosting` |
| Depends on | all `Orkeon.*` libraries (Domain, Application, Infrastructure, Analysis, Scripting, and the `Orkeon.Tools.*` suites) plus `CommandLineParser` and `Microsoft.Extensions.Hosting` |
| Packs via | `.github/workflows/publish.yml` (`dotnet pack Orkeon.sln`) — the whole solution is packed on a `v*` tag, so `Orkeon.Hosting` is included automatically |

The `Orkeon.*` project references become package dependencies in the nuspec; the VFS-compliance analyzer
reference is `PrivateAssets=all` and is correctly excluded from the package.

## `RunnerHost.Build`

`RunnerHost` is a static host builder. `Build` returns a fully configured `IHost`:

```csharp
IHost host = RunnerHost.Build(
    settingsPath: "appsettings.json",   // resolved appsettings path (nullable)
    cliMounts: ["/data:/data:ro"],       // CLI --mount args ("physical:virtual:rights")
    allowExternalMounts: false,          // whitelist mount base paths outside the workspace root
    llmLogPath: null,                    // when set, captures LLM HTTP exchanges as .jsonl
    configureLogging: null,              // optional ILoggingBuilder customization
    configureServices: null);            // optional hook to register runner-specific services
```

It composes `Host.CreateDefaultBuilder()` with:

- **App configuration** — appsettings resolution plus the CLI mount arguments folded into configuration.
- **Services** — `ConfigureRunnerServices` (below).

`RunnerHost` carries an `[SuppressVfsCompliance]` bootstrap exception because it resolves user-supplied
settings paths and provisions VFS mounts *before* the DI container (and thus `IFileSystemService`) exists.

### `ConfigureRunnerServices` behavior

The registration order is deliberate:

1. **Logging** — runner logging (Console + Information by default) and, when `llmLogPath` is set, the LLM
   exchange logging `DelegatingHandler`.
2. **LLM provider first** — `RegisterLlmProvider` reads the `Llm` config section and registers the
   provider (and its `IChatClient`) **before** `AddOrkeonApplication` / `AddOrkeonInfrastructure`. This
   ordering matters: Orkeon infrastructure registers its LLM/`IChatClient` fallbacks with `TryAdd`, so a
   host-supplied provider must be registered first to win.
3. **Core services** — `AddOrkeonApplication()` then `AddOrkeonInfrastructure()`.
4. **Strict tools** — `CrewFactoryOptions.StrictTools` defaults to `true` here (a crew referencing an
   unknown tool fails loudly with `unknown tool(s): …; available: …`); opt out with
   `"Orkeon:CrewFactory:StrictTools": false`. (The library default stays lenient.)
5. **Standard tool suites** — file system, data, web, code, abstractions, session tools; the in-memory
   EventHub plus its agent tools; RaggableTree (semantic-graph tools, opt out with
   `"RaggableTree:Enabled": false`); the WebSearch and `cache_search` tools; and the Brave search tool
   when `BRAVE_API_KEY` is present.
6. **VFS mounts** — `AddOrkeonFileSystem` when `Orkeon:FileSystem:Mounts` is configured.
7. **Tool registry** — `ServiceProviderToolRegistry` is registered as the singleton `IToolRegistry`.
8. **Runner services** — the caller's `configureServices` hook runs last.

## `ServiceProviderToolRegistry`

The `IToolRegistry` implementation that resolves YAML/TS tool names to `IBaseTool` instances **from DI**.
Its constructor takes `IEnumerable<IBaseTool>` — every tool the tool suites registered — and indexes them
by name (case-insensitive). `CrewFactory` consumes it to build agents with their declared tools, which is
why every tool suite registers under `IBaseTool`: a tool that is not registered cannot be resolved (and,
with `StrictTools`, fails crew loading rather than silently dropping).

## `RunnerExecution` — execution flows

`RunnerExecution` is the shared execution glue: graceful shutdown (SIGTERM/SIGINT), `AutoSummaryWriter`
wiring when an `/output:rw` mount is declared, verbosity presets, and the run flows. All entry points
build the host internally (via the same bootstrap), resolve settings/mounts, and return a process exit
code.

| Entry point | Purpose |
|---|---|
| `RunOneShotAsync(opts, loggerCategory, configureServices?, externalCt?)` | Runs a single crew kickoff end-to-end. Exit codes: **0** success, **1** config error, **2** crew failure, **130** canceled. |
| `RunInteractiveLoopAsync(opts, loggerCategory, stopWords, kickoffPerInputAsync, onSessionStart, …)` | REPL loop; each input drives a kickoff via the caller-supplied delegate; a stop word ends the loop (exit 0). |
| `RunListToolsAsync(opts, loggerCategory, configureServices?)` | Builds the host with no crew and prints the sorted, de-duplicated runtime tool names to stdout (logs to stderr) — the runtime tool contract consumed by packaging/lint tooling. |

## Consuming from a web host

A long-running web host does not use `RunnerHost.Build` — that method owns an
entire generic host. Instead the host **replicates `ConfigureRunnerServices`' registration order** inside
its own `Program.cs` (the console wraps this as `AddOrkeonRuntime(configuration)`):

```csharp
// 1. Register the LLM provider FIRST (before AddOrkeonInfrastructure, whose TryAdd fallback would
//    otherwise win).
// 2. Core services:
services.AddOrkeonApplication();
services.AddOrkeonInfrastructure(configuration);
// 3. Tool suites (drive which tools crews can use):
services.AddOrkeonFileSystemTools();
services.AddOrkeonDataTools();
services.AddOrkeonWebTools();
// … the remaining AddOrkeon*Tools() suites …
// 4. VFS mounts from configuration (the web host provisions at least one mount):
services.AddOrkeonFileSystem(configuration);
// 5. Tool registry LAST, so it captures every registered IBaseTool:
services.AddSingleton<IToolRegistry, ServiceProviderToolRegistry>();
```

Because a web host typically runs each crew in its own DI scope (Orkeon's crew repositories are scoped),
`ServiceProviderToolRegistry` — a singleton over the registered `IBaseTool` set — is shared across runs,
while `CrewFactory` and the orchestrator resolve per scope.
