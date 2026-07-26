> 🇫🇷 [Version française](../fr/reference/publication-matrix.md)

# NuGet publication matrix

This file is the single source of truth for **which projects are published to NuGet**, so the
workflows (`ci.yml` validation, `publish.yml` pack + push on tag, `release.yml` installers)
never drift again (OSS-011 / R8.3).

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
resolved. Each entry below is `IsPackable=true` and therefore already lands on the **internal
GitHub Packages feed** via `publish.yml` (see below), but is **not** pushed to NuGet.org by any
workflow yet.

| PackageId | Note |
|---|---|
| `Orkeon.Tools.Abstractions`, `Orkeon.Tools.Analysis`, `Orkeon.Tools.Code`, `Orkeon.Tools.Data`, `Orkeon.Tools.Embeddings.Local`, `Orkeon.Tools.EventHub`, `Orkeon.Tools.FileSystem`, `Orkeon.Tools.Rag`, `Orkeon.Tools.Web` | Tools family — publish as a set once core is stable. |
| `Orkeon.Rag.Abstractions`, `Orkeon.Rag`, `Orkeon.Rag.Onnx`, `Orkeon.Rag.Onnx.Model` | RAG subsystem (RAG-02…06, ADR-006). `Orkeon.Rag.Onnx` + `Orkeon.Rag.Onnx.Model` are the opt-in cross-encoder pair (runtime + embedded int8 weights) — publish the two together. |
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

## Installer archives (`release.yml`)

On a `v*` tag, `release.yml` runs `scripts/package-installers.sh` to attach per-OS
installer archives (`orkeon-<version>-<rid>.tar.gz` / `.zip`) to the GitHub Release. Each
archive bundles every CLI launcher plus one shared esbuild binary. The retired
`orkeon-examples` runner is **no longer packaged** — the `orkeon` CLI replaces it
(`orkeon run crew.yaml` runs the `examples/` YAML crews; `orkeon run script.ork.ts` runs the
scripting DSL).

The `orkeon` CLI is distributed through **three channels**:

| Channel | Artifact | Runtime | Audience |
|---|---|---|---|
| NuGet dotnet tool | `Orkeon.Scripting.Cli` (`PackAsTool`, command `orkeon`) | needs .NET 10 SDK (`dotnet tool install`) | .NET developers |
| Installer archive — slim | `orkeon-slim` launcher | framework-dependent (needs .NET 10 runtime) | devs who already have .NET 10 |
| Installer archive — self-contained | `orkeon` launcher | self-contained (runtime bundled) | onboarding; no .NET install required |

Both installer flavours are built from the same `src/scripting/Orkeon.Scripting.Cli`
csproj and share the one bundled esbuild. `orkeon-trading` is likewise self-contained;
the remaining CLI launchers stay framework-dependent.

## Build-time / internal (not standalone packages)

| PackageId | Note |
|---|---|
| `Orkeon.Generators` | Source generator — consumed at build time. |
| `Orkeon.Compliance.Vfs` | Roslyn analyzer — consumed at build time. |

## How publication is wired

- All NuGet packing and pushing lives in **`publish.yml`** (tag `v*`): `dotnet pack Orkeon.sln`
  (+ the runner tools) driven by `IsPackable`, pushed to **GitHub Packages** with
  `--skip-duplicate` (idempotent re-runs). `ci.yml` validates (build + test) and packs nothing;
  `release.yml` builds the installer archives and the container image, no NuGet packing.
- **Nothing is pushed to NuGet.org today** — the matrix above is the proposal for that
  promotion, gated on D3 and maintainer confirmation.
- Version flows from `src/Directory.Build.props` (currently `0.9.2-beta`); no project overrides it.
