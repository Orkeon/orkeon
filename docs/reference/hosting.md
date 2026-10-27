> 🇫🇷 [Version française](../fr/reference/hosting.md)

# Orkeon.Hosting — runner & host bootstrap

`Orkeon.Hosting` is the shared bootstrap layer that turns the Orkeon libraries into a runnable host.
It owns the wiring order that a working runtime needs — LLM provider, core services, the standard tool
suites, the virtual file system, and the DI-backed tool registry — plus the end-to-end execution flows
(one-shot kickoff, interactive loop, tool listing) used by every Orkeon runner and CLI.

Inside this repository it is consumed by `Orkeon.Scripting.Cli` (the `orkeon` tool) and by
`Orkeon.Host` (the `orkeon-host` daemon). It is **not** distributed as a NuGet package — see
[Distribution](#distribution) below. This page documents its public bootstrap surface, the
[telemetry](#telemetry) a host built on it exports, and the .NET Aspire
integration that launches its executables.

## Distribution

**`Orkeon.Hosting` ships inside the `Orkeon.Scripting` package** (#229). Its own csproj stays
`IsPackable=false`, so there is no `Orkeon.Hosting` id on any feed: the
[publication matrix](publication-matrix.md#the-nugetorg-lineup) lists the `Orkeon.Scripting`
opt-in, whose wrapper (`src/packaging/Orkeon.Scripting/Orkeon.Scripting.Package.csproj`) embeds
`Orkeon.Hosting.dll` next to `Orkeon.Scripting.dll` and `Orkeon.Constants.Cli.dll`.

```bash
dotnet add package Orkeon.Scripting --prerelease
```

It is not in the `Orkeon` umbrella: the runner host composes every built-in tool family, so the
package depends on `Orkeon.Tools` and `Orkeon.Tools.Embeddings.Local` — weight a consumer of the
umbrella alone never restores.

| | |
|---|---|
| Assembly | `Orkeon.Hosting.dll` (`src/hosting/Orkeon.Hosting`) |
| Package | `Orkeon.Scripting` (embedded; the project itself is `IsPackable=false`) |
| Package dependencies | `Orkeon`, `Orkeon.Tools`, `Orkeon.Tools.Embeddings.Local`, `Jint`, `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Options`, `CommandLineParser` |
| Also ships through | the `orkeon` dotnet tool (`Orkeon.Scripting.Cli`) and the `release.yml` installer archives / `.deb` / MSIs, where `Orkeon.Hosting.dll` sits next to `orkeon` and `orkeon-host` |

## Running a crew in-process

A .NET application runs a `crew.ork.ts` — or a `crew.yaml`, or a multi-file crew directory —
without starting the `orkeon` process: `ICrewScriptRunner` is the in-process form of `orkeon run`.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Hosting;

var services = new ServiceCollection().AddOrkeonScripting(options =>
{
    options.SettingsPath = "appsettings.json";   // the Llm section, mounts, tools - as for orkeon run
    options.Mounts.Add("./out:/output:rw");      // --mount
    options.Diagnostics = Console.Error;         // the operator lines orkeon run writes on stderr
});
using var provider = services.BuildServiceProvider();

var output = await provider.GetRequiredService<ICrewScriptRunner>().RunAsync("crew.ork.ts");
Console.WriteLine(output.Succeeded ? output.FinalOutput : output.Error);
```

`new CrewScriptRunner(options)` does the same without a container.

- **What a run is.** Each `RunAsync` builds a runner host of its own through the shared bootstrap
  (`RunnerExecution`, the code `orkeon run` runs): the crew target is resolved and mounted, the
  settings file is found the same way, the mount guards run, then `RunnerHost.Build` composes the
  LLM provider, the built-in tools, the VFS, the permission gate (`Orkeon:Security`) and the
  configured MCP servers. The crew is loaded and kicked off in one scope; the host is disposed
  when the run ends. Nothing but the runner is added to the application's own container.
- **What comes back.** The orchestrator's `CrewOutput`: a crew that ran and failed is an output
  with `Succeeded == false` and its `Error`. A run that cannot start — a missing target, a setting
  or a mount the host refuses, a crew that does not load — throws `CrewScriptRunException` with
  the line `orkeon run` would have printed.
- **The script shape.** A `.ork.ts` must hand its crew off (`globalThis.crew = crew`), the
  declarative shape `orkeon run` sends through the same pipeline. A procedural script (one that
  drives agents itself, with no handoff) is not a crew definition: run it with `orkeon run`.
- **Options.** `SettingsPath`, `Mounts`, `MountIds`, `AllowExternalMounts` and `LlmProfile` are
  the `orkeon run` options of the same names. `ConfigureServices` is applied after Orkeon's
  registrations, so it replaces one (an `ILlmProviderFactory`, a human-input provider).
  `ConfigureLogging` sets the run's logging; without it the run logs nothing, and the operator
  lines go to `Diagnostics` only.
- **esbuild.** A `.ork.ts` is bundled by esbuild, which the package does not ship — the stance of
  the `orkeon` tool ([Scripting](../architecture/scripting.md)). Put it on `PATH`
  (`npm install -g esbuild`), or point `ORKEON_ESBUILD_PATH` or
  `Orkeon:Scripting:Toolchain:EsbuildPath` at a binary. A YAML crew never needs it.
- **Not a daemon.** A crew that must keep running between requests — a chat channel, A2A, several
  hosted crews — belongs in `orkeon-host`
  ([service host](../architecture/service-host.md#8-in-process-or-under-orkeon-host)).

## `RunnerHost.Build`

`RunnerHost` is a static host builder. `Build` returns a fully configured `IHost`:

```csharp
IHost host = RunnerHost.Build(
    settingsPath: "appsettings.json",   // resolved appsettings path (nullable)
    mounts: new RunnerMountPlan          // the whole VFS surface, in one object
    {
        CliMounts = ["/data:/data:ro"],   // CLI --mount args ("physical:virtual:rights")
        InternalMounts = [],              // mounts registered MountVisibility.Internal — resolvable
                                          // by the VFS, never listed to an agent (ADR-008)
        AllowExternalMounts = false,      // whitelist mount base paths outside the workspace root
        SelectedMountIds = [],            // --mount-id values, parsed: the settings entries kept when
                                          // several declare one virtual root (VFS-90)
        CrewMountReferences = [],         // the crew's mounts: block — selects and validates, never restricts
        LlmLogVirtualPath = null,         // when set, a VIRTUAL directory the caller has mounted:
                                          // LLM HTTP exchanges are captured there as .jsonl
    },
    configureLogging: null,              // optional ILoggingBuilder customization
    configureServices: null,             // optional hook to register runner-specific services
    configureBuilder: null,              // optional IHostBuilder hook — orkeon-host uses it for UseSystemd()/UseWindowsService()
    llmProfile: null);                   // optional host LLM profile id (--llm-profile): Llm:Profiles:<id> becomes the run's Llm section
```

An `llmProfile` the configuration does not define is refused with a `RunnerSettingsException` whose message lists the profiles it does define; null or `default` keeps the `Llm` section as it is.

A virtual path is always a name starting with `/` — never a disk path
([ADR-008](../adr/ADR-008-virtual-paths-are-the-only-currency.md)).
`RunnerVirtualRoots` — in the dependency-free `Orkeon.Constants.FileSystem` package
([ADR-009](../adr/ADR-009-shared-constants-satellites.md)) so the engine and the tooling read one
declaration — names the roots the shipped runners take for themselves: `/crew` (the crew
definition's directory), `/script` (a scripting entry point's directory), `/llm-logs`,
`/sandbox` (where the code sandboxes stage what they run) and `/credentials` (the OAuth tokens
of the e-mail accounts, refused to a user mount by every command). `RunnerVirtualRoots.All` is the set a
caller refuses a user `--mount` against; asking for the set rather than comparing the roots one by
one is deliberate, because the omission of `/sandbox` survived a pairwise check for as long as it
was green. A caller that enables exchange logging mounts its log directory internally and passes
`RunnerVirtualRoots.LlmLogs` here — that is what the CLI does.

`LoadCrewAsync` likewise takes the crew target as a **virtual** path: it asks the VFS whether the
target is a directory rather than probing the disk, so a physical path handed to it is denied.

It composes `Host.CreateDefaultBuilder()` with:

- **App configuration** — the default builder's sources removed, those of every Orkeon host
  (`RunnerSettings.ComposeSources`, which `RunnerSettings.ReadConfiguration` — `orkeon doctor`, the
  probe of `orkeon init` — and the REPL compose too): the environment variables without a prefix,
  the `settingsPath` file when it exists, then the `ORKEON_`-prefixed environment
  (`ORKEON_Llm__Model` overrides `Llm:Model`). Neither the `appsettings.json` and
  `appsettings.{Environment}.json` of the content root — the current directory — nor user secrets
  (GAP-36). Then an in-memory layer carrying the mount
  decisions: the `--mount` values placed by virtual root, the internal mounts, the
  `/credentials` mount of the e-mail OAuth tokens, and the `PathSecurity:AdditionalAllowedDirectories`
  entries that let the path validator reach those mounts' folders (a `--mount` outside the working
  directory only with `AllowExternalMounts`).
- **Services** — `ConfigureRunnerServices` (below).
- **`configureBuilder`** — invoked last, on the `IHostBuilder` itself.

Once built, the host first judges its settings (GAP-40), whether the run uses them or not: every
section a registration declared — its options created, so the binder converts them and their rules
run, the names they hold among them —, the section names under `Orkeon:` and its groups, the keys of
every declared section, and `Orkeon:Rag:LlmProfile` against the profiles it offers
([when a setting is refused](./configuration.md#when-a-setting-is-refused)). Only options and the
named factories are created: no store, provider, model or connection. The first refusal is a
`RunnerSettingsException` naming its key, the host disposed: nothing is logged, warned or started.

Then the host logs the mount decisions it took, warns (on the log and on stderr) when
an OAuth e-mail account has no token store or when there is no `Llm` section — the runtime
then falls back to the echo provider —, says where the key of the default and of each profile
offered to crews comes from — the profiles its allow-list hides (`LlmProfileAccessOptions`, which
`orkeon-host` binds) on one line of their own, never warned about — and resolves the OpenTelemetry tracer and meter
providers, because the runners never *start* the host and the providers would otherwise never
exist (see [Telemetry](#telemetry)).

`RunnerHost` carries an `[SuppressVfsCompliance]` bootstrap exception because it resolves user-supplied
settings paths and provisions VFS mounts *before* the DI container (and thus `IFileSystemService`) exists.

### `ConfigureRunnerServices` behavior

The registration order is deliberate:

1. **Logging** — runner logging (a single-line console at **Warning** level by default; `--verbose 1`/`2` or a `configureLogging` callback raises it) and, when `RunnerMountPlan.LlmLogVirtualPath` is set,
   the LLM exchange logging `DelegatingHandler`.
2. **LLM provider first** — `RegisterLlmProvider` reads the `Llm` config section and registers the
   provider (and its `IChatClient`, on that section's configuration) **before** `AddOrkeonApplication` /
   `AddOrkeonInfrastructure` — or the echo provider when the section is missing. The infrastructure
   registers no model of its own: a host that registers none fails at its first LLM resolution, naming
   the missing service (GAP-29).
3. **Core services** — `AddOrkeonApplication()` then `AddOrkeonInfrastructure()` (the
   parameterless overload), then `AddOrkeonTelemetry(configuration)` for the `Telemetry`
   section.
4. **Strict tools** — `CrewFactoryOptions.StrictTools` defaults to `true` here (a crew referencing an
   unknown tool fails loudly with `unknown tool(s): …; available: …`); opt out with
   `"Orkeon:CrewFactory:StrictTools": false`. (The library default stays lenient.)
5. **Permission gate** — `AddOrkeonPermissionGate(configuration)` (config opt-in
   `Orkeon:Security:PermissionGate:Enabled`; a no-op otherwise).
6. **Core tool suites** — file system, data, web, code, abstractions, session tools; then the
   in-memory EventHub plus its agent tools and the EventHub ACL (`AddOrkeonEventHubAcl`,
   permissive default so a crew without a `links:` block behaves as before); then the e-mail
   tools (`AddOrkeonEmailTools(configuration)`, inert until an account is declared) and, when an
   OAuth e-mail account is declared, their token store over the internal `/credentials` root —
   mounted by the configuration step, reached through `PrivilegedFileSystemAccess`.
7. **VFS mounts** — `AddOrkeonFileSystem` when `Orkeon:FileSystem:Mounts` **or**
   `Orkeon:FileSystem:InternalMounts` exists **and holds at least one entry** (two empty arrays
   register nothing). Either list alone makes the VFS real: `--list-tools` has only the second.
   Several entries of `Mounts` may declare one root when each carries an id (VFS-90):
   `MountSelection.Resolve` decides, while the configuration is composed, which one this run
   keeps — a `--mount` on the root, else `SelectedMountIds`, else `CrewMountReferences` — and
   writes the others to `null` at their own index; a selection nothing resolves throws with
   the very text the runners' guards print, so a host built without them refuses the same way.
8. **Late tool suites** — RaggableTree (semantic-graph tools, opt out with
   `"RaggableTree:Enabled": false`; pre-registers local embeddings when they are the selected
   provider), the WebSearch and `cache_search` tools, and the Brave search tool when
   `BRAVE_API_KEY` is present (configuration key or environment variable).
9. **MCP** — `AddOrkeonMcp(configuration)` when the `MCP` section declares at least one
   server under `MCP:Servers` and `MCP:Enabled` is not `false`. Registering is not
   connecting: the servers are connected by the flows below, before the crew loads
   (see [MCP](../architecture/mcp.md#activation)). A section that still carries the removed
   `MCP:EnableServer` fails the build of the host, servers declared or not (GAP-24).
10. **Tool registry** — nothing of its own: `AddOrkeonInfrastructure()` already registered the default
    `ToolRegistry`, which reads every `IBaseTool` the steps above registered when it is first resolved.
11. **Runner services** — the caller's `configureServices` hook runs last.

`semantic_search` is not in this list: it is registered by `AddSemanticSearchTool()`, which
`orkeon run` calls through its `configureServices` hook and `orkeon-host` does not.

## The tool registry (`ToolRegistry`)

The default `IToolRegistry` (`Orkeon.Infrastructure.Tools`, registered by `AddOrkeonInfrastructure()`,
shipped in the `Orkeon` package) resolves YAML/TS tool names to `IBaseTool` instances **from DI**. Its
constructor takes `IEnumerable<IBaseTool>` — every tool the tool suites registered — and indexes them by
name (case-insensitive); two tools registered under one name make the constructor throw, naming both
types. `CrewFactory` consumes it to build agents with their declared tools, which is why every tool suite
registers under `IBaseTool`: a tool that is not registered cannot be resolved (and, with `StrictTools`,
fails crew loading rather than silently dropping). `RegisterToolAsync` adds a tool at run time — the MCP
client does — and **refuses** (returns `false`) a name another tool already holds. Reads and run-time
registrations are safe to interleave: `orkeon-host` runs several crews while its MCP servers connect.
The registry indexes names only — there is no lookup by tag or capability (GAP-11).

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
| `RunValidateAsync(opts, loggerCategory, configureServices?, externalCt?)` | Dry-run behind `--validate`: builds the host and loads the crew (strict tool resolution) without probing the LLM or running a kickoff. |
| `LoadCrewAsync(host, factory, configPath, logger, ct, targetIsDirectory?)` | Loads and maps the crew definition from its **virtual** path (YAML file, crew directory, `.ork.ts`/`.ork.js` script) — the building block the flows above share, and what `orkeon-host` calls per run. It does not connect MCP servers. |

`RunOneShotAsync`, `RunValidateAsync` and `RunListToolsAsync` connect the configured MCP servers
before they load or list anything, so the three see the same tool surface. The guards the CLI runs
before building a host are public too — `EnsureReservedRootsAreFree` (always adds `/credentials`),
`EnsureVirtualRootsAreUnique`, `EnsureMountSelectionIsResolvable`, `EnsureMountSourcesExist` — as are
`RegisterGracefulShutdown`, `DetectOutputMountPath`, `ConfigureVerboseLogging` (`1`: Information for the
Orkeon modules; `2`: Debug) and `IsScriptedCrewDefinition` (`.ork.ts` / `.ork.js`).

## Consuming from a long-running host

A long-running service *can* simply wrap `RunnerHost.Build` — that is exactly what the
`orkeon-host` daemon does (`Orkeon.Host/Program.cs`), passing `configureBuilder` for
`UseSystemd()`/`UseWindowsService()`. The daemon stays out of the mount selection (VFS-90,
D-11): its crews mount under per-crew roots (`/crews*`), so no root is ever declared twice
there, an operator `--mount` carrying an id prefix parses like any other, and no crew
`mounts:` block is read. A host that already owns its `IHostBuilder` (an ASP.NET
app, for example) instead **replicates `ConfigureRunnerServices`' registration order** inside its
own `Program.cs` — there is no packaged shortcut for this; the REPL console inlines the same
sequence by hand:

```csharp
// 1. Register the LLM provider (AddOrkeonLlmProvider): AddOrkeonInfrastructure registers no model
//    of its own, and a container without one fails at its first LLM resolution.
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
// 5. Nothing for the tool registry: AddOrkeonInfrastructure registered the default
//    ToolRegistry, which reads every IBaseTool registered above when first resolved.
```

Because a web host typically runs each crew in its own DI scope (Orkeon's crew repositories are scoped),
the `ToolRegistry` — a singleton over the registered `IBaseTool` set — is shared across runs,
while `CrewFactory` and the orchestrator resolve per scope.

## Telemetry

`AddOrkeonTelemetry(configuration)` reads the `Telemetry` section:

| Key | Default | Effect |
|---|---|---|
| `Enabled` | `true` | `false` registers no provider and no exporter: the meter `Orkeon` measures for no one. |
| `OtlpEndpoint` | — | An explicit OTLP endpoint; it wins over the environment. An `http://` or `https://` address: anything else is refused, naming the key. |
| `MaxMemoryMB` | `2048` | Threshold of the `system_resources` health check. |

**Removed keys (GAP-35).** `ExportToConsole` attached OpenTelemetry's console exporters, which write
on stdout — where `--events jsonl`, the `--list-tools` manifest and `orkeon mcp serve` speak to a
program —, and `PrometheusEndpoint` was bound and read by nothing. Both are gone, with the
`OpenTelemetry.Exporter.Console` package, and a section that still writes one — whatever its value,
`Enabled` `false` included — is refused with an `InvalidOperationException` that names the key and
what replaces it: an OTLP collector (`Telemetry:OtlpEndpoint`, `OTEL_EXPORTER_OTLP_ENDPOINT`, the
.NET Aspire dashboard). In the runners it is a refused setting: exit 1, or 78 for `orkeon-host`. A C#
host that wants the console adds the exporter to its own `AddOpenTelemetry()`.

**Where the data goes.** An explicit `Telemetry:OtlpEndpoint` is used as the exporters' endpoint.
Without one, a non-empty `OTEL_EXPORTER_OTLP_ENDPOINT` attaches the OTLP exporters with no
Orkeon-specific setting — the exporter then reads the endpoint, the protocol and the headers from the
standard `OTEL_EXPORTER_OTLP_*` variables itself, which is how a process launched by .NET Aspire
reports with nothing configured. It reads them in the host's configuration, through its layer of
environment variables without a prefix: the reason every Orkeon host keeps that layer, under its
settings file ([where settings are read from](./configuration.md#where-settings-are-read-from)).
Without either, nothing is exported.

**What is exported.** Traces from the `Orkeon.Crew`, `Orkeon.Agent`, `Orkeon.Task`, `Orkeon.Llm`,
`Orkeon.Tool`, `Orkeon.Memory`, `Orkeon.EventHub` and `Orkeon.Scripting` activity sources plus the HttpClient
instrumentation; metrics from the `Orkeon` meter plus the runtime and HttpClient instrumentation; and,
whenever OTLP export is on, the structured logs (formatted message and scopes included) to the same
endpoint. The resource names the service `Orkeon`. The span and metric names follow the OpenTelemetry
GenAI conventions — see [Opt-in subsystems](opt-in-subsystems.md#traces-and-metrics-follow-the-opentelemetry-genai-conventions).

The section also registers three health checks — `llm_provider`, `memory_provider` (which reads a key
that does not exist: every provider serves it, Pinecone and ChromaDB included),
`system_resources` — which no shipped runner exposes: `orkeon` serves no HTTP, and the only HTTP surface of `orkeon-host` is its opt-in A2A server (`Orkeon:Host:A2A`), which serves no health endpoint.

## .NET Aspire — `Orkeon.Hosting.Aspire`

`Orkeon.Hosting.Aspire` (package `Orkeon.Hosting.Aspire`, see the
[publication matrix](publication-matrix.md)) describes Orkeon processes as resources of an Aspire
AppHost, so the Aspire dashboard shows their spans, metrics and logs
([ADR-011](../adr/ADR-011-aspire-dashboard-observability.md)). It runs no crew itself: it launches the
shipped executables, found on the `PATH` or named by `command`.

**Install.** In an Aspire AppHost project (`Aspire.AppHost.Sdk`):

```bash
dotnet add package Orkeon.Hosting.Aspire --prerelease
dotnet tool install -g Orkeon.Scripting.Cli --prerelease   # the `orkeon` executable AddOrkeonCrewRun launches
```

`orkeon-host` comes from the full installer archives or the per-machine service MSI — which
is not code-signed: Windows asks before it runs it, see the
[service host](../architecture/service-host.md#windows) —; pass its path as `command` when it is not on the
`PATH`. Then `dotnet run` the AppHost and open the dashboard.

**What flows.** Into each process: its arguments and the `ORKEON_` variables set with
`WithOrkeonSetting`/`WithOrkeonModel`, plus the `OTEL_EXPORTER_OTLP_*` and `OTEL_SERVICE_NAME`
variables Aspire injects. Out of it: the traces, metrics and structured logs described under
[Telemetry](#telemetry), and the console output. Nothing else — no endpoint, no health probe, no
state: the dashboard observes, it does not drive a run.

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// the orkeon-host daemon, its crews registered in its settings file
builder.AddOrkeonHost("orkeon-host", settingsPath: "host.appsettings.json")
       .WithOrkeonSetting("Orkeon:Host:RunTimeout", "00:10:00");

// one `orkeon run`, files under ./out, on a local model
builder.AddOrkeonCrewRun("quickstart", crewPath: "../../quickstart/crew.yaml")
       .WithOrkeonModel(new Uri("http://localhost:11434"), "qwen2.5:1.5b");

builder.Build().Run();
```

| Member | What it does |
|---|---|
| `AddOrkeonHost(name, settingsPath?, workingDirectory?, command = "orkeon-host")` | An `OrkeonHostResource` (executable) started as `orkeon-host [--settings <settingsPath>] --allow-external-mounts`, in `workingDirectory` (default: the AppHost directory), with the OTLP exporter wired. No endpoint: the daemon serves no HTTP. |
| `AddOrkeonCrewRun(name, crewPath, outputDirectory?, settingsPath?, command = "orkeon")` | An `OrkeonCrewRunResource` started as `orkeon run <crewPath> --mount <output>:/output:rw --allow-external-mounts [--settings <settingsPath>]` in the AppHost directory, with the OTLP exporter wired. The output directory (default `<AppHost>/out`) is created when the resource is declared. |
| `WithOrkeonSetting(key, value)` | Sets any configuration key through the `ORKEON_` environment the runners read: `Llm:Model` becomes `ORKEON_Llm__Model`. The key is the full configuration path — the daemon's own keys live under `Orkeon:Host`, so `Orkeon:Host:RunTimeout`, not `Host:RunTimeout`. |
| `WithOrkeonModel(baseUrl, model, apiKey?)` | Shorthand for `Llm:BaseUrl` (trailing `/` trimmed), `Llm:Model` and, when given, `Llm:ApiKey`. |

A runnable AppHost lives in
[`examples/aspire/AppHost`](https://github.com/orkeon/orkeon/blob/main/examples/aspire/AppHost/Program.cs).

## Microsoft Agent Framework

To run a crew from a Microsoft Agent Framework application — or a MAF agent inside a crew — see
[Agent Framework interop](agent-framework-interop.md): its example builds its host with
`RunnerHost.Build` and adds the bridge through `configureServices`.
