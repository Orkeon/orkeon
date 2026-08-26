> 🇫🇷 [Version française](../fr/reference/hosting.md)

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
| Depends on | the core `Orkeon.*` libraries (Domain, Application, Infrastructure, Analysis, Scripting) and eight of the nine `Orkeon.Tools.*` suites (`Orkeon.Tools.Rag` is deliberately absent — RAG stays opt-in), plus `CommandLineParser` and `Microsoft.Extensions.Hosting` |
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
    llmLogVirtualPath: null,             // when set, a VIRTUAL directory the caller has mounted:
                                         // LLM HTTP exchanges are captured there as .jsonl
    internalMounts: null,                // mounts registered MountVisibility.Internal — resolvable
                                         // by the VFS, never listed to an agent (ADR-008)
    configureLogging: null,              // optional ILoggingBuilder customization
    configureServices: null,             // optional hook to register runner-specific services
    configureBuilder: null);             // optional IHostBuilder hook — orkeon-host uses it for UseSystemd()/UseWindowsService()
```

A virtual path is always a name starting with `/` — never a disk path
([ADR-008](../adr/ADR-008-virtual-paths-are-the-only-currency.md)). `RunnerMounts` names the
roots the shipped runners take for themselves: `/crew` (the crew definition's directory),
`/script` (a scripting entry point's directory) and `/llm-logs`. A caller that enables exchange
logging mounts its log directory internally and passes `RunnerMounts.LlmLogVirtualRoot` here —
that is what the CLI does.

`LoadCrewAsync` likewise takes the crew target as a **virtual** path: it asks the VFS whether the
target is a directory rather than probing the disk, so a physical path handed to it is denied.

It composes `Host.CreateDefaultBuilder()` with:

- **App configuration** — appsettings resolution plus the CLI mount arguments folded into configuration.
- **Services** — `ConfigureRunnerServices` (below).

`RunnerHost` carries an `[SuppressVfsCompliance]` bootstrap exception because it resolves user-supplied
settings paths and provisions VFS mounts *before* the DI container (and thus `IFileSystemService`) exists.

### `ConfigureRunnerServices` behavior

The registration order is deliberate:

1. **Logging** — runner logging (Console + Information by default) and, when `llmLogVirtualPath` is set,
   the LLM exchange logging `DelegatingHandler`.
2. **LLM provider first** — `RegisterLlmProvider` reads the `Llm` config section and registers the
   provider (and its `IChatClient`) **before** `AddOrkeonApplication` / `AddOrkeonInfrastructure`. This
   ordering matters: Orkeon infrastructure registers its LLM/`IChatClient` fallbacks with `TryAdd`, so a
   host-supplied provider must be registered first to win.
3. **Core services** — `AddOrkeonApplication()` then `AddOrkeonInfrastructure()`.
4. **Strict tools** — `CrewFactoryOptions.StrictTools` defaults to `true` here (a crew referencing an
   unknown tool fails loudly with `unknown tool(s): …; available: …`); opt out with
   `"Orkeon:CrewFactory:StrictTools": false`. (The library default stays lenient.)
5. **Permission gate** — `AddOrkeonPermissionGate(configuration)` (config opt-in
   `Orkeon:Security:PermissionGate:Enabled`; a no-op otherwise).
6. **Core tool suites** — file system, data, web, code, abstractions, session tools; then the
   in-memory EventHub plus its agent tools and the EventHub ACL (`AddOrkeonEventHubAcl`,
   permissive default so a crew without a `links:` block behaves as before).
7. **VFS mounts** — `AddOrkeonFileSystem` when `Orkeon:FileSystem:Mounts` **or**
   `Orkeon:FileSystem:InternalMounts` exists **and holds at least one entry** (two empty arrays
   register nothing). Either list alone makes the VFS real: `--list-tools` has only the second.
8. **Late tool suites** — RaggableTree (semantic-graph tools, opt out with
   `"RaggableTree:Enabled": false`; pre-registers local embeddings when they are the selected
   provider), the WebSearch and `cache_search` tools, and the Brave search tool when
   `BRAVE_API_KEY` is present.
9. **Tool registry** — `ServiceProviderToolRegistry` is registered as the singleton `IToolRegistry`.
10. **Runner services** — the caller's `configureServices` hook runs last.

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
| `RunValidateAsync(opts, loggerCategory, configureServices?)` | Dry-run behind `--validate`: builds the host and loads the crew (strict tool resolution) without probing the LLM or running a kickoff. |
| `LoadCrewAsync(host, opts)` | Loads and maps the crew definition from the resolved target — the building block the flows above share. |

## Consuming from a long-running host

A long-running service *can* simply wrap `RunnerHost.Build` — that is exactly what the
`orkeon-host` daemon does (`Orkeon.Host/Program.cs`), passing `configureBuilder` for
`UseSystemd()`/`UseWindowsService()`. A host that already owns its `IHostBuilder` (an ASP.NET
app, for example) instead **replicates `ConfigureRunnerServices`' registration order** inside its
own `Program.cs` — there is no packaged shortcut for this; the REPL console inlines the same
sequence by hand:

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
