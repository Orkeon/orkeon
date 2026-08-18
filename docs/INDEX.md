> 🇫🇷 [Version française](./fr/INDEX.md)

# Orkeon Documentation

## Structure

The documentation is organized into 6 thematic sections.

### Getting started

| File | Description |
|------|-------------|
| [Three ways to run Orkeon](./getting-started/three-ways-to-run-orkeon.md) | Hub page: from source vs. release binary vs. container — prerequisites, commands, and a comparison table |
| [Run your first example (from source)](./getting-started/run-your-first-example.md) | End-to-end first run: prerequisites, LLM profile matrix, the full crew command, every runner flag, settings resolution, troubleshooting |
| [Overview](./getting-started/overview.md) | Architecture, core concepts (Agent, Task, Tool, Crew), YAML vs Fluent Builder |
| [Bootstrap and execution](./getting-started/bootstrap.md) | Dependency injection, running a Crew, batch/streaming/fire-and-forget modes |
| [YAML, Builders and CrewFactory](./getting-started/yaml-and-builders.md) | Fluent Builders, YAML schema, CrewFactory pipeline, loading modes |

### Architecture

| File | Description |
|------|-------------|
| [LLM providers](./architecture/llm-providers.md) | 12 providers (OpenAI, Anthropic, Azure, Groq, Ollama, etc.), adapters, factory, real-API campaign kit |
| [Memory system](./architecture/memory-system.md) | 5 memory types, 6 providers (InMemory, Redis, SQLite, ChromaDB, Pinecone, LanceDB), cognitive memory |
| [Events, CQRS and observability](./architecture/domain-events.md) | 41 domain events, CQRS pipeline, 2-level callbacks |
| [EventHub and crew lifecycle](./architecture/event-hub-and-crew-lifecycle.md) | Reference specification for inter-agent and inter-crew messaging (EventHub) and crew sleep/wake — Application ports, InMemory + SQLite adapters |
| [Security, resilience and plugins](./architecture/security.md) | 7 security layers, Polly policies, checkpointing, plugin system |
| [VFS compliance](./architecture/vfs-compliance.md) | VFS-only principle (all I/O via `IFileSystemService`): `Orkeon.Compliance.Vfs` Roslyn analyzer, 5 diagnostics, exempted scopes, migration exit criteria |
| [Plugin system](./architecture/plugins.md) | `IOrkeonPlugin` contract, VFS discovery, `AssemblyLoadContext` isolation, opt-in activation `AddOrkeonPlugins`, ⚠️ trust boundary |
| [Scripting DSL](./architecture/scripting.md) | TypeScript-syntax DSL (`.ork.ts`): esbuild transpilation, sandboxed Jint execution, the full Orkeon surface (agents, crews, tools, FSM, graphs, events) via fluent builders |
| [TypeScript CLI commands](./architecture/cli-ts-commands.md) | Interactive REPL commands in `*.cmd.ts` (`defineCommand`) loaded at startup without .NET recompilation, with work dispatch to agents |
| [TypeScript coding agent](./architecture/coding-agent-ts.md) | Agentic coding agent built on the scripted stack: `*.cmd.ts` control plane vs `crew.ork.ts` engine, C# `ToolBase` tools |
| [YAML reference](./architecture/yaml-schema.md) | **Single source** of the complete YAML schema (crew, agents, tasks, circuitBreaker, graphConfig, autonomousBudget) |
| [RaggableTree — semantic graph](./architecture/raggable-tree.md) | 6-phase pipeline, 15 tools, 5 languages, incremental reindexing, watcher, context injection |
| [RAG pipeline](./architecture/rag-pipeline.md) | The `src/rag/` subsystem: ingestion, 7-stage pipeline (transform → retrieve → fuse/MMR → rerank → assemble → generate → groundedness), corrective CRAG graph, web fallback, 5 profiles, measured evaluation |
| [ADR — RaggableTree](./architecture/raggable-tree-adr.md) | Decision for a 6-level stratified graph via Tree-sitter, rejected alternatives, consequences |
| [Architecture Decision Records (ADR)](./adr/) | ADR-002 (Tools.Abstractions shared kernel), ADR-003 (Analysis shared kernels), ADR-004 (scripting naming twins — superseded by ADR-007), ADR-005 (heterogeneous Tools.* family), ADR-006 (RAG subsystem `src/rag/`), ADR-007 (D3: `Orkeon.Cli.Commands.Scripting` rename) |

### Orchestration

| File | Description |
|------|-------------|
| [ProcessTypes comparative guide](./orchestration/process-types.md) | The 6 strategies side by side: matrix, decision tree, pros/cons, costs |
| [FSM — State machine](./orchestration/fsm.md) | Intra-task orchestration, circuit breaker with 4 mechanisms, presets, guards |
| [Graph — State graph](./orchestration/graph.md) | LangGraph-style inter-task orchestration, conditional edges, controlled cycles, retry |
| [Autonomous — Self-organization](./orchestration/autonomous.md) | Multi-dimensional budget, recursive delegation, dynamic spawn, A2A communication |

### Tools

| File | Description |
|------|-------------|
| [Tool inventory](./tools/inventory.md) | 36+ tools by category, YAML resolution, DI registration, identified gaps |
| [Creating a new tool](./tools/new-tool-pattern.md) | Typed pipeline, FieldSchema/ReturnSchema attributes, composition pattern, registration |

### Guides

| File | Description |
|------|-------------|
| [Porting methodology](./guides/porting-methodology.md) | 5 steps to migrate an application, YAML-first vs Code-first, effort estimation |
| [Porting example](./guides/porting-example.md) | Complete e-commerce pipeline: analysis, agent mapping, YAML, C# bootstrap |
| [New orchestration blueprint](./guides/blueprint.md) | 8-step template to add a new ProcessType to the framework |
| [Multi-modal content (vision)](./guides/multimodal.md) | Real vision (R3.9): `MultiModalContent` → `LlmMessage` → Anthropic (image blocks) / OpenAI (`image_url`) payloads, VFS loader, opt-in activation |
| [LLM response format](./guides/llm-response-format.md) | Forced JSON output at the provider boundary (`response_format: json_object`), 5-level override cascade (crew → agent → task → script → call), first wired provider: DeepSeek |
| [SonarQube Quality Gate](./guides/quality-gate.md) | Blocking "Orkeon Transitional" gate (R5.4): transitional thresholds, hardening trajectory, automatic provisioning by the scripts |
| [Local models](./guides/local-models.md) | Run everything on your own machine: Docker Model Runner (pull/configure/inspect, 128K contexts), Ollama, embedded `local-llm` image variant, model switching, troubleshooting |

### Reference

| File | Description |
|------|-------------|
| [Examples catalog](./reference/examples-catalog.md) | Editorial map of `examples/` (9 business categories + RAG/RaggableTree/scripting showcases); the generated `examples/INDEX.md` is the authoritative inventory |
| [Limits and constraints](./reference/limitations.md) | Known constraints of the current version |
| [Experimental APIs](./reference/experimental-apis.md) | `[Experimental]` surfaces (A2A, Autonomous, corrective RAG, MCP), `ORKEXP001–004` diagnostic IDs, how to opt in |
| [Example data policy](./reference/example-data-policy.md) | Why examples ship config not datasets, how to mount your own input (`/data:ro`, `/output:rw`), and contributor rules for bundled sample fixtures |
| [Opt-in subsystems](./reference/opt-in-subsystems.md) | A2A, monitoring, NIST, DLP, tool rate-limiting, key rotation, benchmarking, multi-modal, kickoff hooks, RAG subsystem — explicit activation `AddOrkeonXxx()` (outside default DI) |
| [Hosting & runner bootstrap](./reference/hosting.md) | `Orkeon.Hosting`: `RunnerHost.Build`, `ConfigureRunnerServices` wiring order (LLM-first, tool suites, VFS, `ServiceProviderToolRegistry`), `RunnerExecution` flows, and the web-host consumption pattern |
| [Example README template](./templates/example-readme.md) | Gabarit for `examples/**/README.md`: What it does / Prerequisites / Required data / Run it (per way) / Expected output / Duration & cost |
| [LLM provider comparison](./arkeon/llm-providers-comparatif.md) | Capability matrix per provider (SSE streaming, native tool calling, GBNF grammar, `response_format`, thinking, metrics, resilience), derived from the source code |

---

## Recommended reading paths

### "I want to understand the framework"

```
overview → yaml-and-builders → process-types → fsm → graph → autonomous → inventory → new-tool-pattern
```

Start with the overview to absorb Agent, Task, Crew, Tool. Then explore YAML configuration and the builders. The ProcessTypes comparative guide gives a global view of the 6 strategies; the FSM/Graph/Autonomous docs dive into the advanced modes. The tool inventory shows the native capabilities. Finish with the tool creation pattern to understand extensibility.

### "I have an application to migrate"

```
overview → bootstrap → inventory → porting-methodology → porting-example → new-tool-pattern
```

Absorb the architecture and the DI setup. Identify the available tools. Apply the methodology with the concrete example. Come back to the tool pattern if custom tools are needed.

### "I want to extend the framework"

```
overview → new-tool-pattern → yaml-and-builders → blueprint → inventory
```

Understand the architecture, then master the typed pipeline and the composition pattern. The blueprint guides the creation of new orchestration modes. The inventory serves as a reference to position contributions.

---

## Migration from the legacy documentation

The legacy documentation (`docs/arkeon/` folder) has been reorganized as follows:

| Legacy file | New file(s) |
|-------------|-------------|
| `01_OVERVIEW.md` | `getting-started/overview.md` + `getting-started/bootstrap.md` |
| `02_FEATURES.md` | Split into 8 thematic files (see above) |
| `03_TOOLS_INVENTORY.md` | `tools/inventory.md` |
| `04_NEW_TOOL_PATTERN.md` | `tools/new-tool-pattern.md` |
| `05_PORTING_METHODOLOGY.md` | `guides/porting-methodology.md` |
| `06_PORTING_EXAMPLE.md` | `guides/porting-example.md` |
| `07_FSM_ORCHESTRATION.md` | `orchestration/fsm.md` |
| `08_GRAPH_ORCHESTRATION.md` | `orchestration/graph.md` |
| `09_AUTONOMOUS_ORCHESTRATION.md` | `orchestration/autonomous.md` |
| `10_PROCESS_TYPES.md` | `orchestration/process-types.md` |
| `BLUEPRINT_NEW_ORCHESTRATION.md` | `guides/blueprint.md` |
