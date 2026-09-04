> 🇫🇷 [Version française](../fr/getting-started/three-ways-to-run-orkeon.md)

# Three ways to run Orkeon

> **See also**: [Run your first example](./run-your-first-example.md) · [Overview](./overview.md) · [Back to the index](../INDEX.md)

Orkeon crews can be launched three ways. Pick the one that matches how much you
want to install:

| Way | Prerequisites | Time to first run | Best for |
|---|---|---|---|
| **1. From source** | .NET SDK ≥ 10.0.300, git clone | ~5 min (+ build) | Contributors, reading/modifying code, running any of the 100+ bundled examples |
| **2. Release binary** | Nothing for the CLI packages (Windows zip/MSI, Debian `.deb`) — they bundle the runtime; .NET 10 **runtime** for the extra launchers in the multi-app archive | ~2 min | Running examples and showcases without a source checkout |
| **3. Container** | Docker | ~1 min (after image pull) | CI, reproducible runs, no local .NET at all |

All three drive the same **`orkeon` CLI** and accept the same flags. The crew
config is the positional argument to `orkeon run <config>`; the option flags (`--settings`, `-v`,
`--mount`, `--var`, `--llm-log`, …) are documented once, in detail, in
[Run your first example](./run-your-first-example.md#every-flag-explained).

> **Prefer a window to a prompt?** The Windows and Linux release packages also
> carry **[Orkeon Studio](#orkeon-studio-the-graphical-way-in)** — a graphical
> front-end over that same CLI, installed alongside it. It is not a fourth way of
> running a crew: it edits the same configuration file and shells out to the same
> `orkeon run`.

---

## 1. From source

Clone, build, and run any `config.yaml` under `examples/` through the `orkeon`
CLI project:

```bash
git clone https://github.com/Orkeon/orkeon.git
cd orkeon
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run \
  examples/01-enterprise/01-research-assistant/config.yaml \
  --settings examples/appsettings/appsettings.deepseek.local.json
```

This is the most flexible path — it can run **every** example and picks up your
local code changes. Full walkthrough, LLM-profile setup, and
troubleshooting: [Run your first example](./run-your-first-example.md).

---

## 2. Release binary

Each [GitHub Release](https://github.com/Orkeon/orkeon/releases) attaches a
**per-platform CLI package**, the historical **multi-app archives**, and a NuGet
[dotnet tool](https://www.nuget.org/):

| Artifact | What's inside | Runtime prerequisite | Best for |
|---|---|---|---|
| **`orkeon-cli-<version>-win-x64.zip`** | the `orkeon` CLI + **Orkeon Studio** (`orkeon-studio`, the desktop app) + `install.ps1` | none — self-contained | **Windows: the recommended download** |
| **`orkeon-<version>-win-x64.msi`** | the same two, per-user MSI, with an "Orkeon Studio" Start-menu shortcut | none — self-contained | Windows, if you'd rather double-click and get an "Installed apps" entry |
| **`orkeon_<version>_amd64.deb`** | the `orkeon` CLI at `/usr/bin/orkeon` + the two **Orkeon Studio** terminal apps | none — self-contained | **Debian / Ubuntu: the recommended download** |
| **`orkeon-cli-<version>-osx-arm64.tar.gz`** / **`-osx-x64.tar.gz`** | the `orkeon` CLI alone + `install.sh` (no Studio in V1 — the macOS onboarding channel stays CLI-only) | none — self-contained | **macOS**, Apple Silicon and Intel respectively |
| **`orkeon-<version>-<rid>.tar.gz`** / **`.zip`** | **every** launcher (`orkeon`, `orkeon-repl`, `orkeon-host`…) + the Studio apps their platform supports + `install.sh` / `install.ps1` | mixed — see the command table below | The REPL and the service host |
| **`dotnet tool install --global Orkeon.Scripting.Cli`** | the `orkeon` CLI | .NET 10 **SDK** | Getting just the CLI on a dev box that already builds .NET |

`<rid>` is `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64` (`.tar.gz`) or
`win-x64` (`.zip`). `SHA256SUMS` covers every artifact of the release except the MSI, which
has its own `SHA256SUMS.msi` (each file is produced by the CI job that built the artifact).

The multi-app archive is the only one that carries more than the CLI:

| Command | What it runs | Runtime |
|---|---|---|
| `orkeon` | **The main CLI and default entry point** — `orkeon run <config.yaml>` for any non-finance example, or `orkeon run script.ork.ts` for the scripting DSL | self-contained |
| `orkeon-slim` | The same CLI, framework-dependent and much smaller | needs .NET 10 |
| `orkeon-repl` | Full interactive REPL console (all built-in tools, code analysis, local embeddings) | needs .NET 10 |
| `orkeon-host` | The service host daemon — registers crews and serves them long-running (systemd unit, Windows service via the bundled script or its own per-machine MSI, chat gateway, Discord channel; see [the service host](../architecture/service-host.md)) | self-contained |
| `orkeon-studio` | **Orkeon Studio**, the desktop app — Windows archives only (see [below](#orkeon-studio-the-graphical-way-in)) | self-contained |
| `orkeon-studio-config` / `orkeon-studio-run` | **Orkeon Studio** in the terminal: settings editor and crew launcher | self-contained |

> **Runtime prerequisite, in one line.** The CLI packages (zip, MSI, `.deb`), the
> `orkeon` launcher and the Orkeon Studio apps bundle their
> own runtime and need no .NET install at all. Everything else in the multi-app
> archive — and the dotnet tool — needs the
> [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (verify
> with `dotnet --list-runtimes`). `install.sh` and `install.ps1` detect the case
> and print the install commands for your platform; they never install a runtime
> for you.

### Windows

Two channels, both per-user (no administrator rights, nothing written outside
your profile). **Install one channel at a time** — the MSI refuses to install
over a ZIP install, so uninstall the other one first if you switch.

```powershell
# Channel A — ZIP + install.ps1 (recommended)
Expand-Archive orkeon-cli-<version>-win-x64.zip -DestinationPath .
cd orkeon-cli-<version>-win-x64
.\install.ps1     # -> %LOCALAPPDATA%\Programs\Orkeon, user PATH, "Installed apps" entry
.\install.ps1 -Uninstall
```

```powershell
# Channel B — MSI (double-click, or silently)
msiexec /i orkeon-<version>-win-x64.msi          # same install dir, same PATH entry
msiexec /x orkeon-<version>-win-x64.msi /qn      # uninstall
```

The MSI is **not code-signed**, so SmartScreen shows a publisher warning on
first run — "More info" → "Run anyway", or use the ZIP channel.

Either way, your configuration lives in `%APPDATA%\Orkeon\appsettings.json`, and
**both uninstallers leave it alone**. (An `appsettings.json` left behind in an
install directory by an earlier install is migrated there on upgrade.)

### Linux

The **`.deb` is the recommended channel** on Debian and Ubuntu — self-contained,
so it never pulls a `dotnet-runtime` package or a Microsoft repository:

```bash
sudo apt install ./orkeon_<version>_amd64.deb   # installs /usr/bin/orkeon
sudo apt remove orkeon
```

The multi-app `tar.gz` + `install.sh` is the per-user alternative (and the only
option for `linux-arm64`, or when you want the REPL and the service host):

```bash
tar -xzf orkeon-<version>-linux-x64.tar.gz
cd orkeon-<version>-linux-x64
./install.sh                 # installs to ~/.local; --prefix /usr/local for system-wide
./install.sh --modify-path   # also appends ~/.local/bin to your shell rc files
./install.sh --uninstall
```

Because that archive carries framework-dependent launchers, `install.sh` checks
for a `Microsoft.NETCore.App 10.x` runtime (on the `PATH` or under `DOTNET_ROOT`)
and, if it finds none, prints the exact commands for your distribution —
`sudo apt install dotnet-runtime-10.0` on Ubuntu 25.10+, the
`packages.microsoft.com` repository registration on Debian and Ubuntu LTS, or
`dotnet-install.sh --runtime dotnet --channel 10.0` under `$HOME` when you have
no sudo. The warning never blocks the install: the files land either way, and
`orkeon` itself still runs, being self-contained.

### macOS

No prerequisite either way: the macOS CLI tarballs are self-contained, so no
.NET install is involved.

**Homebrew** will be the recommended channel. The formula lives in the
repository at `installers/homebrew/orkeon.rb`, but the `Orkeon/homebrew-tap`
repository **will only be published with the first tagged release** — until
then the command below does not resolve, and the tar.gz route is the way in:

```bash
brew tap orkeon/tap        # once the tap is published
brew install orkeon
```

**Tarball + `install.sh`** works today. Pick `osx-arm64` on Apple Silicon,
`osx-x64` on Intel:

```bash
tar -xzf orkeon-cli-<version>-osx-arm64.tar.gz
cd orkeon-cli-<version>-osx-arm64
./install.sh                 # installs to ~/.local; --modify-path to update your shell rc
./install.sh --uninstall
```

> **Gatekeeper.** Orkeon is not signed with an Apple Developer ID, and macOS
> tags anything downloaded through a browser with `com.apple.quarantine` —
> which is what produces *"cannot be opened because the developer cannot be
> verified"*. `install.sh` handles both halves of that on its own: it clears the
> quarantine attribute from the installed tree, and it ad-hoc re-signs only the
> bundled Mach-O files that `codesign -v` actually rejects (a valid publisher
> signature is never overwritten). Downloading with `curl` sets no quarantine
> attribute in the first place, and `brew` strips it itself. If a command is
> still killed or refused afterwards, the manual escape hatch is
> `xattr -dr com.apple.quarantine ~/.local/lib/orkeon`.

The multi-app `tar.gz` (REPL, service host) is available for
both macOS architectures too, and needs the .NET 10 runtime for the
framework-dependent launchers it carries — `install.sh` prints the
[download link](https://dotnet.microsoft.com/download/dotnet/10.0) when it is
missing.

### First run

Open a **new** terminal (so the PATH change is picked up), then:

```bash
orkeon init      # writes the global config: which LLM, which model, which endpoint
orkeon doctor    # 9 checks: runtime, config, LLM reachability, esbuild, grammars, …
orkeon run path/to/crew.yaml
```

`orkeon init` is a 5-choice wizard — `ollama`, `docker-model-runner`, `openai`,
`custom`, or `none` — and writes `%APPDATA%\Orkeon\appsettings.json` on Windows,
`~/.config/Orkeon/appsettings.json` on Linux and macOS. It is scriptable end to
end (`--provider`, `--base-url`, `--model`, `--api-key-env`, `--path`, `--force`,
`--no-probe`), which is what CI uses. `orkeon doctor` prints ✅ / ⚠️ / ❌ per
check, exits `1` as soon as one check fails, and takes `--json` for scripts.

> **No LLM configured?** Orkeon does not fail and does not go quiet: it warns on
> stderr — *"No `Llm` section configured — falling back to the echo provider …
> Run `orkeon init` …"* — and runs the crew against the **echo provider**, which
> replays the prompt instead of answering it. That fallback is what makes the
> scripting demos runnable with no key and no server; it is never a working LLM.
> If you see that warning when you expected a real model, run `orkeon init`, then
> `orkeon doctor` to confirm the endpoint is reachable.

### Orkeon Studio, the graphical way in

The Windows and Linux packages install **Orkeon Studio** next to the CLI. It is a
front-end, not a second product: it edits the very `appsettings.json` that
`orkeon init` writes, and it launches crews by running the co-installed `orkeon`
binary. Anything it does can be done from the terminal, and anything it writes is
readable by the CLI — you can switch between the two at any point.

| Platform | Command | Ships in | What it gives you |
|---|---|---|---|
| **Windows** | `orkeon-studio` | the `win-x64` zip and the MSI | A desktop window with a sidebar of screens — settings editor (presets, sections, mounts, raw JSON, diagnostic) and crew launcher (run + history) — with light/dark theme, EN/FR language toggle and a guided tour. The MSI also registers an **"Orkeon Studio" Start-menu shortcut**, so it takes no terminal at all to start |
| **Linux** | `orkeon-studio-config` | the `.deb` and the linux archives | A full-screen terminal editor for the settings file: provider presets, model and endpoint, and the VFS mount table |
| **Linux** | `orkeon-studio-run` | the `.deb` and the linux archives | Pick a target (a `config.yaml`, a crew directory, or a `.ork.ts` script), set the run options — including `--validate` for a dry run — then watch the output live and cancel if you need to |
| **macOS** | — | — | Not in V1 on the onboarding channel: the `orkeon-cli-*-osx-*` tarballs and Homebrew ship the CLI alone. The multi-app `orkeon-<version>-osx-*` archives do carry the two terminal apps (only the WPF app has a RID filter), untested on macOS in V1 |

```bash
orkeon-studio-config    # write ~/.config/Orkeon/appsettings.json without the wizard
orkeon-studio-run       # choose a crew, run it, watch it
```

On Windows, either double-click **Orkeon Studio** in the Start menu (MSI channel)
or run `orkeon-studio` from a terminal — the same window either way. Both terminal
apps also take `--version` and `--help` and print them without opening a
full-screen interface, which is what makes them scriptable and CI-checkable.

### Run

The CLI resolves LLM settings the same way as from source. `orkeon init` covers
the common case; pass `--settings` to point at a specific profile instead (see
the [profile matrix](./run-your-first-example.md#3-choose-an-llm-profile)):

```bash
orkeon run path/to/config.yaml \
  --settings path/to/appsettings.local.json \
  --mount ./out:/output:rw
```

Settings are resolved in order: `--settings`, then `appsettings.json` sitting
next to the config file, then — walking up the parent directories — an
`appsettings/appsettings.json` sub-directory at each level (the shared examples
profile matrix; `_shared/appsettings.json` stays a deprecated fallback), then the
global per-user file written by `orkeon init`, then `ORKEON_*` environment
variables alone.

**A crew can also be a directory**. Point `orkeon run` at a
folder holding a multi-file crew — `config.yaml` for the crew settings, one agent
per file under `agents/`, one task per file under `tasks/`, each file name being
the entity id — and it loads exactly like a single YAML file. The legacy flat
triplet (`crew.yaml` + `agents.yaml` + `tasks.yaml`) is accepted too, and every
option behaves identically on a directory (`--settings`, `-V/--var`,
`--initial-context`, `--mount`, `--validate`, `--verbose`, `--llm-log`):

```bash
orkeon run examples/crew-multifile --validate
# VALIDATION OK: …/examples/crew-multifile (agents=2, tasks=2, tools resolved=0)
```

A directory holding both a YAML layout and a scripting entry point — any `*.ork.ts`
or `*.ork.js` sitting directly in it, whatever the file is called — is refused, naming
both candidates, and so is a directory with no recognized layout: Orkeon never guesses
which one you meant. See
[YAML and builders](./yaml-and-builders.md) for the layout itself.

You can also run a command straight from the extracted archive without
installing: `./libexec/orkeon/orkeon run …`.

---

## 3. Container

The `ghcr.io/orkeon/orkeon-runners` image (built from `Dockerfile.runners`,
published to GHCR) has the **`orkeon` CLI as its default entry point** and ships
all the other runners **plus the bundled examples** — zero local .NET required.

### The `/workspace` convention

Mount your project directory at `/workspace` and reference everything from
there. **One volume, no extra flags**:

```bash
docker run --rm -v "$PWD:/workspace" ghcr.io/orkeon/orkeon-runners \
  run /workspace/crews/my-crew/config.yaml \
  --settings /workspace/appsettings.local.json \
  --mount /workspace/data:/data:ro /workspace/output:/output:rw
```

Three image conveniences make this Just Work:

1. **No `--allow-external-mounts` needed** — the image bakes
   `ORKEON_ALLOW_EXTERNAL_MOUNTS=1`, because the container boundary already
   sandboxes every reachable path (pass `-e ORKEON_ALLOW_EXTERNAL_MOUNTS=0` to
   restore the guard).
2. **Writes just work** — the entrypoint adopts the uid/gid that owns
   `/workspace` before running, so the crew can write to your bind mount and the
   files it creates belong to you on the host. No `--user`, no `chmod`. (It
   still runs unprivileged: when nothing is mounted it falls back to the
   image's non-root `app` user.)
3. **`/workspace` always exists** — even with nothing mounted, so the same
   commands work in CI.

Volume mounts are how the host filesystem reaches the crew's
[VFS](../architecture/vfs-compliance.md): the Docker `-v` maps host → container,
the `--mount` flag maps container → the crew's virtual paths (`/data`,
`/output`, …).

### Running a bundled example

The examples ship inside the image under `/app/examples`, along with a helper
that makes running them a one-liner. The simplest possible session:

```bash
# one-time, on the host: pull the default model (Docker Desktop → Model Runner)
docker model pull ai/granite-4.0-h-tiny

docker run -it --rm -e ORKEON_RUNNER=shell -v "$PWD/out:/output" \
  ghcr.io/orkeon/orkeon-runners
# then, inside the shell:
orkeon-example list            # browse the 105 bundled examples
orkeon-example run 1           # run #1 (research assistant)
orkeon-example show 42         # read an example's README first
```

`orkeon-example run` resolves the number to its config, mounts
`/output` for file results, and picks LLM settings for you (next section). When
a number exists in two categories (`16`, `102`), it lists the candidates —
qualify with the category: `orkeon-example run 02/16`.

**LLM settings inside the container** — the baked default targets **Docker
Model Runner on your host** (`host.docker.internal:12434`): the
`docker model pull` above is the only setup, and every example then works with
zero flags. Forget it and `orkeon-example run` fails fast *before* the crew,
printing the exact pull command (and, when the endpoint serves other models,
the `-e ORKEON_Llm__Model=<name>` override to use one of them —
`docker model list` on the host shows what you have). To use something else,
pick a profile from `/etc/orkeon/profiles` with `ORKEON_LLM_PROFILE`:

| `-e ORKEON_LLM_PROFILE=` | Endpoint | Needs |
|---|---|---|
| *(unset)* = `host-dmr` | Docker Model Runner on the host, `:12434` | `docker model pull …` on the host |
| `host-ollama` | Ollama on the host, `:11434` | `ollama pull llama3.2` on the host |
| `openai` | OpenAI cloud | `-e ORKEON_Llm__ApiKey=sk-…` |
| `local` | model embedded in the image | the `local-llm` image variant (below) |

`ORKEON_Llm__*` env vars override any profile (e.g. `-e ORKEON_Llm__Model=…`).
On a plain Linux engine (no Docker Desktop), add
`--add-host=host.docker.internal:host-gateway` so the host profiles resolve.

**Bigger context window (host models)** — Docker Model Runner serves each model
with its default context size. On recent Docker Desktop versions you can raise
it per model, e.g. 128K for Gemma 4:

```bash
docker model configure --context-size 131072 gemma4:latest   # see: docker model configure --help
docker model configure show gemma4:latest                    # verify — list/inspect only show packaging metadata
```

A large KV cache is RAM-hungry (several extra GB at 128K) — size the host
accordingly. `Llm.MaxTokens` in the Orkeon settings caps the *response* length
and is independent of the server-side context size.

Everything local-model related (DMR pitfalls, Ollama, model switching, context
sizing, troubleshooting) is consolidated in the
[Local models guide](../guides/local-models.md).

No model at all? The bundled scripting demos run on the echo LLM fallback — no
key, no server, no network. The run announces it on stderr (*"No `Llm` section
configured — falling back to the echo provider … Run `orkeon init` …"*), so an
unconfigured container is never mistaken for a working model:

```bash
orkeon run /app/examples/scripting/01-hello-world.ork.ts
```

### Fully local: bake a model into your image

Build a variant that needs **no host-side model server and no API key** —
llama.cpp's `llama-server` plus one GGUF are embedded and served inside the
container on the same URL shape the default settings already use:

```bash
# Granite 4.0 h-tiny (Apache 2.0, ~4.2 GB of weights → ~6 GB image)
docker build -f Dockerfile.runners --target local-llm \
  --build-arg LOCAL_MODEL_URL=https://huggingface.co/ibm-granite/granite-4.0-h-tiny-GGUF/resolve/main/granite-4.0-h-tiny-Q4_K_M.gguf \
  -t orkeon-runners:granite .

# Gemma 4 E4B with a 128K context (Gemma license — keep the image local, don't push it)
docker build -f Dockerfile.runners --target local-llm \
  --build-arg LOCAL_MODEL_URL=https://huggingface.co/unsloth/gemma-4-E4B-it-qat-GGUF/resolve/main/gemma-4-E4B-it-qat-UD-Q4_K_XL.gguf \
  --build-arg LOCAL_MODEL_NAME=ai/gemma4 \
  --build-arg LOCAL_MODEL_CTX=131072 \
  -t orkeon-runners:gemma4 .
# LOCAL_MODEL_CTX bakes the llama-server context size (default 8192); override
# per run with -e ORKEON_LOCAL_LLM_CTX=… — at 128K plan several extra GB of RAM.

docker run -it --rm -m 8g -e ORKEON_RUNNER=shell orkeon-runners:granite
# the model loads at startup (30-90 s), then:
orkeon-example run 1
```

CPU inference: expect roughly 5–15 tokens/s and give the container memory
(`-m 8g`; on WSL2, raise the VM memory in `.wslconfig` if needed).
`-e ORKEON_LOCAL_LLM=0` skips the embedded server. These variants are
build-your-own by design — no pre-built tag is published, so model-license
obligations stay on your side of the wall.

One-shot (no shell) still works — the entry point **is** `orkeon`:

```bash
docker run --rm \
  -v "$PWD/out:/output" \
  ghcr.io/orkeon/orkeon-runners \
  run examples/01-enterprise/01-research-assistant/config.yaml \
  --mount /output:/output:rw
```

### Other runners and the interactive shell

The entry point **is** `orkeon`, so everything after the image name is CLI
arguments (`run <config> …`). To launch a different runner, set the
`ORKEON_RUNNER` env var — `trading`, `repl`, or `shell`:

```bash
docker run --rm -e ORKEON_RUNNER=trading \
  -v "$PWD/appsettings.local.json:/app/appsettings.local.json:ro" \
  ghcr.io/orkeon/orkeon-runners \
  --config examples/03-finance-trading/31-algo-trading/config.yaml \
  --settings /app/appsettings.local.json
```

`ORKEON_RUNNER=shell` opens an interactive **zsh** inside the image (starting
in `/workspace`) — handy for poking at the bundled examples or debugging mounts.
A welcome banner lists the available commands and paths (suppress it with
`-e ORKEON_NO_BANNER=1`), and every runner is on the PATH under the same names
as the release archives (`orkeon`, `orkeon-repl`, …):

```bash
docker run -it --rm -e ORKEON_RUNNER=shell -v "$PWD:/workspace" \
  ghcr.io/orkeon/orkeon-runners
# orkeon /workspace % orkeon-example run 1
# orkeon /workspace % orkeon run /workspace/crews/my-crew/config.yaml --validate
```

---

## Which one should I use?

- **Just want to see a crew run?** Grab a release binary (way 2) or the container
  (way 3) and point `orkeon run` at any `config.yaml`.
  - On **Windows**: `orkeon-cli-<version>-win-x64.zip` + `install.ps1`, or the
    MSI if you prefer double-clicking. One channel at a time.
  - On **Debian / Ubuntu**: `sudo apt install ./orkeon_<version>_amd64.deb`.
  - Then `orkeon init` → `orkeon doctor` → `orkeon run`.
- **Rather not type any of that?** On Windows and Linux those same packages
  install [Orkeon Studio](#orkeon-studio-the-graphical-way-in) — a window (or a
  full-screen terminal app) over the same configuration file and the same
  `orkeon run`.
- **Want the REPL or the service host?** The multi-app
  archive (way 2) — and install the .NET 10 runtime, which those launchers need.
- **Modifying Orkeon or running arbitrary examples?** Run from source (way 1).
- **CI / reproducible / no local toolchain?** Container (way 3).

Whichever you choose, the flags and the `appsettings` profile story are identical
— read them once in [Run your first example](./run-your-first-example.md).
