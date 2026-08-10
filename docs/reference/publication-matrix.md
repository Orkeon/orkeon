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

On a `v*` tag, `release.yml` builds every installer artifact, **smokes the two onboarding
channels on real runners**, and only then attaches everything to the GitHub Release. The
pipeline is `installers → {smoke-windows, smoke-deb, smoke-macos, msi} → release`;
`workflow_dispatch` runs
the same thing minus the publication (no tag, no Release to attach to). The retired
`orkeon-examples` runner is **no longer packaged** — the `orkeon` CLI replaces it
(`orkeon run crew.yaml` runs the `examples/` YAML crews; `orkeon run script.ork.ts` runs the
scripting DSL).

| Artifact | Built by | Contents | Runtime |
|---|---|---|---|
| `orkeon-<version>-<rid>.tar.gz` / `.zip` | `package-installers.sh` (default `--app-set full`) | every CLI launcher + one shared esbuild | mixed: `orkeon` and `orkeon-trading` self-contained, the rest framework-dependent |
| `orkeon-cli-<version>-win-x64.zip` | `package-installers.sh --app-set cli --rids win-x64` | the `orkeon` CLI alone + `install.ps1` | self-contained |
| `orkeon_<version>_amd64.deb` | `package-deb.sh` (reuses the `linux-x64` staging tree — one publish, two packages) | the `orkeon` CLI alone at `/usr/bin/orkeon` | self-contained; `Depends` on system libraries only (libicu / libssl alternations), never on `dotnet-runtime-*` |
| `orkeon-<version>-win-x64.msi` | `build-msi.ps1` (WiX, per-user scope), harvesting the extracted CLI zip | the `orkeon` CLI alone, same pruned publish as the zip | self-contained |
| `orkeon-cli-<version>-osx-arm64.tar.gz` / `-osx-x64.tar.gz` | `package-installers.sh --app-set cli --rids osx-arm64 osx-x64` (cross-published from the ubuntu runner) | the `orkeon` CLI alone + `install.sh` | self-contained |
| `SHA256SUMS` | the `installers` job's packaging scripts (`package-deb.sh` refreshes its own line) | one line per artifact above **except the MSI** | — |
| `SHA256SUMS.msi` | `build-msi.ps1`, in the `msi` job | the MSI alone | — |

Two checksum files rather than one: the ubuntu `installers` job writes `SHA256SUMS` before
the MSI exists — it is built later, on `windows-latest`. Each checksum file is generated by
the job that produced the artifact it covers.

**Blocking smokes.** `smoke-windows` (a `windows-latest` runner) installs
`orkeon-cli-*-win-x64.zip` and walks the onboarding chain on it; `smoke-deb` (a stock
`ubuntu-latest` image) installs the `.deb` through `apt`, walks the same chain, then removes
the package; `smoke-macos` (a `macos-latest`, Apple-silicon runner) extracts the `osx-arm64`
tarball, installs it with `install.sh` and walks the same chain before uninstalling; the `msi`
job runs its own `msiexec /i /qn` → `orkeon doctor --json` → `msiexec /x /qn` chain, asserting
the install directory, the ARP entry and the user `PATH` entry appear and then disappear. All
four install from the **job** artifacts, never from the Release, so a broken payload is caught
before anything is published — the `release` job `needs` all of them.

`smoke-macos` is also the only place the Gatekeeper and code-signing story is exercised: an
unsigned, quarantined or malformed native library (`libtree-sitter*.dylib`, onnxruntime, the
esbuild binary) is killed at load time, so it fails there rather than in a user's terminal.

The debian version string replaces `-` with `~` (`0.9.2-beta` → `orkeon_0.9.2~beta_amd64.deb`)
so a pre-release sorts before its final under `dpkg`.

The MSI's `ProductVersion` drops the suffix instead: Windows Installer versions carry only three
numeric fields, so `build-msi.ps1` truncates `0.9.2-beta` to `0.9.2` for the property that
`<MajorUpgrade>` actually compares. Nothing is silently lost — the full string survives in the
`.msi` filename (`orkeon-0.9.2-beta-win-x64.msi`) and in the `ARPCOMMENTS` property shown in
"Installed apps".

The `orkeon` CLI is distributed through **seven channels**:

| Channel | Artifact | Runtime | Audience |
|---|---|---|---|
| NuGet dotnet tool | `Orkeon.Scripting.Cli` (`PackAsTool`, command `orkeon`) | needs .NET 10 SDK (`dotnet tool install`) | .NET developers. **Still gated on D3** — unchanged by this release, nothing is pushed to NuGet.org |
| Windows zip + `install.ps1` | `orkeon-cli-<version>-win-x64.zip` | self-contained | Windows onboarding — the recommended channel |
| Windows MSI (per-user) | `orkeon-<version>-win-x64.msi` | self-contained | Windows, double-click install and an "Installed apps" entry. One channel at a time: the MSI refuses to install over a zip install |
| Debian package | `orkeon_<version>_amd64.deb` | self-contained | Debian / Ubuntu onboarding — the recommended channel |
| macOS tarball + `install.sh` | `orkeon-cli-<version>-osx-arm64.tar.gz` / `-osx-x64.tar.gz` | self-contained | macOS onboarding today; `install.sh` clears the Gatekeeper quarantine attribute and ad-hoc re-signs the Mach-O files `codesign -v` rejects |
| Homebrew | the same osx tarballs, via `installers/homebrew/orkeon.rb` | self-contained | macOS, once the tap exists — **not published yet**, see below |
| Multi-app installer archive | `orkeon` / `orkeon-slim` launchers | `orkeon` self-contained, `orkeon-slim` framework-dependent | devs who also want the REPL, the TUI runners or the trading showcase |

**Homebrew — formula in the repository, tap not yet created.** `installers/homebrew/orkeon.rb`
is a binary formula: it downloads the osx tarball matching the machine's architecture
(`on_arm` / `on_intel`), installs the payload under the Cellar's `libexec`, and writes a
`bin/orkeon` wrapper that points `ORKEON_ESBUILD_PATH` at the bundled esbuild — the same
contract as `wrapper.sh.tmpl`. Its `test do` block runs `orkeon doctor` rather than
`orkeon --version`, which exits `1`.

`version` and both `url` / `sha256` pairs are **generated**, never hand-edited:
`scripts/update-homebrew-formula.sh --release <tag>` (or `--sums <file>`) rewrites the five
values from a release's `SHA256SUMS` and is idempotent. Until the first tagged release, the
two `sha256` values are the literal placeholders `PLACEHOLDER_SHA256_ARM64` /
`PLACEHOLDER_SHA256_X64`, so an accidental install fails verification instead of fetching
something unverified.

Publishing the `Orkeon/homebrew-tap` repository and pushing the formula to it is a **release
action, not CI** (MAC-00 §8): `brew tap orkeon/tap && brew install orkeon` does not resolve
before that. Submission to homebrew-core and a `.pkg` installer stay out of scope until the
project is code-signed.

All flavours are built from the same `src/scripting/Orkeon.Scripting.Cli` csproj and share
the one bundled esbuild. `orkeon-trading` is likewise self-contained; the remaining CLI
launchers stay framework-dependent — `install.sh` and `install.ps1` detect that case and
print the runtime install commands rather than failing at first launch.

> **Publish behaviour change.** Every `publish` of `src/` and `examples/` now prunes the
> unused tree-sitter grammars (31 → 7 native libraries), via `Directory.Build.targets`.
> Opt out with `-p:OrkeonPruneTreeSitterGrammars=false`. Every artifact in the table above
> carries the pruned payload, the MSI included — which also keeps its WiX component GUIDs
> stable across upgrades.

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
