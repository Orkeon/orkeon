> 🇫🇷 [Version française](README.fr.md)

# <img src="docs/assets/orkeon-mascot.png" alt="Orkeon mascot — a curious chameleon" width="96" align="absmiddle"> Orkeon

**Build and orchestrate AI agent teams — describe them in declarative YAML, programmatic TypeScript (`.ork.ts`), or pure C#; a single full-.NET stack executes them all**

[![NuGet](https://img.shields.io/nuget/v/Orkeon.Domain.svg)](https://www.nuget.org/packages/Orkeon.Domain/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.md)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Build](https://github.com/Orkeon/orkeon/actions/workflows/ci.yml/badge.svg)](https://github.com/Orkeon/orkeon/actions/workflows/ci.yml)

---

## What is Orkeon?

Orkeon is a C# framework for creating and managing collaborative AI agent teams that tackle complex, multi-step tasks using large language models. Agents are organized into crews, each with a defined role, goal, and toolset, and work together through one of six orchestration strategies (sequential, hierarchical, parallel, consensual, graph, or autonomous). Built on Clean Architecture principles, Orkeon provides a fully typed, extensible foundation for production-grade agentic workflows in .NET.

---

## Quick Start — one crew, three ways

The same crew, written at three levels of abstraction. Pick the one that fits — or mix them: they all run on the same .NET execution engine.

**1. Declarative YAML** — no code, no build: edit the file, run it again (`crew.yaml`):

```yaml
name: "research-crew"
goal: "Research AI trends for 2026"
process: "sequential"

agents:
  researcher:
    role: "Researcher"
    goal: "Find and summarize information about AI trends"
    verbose: true

tasks:
  research:
    description: "Search for the latest AI developments and trends"
    expectedOutput: "A comprehensive summary report"
    agent: "researcher"
```

```bash
orkeon run crew.yaml
```

**2. Programmatic TypeScript** — scripting ergonomics (agent bodies, hooks, dynamic spawning, FSM/graph literals), .NET runtime underneath — and still no rebuild: scripts are transpiled on the fly (`crew.ork.ts`):

```typescript
/// <reference orkeon-script="1.0" />

const researcher = agentBuilder()
    .name("Researcher").role("Researcher")
    .goal("Find and summarize information about AI trends")
    .build();

const crew = crewBuilder()
    .name("research-crew")
    .goal("Research AI trends for 2026")
    .withAgent(researcher)
    .withTask({
        description: "Search for the latest AI developments and trends",
        expectedOutput: "A comprehensive summary report",
    })
    .build();

await crew.run();
```

```bash
orkeon run crew.ork.ts
```

**3. Pure C#** — the builder API embedded in your own application, strongly typed end-to-end:

```csharp
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;

var agent = new AgentBuilder()
    .Role("Researcher")
    .Goal("Find and summarize information about AI trends")
    .Verbose()
    .Build();

var crew = new CrewBuilder()
    .Goal("Research AI trends for 2026")
    .Sequential()
    .WithAgent(agent)
    .WithTask(t => t
        .Description("Search for the latest AI developments and trends")
        .ExpectedOutput("A comprehensive summary report"))
    .Build();

// wire the host and kick it off — see docs/getting-started/bootstrap.md
```

**No API key?** Run everything on a model on your own machine (Docker Model
Runner, Ollama, or a model embedded in the container image) — see the
[Local models guide](docs/guides/local-models.md). Full walkthroughs:
[Three ways to run Orkeon](docs/getting-started/three-ways-to-run-orkeon.md) ·
[Run your first example](docs/getting-started/run-your-first-example.md).

---

## Installation

| You want to… | Do this | Details |
|---|---|---|
| **Run crews with zero install** | `docker run -it --rm -e ORKEON_RUNNER=shell ghcr.io/orkeon/orkeon-runners` — interactive shell, 105 bundled examples (`orkeon-example run 1`), local-model ready | [Container guide](docs/getting-started/three-ways-to-run-orkeon.md#3-container) |
| **Install the `orkeon` CLI** | Grab the archive for your platform from the [releases](https://github.com/Orkeon/orkeon/releases) (`linux-x64/arm64`, `osx-x64/arm64`, `win-x64`), then `./install.sh` / `.\install.ps1`. Ships `orkeon`, `orkeon-repl`, and the example runners. Needs the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0) | [Release binaries](docs/getting-started/three-ways-to-run-orkeon.md#2-release-binary) |
| **Embed Orkeon in your app** | `dotnet add package Orkeon.Domain` (+ `Orkeon.Application`, `Orkeon.Infrastructure`, and opt-in packs as needed) | [Bootstrap and execution](docs/getting-started/bootstrap.md) |
| **Hack on the framework** | `git clone` + `dotnet build Orkeon.sln` | [From source](docs/getting-started/three-ways-to-run-orkeon.md#1-from-source) · [Contributing](#contributing) |

---

## Features

| Capability | Details |
|---|---|
| **70+ built-in tools** | File system, web scraping (AngleSharp), HTTP APIs, JSON/CSV/XML/PDF, databases, secure code execution, RAG and semantic search, EventHub messaging, RaggableTree code analysis, delegation/collaboration — see the [tool inventory](docs/tools/inventory.md) |
| **12 LLM providers** | OpenAI, Ollama, Anthropic, Azure OpenAI, Groq, Mistral AI, DeepSeek, Kimi (Moonshot), Qwen, Together AI, HuggingFace, and Z.AI (GLM) — all HTTP-based, extending `HttpLlmProviderBase`; local models via Docker Model Runner, Ollama, or embedded llama.cpp — see the [local models guide](docs/guides/local-models.md) |
| **Vision / multimodal** | Image content flows end-to-end (`MultiModalContent` → Anthropic image blocks / OpenAI `image_url`) with a VFS-backed loader; opt-in via `AddOrkeonMultiModal(...)` — see the [multimodal guide](docs/guides/multimodal.md) |
| **6 memory providers** | Redis (vector search), SQLite, InMemory, ChromaDB (REST API v2), Pinecone, LanceDB (remote REST server) — all composable with the AES-256-GCM at-rest encryption decorator |
| **6 orchestration strategies** | Sequential, Hierarchical, Parallel, Consensual (Majority / SuperMajority / Unanimity voting strategies), Graph (LangGraph-style), Autonomous (multi-dimensional execution budget) — see the [process-type guide](docs/orchestration/process-types.md) |
| **Plugin system** | Drop-in assemblies implementing `IOrkeonPlugin`, discovered in a plugin directory, loaded in isolated collectible `AssemblyLoadContext`s, activated explicitly via `AddOrkeonPlugins(...)` — see [plugins](docs/architecture/plugins.md) |
| **Host bootstrap & scripting** | `Orkeon.Hosting` (`RunnerHost`) wires the full stack for runners/CLIs (appsettings, VFS mounts, providers, tools); the `orkeon` dotnet tool runs TypeScript-syntax `.ork.ts` crew scripts |
| **Source generators** | `Orkeon.Generators` emits the `[TypedDictionary]` wrapper/builder plumbing, keeping the hand-written strongly typed APIs boilerplate-free |
| **Typed pipeline architecture** | `ComponentBase<TRequest, TResponse>` eliminates `Dictionary<string, object>` throughout the stack |
| **YAML configuration** | Full round-trip export/import for agents, tasks, crews, and tool schemas |
| **Fluent Builder API** | `AgentBuilder`, `CrewBuilder`, `CrewTaskBuilder` for ergonomic, discoverable construction |
| **Clean Architecture** | Strict Domain / Application / Infrastructure separation with no cross-layer leakage |
| **CQRS pipeline** | Commands and queries for all aggregates; `ValidatingCommandHandler` decorator; `UnitOfWork` integration |
| **Semantic agent selection** | Embedding-based similarity matching to route tasks to the most suitable agent |
| **Checkpointing & resume** | Execution state persisted to pluggable state stores (InMemory, JSON file, SQLite, PostgreSQL); `CheckpointManager` time-travel (fork, replay, diff) and `ResumeEngine` to resume interrupted runs |
| **A2A communication** | Agent-to-Agent protocol with discovery, `A2AClient`/`A2AServer`, a scoped agent repository over a shared registration store, and optional mTLS / auth-scheme enforcement (client certificate + server-side `RequireMutualTls` / `AllowedAuthSchemes`) |
| **Enterprise security** | Memory encryption at-rest (AES-256-GCM), key rotation with atomic two-phase re-encryption, DLP/PII detection, Azure AD and OIDC auth |
| **Opt-in subsystems** | A2A, monitoring, NIST compliance, DLP, tool rate-limiting, key rotation, benchmarking, multimodal, kickoff hooks — none registered by default, each enabled via its dedicated `AddOrkeonXxx()` extension — see the [opt-in reference](docs/reference/opt-in-subsystems.md) |

---

## Architecture

Orkeon follows Clean Architecture with three concentric layers:

```
+----------------------------------------------------------+
|  Infrastructure  (outer)                                 |
|  LLM providers, memory stores, tools, HTTP clients       |
|                                                          |
|  +------------------------------------------------+      |
|  |  Application  (middle)                         |      |
|  |  Use cases, orchestrators, service interfaces  |      |
|  |                                                |      |
|  |  +----------------------------------------+   |      |
|  |  |  Domain  (inner)                       |   |      |
|  |  |  Agents, Crews, Tasks, Tools, LLMs     |   |      |
|  |  |  Pure business logic, no dependencies  |   |      |
|  |  +----------------------------------------+   |      |
|  +------------------------------------------------+      |
+----------------------------------------------------------+
```

- **Domain**: Core entities and value objects (`Agent`, `Crew`, `CrewTask`, `IBaseTool`, `ILlmProvider`). No external dependencies.
- **Application**: Use cases and orchestration logic. Defines interfaces (ports) implemented by Infrastructure.
- **Infrastructure**: LLM providers, memory stores, tool implementations, and all external integrations.

Around the core, dedicated packages cover hosting (`Orkeon.Hosting`), plugins (`Orkeon.Plugins`), Roslyn source generators (`Orkeon.Generators`), the VFS-compliance analyzer (`Orkeon.Compliance.Vfs`), the TypeScript-syntax scripting DSL (`Orkeon.Scripting` plus the `orkeon` CLI tool), tool packs (`Orkeon.Tools.*`), and the RaggableTree semantic code-analysis engine (`Orkeon.Analysis`).

---

## Documentation

| You are looking for… | Go to |
|---|---|
| **First run, step by step** | [Getting-started overview](docs/getting-started/overview.md) · [Run your first example](docs/getting-started/run-your-first-example.md) |
| **The three ways to run Orkeon** (source / binary / container) | [Three ways to run Orkeon](docs/getting-started/three-ways-to-run-orkeon.md) |
| **Local models** (Docker Model Runner, Ollama, embedded, 128K contexts) | [Local models guide](docs/guides/local-models.md) |
| **The 105 runnable examples** (9 themed categories + `orkeon-example`) | [Examples](examples/README.md) · [Catalog](docs/reference/examples-catalog.md) |
| **Writing crews**: YAML vs builders, host wiring, execution | [YAML & builders](docs/getting-started/yaml-and-builders.md) · [Bootstrap and execution](docs/getting-started/bootstrap.md) |
| **Orchestration modes** (incl. FSM and graph deep dives) | [Process types](docs/orchestration/process-types.md) · [FSM](docs/orchestration/fsm.md) · [Graph](docs/orchestration/graph.md) |
| **Writing your own tools** | [New tool pattern](docs/tools/new-tool-pattern.md) · [Tool inventory](docs/tools/inventory.md) |
| **Architecture deep dives** (plugins, scripting, VFS, security, RaggableTree) | [Architecture docs](docs/architecture/) · [ADRs](docs/adr/) |
| **Everything else** | [Documentation index](docs/INDEX.md) *(also available [in French](docs/fr/INDEX.md))* |

---

## Why Orkeon?

- **Three authoring surfaces, one engine** — the same crew can be a YAML file an analyst edits, a TypeScript script a developer iterates on (both run with zero rebuild), or C# embedded in your product. No rewrite when you graduate from one to the next.
- **Orchestration beyond pipelines** — six strategies, including LangGraph-style state graphs with conditional edges and a fully autonomous mode where agents delegate, spawn, and communicate under a multi-dimensional execution budget (tool calls, depth, wall time, tokens, spawns).
- **Batteries included** — 70+ tools, 12 LLM providers, 6 memory stores, vision, RAG, code analysis: usable out of the box, replaceable through Clean Architecture ports.
- **Local-first** — every example runs against a model on your own machine (Docker Model Runner, Ollama, or llama.cpp embedded in the container image). No API key required to evaluate it.
- **Production posture** — a rights-audited virtual filesystem sandboxes every file access; circuit breakers stop runaway agents; execution state checkpoints and resumes; memory encrypts at rest; DLP and rate limiting are one `AddOrkeonXxx()` away.
- **Typed all the way down** — no `Dictionary<string, object>` plumbing; source generators keep the typed surface boilerplate-free.

---

## Project Status

Orkeon is **0.9.1-beta** on .NET 10, driving toward V1. Recent milestones: the `orkeon` CLI and the `orkeon-runners` container image with 105 bundled examples and local-model workflows; FSM and Graph orchestration; the Autonomous process with execution budgets; the TypeScript scripting DSL; RaggableTree semantic code analysis (15 agent tools); the plugin system; checkpoint/resume; LLM exchange logging; forced JSON response formats; and a 12th LLM provider (Z.AI GLM).

Every pull request is gated in CI:

- merged line coverage must stay at or above **70 %** (scheduled to rise to 75 %) — currently measured at **82 %** overall
- a **blocking SonarQube quality gate** ("Orkeon Transitional") with a documented hardening trajectory — see the [quality-gate policy](docs/guides/quality-gate.md). Latest analysis (July 2026): gate green, **0 vulnerabilities, 0 code smells**, 2.2 % duplication across ~114 k lines of code

Known constraints are tracked in [docs/reference/limitations.md](docs/reference/limitations.md).

---

## Contributing

Contributions are welcome. Please open an issue to discuss significant changes before submitting a pull request. Make sure all tests pass (`dotnet test Orkeon.sln`) and that new code follows the Clean Architecture conventions described in [CLAUDE.md](CLAUDE.md).

### Building from source

The scripting layer (`.ork.ts` support) ships a small esbuild toolchain that is bootstrapped on the first `dotnet build`. On a fresh clone the `Orkeon.Scripting` project runs `npm ci` (strictly from the committed `tools/scripting-esbuild/package-lock.json`, so the lockfile is never mutated) under `tools/scripting-esbuild/` to provision `node_modules/`. This is a one-time, network-touching step per clone.

To skip it entirely (e.g. in CI or when packaging NuGet consumers that do not need esbuild), pass the opt-out flag:

```bash
dotnet build Orkeon.sln -p:SkipScriptingNpmInstall=true
```

If npm is unavailable the build still succeeds; esbuild is then resolved from `PATH` at runtime.

---

## License

Orkeon is released under the [MIT License](LICENSE.md).

## Acknowledgements

Orkeon is an independent .NET framework for AI agent orchestration, inspired by [CrewAI](https://github.com/crewAIInc/crewAI) (MIT License). We gratefully acknowledge the CrewAI authors.
