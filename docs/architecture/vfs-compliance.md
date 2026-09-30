> 🇫🇷 [Version française](../fr/architecture/vfs-compliance.md)

# VFS Compliance

Orkeon enforces a **Virtual File System (VFS) only** principle on framework code: all filesystem access must go through `IFileSystemService` so that mount boundaries, access rights, and path validation are respected uniformly. Direct use of `System.IO.File`, `System.IO.Directory`, `FileStream`, `FileInfo`, `DirectoryInfo`, and `FileSystemWatcher` is forbidden in framework assemblies.

This document describes:
- the `Orkeon.Compliance.Vfs` Roslyn analyzer that enforces the rule at build time
- its seven diagnostic codes and their severities
- the exempt scopes and how to opt out for documented exceptions
- the exit criteria from the VFS migration programme

> **VFS-70 (2026-06-17):** all tool/service `IFileSystemService` dependencies are now
> **required** (non-nullable) — the `EXCEPTION-BACKCOMPAT` `if (_fs is null) { …System.IO… }`
> fallbacks have been eliminated, the FS-related `[Obsolete]` shims removed, and the knowledge
> ingestion path fully routed through the VFS (the `KnowledgeService` audited then was replaced
> by the `Orkeon.Rag` loaders in RAG-02 — same VFS rule, `FileDocumentLoaderBase`). SQLite persistence (`SqliteMemoryProvider`, `SqliteStateStore`) now
> resolves its `Data Source` file through `ResolveAndValidate` (decision 2F-A); the RaggableTree
> batch discovery runs on `IFileSystemService.EnumerateFilesAsync` (decision 2E-A). Two new rules
> (`ORKVFS006`, `ORKVFS007`) close the previously-undetected blind spots.

## The analyzer

Project: `src/analyzers/Orkeon.Compliance.Vfs/`
Target: `netstandard2.0` Roslyn analyzer, wired into `src/Directory.Build.props` with `OutputItemType=Analyzer`.

The analyzer is applied to every project under `src/` except itself (a `ProjectReference` with `ReferenceOutputAssembly=false`), so violations break the build immediately. `tests/analyzers/Orkeon.Compliance.Vfs.Tests` exercises each rule against compiled snippets.

Calls are judged on their **resolved symbol**, not their spelling: `using static System.IO.File; ReadAllText(p)` trips `ORKVFS001` like `File.ReadAllText(p)` does, and a target-typed `FileStream fs = new(path, …)` trips `ORKVFS003` like `new FileStream(path, …)`.

The same analyzer ships on NuGet as `Orkeon.Compliance.Vfs` (a development dependency with no Orkeon runtime dependency — see the [publication matrix](../reference/publication-matrix.md)), for projects that want the same guard rail. Outside this repository its severities are tuned the standard Roslyn way, in `.editorconfig` (`dotnet_diagnostic.ORKVFS007.severity = none` for a project that does not use `IFileSystemService`).

## Diagnostic codes

| Code | Severity | Detects | Suggested fix |
|---|---|---|---|
| <a id="ork-vfs-001"></a>`ORKVFS001` | Error | Direct call to `System.IO.File.*` | Inject `IFileSystemService` and use `TryReadAllBytesAsync` / `WriteAllTextAsync` / `ExistsAsync` / … |
| <a id="ork-vfs-002"></a>`ORKVFS002` | Error | Direct call to `System.IO.Directory.*` | Use `CreateDirectoryAsync`, `DeleteAsync`, `EnumerateFilesAsync` |
| <a id="ork-vfs-003"></a>`ORKVFS003` | Error | `new FileStream(string …)`, `new FileInfo(string)`, `new DirectoryInfo(string)` | Use `OpenReadStreamAsync`, `OpenWriteStreamAsync`, `TryGetEntryAsync` |
| <a id="ork-vfs-004"></a>`ORKVFS004` | Error | `Path.GetFullPath(…)` (may bypass mount validation) | For user-supplied input, call `ResolveAndValidate` |
| <a id="ork-vfs-005"></a>`ORKVFS005` | Error | `new FileSystemWatcher(…)` | Inject `IVirtualFileSystemWatcher` and consume `WatchAsync` (virtual-path change events) |
| <a id="ork-vfs-006"></a>`ORKVFS006` | Error | `new StreamReader(string)` / `new StreamWriter(string)` (path overloads) | Open via `OpenReadStreamAsync` / `OpenWriteStreamAsync` and wrap the returned `Stream` |
| <a id="ork-vfs-007"></a>`ORKVFS007` | Error | A nullable `IFileSystemService?` field or parameter | Inject `IFileSystemService` as a required, non-nullable dependency |

All diagnostics are defined in `src/analyzers/Orkeon.Compliance.Vfs/DiagnosticDescriptors.cs` and emit a stable help link ending in `#ork-vfs-00N` — the anchors in the Code column above are those targets.

## Exempt scopes (path-based)

The analyzer skips file paths that match its allowlist (`SystemIoUsageAnalyzer.IsExemptByPath`). It is intentionally narrow — folder-wide blank checks were replaced by an explicit per-file allowlist so a *new* file under the same folder is **not** silently exempted.

Folder exemptions (`s_exemptFolderSegments`):
- `/core/Orkeon.Domain/FileSystem/` — VFS public contracts (the abstraction itself)
- `/core/Orkeon.Infrastructure/FileSystem/` — VFS implementation (disk/fake/watcher)
- `/tests/` — fixtures rely on `DiskBackedFileSystemService` and real disk
- `/examples/` — out of framework compliance scope

Per-file exemptions (`s_exemptFileSuffixes`) — each carries inline `EXCEPTION-…` / `OUT-OF-SCOPE` markers:
- `Sandbox/SandboxSession.cs` — mounts registered before DI (`EXCEPTION-BOOTSTRAP`)
- `Sandbox/ProcessIsolationSandbox.cs`, `Sandbox/DockerSandbox.cs` — host-binary probing (`OUT-OF-SCOPE`)
- `Security/PathValidator.cs` — symlink/realpath resolution is its core job

Every other legitimately-raw file relies on a narrow `[SuppressVfsCompliance]` at the use site rather than a path exemption (see the ratified permanent exceptions below).

## Opt-out: `[SuppressVfsCompliance]`

For documented exceptions that do not match a path-based exemption, apply the attribute declared in `src/core/Orkeon.Domain/Attributes/SuppressVfsComplianceAttribute.cs` (namespace `Orkeon.Compliance.Vfs`). The analyzer matches it by its full name, not by assembly, so a project that does not reference `Orkeon.Domain` declares its own `internal` copy — `Orkeon.Hosting.Aspire` does:

```csharp
[Orkeon.Compliance.Vfs.SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: provisions the physical directory the /sandbox mount points at")]
internal sealed partial class SandboxSession { … }

[SuppressVfsCompliance("OUT-OF-SCOPE: probes external toolchain (esbuild) binary location, not a VFS mount.")]
internal string ResolveBinary() { … }
```

Placement:
- **Assembly** — broad opt-out for a whole project (use sparingly, prefer a narrower scope).
- **Class / struct** — opt out every member of a type (e.g. a bootstrap class such as `RunnerHost`).
- **Method / constructor / property / field** — narrowest scope.

The `reason` argument is mandatory and should name the audit category it belongs to:
- `EXCEPTION-BOOTSTRAP` — runs before VFS mounts are registered.
- `EXCEPTION-WATCHER-BRIDGE` — adapter that wraps `System.IO.FileSystemWatcher` to implement an Orkeon watcher port.
- `OUT-OF-SCOPE` — host-level probing (SDK install path, system binary discovery) that never targets a VFS mount.

> **Retired categories (VFS-70):** `EXCEPTION-BACKCOMPAT` and `EXCEPTION-OBSOLETE` are **no longer
> accepted**. All tools/services require a non-nullable `IFileSystemService` (a nullable one now
> trips `ORKVFS007`) and the FS-related `[Obsolete]` shims were deleted. Do not reintroduce either
> category.

Any new class of exception should first be captured in the VFS compliance audit (the maintainers' archive, `audit-vfs-compliance-*`) before a suppression is added.

### Ratified permanent exceptions (2D)

These accesses cannot go through the VFS by nature and are **permanently** allowed via the inline `[SuppressVfsCompliance]` markers above (or the path allowlist):

| Area | Location | Category | Why |
|---|---|---|---|
| VFS implementation | `Domain/FileSystem/*`, `Infrastructure/FileSystem/*` | (the abstraction) | It *is* the VFS |
| Bootstrap (pre-DI) | `SandboxSession`, ConsoleApp `Cli*MountBootstrapper`, `Hosting/Runner*`, `Hosting/CrewMountDeclarations` (pre-reads a crew's `mounts:` block, VFS-90) | `EXCEPTION-BOOTSTRAP` | Read appsettings + mount before the VFS exists. Covers *provisioning* a mount, never exposing a physical path as a virtual one — see [ADR-008](../adr/ADR-008-virtual-paths-are-the-only-currency.md) |
| Host probing | `DockerSandbox`, `ProcessIsolationSandbox`, `ProcessGitDiffProvider` | `OUT-OF-SCOPE` | Discover `docker`/`dotnet`/`git` on PATH, never a mount |
| Toolchain | `Scripting/Toolchain/EsbuildTranspiler.cs` | `OUT-OF-SCOPE` | Locates the `esbuild` binary + transpile temp files; host toolchain |
| Host applications (pre-DI) | `orkeon-host` (`CrewHostService`, `HostCrewMounts`, `HostStartup`), `Hosting/CrewDirectoryLayout`, the `orkeon` entrypoint (`Orkeon.Scripting.Cli`, assembly-wide), the REPL's `Program` | `EXCEPTION-BOOTSTRAP` | Validate or classify operator-supplied crew, script and settings paths before any mount exists |
| Orkeon Studio | `Orkeon.Studio.Core` / `.Run` / `.Wpf` (settings file, teams, model profiles, launch history, folder pickers, forge sessions) | `EXCEPTION-BOOTSTRAP` | Studio is a host application: it edits the user's own files and folders, never a crew's virtual paths |
| Studio launch planning | `LaunchMountPlan`, `MountOverrideSemantics`, `ITargetProbe`, `OrkeonBinaryLocator`, `IExecutableProbe`; the Aspire `AddOrkeonCrewRun` | `OUT-OF-SCOPE` | Compose the command line and mount strings of a process they launch, or find the `orkeon` binary |
| Security primitive | `Security/PathValidator.cs` | (allowlist) | symlink/realpath resolution is its job |
| Watcher bridge | `Analysis/.../FileSystemWatcherCodebaseWatcher.cs` | `EXCEPTION-WATCHER-BRIDGE` | Adapts `System.IO.FileSystemWatcher` to `ICodebaseWatcher` |
| RaggableTree probe | `Analysis/Core/FileSystemDiscoverer.cs` (diagnostic probe only) | `OUT-OF-SCOPE` | The *batch discovery itself* runs on `EnumerateFilesAsync`; the probe deliberately counts physical entries to distinguish "VFS yielded nothing" from "disk empty" |
| SQLite persistence | `SqliteMemoryProvider`, `SqliteStateStore` | governed | The engine needs a real path; the `Data Source` is resolved via `ResolveAndValidate` (decision 2F-A) before reaching the driver |

## Adding a new tool or service

1. Declare a **required, non-nullable** constructor parameter of type `IFileSystemService` and inject it via DI (a nullable one trips `ORKVFS007`). Validate it with `ArgumentNullException.ThrowIfNull`.
2. Work in virtual paths (`/workspace/...`, `/output/...`, `/tmp/...`).
3. Never add a `string`-path `System.IO` fallback. There is no back-compat ctor — DI is the only construction path.
4. If you need to enumerate, stream, copy, or watch, use the dedicated `IFileSystemService` methods rather than the equivalent `System.IO` primitives (including `StreamReader`/`StreamWriter` — wrap a `Stream` from `OpenReadStreamAsync`/`OpenWriteStreamAsync`, never a path).

## Mounts, rights and visibility

`IFileSystemService` (`Orkeon.Domain.FileSystem`) is the whole surface a component needs:
`ResolveAndValidate` (virtual → physical, checked against a required `FileAccessRights`),
`ToVirtualPath`, `GetAvailableMounts`, `EnumerateFilesAsync`, `ExistsAsync`,
`GetEntryKindAsync`, `TryGetEntryAsync`, `TryReadAllTextAsync` / `TryReadAllBytesAsync`,
`OpenReadStreamAsync`, `WriteAllTextAsync` / `WriteAllBytesAsync` / `AppendAllTextAsync`,
`OpenWriteStreamAsync` / `OpenAppendStreamAsync`, `CreateDirectoryAsync`, `DeleteAsync`,
`CopyAsync`. Change notifications come from the separate `IVirtualFileSystemWatcher`.
`AddOrkeonFileSystem(configuration)` registers both.

A mount is declared as a string — `[<ulid>|]<physical>:<virtual>:<rights>[;<subpath>:<rights>…]`:

- `<virtual>` must start with `/`; a physical path containing `:` or `;`, or ending with a
  backslash, is quoted (`"C:\src\":/workspace:ro`).
- `<rights>` is `ro` (`FileAccessRights.ReadOnly`), `rw` (`ReadWrite` = Read, Write, Create,
  Delete) or `rwnd` (`ReadWriteNoDelete`); each `;<subpath>:<rights>` overrides the rights of a
  subtree.
- The optional 26-character ULID before `|` identifies the entry (VFS-90) — see the
  [configuration reference](../reference/configuration.md).

`FileAccessRights` is a flags enum (`Read`, `Write`, `Create`, `Delete`); every operation
states the right it needs and the registry refuses the call when the mount (or the subtree
override) does not grant it. `MountVisibility` is `AgentFacing` (the default, listed by
`GetAvailableMounts` and in the agent prompt) or `Internal` (below).

## Internal roots and privileged access

A runner mounts a few roots for itself as **internal** mounts (`Orkeon:FileSystem:InternalMounts`,
`MountVisibility.Internal`): `/llm-logs` (the `--llm-log` exchange files), `/sandbox` (where the
code sandboxes stage what they run) and `/credentials` (the OAuth tokens of the e-mail accounts —
mounted only when an OAuth account is declared). The registry refuses an internal mount to every
ordinary caller and reports it like a path that does not exist, so no agent-facing tool can reach
or even name one ([ADR-008](../adr/ADR-008-virtual-paths-are-the-only-currency.md)). The few
components that must write there ask DI for `PrivilegedFileSystemAccess` by name: the exchange
logger, the code sandboxes and the e-mail token store (`FileSystemEmailTokenStore`, which the
runner host registers over `/credentials/email`). They still go through the VFS. The only raw
`System.IO` on that path is the runner creating the physical `credentials` directory and its
`email` subdirectory (owner-only on Unix) before any mount exists — `Hosting/Runner*` bootstrap,
an already-ratified `EXCEPTION-BOOTSTRAP` scope, not a new category. A user mount claiming one
of these roots is refused (`RunnerVirtualRoots.All`, `/credentials` included whatever the
accounts).

## Per-scope mounts (ambient mount override)

The mount set is a boot-time singleton: `AddOrkeonFileSystem` builds one `FileSystemRegistry` from
`Orkeon:FileSystem:Mounts`, and `IFileSystemService` is a singleton over it. That is correct for a
single-tenant runner, but a host that runs many crews in one process (e.g. a run engine driving a
different profile per run) needs to give **one execution flow its own mounts** without disturbing the
others.

`IFileSystemService` cannot become DI-scoped for this: dozens of singletons inject it, so a scoped
lifetime would be a captive dependency. Instead, per-scope mounts are provided by an **ambient
override** — `IFileSystemScope` (registered as a singleton, backed by `AsyncLocal<FileSystemRegistry?>`,
`AsyncLocalFileSystemScope`). The singleton `FileSystemService` reads it **per operation**: it resolves
against the ambient registry when an execution flow has entered one, and against the boot registry
otherwise. `AsyncLocal` isolates the value per asynchronous control flow, so concurrent runs never see
each other's mounts, and a host that never enters a scope keeps byte-identical behavior.

```csharp
// Inside a run scope: install this run's mounts for this async flow only.
var granted = grantedMountStrings.Select(FileSystemMount.Parse).ToList();
using var registry = ScopedMountComposition.ForExecution(bootRegistry, granted);
using var _ = fileSystemScope.Enter(registry);          // restored on dispose (nesting supported)

// Every IFileSystemService call on THIS async flow now resolves against `granted`;
// other concurrent runs continue to see the boot mounts.
```

**Compose, never hand-build.** Entering a scope REPLACES the mount set — `ActiveRegistry` is
`scope?.Current ?? boot`, never a union — so a registry built from one execution's own folders
alone takes the **whole** boot set down with it for the length of that flow. `ForExecution`
therefore carries the boot mounts forward and lets the execution's own **shadow** them at the
virtual paths they claim; substitution rather than addition is the point of the mechanism, since
two crews each granted `/output` over different folders is exactly what one flat registry cannot
express.

Both halves of the boot set have to survive, for different reasons. The **Internal** ones —
`/llm-logs`, `/sandbox`, `/credentials` — are a confidentiality matter: dropping them stops
exchange logging, both code sandboxes and the e-mail token store resolving, and it drops
`IsUnderInternalMountUnsafe`, the check that stops any of them gaining a second, agent-reachable
address through one of the execution's own mounts. The **agent-facing** ones are just as load-bearing: `orkeon-host` mounts each hosted
crew's directory under `/crews` and then loads the crew *by that virtual path, from inside the
scope*. Composing only the execution's own mounts made `Orkeon:Host:Crews:*:Mounts` — the
feature's own configuration key — unable to load the crew it was set on, and the failure came back
as a generic run-failed message because `ExistsAsync` swallows the denial.

**Who enters one today.** `orkeon-host` is the one shipped composition root that does, once per
hosted crew, from `Orkeon:Host:Crews:*:Mounts` — so two hosted crews may each address their own
`/output` over different folders, which the single flat boot registry cannot express. Every other
runner keeps the boot mounts: one process per launch makes the question moot for them.

The guards apply to scoped mounts exactly as to boot mounts, because they run per operation over
whichever registry is active: the registry enforces mount rights + path-traversal containment, and
`IPathValidator` independently enforces the workspace-root / blocked-path / extension checks
(`PathSecurity:DefaultWorkspaceRoot`, `AdditionalAllowedDirectories`). A scoped mount whose physical
base sits outside the allowed workspace root is denied just like a boot mount would be — granting
a folder in `Orkeon:Host:Crews:*:Mounts` is therefore not sufficient on its own, and
`PathSecurity:AdditionalAllowedDirectories` is where an operator widens the second gate. The caller owns
the scoped `FileSystemRegistry`'s lifetime (`Enter` does not dispose it — dispose it yourself, as above).

## CI behaviour

All seven diagnostics (`ORKVFS001`–`ORKVFS007`) are **errors**, so CI blocks any merge that reintroduces `System.IO` file access, a string-path `StreamReader`/`StreamWriter`, a `Path.GetFullPath` on user input, or a nullable `IFileSystemService` in framework code without a justified `[SuppressVfsCompliance]` attribute.

## Exit criteria (from the VFS migration programme)

- [x] `VIOLATION-HISTORIC == 0` — the 23 historic violations were eliminated in P5-VFS-50.
- [x] `VIOLATION-NEW` reduced to documented exceptions behind `[Obsolete]` / `if (_fs is null)` guards, all covered by `[SuppressVfsCompliance]`.
- [x] `EXCEPTION-BOOTSTRAP` ≤ 15 — 7 at the programme's close, all legitimate (`SandboxSession`). The threshold measured the framework libraries; the host applications added since (runners, `orkeon-host`, the REPL, Studio) carry their own bootstrap scopes, listed in the ratified table above.
- [x] Roslyn analyzer `Orkeon.Compliance.Vfs` in place and wired in `src/Directory.Build.props`.
- [x] Build passes clean; negative test confirms a deliberate `File.ReadAllText` in framework code trips `ORKVFS001`.
- [x] Eliminate residual `EXCEPTION-BACKCOMPAT` suppressions by migrating all tool callers to DI — **done in VFS-70**: 0 `EXCEPTION-BACKCOMPAT` and 0 FS-related `EXCEPTION-OBSOLETE` remain in `src/`; all file tools + the knowledge ingestion path (now the `Orkeon.Rag` loaders, RAG-02) require a non-nullable `IFileSystemService`; SQLite governed via `ResolveAndValidate`; `ORKVFS004` promoted to error and `ORKVFS006`/`ORKVFS007` added to close the `StreamReader/Writer(string)` and nullable-`IFileSystemService` blind spots.
