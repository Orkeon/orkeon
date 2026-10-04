> 🇫🇷 [Version française](../fr/reference/publication-matrix.md)

# NuGet publication matrix

This file is the single source of truth for **which projects are published to NuGet**, so the
workflows (`ci.yml` validation, `publish.yml` pack + push on tag and the dev channel on green
`main`, `release.yml` installers)
never drift again (OSS-011 / R8.3).

> **Status — consolidated lineup implemented (PUB-25, 2026-09-01).** Distribution is one
> `Orkeon` package plus a handful of opt-ins, wired in `publish.yml` and guarded by the
> `scripts/check-package-closure.py` gate. Its first six packages went out with
> `1.0.0-rc.3` (2026-09-09); `Orkeon.Compliance.Vfs`, `Orkeon.Interop.AgentFramework` and
> `Orkeon.Hosting.Aspire` joined the lineup on 2026-09-11, so their first NuGet.org push is
> the first tag cut after that date — nine packages in all. Trusted Publishing to NuGet.org
> is **operational** — the discontinued per-layer packages `Orkeon.Domain` /
> `Orkeon.Application` / `Orkeon.Infrastructure` shipped `1.0.0-rc.1` (2026-08-18) and
> `1.0.0-rc.2` (2026-08-25) through it, and were unlisted on 2026-09-07 (see the owner
> actions below). Decision **D3** (the
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
| `Orkeon` | The umbrella package — the twelve assemblies of the core closure (`Orkeon.Domain`, `Orkeon.Application`, `Orkeon.Infrastructure`, `Orkeon.Constants.{Llm,FileSystem,Configuration,Protocol}`, `Orkeon.Tools.Abstractions`, `Orkeon.Analysis.Abstractions`, `Orkeon.Rag.Abstractions`, `Orkeon.Analysis`, `Orkeon.Rag`) embedded in one nupkg. One install = the complete framework: agents, crews, six orchestration modes, 16 LLM providers, 6 memory stores, RAG, RaggableTree. The Clean Architecture split stays a source-layout discipline, not a distribution contract. |
| `Orkeon.Tools` | The eight built-in tool families (`Analysis`, `Code`, `Data`, `Email`, `EventHub`, `FileSystem`, `Rag`, `Web`) in one nupkg. Separate from `Orkeon` **only for dependency weight**: the Data tools pull database drivers, PDF and spreadsheet libraries, the e-mail tools MailKit and MimeKit — dependencies a consumer who never uses them should not inherit. Depends on `Orkeon`. |
| `Orkeon.Rag.Onnx`, `Orkeon.Rag.Onnx.Model` | Opt-in ONNX cross-encoder reranker pair (runtime + embedded int8 weights) — pushed together; native onnxruntime payload. `Orkeon.Rag.Onnx` depends on `Orkeon`; `Orkeon.Rag.Onnx.Model` is dependency-free (embedded resources only) and is referenced alongside it. |
| `Orkeon.Tools.Embeddings.Local` | Local on-device embeddings (BGE-micro-v2 ONNX). Stays **outside the umbrella** because it carries a pre-release SmartComponents dependency from an archived upstream — putting it in `Orkeon` would force that pre-release on every consumer. Depends on `Orkeon`. |
| `Orkeon.Scripting.Cli` | The `orkeon` dotnet tool (`PackAsTool`; the PackageId is the install command — ADR-007). Publishable on NuGet.org since the iOS/Android onnxruntime natives a CLI tool can never load were excluded: 262.5 MB → 137.6 MB, under the nuget.org size limit. |
| `Orkeon.Compliance.Vfs` | The Roslyn analyzer that refuses direct `System.IO` in your own code (rules `ORKVFS001`–`ORKVFS007`, `analyzers/dotnet/cs`, `DevelopmentDependency`). Standalone by design: it depends on nothing from Orkeon and works in any C# project — add the `PackageReference`, build, and every `File.*`/`Directory.*` call is a diagnostic. In the NuGet.org lineup since 2026-09-11, so its first push there is the first tag cut after that date: `1.0.0-rc.3` reached GitHub Packages only (the NuGet.org push loop was a fixed list of six ids) and was inert anyway, compiled against a Roslyn newer than the SDK's compiler. |
| `Orkeon.Interop.AgentFramework` | The bridge to Microsoft Agent Framework, both ways: an Orkeon crew as a MAF `AIAgent` (`CrewAgent`), a MAF `AIAgent` as the brain (`WithAgentFrameworkAgent`) or as a tool (`WithAgentFrameworkTool`) of an Orkeon agent. Separate from `Orkeon` because the `Microsoft.Agents.AI.Abstractions` dependency is a consumer's choice. Depends on `Orkeon`. |
| `Orkeon.Hosting.Aspire` | The .NET Aspire hosting integration: `AddOrkeonHost` (the `orkeon-host` daemon) and `AddOrkeonCrewRun` (one `orkeon run`) as AppHost resources, `WithOrkeonModel` / `WithOrkeonSetting` for the `ORKEON_` environment, OTLP export wired so the dashboard reads the run. Separate from `Orkeon` because `Aspire.Hosting` is an AppHost's choice. Depends on `Orkeon`. |

### How the packaging projects are built

- The lineup nupkgs come from dedicated **packaging projects** under `src/packaging/` — six
  of them: the `Orkeon` and `Orkeon.Tools` umbrellas, plus the `Orkeon.Rag.Onnx.Package`,
  `Orkeon.Tools.Embeddings.Local.Package`, `Orkeon.Interop.AgentFramework.Package` and
  `Orkeon.Hosting.Aspire.Package` **wrappers**, which pack the four opt-in assemblies with a
  nuspec dependency on the `Orkeon` umbrella. The remaining lineup packages are packed
  straight from their own project: `Orkeon.Rag.Onnx.Model` (`src/rag/`), the `orkeon` tool
  (`src/scripting/Orkeon.Scripting.Cli`) and the analyzer (`src/analyzers/Orkeon.Compliance.Vfs`).
  The embedded library projects themselves
  are `IsPackable=false` and their source layout, namespaces and per-assembly PublicAPI
  freeze are untouched; the real opt-in projects keep normal `ProjectReference`s, so in-repo
  consumers are unaffected — only the wrappers carry the embed pattern below.
- Each packaging project references its embedded project(s) with `PrivateAssets="all"`
  (keeping them out of the nuspec dependency list) and packs their DLL+XML into `lib/`.
  Because `PrivateAssets="all"` also stops the embedded projects' external
  `PackageReference`s from flowing into the nuspec, the **union of those references is
  re-declared by hand** in the packaging csproj.
- `scripts/check-package-closure.py` (run by `publish.yml` right after the pack, on the tag
  and on the dev channel) fails the workflow if (1) a lineup package declares an `Orkeon.*`
  dependency outside the lineup — the NU1101 class of incident — (2) an umbrella's
  hand-declared externals drift from what its embedded projects actually require, or (3) an
  assembly an embedded one reaches transitively ships nowhere in the lineup. Its source half
  (no nupkg needed) also runs on every pull request through `scripts/check-doc-claims.py`,
  which additionally fails when a packable project is neither in the lineup nor
  GitHub-Packages-only, or when one of the hand-written copies of the lineup drifts.
- `publish.yml` pushes **`Orkeon` first**: the other lineup packages depend on it and NuGet
  does not order pushes, so pushing a dependent first would expose a package whose restore
  fails.

## Discontinued packages

The following PackageIds are **no longer packed** (`IsPackable=false`): `Orkeon.Domain`,
`Orkeon.Application`, `Orkeon.Infrastructure`, the five `Orkeon.Constants.*` satellites,
`Orkeon.Analysis`, `Orkeon.Analysis.Abstractions`, `Orkeon.Rag`, `Orkeon.Rag.Abstractions`,
`Orkeon.Tools.Abstractions` and the eight `Orkeon.Tools.<family>` projects, `Orkeon.Cli`,
`Orkeon.Cli.Abstractions`, `Orkeon.Cli.Commands.Scripting`, `Orkeon.Cli.TerminalGui`,
`Orkeon.Scripting`, `Orkeon.Hosting`, `Orkeon.Plugins`. Those that the `Orkeon` and
`Orkeon.Tools` umbrellas embed still ship — inside the umbrella's nupkg, not under their own id.

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
`Orkeon.ConsoleApp` and `Orkeon.Generators`:

| PackageId | Tool command | Source project |
|---|---|---|
| `Orkeon.ConsoleApp` | `orkeon-repl` | `src/apps/Orkeon.ConsoleApp` |
| `Orkeon.Scripting.Cli` | `orkeon` | `src/scripting/Orkeon.Scripting.Cli` |

### Dev channel: the latest `main`, between two tags

Once `ci.yml` is green on a push to `main`, the `publish-dev` job of `publish.yml` packs the
same projects as `<props version>.dev.<n>`, `n` being the number of that CI run — so
`1.0.0-rc.4.dev.412` is the commit CI run #412 validated — and pushes them to **GitHub
Packages only**: never NuGet.org, which can unlist a version but never delete it.
`scripts/prune-dev-packages.sh` then deletes the older dev builds, so the feed holds **every
tagged release plus the latest green `main`**, nothing in between. A tagged version is never
deleted; a version that is neither tagged nor a dev build is reported, not deleted — the
same script removes those in a one-off, reviewed run (`--include-untagged`, dry run first).

- SemVer puts `1.0.0-rc.4.dev.<n>` above `1.0.0-rc.4` and below the next rc, so
  `--prerelease` resolves the dev build. When the props version has no suffix (a stable
  release), the dev builds move to the next patch — `1.0.1-dev.<n>` after `1.0.0` — because
  `1.0.0-dev.<n>` would sort below the release.
- A dev version lives until the next green merge. A `PackageReference` pinned to one keeps
  restoring — NuGet reads the version as a minimum and takes the next build up, with warning
  NU1603 (an error under warnings-as-errors) — but exact pins break: a `dotnet tool` manifest,
  a `packages.lock.json` in locked mode. Float (`*-*`, and `dotnet restore --force-evaluate`
  to move to the newest) to follow the channel; pin a tagged version for anything durable.
- A dev build is not a release: no attestation, no SBOM, nothing on NuGet.org.
- Once the feed is a source, `--prerelease` and floating versions resolve dev builds for
  every Orkeon package, the NuGet.org ones included; `--version` pins a release.
- GitHub Packages requires a token even for a public repository: a personal access token
  (classic) with `read:packages`. Run the commands outside a clone of this repository —
  inside one, its `nuget.config` keeps nuget.org as the only source and you would get the
  latest tag instead.

Step by step — the token, both shells, checking what runs, going back to the releases:
[Follow `main`: the dev channel](../getting-started/three-ways-to-run-orkeon.md#follow-main-the-dev-channel).

```bash
dotnet nuget add source https://nuget.pkg.github.com/Orkeon/index.json \
  --name orkeon-github \
  --username <your-github-username> \
  --password <PAT-with-read:packages> --store-password-in-clear-text

dotnet tool install -g Orkeon.Scripting.Cli --prerelease   # installed already: dotnet tool update, same arguments
# in a project: <PackageReference Include="Orkeon" Version="*-*" />
```

## Installer archives (`release.yml`)

On a `v*` tag, `release.yml` builds every installer artifact, **smokes each onboarding
channel on a real runner** (Windows CLI zip, Windows service, Debian package, macOS
tarball), and only then attaches everything to the GitHub Release. The pipeline is
`installers → {smoke-windows, smoke-windows-service, smoke-deb, smoke-macos, msi} →
release → {verify-published, apt-publish → verify-apt}` (the last two are described in
[The apt repository](#the-apt-repository)); behind those same five jobs, `runners-image` pushes the `orkeon-runners`
container image to GHCR in parallel with `release`, so a red smoke moves neither the
Release nor the `:latest` tag. `workflow_dispatch` runs
the same thing minus the publication (no tag, no Release to attach to). A tag carrying a
pre-release segment (`v1.0.0-rc.4`) is published **as a prerelease**, so it never becomes the
repository's *latest* release; a bare `v1.0.0` does. The retired
`orkeon-examples` runner is **no longer packaged** — the `orkeon` CLI replaces it
(`orkeon run crew.yaml` runs the `examples/` YAML crews; `orkeon run script.ork.ts` runs the
scripting DSL).

| Artifact | Built by | Contents | Runtime |
|---|---|---|---|
| `orkeon-<version>-<rid>.tar.gz` / `.zip` | `package-installers.sh` (default `--app-set full`) | every launcher — `orkeon`, `orkeon-slim`, `orkeon-repl`, `orkeon-host` — + the Orkeon Studio apps admitted by their RID filter (the WPF `orkeon-studio` is `win-x64`-only; the two TUIs ship for every RID) + one shared esbuild + the `deploy/` tree (systemd unit, SCM registration script, Dockerfile.host) | mixed: `orkeon`, `orkeon-host` and the Studio apps self-contained, the rest framework-dependent |
| `orkeon-cli-<version>-win-x64.zip` | `package-installers.sh --app-set cli --rids win-x64` | the `orkeon` CLI + `orkeon-studio` (WPF Orkeon Studio) + `install.ps1` | self-contained |
| `orkeon_<version>_amd64.deb` / `orkeon_<version>_arm64.deb` | `package-deb.sh --arch amd64\|arm64` (reuses the `linux-x64` and `linux-arm64` staging trees — one publish, two packages per architecture) | the `orkeon` CLI at `/usr/bin/orkeon` + the Studio TUIs at `/usr/bin/orkeon-studio-config` and `/usr/bin/orkeon-studio-run` | self-contained; `Depends` on system libraries only (`libicu78` down to `libicu70`, `libssl3t64 \| libssl3`, `libc6 (>= 2.34)`…, so Debian 12/13 and Ubuntu 22.04 to 26.04), never on `dotnet-runtime-*`; `Recommends: orkeon-archive-keyring`; ships `md5sums` (`dpkg -V orkeon`); byte-identical across two builds of the same commit (`SOURCE_DATE_EPOCH`) |
| `orkeon-archive-keyring_<YYYY.MM.DD>_all.deb` | `package-keyring-deb.sh`, once per keyring version (a date, such as `2026.10.04`, from `installers/apt/keyring.version`); later Releases attach the bytes already published, never a rebuild | `/usr/share/keyrings/orkeon-archive-keyring.gpg`, the public key of the [apt repository](#the-apt-repository) | — |
| `orkeon-<version>-win-x64.msi` | `build-msi.ps1` (WiX, per-user scope), harvesting the extracted CLI zip | the `orkeon` CLI + `orkeon-studio` (WPF, with an "Orkeon Studio" Start-menu shortcut), same pruned publish as the zip | self-contained |
| `orkeon-cli-<version>-osx-arm64.tar.gz` / `-osx-x64.tar.gz` | `package-installers.sh --app-set cli --rids osx-arm64 osx-x64` (cross-published from the ubuntu runner) | the `orkeon` CLI alone + `install.sh` (no Studio in V1 — the macOS channel stays CLI-only) | self-contained |
| `SHA256SUMS` | the `installers` job's packaging scripts (`package-deb.sh` refreshes its own line) | one line per artifact above **except the MSI**, plus the SBOM (`orkeon-<version>.sbom.cdx.json`) | — |
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
`ubuntu-latest` image for amd64, an `ubuntu-24.04-arm` runner for arm64 — each entry refuses a
runner of the other architecture) installs the `.deb` through `apt`, checks `dpkg -V orkeon`
is silent, walks the same chain, then removes the package — the linux-x64 tarball smoke runs
on the amd64 entry; `smoke-macos` (a `macos-latest`, Apple-silicon runner) extracts the `osx-arm64`
tarball, installs it with `install.sh` and walks the same chain before uninstalling; the `msi`
job runs its own `msiexec /i /qn` → `orkeon doctor --json` → `msiexec /x /qn` chain, asserting
the install directory, the ARP entry and the user `PATH` entry appear and then disappear.
`smoke-windows-service` installs the **full** win-x64 zip's service channel — virtual account,
`--working-dir` proven with a relative crew path, a refused configuration that stops without
looping and lands in the event log — and the `msi` job smokes the service MSI the same way,
plus a silent reinstall of itself. All of them install from the **job** artifacts, never from
the Release, so a broken payload is caught before anything is published — the `release` job
`needs` them all.

**After publication.** `release.yml` calls `release-verify.yml` in its `verify-published` job,
right after the `release` job of the same run (and it can be run on demand for a given tag). A
`release: published` trigger would never fire: a Release created with the workflow's
`GITHUB_TOKEN` starts no other workflow. It downloads the assets *from the Release page*,
checks that every published asset has exactly one line in `SHA256SUMS` or `SHA256SUMS.msi`
and every line names a published asset, verifies every checksum with no line skipped, and
replays the onboarding smokes of both `.deb` (amd64, and arm64 on an ARM runner) and of the
`osx-arm64` tarball on them — catching an asset that an upload, a replace or a tamper made
different from what the smokes above installed. Run by hand on `v1.0.0-rc.3` or
`v1.0.0-rc.4`, it fails by design: those Releases predate the naming rule below, and carry no
arm64 package.

`smoke-macos` is also the only place the Gatekeeper and code-signing story is exercised: an
unsigned, quarantined or malformed native library (`libtree-sitter*.dylib`, onnxruntime, the
esbuild binary) is killed at load time, so it fails there rather than in a user's terminal.

The `.deb` file name carries the tag's version (`orkeon_1.0.0-rc.4_amd64.deb`), while the
package's `Version:` field replaces `-` with `~` (`1.0.0~rc.4`) so a pre-release sorts before
its final under `dpkg`. The two differ on purpose: GitHub rewrites `~` in an uploaded asset
name, and apt reads the file name from its index, never from the package. No asset name
carries a character outside `[A-Za-z0-9._-]` — `scripts/check-release-assets.sh` fails the
`installers` job, and the `release` job before publishing, otherwise — so `SHA256SUMS` lists every asset under the name it is published with. (The `1.0.0-rc.3` and
`1.0.0-rc.4` Releases predate the rule: their `.deb` was published as
`orkeon_1.0.0.rc.N_amd64.deb` while their `SHA256SUMS` names it with `~`.)

The MSI's `ProductVersion` drops the suffix instead: Windows Installer versions carry only three
numeric fields, so `build-msi.ps1` truncates `1.0.0-rc.1` to `1.0.0` for the property that
`<MajorUpgrade>` actually compares. Nothing is silently lost — the full string survives in the
`.msi` filename (`orkeon-1.0.0-rc.1-win-x64.msi`) and in the `ARPCOMMENTS` property shown in
"Installed apps".

The `orkeon` CLI is distributed through **eight channels**:

| Channel | Artifact | Runtime | Audience |
|---|---|---|---|
| NuGet dotnet tool | `Orkeon.Scripting.Cli` (`PackAsTool`, command `orkeon`) | needs .NET 10 SDK (`dotnet tool install`) | .NET developers. Part of the NuGet.org lineup (PUB-25) — publishable since the package dropped from 262.5 MB to 137.6 MB (iOS/Android onnxruntime natives excluded); first push at `v1.0.0-rc.3` |
| Windows zip + `install.ps1` | `orkeon-cli-<version>-win-x64.zip` | self-contained | Windows onboarding — the recommended channel. Ships `orkeon-studio` (WPF Orkeon Studio) next to the CLI |
| Windows MSI (per-user) | `orkeon-<version>-win-x64.msi` | self-contained | Windows, double-click install and an "Installed apps" entry. Ships `orkeon-studio` with a Start-menu shortcut. One channel at a time: the MSI refuses to install over a zip install |
| Debian / Ubuntu apt repository | the `.deb` packages below, indexed on the `apt` branch (channels `stable`, `rc`, `dev`) | self-contained | Debian / Ubuntu, amd64 and arm64 — the recommended channel: `apt install`, `apt upgrade`. See [The apt repository](#the-apt-repository) |
| Debian package | `orkeon_<version>_amd64.deb` / `_arm64.deb` | self-contained | Debian / Ubuntu without the repository (one version, no updates). Ships the `orkeon-studio-config` / `orkeon-studio-run` TUIs next to the CLI |
| macOS tarball + `install.sh` | `orkeon-cli-<version>-osx-arm64.tar.gz` / `-osx-x64.tar.gz` | self-contained | macOS onboarding today; `install.sh` clears the Gatekeeper quarantine attribute and ad-hoc re-signs the Mach-O files `codesign -v` rejects |
| Homebrew | the same osx tarballs, via `installers/homebrew/orkeon.rb` | self-contained | macOS, once the tap exists — **not published yet**, see below |
| Multi-app installer archive | `orkeon` / `orkeon-slim` launchers | `orkeon` self-contained, `orkeon-slim` framework-dependent | devs who also want the REPL, the service host or the Studio apps |

**Homebrew — formula in the repository, tap not yet created.** `installers/homebrew/orkeon.rb`
is a binary formula: it downloads the osx tarball matching the machine's architecture
(`on_arm` / `on_intel`), installs the payload under the Cellar's `libexec`, and writes a
`bin/orkeon` wrapper that points `ORKEON_ESBUILD_PATH` at the bundled esbuild — the same
contract as `wrapper.sh.tmpl`. Its `test do` block runs `orkeon doctor` rather than
`orkeon --version`: doctor also runs the bundled esbuild and checks that the native libraries
are in place, and an unconfigured LLM is only a warning there.

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

### The apt repository

Debian and Ubuntu users add one source and then manage Orkeon with apt — the user side is
[Install with apt](../guides/install-with-apt.md). The repository lives in this GitHub
repository, in two halves:

- **The packages stay Release assets** — the `.deb` files `release.yml` already builds,
  smokes and attests. Nothing is copied elsewhere.
- **The signed index lives on the orphan `apt` branch**: one directory per channel
  (`InRelease`, `Release`, `Release.gpg`, `Packages`, `Packages.gz`, `by-hash/SHA256/…`), plus
  the binary keyring `orkeon-archive-keyring.gpg` at its root for the first setup. Only the
  workflows push to it, one commit per publication; a ruleset forbids its deletion and any
  force-push. It holds no `.deb`, so it weighs a few kilobytes.

The source reads `URIs: https://github.com/Orkeon/orkeon/` and `Suites: raw/apt/<channel>/` — a
"flat" repository. apt fetches the index at `…/raw/apt/<channel>/`, which GitHub redirects to
`raw.githubusercontent.com`, and resolves each package's `Filename:` —
`releases/download/<tag>/<asset>` — against the same root, so the download lands on the
Release asset. The cost is GitHub's rate limit on anonymous raw traffic (`429`): `apt update`
treats it as a warning, a Docker build retries.

| Channel | Directory | Receives |
|---|---|---|
| `stable` | `stable/` | every `v*` tag without a pre-release segment — the same test that sets `prerelease:` on the Release. Empty until a final version is published |
| `rc` | `rc/` | every `v*` tag, so every package of `stable` is in `rc` too |
| `dev` | `dev/` | the `orkeon` package of every green push to `main`, versioned `<props version with ~>.dev.<n>` (`1.0.0~rc.4.dev.<n>`); its assets sit on the single fixed-tag prerelease `apt-dev`; only the three latest builds are kept. Never published to NuGet.org, never attested as a release |

Each channel's `Release` file keeps `Origin: Orkeon` and `Label: Orkeon` for ever (a change
makes every machine confirm it), sets `Suite:` to the source path without its trailing `/` and
no `Codename` (otherwise apt warns "Conflicting distribution"), declares
`Architectures: amd64 arm64` and `Acquire-By-Hash: yes`, lists SHA-256 digests only, carries a
strictly increasing `Date` (apt ignores an `InRelease` older than the one it has) and no
`Valid-Until`. The index is generated by `apt-ftparchive`; the `by-hash` files are pruned by the
publishing scripts, keeping at least three generations.

**History and immutability.** A channel lists **every** version still attached to a Release, so
`apt install orkeon=<version>` can always go back. A stanza, once published, never changes its
digest for the same package, version and architecture: the generator refuses. `release.yml`
never rewrites the assets of a published tag — it creates the Release as a draft, attaches the
assets, then publishes it, and Releases are immutable — because a re-uploaded asset would make
every machine fail with "Hash Sum mismatch".

**The chain on a tag.** `release` publishes the Release; `apt-publish` (GitHub environment
`apt-signing`, the only place that holds the signing subkey, as the `APT_SIGNING_KEY` and
`APT_SIGNING_PASSPHRASE` secrets, deployable from `v*` tags only) adds the new packages to
`rc` — and to `stable` for a final version —, signs the index and pushes the `apt` branch;
`verify-apt` then replays the installation block of the guide **word for word**, in fresh
Debian 12 and 13 and Ubuntu 22.04, 24.04 and 26.04 containers, on amd64 and arm64: install,
upgrade from the previous version, removal in the documented order. It retries while raw
still serves the previous index. A weekly check compares the index with the assets, the key's
expiry with the 180-day threshold, and raw's availability.

**Maintenance outside a tag** goes through `apt-maintenance.yml` (`workflow_dispatch`, run
**from a tag** so the environment rule admits it), with the same scripts, environment and
concurrency group as `apt-publish`: `resign` signs both channels again with a newer `Date`;
`yank <package>=<version>[/<arch>]` withdraws a stanza and signs again; `seed <tag>…` indexes
assets published before the repository existed.

#### Procedure: extend or rotate the signing subkey

The subkey is valid two years and is extended **at least six months** before it expires — CI
fails below 180 days. An expired key breaks `apt update` on every machine, with no fallback.

1. On the offline machine, extend the subkey
   (`gpg --quick-set-expire <primary fingerprint> 2y <subkey fingerprint>`) — or, to rotate,
   add a new one (`gpg --quick-add-key <primary fingerprint> ed25519 sign 2y`).
2. Export the public certificate, replace `installers/apt/orkeon-archive-keyring.asc`, change
   `installers/apt/keyring.version`, and update the keyring SHA-256 in `SECURITY.md`,
   `SECURITY.fr.md` and both installation pages (the CI guard checks they agree).
3. The next Release publishes the new `orkeon-archive-keyring`; users receive it with
   `apt upgrade`. An extension stops here; run `apt-maintenance.yml` in `resign` mode if the
   index should be signed again before the next tag.
4. For a rotation only, **one Release later**: the maintainer replaces `APT_SIGNING_KEY` with
   the new subkey, exported alone (`gpg --export-secret-subkeys <subkey fingerprint>!`). The
   old subkey expires, or is revoked.

#### Procedure: withdraw a version (yank)

1. Run `apt-maintenance.yml` in `yank` mode, from a tag, naming
   `<package>=<version>[/<arch>]`.
2. Check that the channel's `InRelease` on the `apt` branch carries a newer `Date` and no
   longer lists the version; after an `apt update`, `apt-cache policy orkeon` no longer shows it.
3. Only then, and only if needed, delete the asset from its Release. Any purge of Release
   assets starts with this procedure — an indexed asset that disappears turns into a 404 for
   every user.

#### Procedure: revoke the key

1. Publish the revocation certificate (kept offline since the key was created) in
   `SECURITY.md`, `SECURITY.fr.md` and the installation pages.
2. Freeze publication: no tag, no `apt-maintenance.yml` run, until the new key is in place.
3. Create a new key and a new keyring, publish their fingerprint and SHA-256 as in the
   extension procedure, and sign the index with the new subkey. Users must download the new
   key themselves (the first lines of the installation block) — the only case where they act.

#### Procedure: the key has expired

Users see `EXPKEYSIG` on every `apt update`, and the channel stops updating. Extend the subkey
(procedure above, step 1), publish the certificate and a new `orkeon-archive-keyring` version,
then run `apt-maintenance.yml` in `resign` mode. A machine whose keyring is older than the
extension cannot verify the new index: it reinstalls the keyring by the first lines of the
installation block, or installs the new `orkeon-archive-keyring_<YYYY.MM.DD>_all.deb` downloaded
from its Release (`sudo apt install ./orkeon-archive-keyring_<YYYY.MM.DD>_all.deb`).

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
| `Orkeon.Host` | `IsPackable=false` — ships only as the `orkeon-host` binary in the release archives. |
| `Orkeon.Studio.{Core,Config,Run,Wpf}` | `IsPackable=false` — ship only through the release installers (see [Orkeon Studio](../architecture/studio.md)). |

## How publication is wired

- All NuGet packing and pushing lives in **`publish.yml`** (tag `v*`): the tag/version guard
  and `scripts/check-release-readiness.py` (CHANGELOG section cut, PublicAPI shipped), a
  Release build and the unit + fast tests, `dotnet pack Orkeon.sln`
  driven by `IsPackable`, the `scripts/check-package-closure.py` gate on
  the packed artifacts, a push of everything to **GitHub Packages** with `--skip-duplicate`
  (idempotent re-runs), then the **NuGet.org lineup** (the table above, `Orkeon` first) to
  **NuGet.org**. Its `publish-dev` job also packs every green push to `main`, for GitHub
  Packages only (see *Dev channel* above). `ci.yml` validates (build + test) and packs nothing;
  `release.yml` builds the installer archives and the container image, no NuGet packing.
- NuGet.org auth is **Trusted Publishing (OIDC)** — no long-lived API key. A nuget.org
  policy (repository `Orkeon/orkeon`, workflow `publish.yml`) lets `NuGet/login` exchange
  the job's OIDC token for a short-lived key; the steps are gated on the **`NUGET_USER`
  repository variable** (the nuget.org profile owning the policy). Both are **set up and
  proven** — the rc.1/rc.2 pushes went through this path; if the variable ever disappears
  the steps emit a warning and no-op instead of failing the tag.
  Expanding the NuGet.org lineup is a maintainer decision recorded in this matrix first
  (and mirrored in `check-package-closure.py`'s `LINEUP`, the workflow's push loop, the
  closure-gate arguments and CONTRIBUTING's release process — `check-doc-claims.py` fails
  when any copy disagrees), never a workflow edit made in passing.
- `publish.yml` **refuses a tag that does not match the `src/Directory.Build.props` version**.
  Lesson from the 0.9.1-beta incident (see CHANGELOG 0.9.2-beta): the `v0.9.1-beta.rc*` tags
  re-packed the unchanged props version and `--skip-duplicate` silently skipped every push —
  a "release" that published nothing. The guard keeps `--skip-duplicate` honest.
- **Provenance and SBOM.** Both workflows attest what they publish with
  `actions/attest-build-provenance` (SLSA v1, signed by GitHub's Sigstore instance):
  `publish.yml` every `*.nupkg` of a tag (dev-channel builds are not attested), `release.yml`
  every archive, `.deb` and MSI. The `orkeon-runners` image `release.yml` pushes to GHCR is
  not attested. Both generate a
  **CycloneDX SBOM** of `Orkeon.sln` right after the build (`CycloneDX` dotnet tool, pinned
  version, no third-party action) — `orkeon-<version>.sbom.cdx.json` — and cover it with the
  **same** attestation: a release asset next to the archives and a line in `SHA256SUMS` in
  `release.yml`, a run artefact named `sbom` in `publish.yml`. How to verify any of it, and
  why a nuget.org download must shed its repository signature first, is in
  [Verify what you install](../guides/verify-what-you-install.md).
- Version flows from `src/Directory.Build.props` (currently `1.0.0-rc.4`), the single source of truth: no project overrides it, and the publish workflow's tag guard refuses any `v*` tag that disagrees with it. The dev channel derives its `<version>.dev.<n>` from it.
