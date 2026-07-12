# Three ways to run Orkeon

> **See also**: [Run your first example](./run-your-first-example.md) · [Overview](./overview.md) · [Back to the index](../INDEX.md)

Orkeon crews can be launched three ways. Pick the one that matches how much you
want to install:

| Way | Prerequisites | Time to first run | Best for |
|---|---|---|---|
| **1. From source** | .NET SDK ≥ 10.0.300, git clone | ~5 min (+ build) | Contributors, reading/modifying code, running any of the 100+ bundled examples |
| **2. Release binary** | Nothing for the self-contained `orkeon` archive; .NET 10 **runtime** for the `orkeon-slim` archive | ~2 min | Running examples and showcases without a source checkout |
| **3. Container** | Docker | ~1 min (after image pull) | CI, reproducible runs, no local .NET at all |

All three drive the same **`orkeon` CLI** and accept the same flags. The crew
config is the positional argument to `orkeon run <config>` (the `orkeon-trading`
runner takes it as `--config` instead); the option flags (`--settings`, `-v`,
`--mount`, `--var`, `--llm-log`, …) are documented once, in detail, in
[Run your first example](./run-your-first-example.md#every-flag-explained).

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
local code changes. Finance examples use `examples/03-finance-trading` on the
`orkeon-trading` runner instead. Full walkthrough, LLM-profile setup, and
troubleshooting: [Run your first example](./run-your-first-example.md).

---

## 2. Release binary

Each [GitHub Release](https://github.com/Orkeon/orkeon/releases) attaches, per
platform, **two archives** — plus a NuGet [dotnet tool](https://www.nuget.org/):

| Distribution | Prerequisite | Best for |
|---|---|---|
| **`orkeon-<version>-<rid>`** (self-contained) | none — bundles the runtime | Onboarding, air-gapped hosts, the default download |
| **`orkeon-slim-<version>-<rid>`** (framework-dependent) | [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0) | Smaller archive when you already have .NET 10 |
| **`dotnet tool install --global Orkeon.Scripting.Cli`** | .NET 10 SDK | Getting just the `orkeon` CLI on a dev box |

`<rid>` is `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64` (`.tar.gz`) or
`win-x64` (`.zip`). Each archive bundles **all** the command-line entry points:

| Command | What it runs |
|---|---|
| `orkeon` | **The main CLI and default entry point** — `orkeon run <config.yaml>` for any non-finance example, or `orkeon run script.ork.ts` for the scripting DSL |
| `orkeon-trading` | Trading showcase runner — `orkeon-trading --config <config.yaml>`; adds 44 specialized trading tools |
| `orkeon-repl` | Full interactive REPL console (all built-in tools, code analysis, local embeddings) |
| `orkeon-interactive` | Interactive Terminal.Gui runner |
| `orkeon-claim-verify` | Interactive claim-verification runner |
| `orkeon-spec-forge` | Interactive interview / spec-forge runner |
| `orkeon-tui-keytest` | Terminal.Gui key-diagnostic utility |

> **Runtime prerequisite.** In the **self-contained** `orkeon` archive every
> command runs with no .NET install at all. The **`orkeon-slim`** archive and the
> **dotnet tool** are framework-dependent and require the
> [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (verify
> with `dotnet --list-runtimes`).

### Download, extract, install

```bash
# Linux / macOS
tar -xzf orkeon-<version>-linux-x64.tar.gz
cd orkeon-<version>-linux-x64
./install.sh                 # installs to ~/.local; --prefix /usr/local for system-wide
./install.sh --modify-path   # also appends ~/.local/bin to your shell rc files
```

```powershell
# Windows (PowerShell)
Expand-Archive orkeon-<version>-win-x64.zip
cd orkeon-<version>-win-x64
.\install.ps1                # installs to %LOCALAPPDATA%\Programs\Orkeon, updates user PATH
```

### Run

The CLI resolves LLM settings the same way as from source, so bring an
`appsettings` profile (see the [profile matrix](./run-your-first-example.md#3-choose-an-llm-profile)):

```bash
orkeon run path/to/config.yaml \
  --settings path/to/appsettings.local.json \
  --mount ./out:/output:rw
```

For a finance/trading showcase, swap in the specialized runner:
`orkeon-trading --config path/to/config.yaml --settings …`.

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

The examples ship inside the image under `/app/examples`, so only the output
and settings need mounting:

```bash
docker run --rm \
  -v "$PWD/out:/output" \
  -v "$PWD/appsettings.local.json:/app/appsettings.local.json:ro" \
  ghcr.io/orkeon/orkeon-runners \
  run examples/01-enterprise/01-research-assistant/config.yaml \
  --settings /app/appsettings.local.json \
  --mount /output:/output:rw
```

### Other runners and the interactive shell

The entry point **is** `orkeon`, so everything after the image name is CLI
arguments (`run <config> …`). To launch a different runner, set the
`ORKEON_RUNNER` env var — `trading`, `repl`, `interactive`, `claim-verify`,
`spec-forge`, `tui-keytest`, or `shell`:

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
as the release archives (`orkeon`, `orkeon-trading`, `orkeon-repl`, …):

```bash
docker run -it --rm -e ORKEON_RUNNER=shell -v "$PWD:/workspace" \
  ghcr.io/orkeon/orkeon-runners
# orkeon /workspace % orkeon run /app/examples/01-enterprise/01-research-assistant/config.yaml --validate
```

---

## Which one should I use?

- **Just want to see a crew run?** Grab a release binary (way 2) or the container
  (way 3) and point `orkeon run` at any `config.yaml`.
- **Modifying Orkeon or running arbitrary examples?** Run from source (way 1).
- **CI / reproducible / no local toolchain?** Container (way 3).

Whichever you choose, the flags and the `appsettings` profile story are identical
— read them once in [Run your first example](./run-your-first-example.md).
</content>
