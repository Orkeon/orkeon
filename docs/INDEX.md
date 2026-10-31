> 🇫🇷 [Version française](./fr/INDEX.md)

# Orkeon Documentation

## Structure

The documentation is organized into 7 thematic sections.

> **Language policy** — every page under `docs/` is maintained in English and French in parallel (`docs/fr/` mirrors the tree path-for-path); a missing mirror fails CI (`scripts/check-docs-parity.sh`). See [CONTRIBUTING.md](../CONTRIBUTING.md) for the contract.

> **Online** — this tree and the generated API reference are published at <https://orkeon.github.io/orkeon/>, deployed by `docs.yml` on every `v*` tag. The site therefore always documents a tagged version.

### Getting started

| File | Description |
|------|-------------|
| [Three ways to run Orkeon](./getting-started/three-ways-to-run-orkeon.md) | Hub page: from source vs. release binary vs. container — prerequisites, commands, and a comparison table; how to update, to the latest release or to the latest `main` (dev channel) |
| [Run your first example (from source)](./getting-started/run-your-first-example.md) | End-to-end first run: prerequisites, LLM profile matrix, the full crew command, every runner flag, settings resolution, troubleshooting |
| [Overview](./getting-started/overview.md) | Architecture, core concepts (Agent, Task, Tool, Crew), YAML vs Fluent Builder |
| [Bootstrap and execution](./getting-started/bootstrap.md) | Packages, DI wiring for an embedding host (model, VFS mounts, tool registry), running a Crew, batch/streaming/fire-and-forget modes |
| [YAML, Builders and CrewFactory](./getting-started/yaml-and-builders.md) | Fluent Builders, YAML schema, CrewFactory pipeline, loading modes |
| [Default behaviors](./getting-started/default-behaviors.md) | The deliberately-minimal DI defaults (planner, delegator, knowledge store…): what each does, the warn-once signal, and the replacement gesture — and the two services with no working default (VFS, model) |
| [Forge a team from a need](./getting-started/forge-a-team-from-a-need.md) | The Atelier (`orkeon forge`): need → interview → team → sandboxed try → verdict against your own criteria → promotion, sessions resumable on disk; other options (`--read`, `--reference`, `--with-settings`, `forge rename`) |

### E-mail

Giving agents a mailbox is one of the most requested features: the tutorial gets one working in minutes, the guide covers every provider and setting.

| File | Description |
|------|-------------|
| [Give your agents a mailbox](./getting-started/give-your-agents-a-mailbox.md) | Tutorial: a Gmail account, an app password, and an agent that sorts the unread mail into folders and leaves each reply as a draft — nothing is sent |
| [E-mail tools](./guides/email.md) | Give agents a mailbox: Gmail (app password or OAuth2), Hotmail/Outlook.com through Microsoft Graph, your own IMAP/POP3/SMTP server; per-account rights, the fail-closed send allow-list, prompt-injection screening, `orkeon email login`, where the tokens live, troubleshooting |
| [`orkeon email` commands](./reference/cli.md#orkeon-email) | `accounts` (what is declared, what is ready), `check` (connect and list the folders), `login` / `logout` for OAuth2 accounts — options and exit codes |
| [ADR-012 — the e-mail tool family](./adr/ADR-012-email-tool-family.md) | Why e-mail lives in Orkeon itself, and the alternatives rejected |

### Cloud drives

| File | Description |
|------|-------------|
| [Drive tools](./guides/drive.md) | Give agents a Google Drive or a OneDrive: registering the OAuth application, `orkeon drive login`, rights, the root folder an agent cannot leave, the closed share allow-list, what the token never reaches |
| [`orkeon drive` commands](./reference/cli.md#orkeon-drive) | `accounts`, `check` (find the root folder and list it), `login` / `logout` — options and exit codes |
| [ADR-013 — the drive tool family](./adr/ADR-013-drive-tool-family.md) | Why a tool family rather than a VFS mount, and the OAuth layer the two cloud families share |
| [ADR-014 — shell confinement by default](./adr/ADR-014-shell-confinement-by-default.md) | Why `shell_command` is confined per platform by default, off in containers, and runs one shell session per agent run |
| [ADR-015 — model provider categories and decision stages](./adr/ADR-015-model-provider-categories-and-decision-stages.md) | Why model providers come in categories (generation, embedding, reranking, decision), why TypeSafe has its own `IDecisionModel` port, and why a decision stage judges an LLM call without rewriting it |
| [ADR-016 — decision tasks and conditional fan-out](./adr/ADR-016-decision-tasks-and-conditional-fan-out.md) | Why a decision task answers without an agent, why a `when` condition skips a task instead of failing it, and which processes support them |

### Architecture

| File | Description |
|------|-------------|
| [LLM providers](./architecture/llm-providers.md) | 16 providers (OpenAI, Anthropic, Azure, Grok, Ollama, the OpenRouter and Mammouth aggregators, etc.), capabilities and dialect seams, provider resolution, decorators and LLM exchange logging, real-API campaign kit |
| [Model providers](./architecture/model-providers.md) | The four categories of model providers (generation, embedding, reranking, decision), their contracts and the `ModelProviderCatalog` |
| [Decision models](./architecture/decision-models.md) | TypeSafe's Jev behind `IDecisionModel`: typed questions, decision profiles that block, route or escalate an LLM call (cascade, router), the input guard, the `decision` reranker and `system_one_decide` |
| [Memory system](./architecture/memory-system.md) | 5 memory types, 6 providers (InMemory, Redis, SQLite, ChromaDB, Pinecone, LanceDB), cognitive memory |
| [Events, CQRS and observability](./architecture/domain-events.md) | 23 domain events — the tasks' and agents' as a run goes, the crew's at the end of each kickoff, and by the CQRS commands —, the CQRS pipeline, 2 observation surfaces (`ICrewExecutionHook`, `ICallbackHandler` with a step per tool call) |
| [EventHub and crew lifecycle](./architecture/event-hub-and-crew-lifecycle.md) | Reference specification for inter-agent and inter-crew messaging (EventHub) and crew sleep/wake — Application ports, the in-memory adapter and its five middleware stages (the shipped hosts wire only the ACL), the agent tools; SQLite persistence and sleep/wake are specified, not built yet |
| [The service host and the chat gateway](./architecture/service-host.md) | `orkeon-host`: hosting crews as a daemon (systemd, Windows service, container), per-run isolation, and the Discord gateway with its allow list, thread-is-run routing and stop button |
| [The run event bus](./architecture/run-event-bus.md) | `orkeon run --events jsonl`: the versioned protocol another program reads, the commands it can send back, and the `client://` seat it gets at the run's hub |
| [Security, resilience and plugins](./architecture/security.md) | 6 security layers, Polly policies, checkpointing, plugin system |
| [VFS compliance](./architecture/vfs-compliance.md) | VFS-only principle (all I/O via `IFileSystemService`): `Orkeon.Compliance.Vfs` Roslyn analyzer, 7 diagnostics, exempted scopes, migration exit criteria |
| [Console compliance](./architecture/console-compliance.md) | No `System.Console` in framework code: the `ORKCON001` analyzer, the console adapters and the one sink per executable, `IRunnerConsole` / `ITerminalProbe`, the `Orkeon.Operator` warning channel |
| [Plugin system](./architecture/plugins.md) | `IOrkeonPlugin` contract, VFS discovery, `AssemblyLoadContext` isolation, opt-in activation `AddOrkeonPlugins`, ⚠️ trust boundary |
| [Scripting DSL](./architecture/scripting.md) | TypeScript-syntax DSL (`.ork.ts`): esbuild transpilation, sandboxed Jint execution, the full Orkeon surface (agents, crews, tools, FSM, graphs, events) via fluent builders |
| [TypeScript CLI commands](./architecture/cli-ts-commands.md) | Interactive REPL commands in `*.cmd.ts` (`defineCommand`) loaded at startup without .NET recompilation, with work dispatch to agents |
| [MCP client & server](./architecture/mcp.md) | Dual-era Model Context Protocol integration (`2026-07-28` stateless + legacy revisions), transports, activation, MCP tools and crews, honest gaps |
| [Orkeon Studio](./architecture/studio.md) | The graphical/terminal front-ends over the CLI workflows: the four projects, the shared core, localization, how it ships |
| [Driving crews from the REPL](./architecture/coding-agent-ts.md) | The `.cmd.ts` → `crew.ork.ts` bridge: `*.cmd.ts` control plane vs `crew.ork.ts` engine over the `script-host` service, `ctx.llm.act` as an agent loop, the permission gate and the session ports — with a keyless REPL session you can run |
| [YAML reference](./architecture/yaml-schema.md) | **Single source** of the complete YAML schema (crew, agents, tasks, graphConfig) |
| [RaggableTree — semantic graph](./architecture/raggable-tree.md) | 6-phase pipeline, 28 tools, 12 languages, incremental reindexing, lazy freshness, hybrid search, context injection, what the hosts actually wire |
| [RAG pipeline](./architecture/rag-pipeline.md) | The `src/rag/` subsystem: ingestion, 7-stage pipeline (transform → retrieve → fuse/MMR → rerank → assemble → generate → groundedness), corrective CRAG graph, web fallback, 5 profiles, measured evaluation |
| [ADR — RaggableTree](./architecture/raggable-tree-adr.md) | Decision for a stratified graph via Tree-sitter (5 levels L0–L4 plus the edge layer), rejected alternatives, consequences, 2026-09-30 implementation-status amendment |
| [Architecture Decision Records (ADR)](./adr/README.md) | [ADR-002](./adr/ADR-002-tool-abstractions-shared-kernel.md) (Tools.Abstractions shared kernel), [ADR-003](./adr/ADR-003-shared-kernels-secondaires.md) (Analysis shared kernels), [ADR-004](./adr/ADR-004-jumeaux-de-nommage-scripting.md) (scripting naming twins — superseded by ADR-007), [ADR-005](./adr/ADR-005-famille-tools-heterogene.md) (heterogeneous Tools.* family), [ADR-006](./adr/ADR-006-rag-subsystem.md) (RAG subsystem `src/rag/`), [ADR-007](./adr/ADR-007-d3-renommage-cli-commands-scripting.md) (D3: `Orkeon.Cli.Commands.Scripting` rename), [ADR-008](./adr/ADR-008-virtual-paths-are-the-only-currency.md) (virtual paths are the only currency agents are paid in), [ADR-009](./adr/ADR-009-shared-constants-satellites.md) (satellites of shared constants), [ADR-010](./adr/ADR-010-agent-framework-interop.md) (Microsoft Agent Framework interop as a separate package, both directions), [ADR-011](./adr/ADR-011-aspire-dashboard-observability.md) (the Aspire dashboard is the observability surface; no web Studio), [ADR-012](./adr/ADR-012-email-tool-family.md) (the e-mail tool family, the second motivated exception to the scope freeze), [ADR-013](./adr/ADR-013-drive-tool-family.md) (the drive tool family), [ADR-014](./adr/ADR-014-shell-confinement-by-default.md) (shell confinement by default), [ADR-015](./adr/ADR-015-model-provider-categories-and-decision-stages.md) (model provider categories and decision stages), [ADR-016](./adr/ADR-016-decision-tasks-and-conditional-fan-out.md) (decision tasks and conditional fan-out) |

### Orchestration

| File | Description |
|------|-------------|
| [ProcessTypes comparative guide](./orchestration/process-types.md) | The 6 strategies side by side: matrix, decision tree, pros/cons, costs |
| [FSM — State machine](./orchestration/fsm.md) | Generic Domain FSM engine (used by `orkeon forge`; its circuit-breaker policy bounds Graph and the corrective RAG graph), 4 mechanisms, presets, guards |
| [Graph — State graph](./orchestration/graph.md) | The Graph process mode (fixed topology, 3-mechanism circuit breaker) and the LangGraph-style C# `StateGraph` API: conditional edges, controlled cycles, retry |
| [Autonomous — Self-organization](./orchestration/autonomous.md) | Multi-dimensional budget, manager-assigned tasks, delegation to a peer on failure, host-provided spawn, A2A channel |

### Tools

| File | Description |
|------|-------------|
| [Tool inventory](./tools/inventory.md) | 120 tools by category with their call parameters, YAML resolution (strict/lenient), DI registration, availability per host, identified gaps |
| [Creating a new tool](./tools/new-tool-pattern.md) | Typed pipeline, FieldSchema/ReturnSchema attributes, composition pattern, registration |

### Guides

| File | Description |
|------|-------------|
| [Write a crew in TypeScript](./guides/write-a-crew-in-typescript.md) | The `.ork.ts` DSL end to end: the two script shapes and why picking the wrong one drops half of what you wrote (loudly, since rc.3): tasks, agents, tools, the `withContext` DAG, deliverables, `ctx.llm.act`, state, editor setup |
| [Porting methodology](./guides/porting-methodology.md) | 5 steps to migrate an application, YAML-first vs Code-first, effort estimation |
| [Porting example](./guides/porting-example.md) | Complete e-commerce pipeline: analysis, agent mapping, YAML, C# bootstrap |
| [New orchestration blueprint](./guides/blueprint.md) | 8-step template to add a new ProcessType to the framework |
| [Multi-modal content (vision)](./guides/multimodal.md) | Real vision (R3.9): `MultiModalContent` → `LlmMessage` → Anthropic blocks / OpenAI parts (14 compatible providers) / Ollama images, VFS loader, opt-in activation |
| [LLM response format](./guides/llm-response-format.md) | Constrained output at the provider boundary (`json_object` / `json_schema`), capability-driven translation per provider, cascade crew → agent → task → script → call |
| [SonarQube Quality Gate](./guides/quality-gate.md) | Local SonarQube analysis with the "Orkeon Transitional" gate: transitional thresholds, hardening trajectory, automatic provisioning by the scripts |
| [Local models](./guides/local-models.md) | Run everything on your own machine: Docker Model Runner (pull/configure/inspect, 128K contexts), Ollama, embedded `local-llm` image variant, model switching, troubleshooting |
| [Verify what you install](./guides/verify-what-you-install.md) | What the provenance chain proves and does not (OIDC Trusted Publishing, SLSA attestations, immutable Releases, `SHA256SUMS`, SBOM), the exact `gh attestation verify` / `dotnet nuget verify` commands, the token-free API query, and why a nuget.org package must be un-signed before its digest matches |
| [Install with apt](./guides/install-with-apt.md) | Debian / Ubuntu (amd64, arm64): add the signed Orkeon apt source once, then `apt install` / `apt upgrade`; the `stable`, `rc` and `dev` channels, going back to a version, removal, Docker builds, troubleshooting |
| [Whisper through MCP](./guides/whisper-mcp.md) | Transcribe audio from `orkeon-repl`, `orkeon run` or `orkeon-host`: a local faster-whisper behind an MCP server, its `MCP:Servers` and mount settings, the agent side, and the Orkeon limits the server is written around (paths, text-only results, the 4000-character cap, the 30 s handshake) |

### Reference

| File | Description |
|------|-------------|
| [Scripting DSL reference](./reference/scripting-dsl.md) | Every `.ork.ts` builder and method, with the column that exists nowhere else: which of the two shapes honours it. Plus the known gaps between the typings and the runtime |
| [Examples catalog](./reference/examples-catalog.md) | Editorial map of `examples/` (9 business categories + RAG/RaggableTree/scripting showcases); the generated `examples/INDEX.md` is the authoritative inventory |
| [`orkeon` CLI reference](./reference/cli.md) | Every command (`run`, `init`, `llm`, `rag`, `forge`, `usecases`, `email`, `drive`, `doctor`, `settings`, `typings`, `mcp`) with options and examples, plus `orkeon-repl` |
| [Configuration reference](./reference/configuration.md) | The single map of the settings (`Llm`, `Orkeon:*`, `MCP`…): sources and precedence, then every key by category — one line per key, with its type, its default and the hosts that read it |
| [Limits and constraints](./reference/limitations.md) | Known constraints of the current version, including what ships but is not wired yet |
| [A2A conformance matrix](./reference/a2a-conformance.md) | Honest position vs the A2A v1.0 spec: operations, data model, bindings, security — what interoperates and what does not |
| [Experimental APIs](./reference/experimental-apis.md) | `[Experimental]` surfaces (A2A, Autonomous, corrective RAG, MCP), `ORKEXP001–004` diagnostic IDs, how to opt in |
| [Example data policy](./reference/example-data-policy.md) | Why examples ship config not datasets, how to mount your own input (`/data:ro`, `/output:rw`), and contributor rules for bundled sample fixtures |
| [Opt-in subsystems](./reference/opt-in-subsystems.md) | A2A, monitoring, NIST, DLP, [tool rate-limiting](./reference/configuration.md#toolratelimiting), key rotation, benchmarking, multi-modal, kickoff hooks, RAG subsystem — explicit activation `AddOrkeonXxx()` (outside default DI); plus the building blocks `AddOrkeonInfrastructure()` registers and which of them nothing consumes |
| [Hosting & runner bootstrap](./reference/hosting.md) | `Orkeon.Hosting`: `RunnerHost.Build`, `ConfigureRunnerServices` wiring order (LLM-first, tool suites, VFS, the default `ToolRegistry`), `RunnerExecution` flows, telemetry (OTLP), .NET Aspire (`AddOrkeonHost`, `AddOrkeonCrewRun`), and the web-host consumption pattern |
| [Microsoft Agent Framework interop](./reference/agent-framework-interop.md) | An Orkeon crew as a MAF `AIAgent` (`CrewAgent`, `AddOrkeonAgentFramework`); a MAF agent as an Orkeon agent's model (`WithAgentFrameworkAgent`) or tool (`WithAgentFrameworkTool`) |
| [Example README template](./templates/example-readme.md) | Template for `examples/**/README.md`: What it does / Prerequisites / Required data / Run it (per way) / Expected output / Duration & cost |
| [LLM provider comparison](./reference/llm-providers-comparison.md) | Capability matrix per provider (SSE streaming, native tool calling, GBNF grammar, `response_format`, thinking, metrics, resilience), derived from the source code |
| [Publication matrix](./reference/publication-matrix.md) | **Source of truth** for what ships where: NuGet.org vs GitHub Packages, dotnet tools, installer artifacts, version flow, the dev channel (the latest `main` between two tags) |

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

### "I want my agents to handle e-mail"

```
give-your-agents-a-mailbox → email (guide) → cli (orkeon email) → security
```

Get one mailbox working end to end with the tutorial, then open the guide for your provider (Gmail OAuth2, Outlook.com/Hotmail through Microsoft Graph, your own IMAP/POP3/SMTP server), the rights model and the send allow-list. The CLI reference lists the `orkeon email` commands; the security page places the prompt-injection screen among the other layers.
