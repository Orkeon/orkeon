> 🇫🇷 [Version française](../fr/reference/publication-matrix.md)

# NuGet publication matrix

This file is the single source of truth for **which projects are published to NuGet**, so
`ci.yml` (validation) and `release.yml` (push on tag) never drift again (OSS-011 / R8.3).

> **Status — proposal, pending maintainer confirmation.** Only the three core libraries are
> published today. Expanding to the rest of the ecosystem is gated on decision **D3** (the two
> scripting twins `Orkeon.Cli.Scripting` / `Orkeon.Scripting.Cli` must not have their names
> locked into NuGet before the rename question is settled — renaming after a first publish is a
> permanent cost) and on the maintainer confirming the product intent below.

## Published in v1 (today)

| PackageId | Why |
|---|---|
| `Orkeon.Domain` | Core entities and interfaces — the dependency root. |
| `Orkeon.Application` | Use cases, ports, orchestration. |
| `Orkeon.Infrastructure` | Adapters (LLMs, memory, strategies). Documented as installable in the README; this is why `release.yml` was fixed to pack it. |

## Proposed for a later release (deferred)

The announced ecosystem (the tools family, the `orkeon` CLI tool, hosting, plugins) is meant to
be installable, but is held back until the core packages are proven on NuGet **and** D3 is
resolved. Each entry below is `IsPackable=true` (so it builds a package locally) but is **not**
pushed by any workflow yet.

| PackageId | Note |
|---|---|
| `Orkeon.Tools.Abstractions`, `Orkeon.Tools.Analysis`, `Orkeon.Tools.Code`, `Orkeon.Tools.Data`, `Orkeon.Tools.Embeddings.Local`, `Orkeon.Tools.EventHub`, `Orkeon.Tools.FileSystem`, `Orkeon.Tools.Web` | Tools family — publish as a set once core is stable. |
| `Orkeon.Analysis`, `Orkeon.Analysis.Abstractions` | RaggableTree. |
| `Orkeon.Cli`, `Orkeon.Cli.Abstractions`, `Orkeon.Cli.TerminalGui` | CLI libraries. |
| `Orkeon.Cli.Scripting` | **Gated on D3** — twin-name rename window closes at first publish. |
| `Orkeon.Scripting`, `Orkeon.Scripting.Cli` | `Orkeon.Scripting.Cli` is the `orkeon` dotnet tool (`PackAsTool`). **Gated on D3.** |
| `Orkeon.Hosting` | Packaging host (created by R1.5) — strong candidate to ship with the core bundle. |
| `Orkeon.Plugins` | Plugin system. |

## Published to GitHub Packages for `experiments/` (dotnet tools)

`publish.yml` (tag `v*`) packs `Orkeon.sln` and pushes every packable project to
**GitHub Packages** (`nuget.pkg.github.com/Orkeon`) with `--skip-duplicate`. This feed is what
`experiments/` consumes in packages mode. The interactive runners ship as dotnet tools so no
launcher needs a source clone:

| PackageId | Tool command | Source project |
|---|---|---|
| `Orkeon.Runners.Shared` | — (library) | `examples/runners/_shared` |
| `Orkeon.Runners.ClaimVerification` | `orkeon-claim-verify` | `examples/runners/interactive-claim-verification` |
| `Orkeon.Runners.InterviewSpecForge` | `orkeon-spec-forge` | `examples/runners/interactive-interview-spec-forge` |
| `Orkeon.ConsoleApp` | `orkeon-repl` | `src/apps/Orkeon.ConsoleApp` |
| `Orkeon.Scripting.Cli` | `orkeon` | `src/scripting/Orkeon.Scripting.Cli` |

## Build-time / internal (not standalone packages)

| PackageId | Note |
|---|---|
| `Orkeon.Generators` | Source generator — consumed at build time. |
| `Orkeon.Compliance.Vfs` | Roslyn analyzer — consumed at build time. |

## How publication is wired

- Both `ci.yml` and `release.yml` pack the **same** explicit list (the three core libraries).
  When the matrix is confirmed and the deferred set is promoted, switch both to a single
  `dotnet pack Orkeon.sln -c Release` driven by `IsPackable`, so the perimeter is identical by
  construction.
- `--skip-duplicate` makes re-runs idempotent; pushes are tag-gated (`refs/tags/`).
- Version flows from `src/Directory.Build.props` (`0.9.0-beta`); no project overrides it.
