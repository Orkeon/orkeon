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

The analyzer is applied to every project under `src/` except itself, so violations break the build immediately. Tests run the same analyzer via the `Orkeon.Compliance.Vfs.Tests` project.

## Diagnostic codes

| Code | Severity | Detects | Suggested fix |
|---|---|---|---|
| `ORKVFS001` | Error | Direct call to `System.IO.File.*` | Inject `IFileSystemService` and use `TryReadAllBytesAsync` / `WriteAllTextAsync` / `ExistsAsync` / … |
| `ORKVFS002` | Error | Direct call to `System.IO.Directory.*` | Use `CreateDirectoryAsync`, `DeleteAsync`, `EnumerateFilesAsync` |
| `ORKVFS003` | Error | `new FileStream(string …)`, `new FileInfo(string)`, `new DirectoryInfo(string)` | Use `OpenReadStreamAsync`, `OpenWriteStreamAsync`, `TryGetEntryAsync` |
| `ORKVFS004` | Error | `Path.GetFullPath(…)` (may bypass mount validation) | For user-supplied input, call `ResolveAndValidate` |
| `ORKVFS005` | Error | `new FileSystemWatcher(…)` | Use the VFS watcher abstraction |
| `ORKVFS006` | Error | `new StreamReader(string)` / `new StreamWriter(string)` (path overloads) | Open via `OpenReadStreamAsync` / `OpenWriteStreamAsync` and wrap the returned `Stream` |
| `ORKVFS007` | Error | A nullable `IFileSystemService?` field or parameter | Inject `IFileSystemService` as a required, non-nullable dependency |

All diagnostics are defined in `src/analyzers/Orkeon.Compliance.Vfs/DiagnosticDescriptors.cs` and emit a stable help link.

## Exempt scopes (path-based)

The analyzer skips file paths that match its allowlist (`SystemIoUsageAnalyzer.IsExemptByPath`). It is intentionally narrow — folder-wide blank checks were replaced by an explicit per-file allowlist so a *new* file under the same folder is **not** silently exempted.

Folder exemptions (`s_exemptFolderSegments`):
- `/core/Orkeon.Domain/FileSystem/` — VFS public contracts (the abstraction itself)
- `/core/Orkeon.Infrastructure/FileSystem/` — VFS implementation (disk/fake/watcher)
- `/tests/` — fixtures rely on `DiskBackedFileSystemService` and real disk
- `/examples/` — out of framework compliance scope

Per-file exemptions (`s_exemptFileSuffixes`) — each carries inline `EXCEPTION-…` / `OUT-OF-SCOPE` markers:
- `Sandbox/SandboxMountBootstrapper.cs` — mounts registered before DI (`EXCEPTION-BOOTSTRAP`)
- `Sandbox/ProcessIsolationSandbox.cs`, `Sandbox/DockerSandbox.cs` — host-binary probing (`OUT-OF-SCOPE`)
- `Security/PathValidator.cs` — symlink/realpath resolution is its core job

Every other legitimately-raw file relies on a narrow `[SuppressVfsCompliance]` at the use site rather than a path exemption (see the ratified permanent exceptions below).

## Opt-out: `[SuppressVfsCompliance]`

For documented exceptions that do not match a path-based exemption, apply the attribute from `Orkeon.Domain.Attributes/SuppressVfsComplianceAttribute.cs`:

```csharp
[Orkeon.Compliance.Vfs.SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: mount registration runs before DI")]
public sealed class SandboxMountBootstrapper { … }

[Obsolete("Use ReadAsync(…) instead")]
[Orkeon.Compliance.Vfs.SuppressVfsCompliance("EXCEPTION-OBSOLETE: transitional API; callers should migrate")]
public static string LoadLegacy(string path) => File.ReadAllText(path);
```

Placement:
- **Assembly** — broad opt-out for a whole project (use sparingly, prefer a narrower scope).
- **Class / struct** — opt out every member of a type (tool classes with `if (_fs is null)` fallbacks).
- **Method / constructor / property / field** — narrowest scope.

The `reason` argument is mandatory and should name the audit category it belongs to:
- `EXCEPTION-BOOTSTRAP` — runs before VFS mounts are registered.
- `EXCEPTION-WATCHER-BRIDGE` — adapter that wraps `System.IO.FileSystemWatcher` to implement an Orkeon watcher port.
- `OUT-OF-SCOPE` — host-level probing (SDK install path, system binary discovery) that never targets a VFS mount.

> **Retired categories (VFS-70):** `EXCEPTION-BACKCOMPAT` and `EXCEPTION-OBSOLETE` are **no longer
> accepted**. All tools/services require a non-nullable `IFileSystemService` (a nullable one now
> trips `ORKVFS007`) and the FS-related `[Obsolete]` shims were deleted. Do not reintroduce either
> category.

Any new class of exception should first be captured in the VFS compliance audit (`project/audit-vfs-compliance-*.md`) before a suppression is added.

### Ratified permanent exceptions (2D)

These accesses cannot go through the VFS by nature and are **permanently** allowed via the inline `[SuppressVfsCompliance]` markers above (or the path allowlist):

| Area | Location | Category | Why |
|---|---|---|---|
| VFS implementation | `Domain/FileSystem/*`, `Infrastructure/FileSystem/*`, `Scripting.Cli/CliFileSystemService.cs` | (the abstraction) | It *is* the VFS |
| Bootstrap (pre-DI) | `SandboxMountBootstrapper`, ConsoleApp `Cli*MountBootstrapper`, `Hosting/Runner*` | `EXCEPTION-BOOTSTRAP` | Read appsettings + mount before the VFS exists |
| Host probing | `DockerSandbox`, `ProcessIsolationSandbox`, `ProcessGitDiffProvider` | `OUT-OF-SCOPE` | Discover `docker`/`dotnet`/`git` on PATH, never a mount |
| Toolchain | `Scripting/Toolchain/EsbuildTranspiler.cs` | `OUT-OF-SCOPE` | Locates the `esbuild` binary + transpile temp files; host toolchain |
| Security primitive | `Security/PathValidator.cs` | (allowlist) | symlink/realpath resolution is its job |
| Watcher bridge | `Analysis/.../FileSystemWatcherCodebaseWatcher.cs` | `EXCEPTION-WATCHER-BRIDGE` | Adapts `System.IO.FileSystemWatcher` to `ICodebaseWatcher` |
| RaggableTree probe | `Analysis/Core/FileSystemDiscoverer.cs` (diagnostic probe only) | `OUT-OF-SCOPE` | The *batch discovery itself* runs on `EnumerateFilesAsync`; the probe deliberately counts physical entries to distinguish "VFS yielded nothing" from "disk empty" |
| SQLite persistence | `SqliteMemoryProvider`, `SqliteStateStore` | governed | The engine needs a real path; the `Data Source` is resolved via `ResolveAndValidate` (decision 2F-A) before reaching the driver |

## Adding a new tool or service

1. Declare a **required, non-nullable** constructor parameter of type `IFileSystemService` and inject it via DI (a nullable one trips `ORKVFS007`). Validate it with `ArgumentNullException.ThrowIfNull`.
2. Work in virtual paths (`/workspace/...`, `/output/...`, `/tmp/...`).
3. Never add a `string`-path `System.IO` fallback. There is no back-compat ctor — DI is the only construction path.
4. If you need to enumerate, stream, copy, or watch, use the dedicated `IFileSystemService` methods rather than the equivalent `System.IO` primitives (including `StreamReader`/`StreamWriter` — wrap a `Stream` from `OpenReadStreamAsync`/`OpenWriteStreamAsync`, never a path).

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
// Inside a run scope: install the run profile's mounts for this async flow only.
var mounts = profileMountStrings.Select(FileSystemMount.Parse).ToList();
using var registry = new FileSystemRegistry(mounts);   // caller owns the registry lifetime
using var _ = fileSystemScope.Enter(registry);          // restored on dispose (nesting supported)

// Every IFileSystemService call on THIS async flow now resolves against `mounts`;
// other concurrent runs continue to see the boot mounts.
```

The guards apply to scoped mounts exactly as to boot mounts, because they run per operation over
whichever registry is active: the registry enforces mount rights + path-traversal containment, and
`IPathValidator` independently enforces the workspace-root / blocked-path / extension checks
(`PathSecurity:DefaultWorkspaceRoot`, `AdditionalAllowedDirectories`). A scoped mount whose physical
base sits outside the allowed workspace root is denied just like a boot mount would be. The caller owns
the scoped `FileSystemRegistry`'s lifetime (`Enter` does not dispose it — dispose it yourself, as above).

## CI behaviour

All seven diagnostics (`ORKVFS001`–`ORKVFS007`) are **errors**, so CI blocks any merge that reintroduces `System.IO` file access, a string-path `StreamReader`/`StreamWriter`, a `Path.GetFullPath` on user input, or a nullable `IFileSystemService` in framework code without a justified `[SuppressVfsCompliance]` attribute.

## Exit criteria (from the VFS migration programme)

- [x] `VIOLATION-HISTORIC == 0` — the 23 historic violations were eliminated in P5-VFS-50.
- [x] `VIOLATION-NEW` reduced to documented exceptions behind `[Obsolete]` / `if (_fs is null)` guards, all covered by `[SuppressVfsCompliance]`.
- [x] `EXCEPTION-BOOTSTRAP` ≤ 15 — currently 7, all legitimate (`SandboxMountBootstrapper`).
- [x] Roslyn analyzer `Orkeon.Compliance.Vfs` in place and wired in `src/Directory.Build.props`.
- [x] Build passes clean; negative test confirms a deliberate `File.ReadAllText` in framework code trips `ORKVFS001`.
- [x] Eliminate residual `EXCEPTION-BACKCOMPAT` suppressions by migrating all tool callers to DI — **done in VFS-70**: 0 `EXCEPTION-BACKCOMPAT` and 0 FS-related `EXCEPTION-OBSOLETE` remain in `src/`; all file tools + the knowledge ingestion path (now the `Orkeon.Rag` loaders, RAG-02) require a non-nullable `IFileSystemService`; SQLite governed via `ResolveAndValidate`; `ORKVFS004` promoted to error and `ORKVFS006`/`ORKVFS007` added to close the `StreamReader/Writer(string)` and nullable-`IFileSystemService` blind spots.
