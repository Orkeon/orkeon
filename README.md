> 🇫🇷 [Version française](README.fr.md)

# <img src="docs/assets/orkeon-mascot.png" alt="Orkeon mascot — a curious chameleon" width="96" align="absmiddle"> Orkeon

**AI agent teams that stay inside the lines — every file, endpoint and budget an agent may touch is declared, then enforced. Described in YAML, TypeScript (`.ork.ts`) or C#; one .NET runtime executes all three.**

[![Release](https://img.shields.io/github/v/release/Orkeon/orkeon?include_prereleases)](https://github.com/Orkeon/orkeon/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.md)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Build](https://github.com/Orkeon/orkeon/actions/workflows/ci.yml/badge.svg)](https://github.com/Orkeon/orkeon/actions/workflows/ci.yml)

---

## The problem

You let a coding agent loose on a repository. It reads what it needs, then writes — a file two directories up, a `~/.config` it had no business in, a `/tmp` script it runs next. Nothing stopped it because nothing was there to stop it: the agent's tools called `File.WriteAllText` on whatever path the model produced.

Orkeon puts the boundary in front of the model, not behind it:

- **A virtual file system.** Agents only ever see virtual paths (`/workspace`, `/output`); each one is a mount you declared, with the rights you gave it (`ro`/`rw`). A path outside a mount is refused before any byte lands on disk.
- **A sandbox for code**, an **execution budget** for autonomy — tool calls, depth, wall time, tokens, spawned agents — and **circuit breakers** for loops. The agent runs out of permission before it runs out of ideas.
- **A Roslyn analyzer for your own code.** [`Orkeon.Compliance.Vfs`](docs/architecture/vfs-compliance.md) is a standalone NuGet package with no Orkeon dependency: add it to any C# project and every direct `System.IO` call is a compile error — the line an agent (or a colleague) would have slipped in does not build.

Around that boundary sits a complete agent-team framework: crews of agents with roles, goals and tools, six orchestration strategies (sequential, hierarchical, parallel, consensual, graph, autonomous), 14 LLM providers, memory, RAG, semantic code analysis — typed end to end, Clean Architecture, .NET 10.

## Try it in two minutes — no API key

A local model, one agent, one writable mount. From a clone of this repository (`git clone --depth 1 https://github.com/Orkeon/orkeon && cd orkeon`), with [Ollama](https://ollama.com) installed and the .NET 10 SDK:

<!-- quickstart:begin -->
```bash
ollama pull llama3.2:1b
dotnet tool install -g Orkeon.Scripting.Cli --prerelease
mkdir -p out && orkeon run examples/quickstart/crew.yaml --mount ./out:/output:rw
```
<!-- quickstart:end -->

The agent writes `./out/hello.md` — and only there: `/output` is the single mount, `rw`. Change the crew's task to write anywhere else and watch the file-system service refuse it. The block above is executed literally by CI on every change ([Quickstart workflow](https://github.com/Orkeon/orkeon/actions/workflows/quickstart.yml)): if it stops working, the build goes red before you find out. Everything about local models — Docker Model Runner, Ollama, a model baked into the container image — is in the [local models guide](docs/guides/local-models.md).

## Forge a team from a need

You do not have to write the crew. Describe the need; `orkeon forge` interviews you, drafts the team, renders it (YAML or `.ork.ts`), validates it, **runs it in a sandbox**, diagnoses the run and asks for your verdict — then promotes the result into your project when you say so:

```bash
orkeon init                                  # once: pick a provider and a model (Ollama included)
orkeon forge "a team that triages the issues of a GitHub repository every morning"
orkeon forge list                            # every session on disk, resumable
orkeon forge promote <slug> --to ./crews     # adopt the crew that passed
```

The cycle is *brief → blueprint → render → validate → test → diagnose → verdict*, with sessions you can resume, edit and re-test. It needs a configured model: without one it stops at the door with `FORGE-LLM-UNAVAILABLE` and points you to `orkeon init`. Walkthrough: [Forge a team from a need](docs/getting-started/forge-a-team-from-a-need.md).

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
| **Install the `orkeon` CLI** | Windows and Debian/Ubuntu: the quickstarts below. macOS: the CLI tarball below (`osx-arm64`, `osx-x64`). `linux-arm64` — and anyone who also wants the REPL or the service host — takes the multi-app `orkeon-<version>-<rid>.tar.gz` from the [releases](https://github.com/Orkeon/orkeon/releases), then `./install.sh` | [Release binaries](docs/getting-started/three-ways-to-run-orkeon.md#2-release-binary) |
| **Embed Orkeon in your app** | `dotnet add package Orkeon --prerelease` — the complete framework in one package. Optionally add `Orkeon.Tools` (the built-in tool families) and the opt-ins (`Orkeon.Rag.Onnx`, `Orkeon.Tools.Embeddings.Local` — the latter pins a pre-release upstream, `SmartComponents.LocalEmbeddings`, and will keep doing so past 1.0: see [limitations](docs/reference/limitations.md)) — see the [publication matrix](docs/reference/publication-matrix.md). The `orkeon` CLI tool and the container image above are unchanged | [Bootstrap and execution](docs/getting-started/bootstrap.md) |
| **Verify what you download** | Every package and installer carries a GitHub-signed build provenance attestation and a `SHA256SUMS` line: `gh attestation verify <file> --repo Orkeon/orkeon` — no trust in this page required | [Verify what you install](docs/guides/verify-what-you-install.md) |
| **Hack on the framework** | `git clone` (**without** `--recursive`) + `dotnet build Orkeon.sln` | [From source](docs/getting-started/three-ways-to-run-orkeon.md#1-from-source) · [Contributing](#contributing) |

> **Clone without `--recursive`.** This repository declares **private maintainer
> submodules**: they are not available in a public clone. Nothing in the
> build, the tests or the contribution workflow needs them, and a failing
> `git submodule update` on those paths is expected and harmless — see
> [CONTRIBUTING.md](CONTRIBUTING.md).

**Windows** — download `orkeon-cli-<version>-win-x64.zip` (or the `.msi`) from the [releases](https://github.com/Orkeon/orkeon/releases); it is self-contained, no .NET needed:

```powershell
# Verify the download first: every release ships a SHA256SUMS asset (SHA256SUMS.msi for the .msi)
(Get-FileHash orkeon-cli-<version>-win-x64.zip -Algorithm SHA256).Hash.ToLower()
Select-String -Path SHA256SUMS -Pattern 'win-x64\.zip'   # the two hashes must match

Expand-Archive orkeon-cli-<version>-win-x64.zip -DestinationPath .; cd orkeon-cli-<version>-win-x64
.\install.ps1        # or: msiexec /i orkeon-<version>-win-x64.msi -- pick one channel, not both
orkeon init          # in a NEW terminal: pick your LLM provider and model
orkeon run crew.yaml
```

**Debian / Ubuntu** — download `orkeon_<version>_amd64.deb`; self-contained too, no `dotnet-runtime` package pulled in:

```bash
# SHA256SUMS is a release asset too — download it alongside the .deb and check
grep " orkeon_<version>_amd64.deb$" SHA256SUMS | sha256sum --check   # expect: OK

sudo apt install ./orkeon_<version>_amd64.deb
orkeon init          # writes ~/.config/Orkeon/appsettings.json
orkeon run crew.yaml
```

**macOS** — Homebrew becomes the recommended route once the `Orkeon/homebrew-tap` repository ships with the first tagged release:

```bash
brew tap orkeon/tap && brew install orkeon    # once the tap is published
orkeon init
orkeon run crew.yaml
```

Until then (and on any machine), the self-contained tarball — `osx-arm64` for Apple Silicon, `osx-x64` for Intel. `install.sh` clears the Gatekeeper quarantine attribute for you:

```bash
# The asset name carries the version, and GitHub's `latest/download/` shortcut skips
# prereleases — so resolve the newest tag first (or copy the asset link off the
# releases page, which is the same thing done by hand).
TAG=$(curl -fsSL https://api.github.com/repos/Orkeon/orkeon/releases | grep -m1 '"tag_name"' | cut -d'"' -f4)
VER=${TAG#v}; BASE=https://github.com/Orkeon/orkeon/releases/download/$TAG

curl -fsSL -O "$BASE/orkeon-cli-$VER-osx-arm64.tar.gz"     # osx-x64 on Intel
curl -fsSL -O "$BASE/SHA256SUMS"
grep " orkeon-cli-$VER-osx-arm64.tar.gz$" SHA256SUMS | shasum -a 256 --check -   # expect: OK

tar -xzf "orkeon-cli-$VER-osx-arm64.tar.gz" && cd "orkeon-cli-$VER-osx-arm64" && ./install.sh
orkeon init
```

`orkeon doctor` checks the install (runtime, config, LLM reachability, esbuild, grammars) whenever something looks off.

---

## Features

Every number below is recomputed from the tree on each CI run — `bash scripts/count-surface.sh` prints them next to the rule that counts them, and a README that disagrees fails the build.

| Capability | Details |
|---|---|
| **79 built-in tools** | File system, web scraping (AngleSharp), HTTP APIs, JSON/CSV/XML/PDF/Office (DOCX & XLSX read/write), databases, secure code execution, RAG and semantic search, EventHub messaging, RaggableTree code analysis, delegation/collaboration — see the [tool inventory](docs/tools/inventory.md) |
| **14 LLM providers** | OpenAI, Ollama, Anthropic, Azure OpenAI, Mistral AI, DeepSeek, Kimi (Moonshot), Qwen, Together AI, HuggingFace, Z.AI (GLM), Google Gemini, Grok (x.AI), and MiniMax — all HTTP-based, extending `HttpLlmProviderBase`; local models via Docker Model Runner, Ollama, or embedded llama.cpp — see the [local models guide](docs/guides/local-models.md) |
| **Vision / multimodal** | Image content flows end-to-end (`MultiModalContent` → Anthropic image blocks / OpenAI `image_url`) with a VFS-backed loader; opt-in via `AddOrkeonMultiModal(...)` — see the [multimodal guide](docs/guides/multimodal.md) |
| **6 memory providers** | Redis (vector search), SQLite, InMemory, ChromaDB (REST API v2), Pinecone, LanceDB (remote REST server) — one `IMemoryProvider` port, composable decorators |
| **6 orchestration strategies** | Sequential, Hierarchical, Parallel, Consensual (Majority / SuperMajority / Unanimity / WeightedConsensus / BordaCount voting strategies), Graph (LangGraph-style), Autonomous (multi-dimensional execution budget) — see the [process-type guide](docs/orchestration/process-types.md) |
| **Microsoft Agent Framework interop** | `Orkeon.Interop.AgentFramework`: an Orkeon crew runs as a MAF `AIAgent`; a MAF `AIAgent` becomes the brain (`WithAgentFrameworkAgent`) or a tool (`WithAgentFrameworkTool`) of an Orkeon agent — see [ADR-010](docs/adr/ADR-010-agent-framework-interop.md) and `examples/interop/agent-framework/` |
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
| **Opt-in subsystems** | A2A, monitoring, tool rate-limiting, benchmarking, multimodal, kickoff hooks and more — none registered by default, each enabled via its dedicated `AddOrkeonXxx()` extension — see the [opt-in reference](docs/reference/opt-in-subsystems.md) |

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
|  |  |  Pure business logic, one satellite of constants  |   |      |
|  |  +----------------------------------------+   |      |
|  +------------------------------------------------+      |
+----------------------------------------------------------+
```

- **Domain**: Core entities and value objects (`Agent`, `Crew`, `CrewTask`, `IBaseTool`, `ILlmProvider`). No external dependencies.
- **Application**: Use cases and orchestration logic. Defines interfaces (ports) implemented by Infrastructure.
- **Infrastructure**: LLM providers, memory stores, tool implementations, and all external integrations.

Around the core, dedicated projects cover hosting (`Orkeon.Hosting`, plus the `orkeon-host` service daemon of `Orkeon.Host` and the `orkeon run --events jsonl` run event bus), plugins (`Orkeon.Plugins`), Roslyn source generators (`Orkeon.Generators`), the VFS-compliance analyzer (`Orkeon.Compliance.Vfs` — a standalone NuGet package with no Orkeon dependency: `<PackageReference Include="Orkeon.Compliance.Vfs" PrivateAssets="all" />` makes direct `System.IO` a compile error in any C# project), the TypeScript-syntax scripting DSL (`Orkeon.Scripting` plus the `orkeon` CLI tool), the tool families (`Orkeon.Tools.*`, shipped together as the `Orkeon.Tools` package), and the RaggableTree semantic code-analysis engine (`Orkeon.Analysis`, shipped inside the `Orkeon` package).

---

## Documentation

| You are looking for… | Go to |
|---|---|
| **First run, step by step** | [Getting-started overview](docs/getting-started/overview.md) · [Run your first example](docs/getting-started/run-your-first-example.md) |
| **The three ways to run Orkeon** (source / binary / container) | [Three ways to run Orkeon](docs/getting-started/three-ways-to-run-orkeon.md) |
| **Local models** (Docker Model Runner, Ollama, embedded, 128K contexts) | [Local models guide](docs/guides/local-models.md) |
| **The 105 runnable examples** (9 themed categories + `orkeon-example`) | [Examples](https://github.com/Orkeon/orkeon/tree/main/examples) · [Catalog](docs/reference/examples-catalog.md) |
| **Writing crews**: YAML, builders, TypeScript, host wiring, execution | [YAML & builders](docs/getting-started/yaml-and-builders.md) · [Write a crew in TypeScript](docs/guides/write-a-crew-in-typescript.md) · [Bootstrap and execution](docs/getting-started/bootstrap.md) |
| **Orchestration modes** (incl. FSM and graph deep dives) | [Process types](docs/orchestration/process-types.md) · [FSM](docs/orchestration/fsm.md) · [Graph](docs/orchestration/graph.md) |
| **Writing your own tools** | [New tool pattern](docs/tools/new-tool-pattern.md) · [Tool inventory](docs/tools/inventory.md) |
| **Architecture deep dives** (plugins, scripting, VFS, security, RaggableTree) | [Architecture docs](docs/INDEX.md) · [ADRs](docs/adr/README.md) |
| **Everything else** | [Documentation index](docs/INDEX.md) *(also available [in French](docs/fr/INDEX.md))* |

These pages, together with the generated API reference, are published as a browsable site
at **<https://orkeon.github.io/orkeon/>**. `docs.yml` deploys it on every `v*` tag, so the site goes
live with the first tagged release and always documents a tagged version.

---

## Why Orkeon?

- **Three authoring surfaces, one engine** — the same crew can be a YAML file an analyst edits, a TypeScript script a developer iterates on (both run with zero rebuild), or C# embedded in your product. No rewrite when you graduate from one to the next.
- **Orchestration beyond pipelines** — six strategies, including LangGraph-style state graphs with conditional edges and a fully autonomous mode where agents delegate, spawn, and communicate under a multi-dimensional execution budget (tool calls, depth, wall time, tokens, spawns).
- **Batteries included** — 79 tools, 14 LLM providers, 6 memory stores, vision, RAG, code analysis: usable out of the box, replaceable through Clean Architecture ports.
- **Local-first** — every example runs against a model on your own machine (Docker Model Runner, Ollama, or llama.cpp embedded in the container image). No API key required to evaluate it.
- **The boundary is the product** — a rights-audited virtual filesystem in front of every file access, a Roslyn analyzer that refuses raw `System.IO` in your own code, execution budgets and circuit breakers for autonomy, checkpoint and resume for long runs. Rate limiting, monitoring and the rest are one `AddOrkeonXxx()` away.
- **Typed all the way down** — no `Dictionary<string, object>` plumbing; source generators keep the typed surface boilerplate-free.

---

## Project Status

The current version is **1.0.0-rc.3** on .NET 10 — the V1 release candidate (`src/Directory.Build.props` is the single source of truth; the Release badge above and `git tag` say what is tagged). Recent milestones: the NuGet distribution consolidated into a single `Orkeon` package (plus `Orkeon.Tools` and a few opt-ins — see the [publication matrix](docs/reference/publication-matrix.md)); the `orkeon` CLI and the `orkeon-runners` container image with 105 bundled examples and local-model workflows; FSM and Graph orchestration; the Autonomous process with execution budgets; the TypeScript scripting DSL; RaggableTree semantic code analysis (15 agent tools); the plugin system; checkpoint/resume; dual-era MCP client and server; A2A task persistence; a mechanically frozen public API surface; and the LLM provider fleet grown to 14, each under real-execution campaign proof (latest arrivals: Google Gemini, Grok/x.AI, MiniMax).

Every pull request is gated in CI:

- the build compiles with **`-warnaserror` and the full .NET analyzer set** — any new compiler, analyzer, or NuGet-audit warning fails the build
- the **public API surface is frozen** (Microsoft.CodeAnalysis.PublicApiAnalyzers; undeclared API changes are build errors)
- the unit and fast test suites (Integration/Slow suites run nightly in a dedicated workflow) and the EN/FR documentation parity gate; path-filtered gates add the examples linters on PRs touching `examples/`, and a strict docfx build (`--warningsAsErrors`) on PRs touching sources or docs

**Line coverage is measured in public.** The [Coverage workflow](https://github.com/Orkeon/orkeon/actions/workflows/coverage.yml) runs the unit and fast suites under `dotnet-coverage` on every push to `main` (and weekly): the figure is on each run's summary page, the Cobertura file and an HTML report are its `coverage` artefact. No number is quoted here — a number typed into a README is a claim, a run is a measurement. Static analysis runs on a local SonarQube via `scripts/sonar-analyze.sh` under the [quality-gate policy](docs/guides/quality-gate.md); its reports are not tracked by the repository, so its figures are not quoted here either.

Known constraints are tracked in [docs/reference/limitations.md](docs/reference/limitations.md). What happens if the project stops — MIT, a reproducible build, no private infrastructure, forkable by anyone — is written down in [SUPPORT.md](SUPPORT.md#if-the-project-stops).

---

## Contributing

Contributions are welcome. Please open an issue to discuss significant changes before submitting a pull request. Make sure the tests CI runs pass (`dotnet test Orkeon.sln --filter "Category!=Integration&Category!=Slow"` — [CONTRIBUTING.md](CONTRIBUTING.md) explains why the filter is not optional) and that new code follows the Clean Architecture conventions described in [CONTRIBUTING.md](CONTRIBUTING.md).

### Building from source

The scripting layer (`.ork.ts` support) ships a small esbuild toolchain that is bootstrapped on the first `dotnet build`. On a fresh clone the `Orkeon.Scripting` project runs `npm ci` (strictly from the committed `tools/scripting-esbuild/package-lock.json`, so the lockfile is never mutated) under `tools/scripting-esbuild/` to provision `node_modules/`. This is a one-time, network-touching step per clone.

To skip it entirely (e.g. in CI or when packaging NuGet consumers that do not need esbuild), pass the opt-out flag:

```bash
dotnet build Orkeon.sln -p:SkipScriptingNpmInstall=true
```

If npm is unavailable the build still succeeds; esbuild is then resolved from `PATH` at runtime.

---

## Community and support

- **Getting help** — [SUPPORT.md](SUPPORT.md) names the venues: [GitHub Discussions](https://github.com/Orkeon/orkeon/discussions) for questions and show-and-tell, the [issue forms](https://github.com/Orkeon/orkeon/issues/new/choose) for bugs and feature requests. There is no Discord or Slack channel.
- **Reporting a vulnerability** — [SECURITY.md](SECURITY.md). Never a public issue: use GitHub Private Vulnerability Reporting (repository → *Security* → *Report a vulnerability*).
- **Community expectations** — the [Code of Conduct](CODE_OF_CONDUCT.md) (Contributor Covenant 2.1) applies to every space of the project.

---

## License

Orkeon is released under the [MIT License](LICENSE.md).

