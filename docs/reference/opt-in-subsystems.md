> 🇫🇷 [Version française](../fr/reference/opt-in-subsystems.md)

# Opt-in subsystems

> Decision R4.9 (QCM 2026-06-11): the 13 dormant DI ports identified by the audit
> (MORT-003) and the associated subsystems are **kept**, **removed from
> default registration** and **explicitly activatable**. None of these services
> is registered by `AddOrkeonApplication()` or by `AddOrkeonInfrastructure()`
> (both overloads): each one is enabled through its dedicated `AddOrkeonXxx()` extension.

## Principle

These subsystems are complete and unit-tested, but **no production execution
path consumes them yet**: registering them by default bloated the DI container
with no benefit and masked their real status. The move to opt-in makes their
activation **intentional, documented and verifiable**.

Each activation extension is **self-contained**: it registers (via `TryAdd*`)
all the dependencies the subsystem needs that are not already provided by
the host. Prerequisites common to all of them: logging
(`services.AddLogging()`) and, for the subsystems that bind configuration
options, a registered `IConfiguration` (always present in an application
based on `Host`/`WebApplication`).

The recommended order is to call the extension **after** `AddOrkeonInfrastructure()`:
the `TryAdd*` calls then leave priority to the services already wired by the core.

```csharp
services.AddOrkeonApplication();
services.AddOrkeonInfrastructure();

// Activations explicites (exemples)
services.AddOrkeonMonitoring(configuration);
services.AddOrkeonDlp();
services.AddOrkeonA2A(options => options.EnableServer = true);
```

## Catalog

| Subsystem | Activation | Project | Exposed ports | Maturity |
|---|---|---|---|---|
| A2A server/protocol | `AddOrkeonA2A(...)` | Infrastructure | `IA2AServer`, `IA2AClient`, `IA2AAgentDiscovery`, `IA2ATaskRouter` | Beta |
| Monitoring backend | `AddOrkeonMonitoring(...)` | Infrastructure | `IMetricsAggregation`, `ITraceExplorer` | Beta |
| NIST compliance | `AddOrkeonNistCompliance()` | Infrastructure | `INistComplianceReporter` | Experimental |
| DLP (Data Loss Prevention) | `AddOrkeonDlp()` | Infrastructure | `IDlpPolicyProvider`, `IPiiDetector`, `IDlpInterceptor` (×5) | Experimental |
| Tool rate-limiting & token budget | `AddOrkeonToolRateLimiting()` | Infrastructure | `IToolRateLimiter`, `ITokenBudgetTracker` | Experimental |
| Key rotation | `AddOrkeonKeyRotation()` | Infrastructure | `IKeyRotationService` | Beta |
| Evaluation benchmarking | `AddOrkeonBenchmarking()` | Infrastructure | `IBenchmarkRunner` | Beta |
| Multi-modal content (vision) | `AddOrkeonMultiModal(...)` | Infrastructure | `IContentValidationService`, `IMultiModalContentLoader` | Beta (real since R3.9) |
| Kickoff hooks | `AddOrkeonKickoffHooks()` | Application | `ICrewKickoffHookRunner` | Experimental |
| Codebase context (RaggableTree) | `AddRaggableTree(options)` — ⚠️ **runner hosts opt out instead**: `RunnerHost` registers it by default and `RaggableTree:Enabled = false` disables it | Analysis | `ICodebaseContextProvider` | Beta |
| RAG subsystem (RAG-02…06) | `AddOrkeonRag(config)` (namespace `Orkeon.Rag.DependencyInjection`) + `AddOrkeonRagTools()` (`Orkeon.Tools.Rag`); profiles `fast`/`balanced`/`quality`/`adaptive`/`corrective` via `Orkeon:Rag:Profile` (default `fast`); `balanced`/`quality`/`adaptive` need the ONNX cross-encoder — `AddOrkeonOnnxReranker()` (`Orkeon.Rag.Onnx` + `Orkeon.Rag.Onnx.Model`, embedded weights, offline); `corrective` does not (the graph loops instead of reranking); the corrective web fallback is config-only — `AddOrkeonRag` already wires the transport, the two `Enabled` switches govern (`Orkeon:Rag:Corrective:WebFallback` policy, `Orkeon:Rag:WebFallback` transport) — see the detailed section below | Orkeon.Rag.Abstractions / Orkeon.Rag / Orkeon.Tools.Rag / Orkeon.Rag.Onnx / Orkeon.Rag.Onnx.Model | `IRagPipeline` (staged / corrective graph), `IRagProfileResolver`, `IIngestionPipeline`, `IDocumentStore`, `IRagEvalHarness`, `rag_search`/`rag_ingest`/`rag_eval` (`IBaseTool`) | Beta |
| EventHub middleware stages | `AddOrkeonEventHubObservability()` (logging + telemetry) · `AddOrkeonEventHubAcl(policy?)` (enforces the crews' `links:` blocks — **already wired by the runner host and `orkeon-host`, with the permissive default**: a crew that declares no links behaves as before, one that declares is held to them; only the closed policy is an opt-in) · `AddOrkeonEventHubIdempotency(capacity?)` (point-to-point only, in-memory, does not survive the process) · `AddOrkeonEventHubValidation()` (a declared `SchemaId` must be registered — the contract's existence, not the payload's conformance). Each is independent; registration order is the pipeline order — see [EventHub §12](../architecture/event-hub-and-crew-lifecycle.md) | Infrastructure | `IEventHubMiddleware`, `ICrewLinkProvider`/`ICrewLinkRegistry`, `IEventSchemaRegistry` | Beta |
| Execution state persistence (R3.8) | `AddCrewExecutionStatePersistence(...)` | Infrastructure | `ICrewExecutionStateManager` (durable via `IStateStore`) | Beta |
| Permission gate (per tool call) | `AddOrkeonPermissionGate(config)` + `Orkeon:Security:PermissionGate:Enabled = true` | Infrastructure | `IPermissionGate` (`ModePermissionGate`) — consumed by the scripted `ctx.llm.act` loop | Beta |
| Shell interpreters & mutating git | config only: `Orkeon:Tools:Shell:AllowInterpreters = true` | Tools.Code | (re-registers `ShellCommandTool` with `allowInterpreters: true` — RCE-equivalent, security warning emitted) | Beta |
| Shell allowlist customization | config only: `Orkeon:Tools:Shell:ExtraAllowedCommands` (additive) / `Orkeon:Tools:Shell:AllowedCommands` (full replacement — cancels `AllowInterpreters`) | Tools.Code | (shapes the `ShellCommandTool` executable allowlist; absent/empty section = defaults) | Beta |
| Native LLM console streaming | `AddLlmConsoleStreaming(config)` + `Orkeon:Cli:ConsoleStreaming:Enabled = true` | Cli.Scripting | `ILlmDeltaSink` (`ConsoleLlmDeltaSink`) — streamed `ctx.llm.act` deltas rendered on the REPL console | Beta |
| Plugin system | `AddOrkeonPlugins(...)` (never registered implicitly) | Orkeon.Plugins | `IOrkeonPlugin`, `IPluginRegistry` — directory discovery, isolated collectible `AssemblyLoadContext`s; ⚠️ loaded assemblies run with full trust — see [Plugins](../architecture/plugins.md) | Beta |
| Local on-device embeddings | `AddOrkeonLocalEmbeddings()` | Tools.Embeddings.Local | `IEmbeddingProvider` (BGE-micro-v2 ONNX, 384 dims, CPU, no API key) — first link of the embedding resolution chain | Beta |
| External memory providers (LanceDB, Redis) | `AddOrkeonLanceDb(...)` / `AddOrkeonRedisMemory(...)` (or type keys via `MemoryProviderFactory`). ⚠️ ChromaDB and Pinecone are **not** opt-in: `AddOrkeonInfrastructure(configuration)` auto-registers them when their `Orkeon:ChromaDb`/`Orkeon:Pinecone` section exists | Infrastructure | `IMemoryProvider` implementations — see [Memory system](../architecture/memory-system.md) | Beta |
| Cognitive memory | `AddOrkeonCognitiveMemory(...)` | Infrastructure | cognitive memory layering over `IMemoryProvider` | Experimental |

> **Not in this catalog — registered by `AddOrkeonInfrastructure()` and gated by
> configuration, not by a registration gesture**: MCP (`MCP` section, via the
> `IConfiguration` overload), the parameterless checkpointing store
> (`AddOrkeonCheckpointing()`; the SQLite/Postgres variants stay explicit), the
> Guardian pipeline, the Flows engine, Training, Consensus, CostTracking,
> Encryption, Auth and CodeSandbox. Their configuration sections are mapped in
> the [Configuration reference](./configuration.md).

**Maturity** — *Beta*: complete and tested implementation, API likely to evolve
before v1. *Experimental*: functional implementation but not wired into the
execution pipeline (the host invokes the service itself) or behavior still
partial (flagged case by case below).

---

## A2A server/protocol — `AddOrkeonA2A(...)`

> **Task persistence add-on (PUB-08)**: `AddOrkeonA2ATaskPersistence()` stores A2A task
> lifecycles over the opt-in checkpointing `IStateStore` (register one first), turning
> `GET /a2a/tasks/{id}` into a real 200/404 endpoint instead of the explicit 501.

- **Role**: inter-process agent-to-agent communication (A2A protocol):
  agent discovery (`/.well-known/agent.json`), HTTP client, task routing
  and, optionally, an HTTP server (`HttpListener`) exposing `POST /a2a/tasks/send`,
  `sendSubscribe` (SSE), `GET/DELETE /a2a/tasks/{id}`.
- **Activation**:
  ```csharp
  // By configuration (section "A2A"; "A2A:Security" for mTLS/auth)
  services.AddOrkeonA2A(configuration);

  // Or by delegate, without IConfiguration
  services.AddOrkeonA2A(options => options.EnableServer = true);
  ```
  `IA2AServer` is only registered when `EnableServer` is true.
- **Dependencies**: the extension itself registers (TryAdd) `IHttpClientFactory`,
  `IDomainEventDispatcher`, `IUnitOfWork`, as well as the A2A agent directory
  (`IAgentRegistrationStore` singleton + `IAgentRepository` scoped, see below)
  when the host has not already wired them via `AddOrkeonInfrastructure()`.
- **Agent repository lifecycle (R4.6 / ANT-001, "scoped per request" decision)**:
  - `IAgentRepository` is **scoped**; the server and the router (singletons) never
    capture it — they open a **DI scope per A2A request** via
    `IServiceScopeFactory` and resolve the repository inside it. Resolution passes with
    `ValidateScopes = true` (default in Development).
  - A scoped repository keeps nothing between two requests: registrations live
    in `IAgentRegistrationStore`, a **thread-safe singleton backing store**
    (`InMemoryAgentRegistrationStore`, `ConcurrentDictionary`) from which the scoped
    repository (`SharedStoreAgentRepository`) hydrates itself on every call. Agents
    registered by the pipeline (in its own scopes) are therefore visible to the A2A
    router, and vice versa.
  - Composition: if the host called `AddOrkeonInfrastructure()` (which registers the
    `InMemoryAgentRepository` with a **per-scope** store), `AddOrkeonA2A(...)` **overrides
    this default implementation** with `SharedStoreAgentRepository` (same scoped
    lifetime, shared data) — otherwise the router would never find the agents.
    Any **other** `IAgentRepository` registration (custom/DB repository, which already
    provides cross-request storage) is left intact. Call `AddOrkeonA2A(...)` **after**
    `AddOrkeonInfrastructure()` and after any custom repository (recommended order,
    see [Principle](#principle)); a custom repository registered *after* `AddOrkeonA2A(...)`
    wins (last registration).
- **Known limits**: the in-memory store is local to the process — for a multi-instance
  agent directory, provide a custom `IAgentRepository` backed by external shared
  storage (it will be respected as-is by the extension).

## Monitoring backend — `AddOrkeonMonitoring(...)`

- **Role**: in-process aggregation of Orkeon metrics (`MeterListener` on
  `OrkeonMetrics`) and exploration of recent traces (circular buffer) for
  exposure through a host endpoint (dashboard, diagnostic API).
- **Activation**:
  ```csharp
  services.AddOrkeonMonitoring(configuration); // lie Orkeon:Monitoring
  services.AddOrkeonMonitoring();              // default options
  ```
- **Dependencies**: logging only (default options are registered when no
  configuration is passed).
- **Note**: does not replace OpenTelemetry telemetry (`AddOrkeonTelemetry`),
  which remains wired by the `AddOrkeonInfrastructure(IConfiguration)` overload.

## Traces and metrics follow the OpenTelemetry GenAI conventions

Not an opt-in — every run emits them; only the exporter is (`AddOrkeonTelemetry`,
`Orkeon:Telemetry:OtlpEndpoint`). Since 2026-09-11 the execution path itself carries the
spans (they used to live in helpers nothing in production called), named and attributed
by the [OpenTelemetry generative-AI semantic conventions](https://opentelemetry.io/docs/specs/semconv/gen-ai/)
so Langfuse, Honeycomb, Application Insights or the Aspire dashboard read them without a
mapping. The names are the constants of `Orkeon.Constants.Llm.GenAiAttributes`.

| Span (`ActivitySource`) | Name | Attributes |
|---|---|---|
| Agent turn (`Orkeon.Agent`) | `invoke_agent {agent}` | `gen_ai.operation.name=invoke_agent`, `gen_ai.agent.name`, `gen_ai.agent.id`, `gen_ai.provider.name`, `gen_ai.request.model`, `orkeon.task.id` |
| Model call (`Orkeon.Llm`, kind Client) | `chat {model}` | `gen_ai.operation.name=chat`, `gen_ai.provider.name` (lower-case), `gen_ai.request.model`, `gen_ai.request.temperature`, `gen_ai.request.max_tokens`, `gen_ai.response.model`, `gen_ai.response.id`, `gen_ai.response.finish_reasons`, `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens`, `error.type` on failure |
| Tool execution (`Orkeon.Tool`) | `execute_tool {tool}` | `gen_ai.operation.name=execute_tool`, `gen_ai.tool.name`, `gen_ai.tool.call.id` (the model's own id), `gen_ai.agent.name`, `error.type` on failure |
| Scripting runtime (`Orkeon.Scripting`) | the same three, plus `crew.run` | same attributes; `orkeon.llm.method` (`complete`/`act`), `orkeon.crew.name`, `orkeon.crew.process` |

Metrics (`OrkeonMetrics`, meter `Orkeon`): `gen_ai.client.token.usage` (histogram, tokens,
`gen_ai.token.type` = `input` | `output`) and `gen_ai.client.operation.duration` (histogram,
seconds), both tagged with `gen_ai.provider.name` and `gen_ai.request.model`; the
`orkeon.*` counters (calls, tool executions, cost) keep their names. What has no
convention keeps the `orkeon.` prefix — the crew, the task, the estimated cost.

## NIST compliance — `AddOrkeonNistCompliance()`

- **Role**: generation of NIST SP 800-53 compliance reports from the audit
  trail (`IAuditLogger`).
- **Activation**: `services.AddOrkeonNistCompliance();`
- **Dependencies**: the extension TryAdds a fallback audit chain
  (`StructuredLogAuditSink` + `AuditLogger`); when the host has called
  `AddOrkeonInfrastructure()`, the core's audit chain (structured +
  in-memory sinks, `Security:Audit` options) is used.
- **Known limits**: the depth of the report depends on the events actually
  audited by the host; no automated control is executed.

## DLP — `AddOrkeonDlp()`

- **Role**: data loss prevention — PII detection through regular
  expressions (`IPiiDetector`), per-channel policies (`IDlpPolicyProvider`,
  `Orkeon:Dlp` section) and 5 channel interceptors (tool output, delegation, logs,
  memory, external output).
- **Activation**: `services.AddOrkeonDlp();`
- **Dependencies**: logging + `IConfiguration` (binds `Orkeon:Dlp`). Self-contained
  for the rest.
- **Known limits**: the interceptors are **not** invoked automatically by
  the execution pipeline — the host resolves them (`GetServices<IDlpInterceptor>()`) and
  applies them to the channels it wants to protect. Not to be confused with the
  `PiiDetectionValidator` of the output validation pipeline, which remains registered
  by default and is independent.

## Tool rate-limiting & token budget — `AddOrkeonToolRateLimiting()`

- **Role**: per-tool rate limiting (`IToolRateLimiter`, `ToolRateLimiting`
  section) and per-agent/crew token budget tracking
  (`ITokenBudgetTracker`, `TokenBudget` section).
- **Activation**: `services.AddOrkeonToolRateLimiting();`
- **Dependencies**: logging + `IConfiguration`. If `AddOrkeonCostTracking()` (included
  in the core) registered `IModelPricingRegistry`, the tracker uses it for the
  tokens → cost conversion.
- **Known limits**: not wired into tool execution — the host queries the
  limiter/tracker around its tool calls. **LLM** rate-limiting
  (`ILlmRateLimiter`), for its part, remains registered by default because it is
  consumed by the execution orchestrator.

## Key rotation — `AddOrkeonKeyRotation()`

- **Role**: **real** re-encryption (R2.8) of an encrypted memory store's entries during
  a key rotation —
  `IKeyRotationService.RotateAsync(store, oldProvider, newProvider, options?)` — plus the
  generation and persistence of a new key via
  `ProvisionNewKeyAsync(secretStore, secretName, keySizeInBits)`
  (`IWritableSecretProvider`, implemented by `DpapiSecretProvider`).
- **How it works**: two-phase traversal. *Staging*: each entry is decrypted
  with the old key, re-encrypted with the new one and written under a staging key
  (`<key>::rotation-staging`) — the originals remain intact. *Switchover*: each
  re-encrypted copy replaces its original, then the staging is deleted. A state marker
  (`__orkeon.key-rotation.state`, plain JSON) persists the key version and the phase;
  each re-encrypted entry carries the `orkeon-key-version` property.
- **Failures and recovery**:
  - failure during *staging* → **automatic rollback** of the staging copies: the store
    remains fully readable with the old key;
  - failure during *switchover* → **roll-forward**: re-running `RotateAsync` with the same
    providers resumes the rotation where it stopped. Classification through
    authenticated decryption (AES-GCM) guarantees idempotence: no entry lost,
    none double-encrypted;
  - entry unreadable with both keys → **fail-closed** failure by default (rollback);
    `KeyRotationOptions.ContinueOnUnreadableEntries` allows skipping it (entry left
    as-is and reported in `KeyRotationResult.Errors`).
- **Activation**: `services.AddOrkeonKeyRotation();` — to be paired with
  `AddOrkeonEncryption()` (the AES-256-GCM provider itself stays in the core).
- **Dependencies**: logging only (the store and the providers are passed as
  method arguments).
- **How to proceed**:
  ```csharp
  var rotation = provider.GetRequiredService<IKeyRotationService>();

  // 1. Generate and persist the new key under a versioned secret name
  //    (refuses to overwrite an existing secret — fail-closed)
  await rotation.ProvisionNewKeyAsync(writableSecrets, "orkeon-encryption-key-v2");

  // 2. Re-encrypt the raw store (under the decorator) from the old key to the new one
  var result = await rotation.RotateAsync(rawStore, oldProvider, newProvider);

  // 3. Switch the host configuration over to the new secret name
  ```
- **Known limits**: the rotation runs on the **raw** memory provider (under the
  `EncryptedMemoryProviderDecorator` decorator, type `MemoryProviderBase` for
  key enumeration) and assumes no concurrent write occurs during the
  rotation window. `AesEncryptionProvider.RotateKeyAsync` is neutralized
  (`NotSupportedException`, no more false success log): an "in-place" rotation
  would orphan the existing data — go through `IKeyRotationService`.

## Evaluation benchmarking — `AddOrkeonBenchmarking()`

- **Role**: repeated execution of an evaluation suite per test case with
  mean/stddev statistics (`IBenchmarkRunner`).
- **Activation**: `services.AddOrkeonBenchmarking();` — to be paired with
  `AddOrkeonEvaluation()` (included in the core) to build the suites.
- **Dependencies**: none (the evaluation suite is provided via `BenchmarkConfig`
  at call time).

## Multi-modal content (vision) — `AddOrkeonMultiModal(...)`

- **Status: real (R3.9)** — vision is wired end to end: image contents
  flow from the `MultiModalContent` abstractions (Domain) to provider payloads via
  `LlmMessage.MultiModalContent`. `AnthropicLlmProvider` emits image content
  blocks conforming to the Messages API (`{"type":"image","source":{"type":"base64"|"url",…}}`);
  `OpenAIProvider` emits `image_url` parts conforming to Chat Completions (http(s) URL
  or base64 data URL). See the [multi-modal guide](../guides/multimodal.md).
- **Role**: validation of multi-modal contents (size, format, duration) via
  `IContentValidationService` (`Orkeon:MultiModal` options) and image loading
  from the virtual file system via `IMultiModalContentLoader`
  (VFS read → bytes → base64, MIME type inferred from the extension, validation of the
  configured constraints).
- **Activation**:
  ```csharp
  services.AddOrkeonMultiModal(configuration); // lie Orkeon:MultiModal
  services.AddOrkeonMultiModal();              // default options
  ```
- **Dependencies**: `IContentValidationService` only needs the options;
  `IMultiModalContentLoader` requires a resolvable `IFileSystemService`
  (`AddOrkeonFileSystem(configuration)` in real hosts).
- **Known limits**: vision payload composition is capability-driven
  (`LlmProviderCapabilities.Vision`, translated once by `OpenAICompatibleProviderBase`)
  — **all 14 providers** declare it (DeepSeek since the 2026-08-30 campaign measured
  `deepseek-v4-flash-vision-exp`); a provider without the capability degrades
  multi-modal messages to their text fallback
  (`LlmMessage.Content`). Supported image formats: png, jpeg, gif, webp.
  Audio/file parts are not sent by any provider and throw an explicit
  `NotSupportedException` if they reach a vision payload.

## Kickoff hooks — `AddOrkeonKickoffHooks()`

- **Role**: execution of `BeforeKickoffHook` / `AfterKickoffHook` callbacks around
  a crew's kickoff, in increasing `Priority` order, with `ContinueOnError`
  semantics (exceptions from tolerant hooks are logged then ignored).
- **Activation**:
  ```csharp
  services.AddOrkeonKickoffHooks(); // runner seul
  services.AddBeforeKickoffHook(new BeforeKickoffHook
  {
      Name = "audit",
      Priority = 1,
      Execute = crew => /* ... */ System.Threading.Tasks.Task.CompletedTask,
  }); // enregistre le hook ET le runner
  ```
- **Dependencies**: none (hook collections empty by default, optional logging).
- **Known limits**: the orchestrator does not (yet) invoke the runner — wiring it
  into the execution pipeline belongs to the orchestrator decomposition (R4.1).
  In the meantime, the host calls it itself:
  ```csharp
  var runner = provider.GetRequiredService<ICrewKickoffHookRunner>();
  await runner.RunBeforeKickoffAsync(crew, ct);
  var result = await orchestrator.KickoffAsync(crew.Id, input, ct);
  await runner.RunAfterKickoffAsync(crew, result, ct);
  ```

## Codebase context (RaggableTree) — `AddRaggableTree(options)`

- **Role**: `ICodebaseContextProvider` produces codebase summaries meant to be
  injected into the agents' context (RaggableTree feature).
- **Activation**: registered by `AddRaggableTree(options)` (`Orkeon.Analysis`
  project). The core library never calls it — but the **runner host does, by
  default** (`orkeon run`/`orkeon-host`; opt out with `"RaggableTree:Enabled": false`),
  consistent with this catalog's own entry above. See
  [raggable-tree.md](../architecture/raggable-tree.md).
- **Known limits**: no framework component consumes it automatically —
  the host resolves it and injects the summaries wherever it wishes (system prompt,
  task context…).

## RAG subsystem — `AddOrkeonRag(configuration)` (RAG-02…06)

- **Role**: retrieval-augmented generation — ingestion (loaders, chunking,
  validation, incremental manifest), staged retrieval pipeline (transform →
  retrieve → fuse (+ opt-in MMR) → rerank → assemble → generate → groundedness),
  the corrective CRAG graph, profile presets and the offline eval harness.
  Full guide: [rag-pipeline.md](../architecture/rag-pipeline.md), decisions:
  [ADR-006](../adr/ADR-006-rag-subsystem.md).
- **Activation**:
  ```csharp
  services.AddOrkeonRag(configuration);   // Orkeon.Rag.DependencyInjection
  services.AddOrkeonRagTools();           // Orkeon.Tools.Rag: rag_search / rag_ingest / rag_eval
  services.AddOrkeonOnnxReranker();       // opt-in — required by balanced/quality/adaptive
  ```
  `AddOrkeonRag` already wires the query transformers, routing, hybrid BM25+RRF,
  the corrective graph **and the web-fallback transport registration** —
  calling `AddOrkeonRagWebFallback(configuration)` yourself is a no-op; the
  feature is governed by the two config switches below.
- **Profiles** (`Orkeon:Rag:Profile`, default `fast`; per-key overrides apply on
  top of the preset): `fast` (vector only) and `corrective` (CRAG graph — loops
  instead of a linear rerank stage) run without the ONNX packages;
  `balanced`/`quality` enable the cross-encoder rerank stage and `adaptive`
  delegates its `SingleShot` route to `balanced`, so those three **fail fast at
  the first query** (actionable exception) unless `AddOrkeonOnnxReranker()`
  (`Orkeon.Rag.Onnx` + `Orkeon.Rag.Onnx.Model`, int8 weights embedded, offline)
  is registered.
- **Web fallback (double opt-in, off by default)**: the corrective graph's
  `web_fallback` node runs only when **both** `Orkeon:Rag:Corrective:WebFallback:Enabled`
  (policy) and `Orkeon:Rag:WebFallback:Enabled` + a non-empty `Endpoint`
  (transport, SearxNG) are set. Every fetched page is screened by
  `PromptInjectionDocumentValidator` (`Rejected` never enters the working set;
  `Suspicious` follows the configured policy) — see
  [security.md](../architecture/security.md).
- **Projects**: `Orkeon.Rag.Abstractions` (contracts, Domain-only),
  `Orkeon.Rag` (implementations), `Orkeon.Tools.Rag` (agent tools),
  `Orkeon.Rag.Onnx` + `Orkeon.Rag.Onnx.Model` (opt-in reranker + weights).
- **Known limits**: see [limitations.md](./limitations.md) — offline degradation
  of the LLM-dependent nodes (`corrective`/`adaptive`), heuristic classifier by
  default, in-process BM25 unpersisted.

## Execution state persistence — `AddCrewExecutionStatePersistence(...)` (R3.8)

- **Role**: durable persistence of crew execution states
  (`ScopedCrewExecutionStateManager`) in a checkpointing state store
  (`IStateStore`): every transition (creation, update, completion, archiving)
  is persisted, and on restart the states are reloaded — including **resumption**
  via `CreateStateAsync(crewId, executionId, input)` when a persisted state already
  exists for the resume identifier (crash recovery).
- **Activation** (two conditions — a store **and** the option):
  ```csharp
  // 1. Un state store de checkpointing (in-memory, SQLite ou PostgreSQL)
  services.AddOrkeonSqliteCheckpointing("Data Source=orkeon-state.db");
  // ou : services.AddOrkeonCheckpointing();              // in-memory
  // ou : services.AddOrkeonPostgresCheckpointing(configuration);

  // 2. L'opt-in de persistance
  services.AddCrewExecutionStatePersistence();             // code-first
  services.AddCrewExecutionStatePersistence(configuration); // lie Orkeon:ExecutionState:Persistence
  ```
  With the `AddOrkeonInfrastructure(IConfiguration)` overload, the presence of the
  `Orkeon:ExecutionState:Persistence` section (`Enabled`, `DeleteFromStoreOnArchive`)
  is enough to bind the options.
- **Backward-compatible default**: without activation, states remain **in-memory only**
  (no recovery after a crash) — historical behavior unchanged. If the option is
  enabled without a registered store, the manager logs a warning and continues in
  memory.
- **Cohabitation**: execution state sessions are **namespaced**
  (`crew-exec:` prefix on the session id and the crew id) and never pollute the
  task checkpointing sessions (`CheckpointManager`/`ResumeEngine`) sharing
  the same store.
- **Known limits**: execution metadata is persisted as invariant strings
  (primitive reads are still converted via `GetValue<T>`); the `ToolsUsed`
  telemetry of task outputs is not persisted (v1).

---

## Permission gate — `AddOrkeonPermissionGate(configuration)`

- **Activation**: called by `RunnerHost` and the ConsoleApp bootstrap, but registers
  nothing unless `Orkeon:Security:PermissionGate:Enabled = true`. `Interactive`
  (default `false`) declares an approval channel; leave it `false` until the REPL
  Ask flow lands (v2).
- **Effect**: `ModePermissionGate` is consulted by the scripted `ctx.llm.act` loop
  before EACH tool execution. Modes: `bypassPermissions` → allow all; `plan` → deny
  non-reads; `acceptEdits` → auto-accept reads + `file_write`, ask for shell;
  `default` → ask everything. Headless, every ask degrades to a motivated
  `DENIED:` fed back to the model as the tool result (no exception). Unknown tools
  are classified as writes (fail-closed).
- **Declarative classification**: tools can self-declare their class via
  `IBaseTool.Access` (`ToolAccess.Read` / `Edit` / `Execute`) — a declaration wins
  over the gate's name tables; `Unspecified` (the default) falls back to the tables.
  The built-in read tools (file/directory/session/memory/analysis), `file_write`
  (Edit) and `shell_command`/`http_api` (Execute) all declare themselves.
- **Backward-compatible default**: flag absent → no gate registered → `act`
  behaves exactly as before. Scripts opt into a mode per call via the
  `permissionMode` act option.

## Shell interpreters & mutating git — `Orkeon:Tools:Shell:AllowInterpreters`

- **Activation**: config only — `AddOrkeonCodeTools()` reads the flag and, when
  `true`, constructs `ShellCommandTool` with `allowInterpreters: true` (re-enables
  `node`/`dotnet`/`npm`/`find` AND lifts the read-only git subcommand restriction so
  `git add`/`commit`/`branch` work). The tool logs its security warning when active.
- **Security**: RCE-equivalent on the host — reserve it for trusted coding-agent
  hosts running inside a confined workspace (the scripted-commands REPL is the
  reference consumer — see `examples/cli-ts-commands/`; a commit command cannot
  work without it).
- **Backward-compatible default**: flag absent → strict read-only allowlist,
  historical behavior unchanged.
- **Allowlist customization** (two string-array keys, both read by
  `AddOrkeonCodeTools()`):
  - `Orkeon:Tools:Shell:ExtraAllowedCommands` — **additive** on top of the default
    allowlist (or of a replacement list); the recommended way to allow
    `make`/`cargo`/etc. for a trusted host. Composes with `AllowInterpreters`.
  - `Orkeon:Tools:Shell:AllowedCommands` — full verbatim **replacement**; per the
    `ShellCommandTool` ctor contract it cancels `AllowInterpreters` and re-enables
    the read-only git subcommand restriction.
  - An absent or empty section binds to `null` (defaults) — never to an empty
    array, which would block every command.
- **VFS path rewriting** (always on, no flag): arguments naming a mount-prefixed
  virtual path (incl. `--out=/workspace/dist`) are resolved virtual→physical before
  the process starts — a denied path fails the call with the redacted reason — and
  stdout/stderr are rewritten physical→virtual, so the model only ever sees virtual
  paths. `AgentFacing` mounts, ordinal matching; without mounts both passes are
  no-ops.

## Native LLM console streaming — `AddLlmConsoleStreaming(configuration)`

- **Activation**: called by the ConsoleApp bootstrap, but registers nothing unless
  `Orkeon:Cli:ConsoleStreaming:Enabled = true`.
- **Effect**: registers `ConsoleLlmDeltaSink` as the `ILlmDeltaSink`. When a sink is
  present and the LLM provider streams (`IStreamingLlmProvider`), the scripted
  `ctx.llm.act` loop switches to the SSE path and every content delta is written
  incrementally to the host `IConsoleAdapter` — the REPL renders tokens as they
  arrive without the script passing `onDelta`. Turns that stream no visible content
  (pure tool calls) emit nothing. The sink composes with a script-side `onDelta`
  (both receive every delta).
- **Backward-compatible default**: flag absent → no sink registered → `act` keeps
  its buffered rendering (byte-identical), scripts can still stream via `onDelta`.

---

## Non-regression guarantees

The registration tests
(`tests/core/Orkeon.Infrastructure.Tests/DependencyInjection/OptInSubsystemsRegistrationTests.cs`
and `tests/core/Orkeon.Application.Tests/DependencyInjection/KickoffHookExtensionsTests.cs`)
verify:

1. that **no dormant port of the original opt-in set** is registered by
   `AddOrkeonApplication()` / `AddOrkeonInfrastructure()` (both overloads) —
   the rows added later (plugins, local embeddings, LanceDB/Redis, cognitive
   memory) are guarded by their own suites, not by this test;
2. that each `AddOrkeonXxx()` produces a graph that is **resolvable** on its own (with
   logging and, where applicable, an `IConfiguration`);
3. that the opt-ins **compose** with the core (`TryAdd` semantics, no duplicates).
