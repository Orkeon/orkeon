> 🇫🇷 [Version française](README.fr.md)

# Orkeon

**Build and orchestrate AI agent teams in .NET**

[![NuGet](https://img.shields.io/nuget/v/Orkeon.Domain.svg)](https://www.nuget.org/packages/Orkeon.Domain/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.md)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Build](https://github.com/Orkeon/orkeon/actions/workflows/ci.yml/badge.svg)](https://github.com/Orkeon/orkeon/actions/workflows/ci.yml)

---

## What is Orkeon?

Orkeon is a C# framework for creating and managing collaborative AI agent teams that tackle complex, multi-step tasks using large language models. Agents are organized into crews, each with a defined role, goal, and toolset, and work together through one of six orchestration strategies (sequential, hierarchical, parallel, consensual, graph, or autonomous). Built on Clean Architecture principles, Orkeon provides a fully typed, extensible foundation for production-grade agentic workflows in .NET.

---

## Quick Start

> For the full step-by-step guide, see [Getting Started — Overview](docs/getting-started/overview.md).

```csharp
using Orkeon.Domain.Builders;

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
```

---

## Installation

```bash
dotnet add package Orkeon.Domain --version 0.9.0-beta
dotnet add package Orkeon.Application --version 0.9.0-beta
dotnet add package Orkeon.Infrastructure --version 0.9.0-beta
```

### Install the CLI from release archives

Each [GitHub Release](https://github.com/Orkeon/orkeon/releases) ships one archive
per platform (`linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64` as `.tar.gz`;
`win-x64` as `.zip`) containing every CLI executable: `orkeon`, `orkeon-repl`,
and the example runners (`orkeon-examples`, `orkeon-trading`, `orkeon-interactive`,
`orkeon-tui-keytest`, `orkeon-claim-verify`, `orkeon-spec-forge`).

Prerequisite: the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
(binaries are framework-dependent).

```bash
# Linux / macOS
tar -xzf orkeon-<version>-<rid>.tar.gz
cd orkeon-<version>-<rid>
./install.sh            # installs to ~/.local; --prefix /usr/local for system-wide
```

```powershell
# Windows (PowerShell)
Expand-Archive orkeon-<version>-win-x64.zip
cd orkeon-<version>-win-x64
.\install.ps1           # installs to %LOCALAPPDATA%\Programs\Orkeon, updates user PATH
```

Archives are produced by `scripts/package-installers.sh` (any OS target can be
built from Linux/macOS) and attached automatically to releases by the
`release.yml` workflow on `v*` tags.

---

## Features

| Capability | Details |
|---|---|
| **70+ built-in tools** | File system, web scraping (AngleSharp), HTTP APIs, JSON/CSV/XML/PDF, databases, secure code execution, RAG and semantic search, EventHub messaging, RaggableTree code analysis, delegation/collaboration — see the [tool inventory](docs/tools/inventory.md) |
| **11 LLM providers** | OpenAI, Ollama, Anthropic, Azure OpenAI, Groq, Together AI, Qwen, DeepSeek, Kimi (Moonshot), HuggingFace, and Mistral AI — all HTTP-based, extending `HttpLlmProviderBase` |
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

For a detailed walkthrough, see the [documentation index](docs/INDEX.md).

---

## Why Orkeon?

| Feature | Orkeon | Semantic Kernel |
|---|---|---|
| Language | C# / .NET 10 | C# / .NET 8+ |
| Multi-Agent | Native crews | Plugins |
| Tools | 70+ built-in | Plugin-based |
| Architecture | Clean Architecture | Kernel pattern |
| LLM Providers | 11 built-in | 3+ via connectors |

---

## Project Status

Orkeon is in **0.9.0-beta** on .NET 10, working toward V1. Recent additions include the plugin system, the `Orkeon.Hosting` bootstrap package, Roslyn source generators, execution-state persistence with resume, Consensual voting strategies, key rotation, and native vision support.

Every pull request is gated in CI:

- merged line coverage must stay at or above **70 %** (scheduled to rise to 75 %)
- a **blocking SonarQube quality gate** ("Orkeon Transitional") with a documented hardening trajectory — see the [quality-gate policy](docs/guides/quality-gate.md)

Known constraints are tracked in [docs/reference/limitations.md](docs/reference/limitations.md).

---

## Docker

Run Orkeon with Docker and Ollama (free local LLM):

```bash
# Using Ollama (free, local)
docker compose up

# Using OpenAI
OPENAI_API_KEY=your-key docker compose up
```

Build the image manually:

```bash
docker build -t orkeon .
docker run -e OPENAI_API_KEY=your-key orkeon
```

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
