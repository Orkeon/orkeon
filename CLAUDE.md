# CLAUDE.md

Orkeon is a C# framework (.NET 10) for teams of AI agents that collaborate on tasks through LLMs: crews of agents, tasks and tools, run in six orchestration modes over 16 LLM providers with 119 built-in tool classes. Version `1.0.0-rc.5` is in preparation. `git tag` is the authority on what is tagged: never assert tag state in prose.
Start from `docs/INDEX.md` (`docs/fr/` is its full French mirror). Known gaps between what ships and what is wired are in `docs/reference/limitations.md`, tracked in `backstage/tasks/GAP-00-PLAN.md`: check there before assuming a registered surface is invoked.
Decisions live in `docs/adr/`; breaking changes and migration tables in `CHANGELOG.md`.

## Commands

The test runner is **Microsoft.Testing.Platform, not VSTest** (`global.json` `"test": { "runner": "Microsoft.Testing.Platform" }`). A `--filter` that matches zero tests in a module is an **error (exit 8)**, not an empty success: never exclude a project by name from a solution-wide run, and add `-- --ignore-exit-code 8` to a solution-wide filter most modules do not match. VSTest-only flags (`--collect`, `--logger`, `--blame`) are rejected.

```bash
dotnet build Orkeon.sln --no-restore            # CI builds -c Release -warnaserror: zero warnings
dotnet test Orkeon.sln --no-build --filter "Category!=Integration&Category!=Slow"   # what ci.yml runs
dotnet test tests/core/Orkeon.Domain.Tests/Orkeon.Domain.Tests.csproj --no-build --filter "FullyQualifiedName~MyTest"
dotnet test Orkeon.sln --no-build --filter "Category=Integration|Category=Slow" -- --ignore-exit-code 8   # Docker required
dotnet restore Orkeon.sln                       # only when a dependency changed (see below)
python3 scripts/third-party-notices.py          # after any package change; CI runs it with --check
dotnet pack Orkeon.sln --configuration Release --output ./artifacts
```

Tests needing Docker, a network service or minutes of runtime carry `[Trait("Category", "Integration")]` or `[Trait("Category", "Slow")]`; `integration.yml` runs them nightly.

SonarQube: `SONAR_TOKEN=<your-token> bash scripts/sonar-analyze.sh` starts the server, scans with coverage and writes the report under `sonarqube/`. The 9.9 LTS server takes `sonar.login`, not `sonar.token`; coverage goes through `dotnet-coverage` (`--collect:"XPlat Code Coverage"` does not work under MTP); there is no `sonar-project.properties`, every parameter is passed on the command line.

## Network constraints (metered mobile connection)

Bandwidth on this machine is capped. Never re-download anything that is already cached.

- `dotnet build`: always pass `--no-restore`, unless a `.csproj`, `Directory.Packages.props` or lock file changed during this task.
- `dotnet test`: always pass `--no-build` once a build has succeeded in this task.
- `dotnet restore`: run only when a dependency actually changed. Under Linux every restore reads and fills `/workspace/packages` (`RestorePackagesPath` in `Directory.Build.props`; a worktree links its `packages` there), never `/home/node/.nuget/packages`: a package found only in the latter is downloaded again. Never override that path, per-project or otherwise. An offline restore passes `-p:NuGetAudit=false`: the audit reads nuget.org's advisories again (650 KiB) whenever their cache has expired — CI keeps the audit.
- `git clone`: always `--depth 1 --filter=blob:none`. Reuse an existing checkout rather than cloning again.
- `npm ci`: only when `package-lock.json` changed. Otherwise use `npm install --prefer-offline`.
- `ollama pull` is forbidden. Run `ollama list` first — models are pre-provisioned.
- Never run `dotnet nuget locals --clear`, `docker system prune -a`, or `docker build --no-cache`, and never delete a cache directory.
- Clean up git worktrees and temporary directories at the end of every task.

## Rules

### Branching: never start work on `main`

Before the first change of a new piece of work, read `git branch --show-current`. On `main`, create a branch before any file is touched: `feature/<slug>` (a new capability or behaviour), `fix/<slug>` (a bug, regression or CI repair) or `chore/<slug>` (no behaviour change: docs, refactoring, dependencies, tooling, submodule bumps). On any other branch, continue on it. The slug is short, lowercase and hyphenated. Nothing is ever committed directly to `main`.

### Virtual File System: mandatory for all I/O

Framework code never calls `System.IO.File.*`, `System.IO.Directory.*`, or `new FileStream/FileInfo/DirectoryInfo/FileSystemWatcher`; the `Orkeon.Compliance.Vfs` analyzer enforces it. Inject `IFileSystemService` and work in virtual paths (`/workspace/...`, `/output/...`, `/tmp/...`): it checks mounts and `FileAccessRights` and redacts physical paths from errors. Exceptions:
- the VFS itself (`src/core/Orkeon.Domain/FileSystem/`, `src/core/Orkeon.Infrastructure/FileSystem/`);
- bootstrap code that provisions mounts before DI (e.g. `SandboxSession`), marked `// EXCEPTION-BOOTSTRAP`;
- system probing (the `docker` binary on PATH, the SDK path), marked `// OUT-OF-SCOPE`;
- tests, through `DiskBackedFileSystemService` fixtures.

Spec: `docs/architecture/vfs-compliance.md`.

### Executable bit

Every `.sh` script, and every script with a shebang meant to be run directly (`.py`, `.mjs`), is tracked as mode `100755`. An NTFS mount hides the problem (`chmod +x` does not persist, everything looks `rwxrwxrwx`); a Linux or macOS clone restores `100644` and fails with `permission denied`. Set the bit in the index with `git update-index --chmod=+x <path>`, then check `git ls-files -s -- '*.sh'` (every line starts with `100755`). The same rule holds in the `backstage` and `experiments` submodules.

### Test doubles and assertions

Use native xUnit assertions only: no mocking framework (Moq, NSubstitute, FakeItEasy) and no fluent assertion library (FluentAssertions, Shouldly, Verify). Doubles are hand-written classes named `Mock*`, `Fake*` or `Stub*` after the interface they implement. They live in the consuming test project's `Doubles/` (or `Fakes/`) folder and expose plain fields to configure or inspect. The reference is `tests/core/Orkeon.Infrastructure.Tests/Doubles/MockTaskRepository.cs`. Tests must hold when several passes run at once (`LoopbackPorts`, no unit test bounding a duration): `CONTRIBUTING.md`, "Testing".

### DTO conventions (`Orkeon.Application`)

- `sealed record` with `{ get; init; }`, the `required` keyword (never `[Required]`), `ImmutableList<T>` / `ImmutableDictionary<K,V>` (never `List<T>` / `Dictionary<K,V>`).
- Suffixes: `*Dto` for read/transfer, `*Request` for the input of a port, `*Command` for a CQRS command, `*Response` — a port's answer record (`AgentChannelResponse`).
- `[JsonPropertyName("snake_case")]` on every API-exposed DTO.
- Business rules go in `ICommandValidator<T>`, never Data Annotations.
- One DTO per concept, no duplicate definitions; enums in dedicated `*Enums.cs` files in the entity's DTOs folder.

### Other rules

- **Namespace collision**: `Orkeon.Domain.Task` collides with `System.Threading.Tasks.Task`. In a file that imports both, write every `Task` as `System.Threading.Tasks.Task`.
- **Two scripting projects** (ADR-007): `Orkeon.Cli.Commands.Scripting` (`src/cli/`) is a library of TypeScript-scripted interactive commands; `Orkeon.Scripting.Cli` (`src/scripting/`) is the `orkeon` dotnet tool (`AssemblyName=orkeon`). The project whose last segment is `Cli` is the executable.
- **Version**: `src/Directory.Build.props` (`VersionPrefix` + `VersionSuffix`) is the single source of truth; the publish workflow refuses a `v*` tag that does not match it.
- **LLM options**: an option declared on a provider that cannot honour it produces a structured warning, never a silent drop (`docs/architecture/llm-providers.md`).
- **Tool names**: `IToolRegistry.RegisterToolAsync` refuses a name another tool already holds; new tools follow `docs/tools/new-tool-pattern.md`.
- **Doc claims**: `scripts/check-doc-claims.py` checks the counts and the version stated here and in the READMEs against the code, and `scripts/check-docs-parity.sh` checks the `docs/fr/` mirror. Run both after changing either. A changed docs page is mirrored in `docs/fr/` in the same commit.

## Architecture map

The repository holds **49 src projects** and **37 test projects** (`Orkeon.sln`; examples in `examples/Orkeon.Examples.sln`), all `net10.0` (`net10.0-windows` for `Orkeon.Studio.Wpf`).
- `src/core/`: Clean Architecture. `Orkeon.Domain` holds entities, value objects and most interfaces (`ILlmProvider`, `IMemoryProvider`, `IAgentFactory`) and references only `Orkeon.Constants.Llm` (ADR-009). `Orkeon.Application` holds use cases, ports and DTOs. `Orkeon.Infrastructure` holds the adapters: 16 LLM providers in `LLMs/`, memory stores, and orchestration strategies in `Crew/Strategies/` + `Consensus/`. Allowed cross-layer exceptions are in ADR-002, 003, 005, 006 and 012.
- `src/tools/`: the tool families (`ToolBase<TReq,TRes>`). `src/rag/`: the RAG subsystem (ADR-006). `src/analysis/`: RaggableTree, the semantic code graph.
- `src/scripting/`: the `.ork.ts` DSL (Jint + esbuild) and the `orkeon` CLI. `src/cli/`: REPL command contracts and the Terminal.Gui console.
- `src/hosting/`: `RunnerHost`, the `orkeon-host` daemon, Aspire. `src/apps/`: the REPL and Orkeon Studio (TUI + WPF). `src/plugins/`, `src/interop/` (Microsoft Agent Framework, ADR-010), `src/packaging/` (NuGet).
- `src/constants/`: zero-dependency shared-constant satellites. `src/analyzers/`: the VFS analyzer. `src/generators/`: source generators.
- `tests/` mirrors `src/`; shared fixtures are in `tests/shared/Orkeon.Tests.Shared`.
- `tools/`: npm bootstraps (esbuild, type checker), the vision-probe image generator. `scripts/`: Sonar, doc gates, example linters, third-party notices.
- `backstage/` (private submodule): project management. Tasks are `backstage/tasks/<PREFIX>-<NN>-<slug>.md`, never a root `todo/`. `experiments/` (private submodule): experiments.
