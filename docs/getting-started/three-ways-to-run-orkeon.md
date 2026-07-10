# Three ways to run Orkeon

> **See also**: [Run your first example](./run-your-first-example.md) · [Overview](./overview.md) · [Back to the index](../INDEX.md)

Orkeon crews can be launched three ways. Pick the one that matches how much you
want to install:

| Way | Prerequisites | Time to first run | Best for |
|---|---|---|---|
| **1. From source** | .NET SDK ≥ 10.0.300, git clone | ~5 min (+ build) | Contributors, reading/modifying code, running any of the 100+ bundled examples |
| **2. Release binary** | .NET 10 **runtime** only (self-contained runners need nothing) | ~2 min | Running the showcase runners without a source checkout |
| **3. Container** | Docker | ~1 min (after image pull) | CI, reproducible runs, no local .NET at all |

All three ultimately invoke the same runner and accept the same flags
(`--config`, `--settings`, `-v`, `--mount`, `--var`, `--llm-log`, …). Those flags
are documented once, in detail, in
[Run your first example](./run-your-first-example.md#every-flag-explained).

---

## 1. From source

Clone, build, and run any `config.yaml` under `examples/`:

```bash
git clone https://github.com/Orkeon/orkeon.git
cd orkeon
dotnet run --project examples/runners/standard -- \
  --config examples/01-enterprise/01-research-assistant/config.yaml \
  --settings examples/appsettings/appsettings.deepseek.local.json
```

This is the most flexible path — it can run **every** example and picks up your
local code changes. Full walkthrough, LLM-profile setup, and troubleshooting:
[Run your first example](./run-your-first-example.md).

---

## 2. Release binary

Each [GitHub Release](https://github.com/Orkeon/orkeon/releases) attaches **one
archive per platform**, named `orkeon-<version>-<rid>` — `.tar.gz` for Linux/macOS
(`linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`) and `.zip` for Windows
(`win-x64`). Each archive bundles **all** the command-line runners:

| Command | What it runs |
|---|---|
| `orkeon` | The scripting CLI — `orkeon run script.ork.ts` or `orkeon run crew.yaml` |
| `orkeon-repl` | Full interactive REPL console (all built-in tools, code analysis, local embeddings) |
| `orkeon-examples` | **Standard example runner** — `orkeon-examples --config <config.yaml>` (self-contained: no .NET runtime needed) |
| `orkeon-trading` | Trading showcase runner (self-contained: no .NET runtime needed) |
| `orkeon-interactive` | Interactive Terminal.Gui runner |
| `orkeon-claim-verify` | Interactive claim-verification runner |
| `orkeon-spec-forge` | Interactive interview / spec-forge runner |
| `orkeon-tui-keytest` | Terminal.Gui key-diagnostic utility |

> **Runtime prerequisite.** The showcase runners `orkeon-examples` and
> `orkeon-trading` are published **self-contained** — they run with no .NET
> install at all. The remaining commands in the archive are framework-dependent
> and require the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
> (verify with `dotnet --list-runtimes`).

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

The runners resolve LLM settings the same way as from source, so bring an
`appsettings` profile (see the [profile matrix](./run-your-first-example.md#3-choose-an-llm-profile)):

```bash
orkeon-examples \
  --config path/to/config.yaml \
  --settings path/to/appsettings.local.json \
  --mount ./out:/output:rw
```

You can also run a runner straight from the extracted archive without
installing: `./libexec/orkeon-examples/Orkeon.Examples.Runner --config …`.

---

## 3. Container

The `ghcr.io/orkeon/orkeon-runners` image (built from `Dockerfile.runners`,
published to GHCR) ships the standard and trading runners **plus the bundled
examples** — zero local .NET required.

Volume mounts are how the host filesystem reaches the crew's
[VFS](../architecture/vfs-compliance.md): map a host directory to a container
path, then point `--mount`/`--config`/`--settings` at the container path.

```bash
docker run --rm \
  -v "$PWD/out:/output" \
  -v "$PWD/appsettings.local.json:/app/appsettings.local.json:ro" \
  ghcr.io/orkeon/orkeon-runners \
  --config examples/01-enterprise/01-research-assistant/config.yaml \
  --settings /app/appsettings.local.json \
  --mount /output:/output:rw
```

Volume-to-VFS mapping in that command:

| Host | Container volume | Crew VFS mount |
|---|---|---|
| `$PWD/out` | `/output` | `--mount /output:/output:rw` → virtual `/output` |
| `$PWD/appsettings.local.json` | `/app/appsettings.local.json` | passed via `--settings` |

The image's entry point **is** the runner, so everything after the image name is
runner flags.

---

## Which one should I use?

- **Just want to see a crew run?** Grab a release binary (way 2) or the container
  (way 3) and point `orkeon-examples` at any `config.yaml`.
- **Modifying Orkeon or running arbitrary examples?** Run from source (way 1).
- **CI / reproducible / no local toolchain?** Container (way 3).

Whichever you choose, the flags and the `appsettings` profile story are identical
— read them once in [Run your first example](./run-your-first-example.md).
</content>
