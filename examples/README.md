# Orkeon Examples

101 use cases demonstrating Orkeon's capabilities, from enterprise classics to experimental AI agent orchestrations.

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
3. `_shared/appsettings.json` (shared default for all examples)

Pre-configured profiles are available in `_shared/`:

| Profile | File | Use case |
|---------|------|----------|
| **Docker Models (localhost)** | `_shared/appsettings.json` | Default -- Docker Desktop Models on Windows/Mac |
| **Docker Models (container)** | `_shared/appsettings.docker.json` | Running from inside a Docker container |
| **OpenAI** | `_shared/appsettings.openai.json` | OpenAI API (requires `OPENAI_API_KEY`) |

To switch profile, either copy a profile to `_shared/appsettings.json` or pass `--settings`:

```bash
# Use Docker profile from a container:
dotnet run --project examples/runners/standard -- \
  --config examples/01-enterprise/01-research-assistant/config.yaml \
  --settings examples/_shared/appsettings.docker.json

# Use OpenAI:
export OPENAI_API_KEY="sk-..."
dotnet run --project examples/runners/standard -- \
  --config examples/01-enterprise/01-research-assistant/config.yaml \
  --settings examples/_shared/appsettings.openai.json
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

Test all 101 examples at once:

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

- [`_shared/tools/`](_shared/tools/) -- Custom tools shared across examples
- [`_shared/templates/`](_shared/templates/) -- YAML configuration templates
- [`_shared/appsettings*.json`](_shared/) -- LLM configuration profiles

## Solution

The examples solution (`Orkeon.Examples.sln`) references the runner projects. Build with:

```bash
dotnet build examples/Orkeon.Examples.sln
```
