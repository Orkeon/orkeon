# Orkeon Examples

105 use cases demonstrating Orkeon's capabilities, from enterprise classics to experimental AI agent orchestrations.

## Runner Architecture

Examples are **data-driven**: each example is a directory containing a `config.yaml` (crew definition). A shared **runner** binary loads the configuration and executes the crew.

| Runner | Location | Purpose |
|--------|----------|---------|
| **standard** | `runners/standard/` | General-purpose runner with all standard tools (FileSystem, Web, Data, Code) |
| **trading** | `runners/trading/` | Extends standard with 44 specialized trading tools |
| **_shared** | `runners/_shared/` | Shared library (ServiceProviderToolRegistry, common utilities) |

## LLM Configuration

The runners use a **fallback chain** to find `appsettings.json`:

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

The committed default is `appsettings/appsettings.json`; runners pick it up
automatically. To use another profile, copy the matching `.example` template
(dropping the `.example` suffix), fill in your key, and pass `--settings` (see
`appsettings/README.md`):

```bash
# Docker Models from a container:
cp examples/appsettings/appsettings.docker-model-runner.local.json.example \
   examples/appsettings/appsettings.docker-model-runner.local.json
# then edit localhost -> host.docker.internal
dotnet run --project examples/runners/standard -- \
  --config examples/01-enterprise/01-research-assistant/config.yaml \
  --settings examples/appsettings/appsettings.docker-model-runner.local.json

# OpenAI:
cp examples/appsettings/appsettings.openai.local.json.example \
   examples/appsettings/appsettings.openai.local.json
export OPENAI_API_KEY="sk-..."   # or put the key in the copied file
dotnet run --project examples/runners/standard -- \
  --config examples/01-enterprise/01-research-assistant/config.yaml \
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

# Direct dotnet run:
dotnet run --project examples/runners/standard -- --config examples/01-enterprise/01-research-assistant/config.yaml
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
- [`runners/_shared/`](runners/_shared/) -- Shared runner library (ServiceProviderToolRegistry, common utilities)

## Solution

The examples solution (`Orkeon.Examples.sln`) references the runner projects. Build with:

```bash
dotnet build examples/Orkeon.Examples.sln
```
