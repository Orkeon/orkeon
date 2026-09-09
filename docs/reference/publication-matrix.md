> 🇫🇷 [Version française](../fr/reference/publication-matrix.md)

# NuGet publication matrix

This file is the single source of truth for **which projects are published to NuGet**, so the
workflows (`ci.yml` validation, `publish.yml` pack + push on tag, `release.yml` installers)
never drift again (OSS-011 / R8.3).

> **Status — consolidated lineup implemented (PUB-25, 2026-09-01).** Distribution is one
> `Orkeon` package plus a handful of opt-ins, wired in `publish.yml` and guarded by the
> `scripts/check-package-closure.py` gate; the first push of this lineup lands at the
> `v1.0.0-rc.3` tag. Trusted Publishing to NuGet.org is **operational** — the discontinued
> per-layer packages `Orkeon.Domain` / `Orkeon.Application` / `Orkeon.Infrastructure` shipped
> `1.0.0-rc.1` (2026-08-18) and `1.0.0-rc.2` (2026-08-25) through it, and are to be unlisted
> once the new lineup is published (see the owner actions below). Decision **D3** (the
> scripting naming twins) is **resolved** — PUB-02, 2026-08-17,
> [ADR-007](../adr/ADR-007-d3-renommage-cli-commands-scripting.md): the command library was
> renamed `Orkeon.Cli.Scripting` → `Orkeon.Cli.Commands.Scripting` before any NuGet publish
> locked the old name in.

## The NuGet.org lineup

One package installs the whole framework; everything else in the lineup is an opt-in kept
separate only because of what it would force on every consumer (dependency weight, native
runtimes, a pre-release upstream).

| PackageId | Why |
|---|---|
| `Orkeon` | The umbrella package — the eleven assemblies of the core closure (`Orkeon.Domain`, `Orkeon.Application`, `Orkeon.Infrastructure`, `Orkeon.Constants.{Llm,FileSystem,Configuration}`, `Orkeon.Tools.Abstractions`, `Orkeon.Analysis.Abstractions`, `Orkeon.Rag.Abstractions`, `Orkeon.Analysis`, `Orkeon.Rag`) embedded in one nupkg. One install = the complete framework: agents, crews, six orchestration modes, 14 LLM providers, 6 memory stores, RAG, RaggableTree. The Clean Architecture split stays a source-layout discipline, not a distribution contract. |
| `Orkeon.Tools` | The seven built-in tool families (`Analysis`, `Code`, `Data`, `EventHub`, `FileSystem`, `Rag`, `Web`) in one nupkg. Separate from `Orkeon` **only for dependency weight**: the Data tools pull database drivers, PDF and spreadsheet libraries a consumer who never uses them should not inherit. Depends on `Orkeon`. |
| `Orkeon.Rag.Onnx`, `Orkeon.Rag.Onnx.Model` | Opt-in ONNX cross-encoder reranker pair (runtime + embedded int8 weights) — pushed together; native onnxruntime payload. `Orkeon.Rag.Onnx` depends on `Orkeon`; `Orkeon.Rag.Onnx.Model` is dependency-free (embedded resources only) and is referenced alongside it. |
| `Orkeon.Tools.Embeddings.Local` | Local on-device embeddings (BGE-micro-v2 ONNX). Stays **outside the umbrella** because it carries a pre-release SmartComponents dependency from an archived upstream — putting it in `Orkeon` would force that pre-release on every consumer. Depends on `Orkeon`. |
| `Orkeon.Scripting.Cli` | The `orkeon` dotnet tool (`PackAsTool`; the PackageId is the install command — ADR-007). Publishable on NuGet.org since the iOS/Android onnxruntime natives a CLI tool can never load were excluded: 262.5 MB → 137.6 MB, under the nuget.org size limit. |

### How the packaging projects are built

- The lineup nupkgs come from dedicated **packaging projects** under `src/packaging/` — four
  of them: the `Orkeon` and `Orkeon.Tools` umbrellas, plus the `Orkeon.Rag.Onnx.Package` and
  `Orkeon.Tools.Embeddings.Local.Package` **wrappers**, which pack the two opt-in assemblies
  with a nuspec dependency on the `Orkeon` umbrella. The embedded library projects themselves
  are `IsPackable=false` and their source layout, namespaces and per-assembly PublicAPI
  freeze are untouched; the real opt-in projects keep normal `ProjectReference`s, so in-repo
  consumers are unaffected — only the wrappers carry the embed pattern below.
- Each packaging project references its embedded project(s) with `PrivateAssets="all"`
  (keeping them out of the nuspec dependency list) and packs their DLL+XML into `lib/`.
  Because `PrivateAssets="all"` also stops the embedded projects' external
  `PackageReference`s from flowing into the nuspec, the **union of those references is
  re-declared by hand** in the packaging csproj.
- `scripts/check-package-closure.py` (run by `publish.yml` right after the pack) fails the
  workflow if (1) a lineup package declares an `Orkeon.*` dependency outside the lineup — the
  NU1101 class of incident — or (2) an umbrella's hand-declared externals drift from what its
  embedded projects actually require.
- `publish.yml` pushes **`Orkeon` first**: the other lineup packages depend on it and NuGet
  does not order pushes, so pushing a dependent first would expose a package whose restore
  fails.

## Discontinued packages

The following PackageIds are **no longer packed** (`IsPackable=false`): `Orkeon.Domain`,
`Orkeon.Application`, `Orkeon.Infrastructure`, the five `Orkeon.Constants.*` satellites, the
seven `Orkeon.Tools.<family>` packages, `Orkeon.Cli`, `Orkeon.Cli.Abstractions`,
`Orkeon.Cli.Commands.Scripting`, `Orkeon.Cli.TerminalGui`, `Orkeon.Scripting`,
`Orkeon.Hosting`, `Orkeon.Plugins`.

**Migration**: the source code and namespaces are unchanged, so consumer code compiles as-is —
only the install changes. Uninstall the per-layer packages and
`dotnet add package Orkeon --prerelease` (add `Orkeon.Tools` if you used a
`Orkeon.Tools.<family>` package other than `Embeddings.Local`).

## Actual NuGet.org state, and the remaining owner actions

`orkeon.domain`, `orkeon.application` and `orkeon.infrastructure` **were published** on
NuGet.org: `1.0.0-rc.1` (2026-08-18) and `1.0.0-rc.2` (2026-08-25), pushed by `publish.yml`
through Trusted Publishing. Two of the three were not restorable there (`NU1101`): they
declared five `Orkeon.*` dependencies that were never published — the incident that motivated
the closure gate above. All six versions were **unlisted on 2026-09-07** — unlisted, not
deleted: the bytes are still served, so a consumer pinning one of them still restores.

Owner actions already done:

- ✅ **2026-09-07 — the six `rc.1` / `rc.2` versions of the per-layer trio are unlisted**
  (`listed: false` on the three packages). Nobody is offered a package that cannot restore.
- ✅ **2026-09-08 — the `Orkeon` prefix is reserved** on nuget.org for the owner account
  `arion-orkeon`: the bare `Orkeon` ID *and* the `Orkeon.*` family. Any matching package ID
  pushed by another account is rejected from now on — the ~30 `Orkeon.*` assembly names this
  documentation makes public can no longer be squatted.
- ✅ The `NUGET_USER` repository variable and the Trusted Publishing policy are operational
  (the rc.1/rc.2 pushes prove it).

**2026-09-09 — the v1 lineup is published.** `1.0.0-rc.3` of all six packages was pushed by
`publish.yml` through Trusted Publishing (owner `arion-orkeon`, the account holding the
`Orkeon` prefix reservation). The family finally has a page on nuget.org.

It took two attempts, and the first one is worth keeping: that run went the whole way —
build, tests, pack, closure gate, provenance attestation, GitHub Packages, OIDC key
exchange — and died on the last step, pushing nothing. The lineup glob
`artifacts/${id}.[0-9]*.nupkg` was quoted, so it reached `dotnet nuget push` as a literal,
and that CLI resolves wildcards through NuGet's own `PathResolver`, which understands `*`
and `?` and no character class. It answered `File does not exist` about a file sitting in
`artifacts/`. The GitHub Packages push in the same job never met it — `artifacts/*.nupkg`
is inside that dialect. Fixed by letting the shell expand the pattern.

A freshly pushed package is **not immediately downloadable**: NuGet.org validates it first,
and until that finishes its page answers 200 with "not been indexed" while
`v3-flatcontainer` still 404s. `Orkeon.Rag.Onnx.Model` sat there longest, which is expected
— it is the package embedding the int8 model weights. Nothing to do but wait; it is not a
failed push, and `--skip-duplicate` makes a re-push a no-op either way.

## Published to GitHub Packages

`publish.yml` (tag `v*`) packs `Orkeon.sln` and pushes **every packable project** to
**GitHub Packages** (`nuget.pkg.github.com/Orkeon`) with `--skip-duplicate`. That is the
NuGet.org lineup above **plus** the build-time and runner packages that stay off NuGet.org:
`Orkeon.ConsoleApp`, `Orkeon.Generators` and `Orkeon.Compliance.Vfs`:

| PackageId | Tool command | Source project |
|---|---|---|
| `Orkeon.ConsoleApp` | `orkeon-repl` | `src/apps/Orkeon.ConsoleApp` |
| `Orkeon.Scripting.Cli` | `orkeon` | `src/scripting/Orkeon.Scripting.Cli` |

## Installer archives (`release.yml`)

On a `v*` tag, `release.yml` builds every installer artifact, **smokes each onboarding
channel on a real runner** (Windows CLI zip, Windows service, Debian package, macOS
tarball), and only then attaches everything to the GitHub Release. The pipeline is
`installers → {smoke-windows, smoke-windows-service, smoke-deb, smoke-macos, msi} →
release`; behind those same five jobs, `runners-image` pushes the `orkeon-runners`
container image to GHCR in parallel with `release`, so a red smoke moves neither the
Release nor the `:latest` tag. `workflow_dispatch` runs
the same thing minus the publication (no tag, no Release to attach to). The retired
`orkeon-examples` runner is **no longer packaged** — the `orkeon` CLI replaces it
(`orkeon run crew.yaml` runs the `examples/` YAML crews; `orkeon run script.ork.ts` runs the
scripting DSL).

| Artifact | Built by | Contents | Runtime |
|---|---|---|---|
| `orkeon-<version>-<rid>.tar.gz` / `.zip` | `package-installers.sh` (default `--app-set full`) | every CLI launcher + the Orkeon Studio apps admitted by their RID filter (the WPF `orkeon-studio` is `win-x64`-only; the two TUIs ship for every RID) + one shared esbuild + the `deploy/` tree (systemd unit, SCM registration script, Dockerfile.host) | mixed: `orkeon`, `orkeon-host` and the Studio apps self-contained, the rest framework-dependent |
| `orkeon-cli-<version>-win-x64.zip` | `package-installers.sh --app-set cli --rids win-x64` | the `orkeon` CLI + `orkeon-studio` (WPF Orkeon Studio) + `install.ps1` | self-contained |
| `orkeon_<version>_amd64.deb` | `package-deb.sh` (reuses the `linux-x64` staging tree — one publish, two packages) | the `orkeon` CLI at `/usr/bin/orkeon` + the Studio TUIs at `/usr/bin/orkeon-studio-config` and `/usr/bin/orkeon-studio-run` | self-contained; `Depends` on system libraries only (libicu / libssl alternations), never on `dotnet-runtime-*` |
| `orkeon-<version>-win-x64.msi` | `build-msi.ps1` (WiX, per-user scope), harvesting the extracted CLI zip | the `orkeon` CLI + `orkeon-studio` (WPF, with an "Orkeon Studio" Start-menu shortcut), same pruned publish as the zip | self-contained |
| `orkeon-cli-<version>-osx-arm64.tar.gz` / `-osx-x64.tar.gz` | `package-installers.sh --app-set cli --rids osx-arm64 osx-x64` (cross-published from the ubuntu runner) | the `orkeon` CLI alone + `install.sh` (no Studio in V1 — the macOS channel stays CLI-only) | self-contained |
| `SHA256SUMS` | the `installers` job's packaging scripts (`package-deb.sh` refreshes its own line) | one line per artifact above **except the MSI** | — |
| `orkeon-host-<version>-win-x64.msi` | `build-msi-service.ps1` (WiX, per-machine scope), harvesting the extracted full zip | the self-contained `orkeon-host` publish alone, registered as the `Orkeon` service under `NT SERVICE\Orkeon` (no CLI, no wrapper, no `deploy/` — the package registers declaratively) | self-contained |
| `SHA256SUMS.msi` | `build-msi.ps1` then `build-msi-service.ps1`, in order, in the `msi` job | the two MSIs | — |

### The service host

`orkeon-host` ships inside the full archive (`--app-set full`), self-contained: a daemon supervised by systemd or the Windows SCM must not depend on a runtime someone may upgrade underneath it. It is **not** a dotnet tool — it is installed as a service, not invoked from a shell. On Windows it also ships as its own **per-machine MSI** (`orkeon-host-<version>-win-x64.msi`), a separate product from the per-user CLI MSI: the two coexist, and the package registers the service declaratively — same account, same paths, same recovery as the script channel.

Its deployment artifacts live in [`deploy/`](https://github.com/orkeon/orkeon/tree/main/deploy) and ship inside the full archive next to the daemon: a systemd unit (`Type=notify`, restart on failure — config errors exit 78 and do not loop, hardened), a PowerShell script registering it with the SCM, and a Dockerfile. None of the three carries a secret — the bot token and the API keys are named by environment variable in the configuration and provided by the machine, so a unit file or an image layer can be read by anyone without leaking anything.

Two checksum files rather than one: the ubuntu `installers` job writes `SHA256SUMS` before
the MSI exists — it is built later, on `windows-latest`. Each checksum file is generated by
the job that produced the artifact it covers.

**Blocking smokes.** `smoke-windows` (a `windows-latest` runner) installs
`orkeon-cli-*-win-x64.zip` and walks the onboarding chain on it; `smoke-deb` (a stock
`ubuntu-latest` image) installs the `.deb` through `apt`, walks the same chain, then removes
the package; `smoke-macos` (a `macos-latest`, Apple-silicon runner) extracts the `osx-arm64`
tarball, installs it with `install.sh` and walks the same chain before uninstalling; the `msi`
job runs its own `msiexec /i /qn` → `orkeon doctor --json` → `msiexec /x /qn` chain, asserting
the install directory, the ARP entry and the user `PATH` entry appear and then disappear.
`smoke-windows-service` installs the **full** win-x64 zip's service channel — virtual account,
`--working-dir` proven with a relative crew path, a refused configuration that stops without
looping and lands in the event log — and the `msi` job smokes the service MSI the same way,
plus a silent reinstall of itself. All of them install from the **job** artifacts, never from
the Release, so a broken payload is caught before anything is published — the `release` job
`needs` them all.

`smoke-macos` is also the only place the Gatekeeper and code-signing story is exercised: an
unsigned, quarantined or malformed native library (`libtree-sitter*.dylib`, onnxruntime, the
esbuild binary) is killed at load time, so it fails there rather than in a user's terminal.

The debian version string replaces `-` with `~` (`1.0.0-rc.1` → `orkeon_1.0.0~rc.1_amd64.deb`)
so a pre-release sorts before its final under `dpkg`.

The MSI's `ProductVersion` drops the suffix instead: Windows Installer versions carry only three
numeric fields, so `build-msi.ps1` truncates `1.0.0-rc.1` to `1.0.0` for the property that
`<MajorUpgrade>` actually compares. Nothing is silently lost — the full string survives in the
`.msi` filename (`orkeon-1.0.0-rc.1-win-x64.msi`) and in the `ARPCOMMENTS` property shown in
"Installed apps".

The `orkeon` CLI is distributed through **seven channels**:

| Channel | Artifact | Runtime | Audience |
|---|---|---|---|
| NuGet dotnet tool | `Orkeon.Scripting.Cli` (`PackAsTool`, command `orkeon`) | needs .NET 10 SDK (`dotnet tool install`) | .NET developers. Part of the NuGet.org lineup (PUB-25) — publishable since the package dropped from 262.5 MB to 137.6 MB (iOS/Android onnxruntime natives excluded); first push at `v1.0.0-rc.3` |
| Windows zip + `install.ps1` | `orkeon-cli-<version>-win-x64.zip` | self-contained | Windows onboarding — the recommended channel. Ships `orkeon-studio` (WPF Orkeon Studio) next to the CLI |
| Windows MSI (per-user) | `orkeon-<version>-win-x64.msi` | self-contained | Windows, double-click install and an "Installed apps" entry. Ships `orkeon-studio` with a Start-menu shortcut. One channel at a time: the MSI refuses to install over a zip install |
| Debian package | `orkeon_<version>_amd64.deb` | self-contained | Debian / Ubuntu onboarding — the recommended channel. Ships the `orkeon-studio-config` / `orkeon-studio-run` TUIs next to the CLI |
| macOS tarball + `install.sh` | `orkeon-cli-<version>-osx-arm64.tar.gz` / `-osx-x64.tar.gz` | self-contained | macOS onboarding today; `install.sh` clears the Gatekeeper quarantine attribute and ad-hoc re-signs the Mach-O files `codesign -v` rejects |
| Homebrew | the same osx tarballs, via `installers/homebrew/orkeon.rb` | self-contained | macOS, once the tap exists — **not published yet**, see below |
| Multi-app installer archive | `orkeon` / `orkeon-slim` launchers | `orkeon` self-contained, `orkeon-slim` framework-dependent | devs who also want the REPL, the service host or the Studio apps |

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
the one bundled esbuild. The remaining CLI
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
| `Orkeon.Generators` | Source generator — consumed at build time. GitHub Packages only. |
| `Orkeon.Compliance.Vfs` | Roslyn analyzer — consumed at build time. GitHub Packages only. |
| `Orkeon.Host` | `IsPackable=false` — ships only as the `orkeon-host` binary in the release archives. |
| `Orkeon.Studio.{Core,Config,Run,Wpf}` | `IsPackable=false` — ship only through the release installers (see [Orkeon Studio](../architecture/studio.md)). |

## How publication is wired

- All NuGet packing and pushing lives in **`publish.yml`** (tag `v*`): `dotnet pack Orkeon.sln`
  driven by `IsPackable`, the `scripts/check-package-closure.py` gate on
  the packed artifacts, a push of everything to **GitHub Packages** with `--skip-duplicate`
  (idempotent re-runs), then the **NuGet.org lineup** (the table above, `Orkeon` first) to
  **NuGet.org**. `ci.yml` validates (build + test) and packs nothing; `release.yml` builds the
  installer archives and the container image, no NuGet packing.
- NuGet.org auth is **Trusted Publishing (OIDC)** — no long-lived API key. A nuget.org
  policy (repository `Orkeon/orkeon`, workflow `publish.yml`) lets `NuGet/login` exchange
  the job's OIDC token for a short-lived key; the steps are gated on the **`NUGET_USER`
  repository variable** (the nuget.org profile owning the policy). Both are **set up and
  proven** — the rc.1/rc.2 pushes went through this path; if the variable ever disappears
  the steps emit a warning and no-op instead of failing the tag.
  Expanding the NuGet.org lineup is a maintainer decision recorded in this matrix first
  (and mirrored in the workflow's lineup list + the closure-gate arguments), never a
  workflow edit made in passing.
- `publish.yml` **refuses a tag that does not match the `src/Directory.Build.props` version**.
  Lesson from the 0.9.1-beta incident (see CHANGELOG 0.9.2-beta): the `v0.9.1-beta.rc*` tags
  re-packed the unchanged props version and `--skip-duplicate` silently skipped every push —
  a "release" that published nothing. The guard keeps `--skip-duplicate` honest.
- Version flows from `src/Directory.Build.props` (currently `1.0.0-rc.3`), the single source of truth: no project overrides it, and the publish workflow's tag guard refuses any `v*` tag that disagrees with it.
