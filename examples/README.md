# Orkeon Examples

105 use cases demonstrating Orkeon's capabilities, from enterprise classics to experimental AI agent orchestrations.

## Run in Docker (simplest path)

All 105 examples ship inside the `ghcr.io/orkeon/orkeon-runners` image with an
`orkeon-example` helper — no .NET, no checkout:

```bash
# one-time, on the host: pull the default model (Docker Desktop → Model Runner)
docker model pull ai/granite-4.0-h-tiny

docker run -it --rm -e ORKEON_RUNNER=shell -v "$PWD/out:/output" \
  ghcr.io/orkeon/orkeon-runners
# then, inside the shell:
orkeon-example list            # browse the examples
orkeon-example run 1           # run #1 — trading examples dispatch automatically
orkeon-example show 42         # read an example's README first
orkeon run /app/examples/scripting/01-hello-world.ork.ts   # no LLM needed at all
```

Without the pulled model, `orkeon-example run` stops before the crew with the
exact `docker model pull` command to fix it (`docker model list` shows what you
have; run a different one with `-e ORKEON_Llm__Model=<name>`). File output
lands under `/output` — the `-v $PWD/out:/output` above keeps it on the host.
LLM settings default to **Docker Model Runner on your host**; switch with
`-e ORKEON_LLM_PROFILE=<name>`:

| `ORKEON_LLM_PROFILE` | Endpoint | Needs |
|---|---|---|
| *(unset)* = `host-dmr` | Docker Model Runner on the host | `docker model pull …` |
| `host-ollama` | Ollama on the host (`:11434`) | `ollama pull llama3.2` |
| `openai` | OpenAI cloud | `-e ORKEON_Llm__ApiKey=sk-…` |
| `local` | model embedded in the image | a `--target local-llm` build |

Full container guide — including baking a Granite/Gemma model into your own
image variant: [Three ways to run Orkeon §3](../docs/getting-started/three-ways-to-run-orkeon.md#3-container).
Everything about local models (DMR, Ollama, context sizes, troubleshooting):
[Local models guide](../docs/guides/local-models.md).

## Runner Architecture

Examples are **data-driven**: each example is a directory containing a `config.yaml` (crew definition). The **`orkeon` CLI** loads the configuration and executes the crew.

| Command | Scope | Purpose |
|--------|-------|---------|
| **`orkeon run <config.yaml>`** | all non-finance examples | The default entry point — general-purpose toolset (FileSystem, Web, Data, Code). Available as an installed binary, a `dotnet tool`, or `dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run …` from a source checkout |
| **`orkeon-trading --config <config.yaml>`** | `03-finance-trading/*` | Adds 44 specialized trading tools on top of the standard toolset |

## LLM Configuration

The CLI uses a **fallback chain** to find `appsettings.json`:

1. `--settings path/to/appsettings.json` (explicit CLI arg)
2. `appsettings.json` next to the example's `config.yaml` (per-example override)
3. `appsettings/appsettings.json` (shared default for all examples)

Pre-configured profiles are available in `appsettings/` (see `appsettings/README.md`):

| Profile | File | Use case |
|---------|------|----------|
| **Docker Models (localhost)** | `appsettings/appsettings.json` | Default -- Docker Desktop Models on Windows/Mac |
| **Docker Models (container)** | `appsettings/appsettings.docker-model-runner.local.json.example` | From inside a container (swap `localhost` for `host.docker.internal`) |
| **OpenAI** | `appsettings/appsettings.openai.local.json.example` | OpenAI API (requires `OPENAI_API_KEY`) |
| **DeepSeek / Z.AI GLM** | `appsettings/appsettings.{deepseek,glm,glm-medium}.local.json.example` | Cloud providers (see `appsettings/README.md`) |

The committed default is `appsettings/appsettings.json`; the CLI picks it up
automatically. To use another profile, copy the matching `.example` template
(dropping the `.example` suffix), fill in your key, and pass `--settings` (see
`appsettings/README.md`):

```bash
# Docker Models from a container:
cp examples/appsettings/appsettings.docker-model-runner.local.json.example \
   examples/appsettings/appsettings.docker-model-runner.local.json
# then edit localhost -> host.docker.internal
orkeon run examples/01-enterprise/01-research-assistant/config.yaml \
  --settings examples/appsettings/appsettings.docker-model-runner.local.json

# OpenAI:
cp examples/appsettings/appsettings.openai.local.json.example \
   examples/appsettings/appsettings.openai.local.json
export OPENAI_API_KEY="sk-..."   # or put the key in the copied file
orkeon run examples/01-enterprise/01-research-assistant/config.yaml \
  --settings examples/appsettings/appsettings.openai.local.json
```

Environment variables with prefix `ORKEON_` override any JSON setting.

## How to Run an Example

```bash
# From the repository root:
./examples/run-example.sh <category>/<example>

# Example:
./examples/run-example.sh 01-enterprise/01-research-assistant

# PowerShell:
.\examples\run-example.ps1 01-enterprise/01-research-assistant

# Direct via the orkeon CLI (installed binary or dotnet tool):
orkeon run examples/01-enterprise/01-research-assistant/config.yaml

# ...or from a source checkout, through the CLI project:
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run examples/01-enterprise/01-research-assistant/config.yaml
```

## Automated Testing

Test all 105 examples at once:

```bash
# Build only:
./examples/test-all-examples.sh --level build

# Test config loading (fast, no LLM calls):
./examples/test-all-examples.sh --level load

# Full execution (slow, calls LLM):
./examples/test-all-examples.sh --level run --timeout 300

# Test a single category:
./examples/test-all-examples.sh --category 01

# PowerShell:
.\examples\test-all-examples.ps1 -Level load
```

Reports are generated in `examples/test-reports/`.

## Categories

The full generated catalog (process, agents, tools per example) lives in [INDEX.md](INDEX.md) — regenerate it with `bash scripts/generate-examples-index.sh`.

| # | Category | Examples | Description |
|---|----------|----------|-------------|
| 01 | [Enterprise](01-enterprise/) | 1-15 | Classiques Entreprise -- research, code review, email, reports, support |
| 02 | [Science & Research](02-science-research/) | 16-30 | Sciences & Recherche -- meta-analysis, debates, genomics, knowledge graphs |
| 03 | [Finance & Trading](03-finance-trading/) | 31-45 | Finance & Trading -- algo trading, fraud detection, compliance, ESG |
| 04 | [Health & Wellness](04-health-wellness/) | 46-55 | Sante & Bien-etre -- diagnosis, nutrition, clinical trials, telemedicine |
| 05 | [Education](05-education/) | 56-65 | Education & Formation -- tutoring, exams, gamification, mentoring |
| 06 | [Engineering & DevOps](06-engineering-devops/) | 66-75 | Ingenierie & DevOps -- CI/CD, incident response, chaos engineering |
| 07 | [Creative & Media](07-creative-media/) | 76-85 | Creativite & Media -- narrative, podcast, music, worldbuilding |
| 08 | [IoT & Smart Systems](08-iot-smart-systems/) | 86-95 | IoT, Monde Physique & Smart Systems -- smart home, fleet, energy |
| 09 | [Experimental](09-experimental/) | 96-101 | Avant-Garde & Experimental -- self-adaptive crews, civilization sim |

## Shared Resources

- [`appsettings/`](appsettings/) -- LLM configuration profiles (committed default + provider templates; see [`appsettings/README.md`](appsettings/README.md))

## Solution

Build the framework (and the `orkeon` CLI) from the root solution:

```bash
dotnet build Orkeon.sln
```
