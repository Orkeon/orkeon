> 🇫🇷 [Version française](../fr/reference/configuration.md)

# Configuration reference (`appsettings.json`)

This page is the single map of the configuration sections the framework reads. Column
"Opt-in" names the activation gesture when the section only takes effect after a dedicated
registration — see [Opt-in subsystems](./opt-in-subsystems.md) for each one's details.

## Where settings are read from

Runner hosts (`RunnerHost.Build`, used by `orkeon run` and the YAML runners) layer, on top
of the standard .NET host sources:

1. **One resolved `appsettings.json`** — resolution chain (`RunnerSettings.ResolveSettingsPath`):
   explicit `--settings <path>` → `appsettings.json` next to the crew config →
   `appsettings/appsettings.json` walking up the directory tree (`examples/appsettings/appsettings.json`
   in this repository; `_shared/appsettings.json` is a deprecated fallback) → the global
   per-user config written by `orkeon init`. No file found ⇒ environment variables only.
2. **Environment variables with the `ORKEON_` prefix** (`AddEnvironmentVariables("ORKEON_")`
   in `RunnerHost` and in `orkeon doctor`). Standard .NET mapping: `__` separates levels —
   `ORKEON_Llm__ApiKey` overrides `Llm:ApiKey`, `ORKEON_Orkeon__Rag__Profile` overrides
   `Orkeon:Rag:Profile`. Env vars are added **after** the file, so they win.
3. **CLI mount overrides** — each `--mount` argument becomes an in-memory
   `Orkeon:FileSystem:Mounts:<i>` entry (highest precedence), placed **by virtual root**:
   a `--mount` on a root the declared array (layers 1 + 2) already holds is written at the
   first entry's index and **replaces every declared entry of that root** for the run; a
   `--mount` on a new root is appended after the highest declared index. The runner's own
   `/crew` (or `/script`) mount and the `InternalMounts` are always appended. A declared
   entry may carry an **id** — the 26-character ULID before a `|`,
   `01J9Z3K4M5N6P7Q8R9S0T1V2W3|C:\data:/output:rw` (VFS-90) — and several entries may
   declare one root when each carries one: `--mount-id <ulid>`, else the crew's `mounts:`
   block, selects the entry the run keeps, and every other entry of that root is
   **withdrawn** — its key is written to `null` at its own index, its base path is not
   whitelisted and its folder is not probed.

The same `ORKEON_` prefix also feeds `EnvironmentSecretProvider` (secret lookup, e.g.
`OPENAI_API_KEY` → `ORKEON_OPENAI_API_KEY`; the `web_search` tool's Tavily key is
`ORKEON_TAVILY_API_KEY`). The second stop of that chain is the `Secrets` section of the
file (`Secrets:TAVILY_API_KEY`), the environment variable winning when both exist.

**What this means in practice.** The file is the durable, shared base; everything laid
over it is an ephemeral layer that lives and dies with one process. `orkeon doctor`'s
`llm-config` check composes exactly like a runner (same resolution chain, same
`ORKEON_` overlay), so its verdict answers: *what would a run launched from this shell
use, absent any per-launch overlay?* Orkeon Studio's named model profiles ride layer 2:
the profile elected as default is copied into the file's `Llm` section (so a manual
terminal `orkeon run` follows the same election — that is what `llm-config` reflects),
while a team that elected a different profile receives it as `ORKEON_Llm__*` variables
on its own launch only — `llm-config` cannot see those, because they exist nowhere
until that launch starts. Every profile is also written into the file's `Llm:Profiles`,
without its key, as a host profile a crew can name ([below](#studio-writes-this-section)),
and every launch from Studio carries them all, keys included, as
`ORKEON_Llm__Profiles__<id>__*`. No file is ever generated: the composition is in-memory.

## LLM provider (`Llm` section)

The `Llm` section is read by `RunnerHost.RegisterLlmProvider` and turned into an
`ILlmProvider` via `ILlmProviderFactory`. **The provider is inferred automatically**, in
order: base-URL host patterns (e.g. `deepseek.com` → DeepSeek, `api.x.ai` → Grok,
`/engines/` → Docker Model Runner/OpenAI-compatible), then model-name patterns, then
API-key shape; default `openai`. A section without `Model` runs on that provider's own default
model (the *Default (code)* column of the
[provider comparison](llm-providers-comparison.md#defaults-and-newer-models--catalogue-review-of-2026-09-19)),
never on OpenAI's pinned onto another vendor. Keys: `Model`, `BaseUrl`, `ApiKey` (prefer
`ORKEON_Llm__ApiKey` — the variable each provider's key conventionally lives in, and the three
names confused with it, are in the
[provider comparison](llm-providers-comparison.md#api-keys-the-variable-per-provider)), `Temperature`, `MaxTokens`, `TimeoutSeconds`, `MaxRetries` (default 10), and
`Thinking:{Enabled,Effort}` for thinking-capable providers, and `Grammar` (default `false`):
set it to `true` only when `BaseUrl` points at a llama.cpp-compatible server (Docker Model
Runner, `llama-server`) — the one kind of endpoint that honours the GBNF `grammar` field a
`structured_output` deliverable produces; elsewhere the grammar is dropped with a warning naming
the key ([provider comparison](llm-providers-comparison.md)). `TimeoutSeconds` defaults to 30 s,
too short for a model that thinks before it answers (Kimi K2.6, DeepSeek V4 and GLM do so by
default): set 600 s, or turn thinking off with `Thinking:Enabled = false`. A call that hits the
timeout is retried once, then fails its task with a message naming the setting — it is never
reported as an empty answer (LLM-11). **This section is the base of every call** to its provider:
a component that passes a configuration of its own — the planner, the cognitive memory, the
context-window and RaggableTree summarizers, the agent loops outside the chat client, a chat
client registered without one — completes the section's rather than replacing it. What it leaves
unset is the section's (the key, `BaseUrl`, `TimeoutSeconds`, `Thinking`, `MaxTokens`…); what it
sets wins (the planner's temperature 0.3, an analysis's output cap). A profile's provider works
the same way with its own keys. `MaxTokens` is a **pin**: left
out, the request carries the model's **documented maximum output** from the
`LlmModelOutputLimits` catalogue (128K on `gpt-5.6-sol` and the Claude 5 generation, 384K on
`deepseek-flash`, 131 072 on the GLM-5 and Qwen 3.7/3.8 families, 65 536 on Gemini 3.x Flash —
see the [output caps table](llm-providers-comparison.md#output-caps--the-documented-maximum-per-model-llm-10)),
no cap at all where the vendor documents none (Mistral, a local Ollama), and **4096 only for a
model the catalogue does not know** — the value the engine used to send for every model, which
a reasoning model spends thinking before it writes a word and answers empty (an empty final
answer fails the task rather than passing for a completed one). Pin it when the model is not in
the catalogue or when you want a tighter cap; a Studio model profile pins it as
`ORKEON_Llm__MaxTokens`, and the profile editor says what an empty field means for the chosen
model. A catalogue cap the endpoint refuses is retried once without the field, with a warning
naming the model. Without an `Llm` section the
runtime degrades to the echo provider and warns once. See
[LLM providers](../architecture/llm-providers.md); templates live in
`examples/appsettings/*.json.example`.

### Named profiles (`Llm:Profiles`)

A host can offer more than one provider. Each child of `Llm:Profiles` is a **profile**: a
name, and a provider described with exactly the keys of the `Llm` section (`BaseUrl`, `ApiKey`,
`Model`, `Temperature`, `MaxTokens`, `TimeoutSeconds`, `MaxRetries`, `Thinking`, `Grammar`).
The `Llm` section itself stays the **default** profile — the one every agent runs on unless it
names another. A crew picks a profile **by name**, never by key or endpoint: `llm: { profile:
claude }` on the crew, an agent or a task's `llm_override` in YAML, `llm.profile("claude")` on an
agent and `taskBuilder().withProfile("claude")` on a task in `.ork.ts` ([YAML and builders](../getting-started/yaml-and-builders.md#one-provider-per-agent-profiles)).

```json
{
  "Llm": {
    "BaseUrl": "https://api.deepseek.com/v1", "Model": "deepseek-v4-flash",
    "Profiles": {
      "claude": { "BaseUrl": "https://api.anthropic.com/v1", "Model": "claude-sonnet-5" },
      "local":  { "BaseUrl": "http://localhost:11434", "Model": "qwen3" }
    }
  }
}
```

Keys stay out of files the same way: `ORKEON_Llm__Profiles__claude__ApiKey`. Each profile's
provider is built once, on first use, metered like the default one — the run's `cost.updated`
readings name each call's provider, and Studio's status bar breaks the tokens down per
provider. The profiles are validated when the host starts: `default` is a reserved name (it
designates the `Llm` section, and a crew may name it to bring an agent back to the default), and
an invalid `BaseUrl` or a value that is not a number fails the start with the key to fix. A
crew naming a profile the host does not define **fails to load**, and the message lists the
profiles the host offers. **A model left unset is the profile's own, on every path**: a YAML or
`.ork.ts` `llm:` block without `model`, a C# agent's `.Thinking()` or `.MaxOutputTokens(n)`,
`llm.default_` on a host that configures no model, the planner, the agent loops outside the chat
client and the cognitive memory's analysis calls all leave the model to the provider they reach,
which sends the model its profile configures, else its own default — never an empty one, never
OpenAI's on another vendor.

Who runs on which profile:

- **An agent's turns** — on its own `llm:` profile, or its task's `llm_override` for that task
  (`taskBuilder().withProfile(name)` in `.ork.ts`).
- **The manager** of a hierarchical crew (and the one that hands an autonomous crew's tasks out) —
  on the LLM the crew gives it: in C# the provider `CrewBuilder.WithManagerLlm` sets, metered like
  the host's own; else its manager agent's `llm:` block, profile and model; else the default profile.
- **The RAG subsystem** — grounded generation, query transformers, the listwise reranker, the
  corrective graph's evaluator and groundedness checker, the `llm` classifier and the evaluation
  judge — on the profile `Orkeon:Rag:LlmProfile` names (unset: the default). A name the host does
  not offer refuses the start of the host, listing the ones it does; the profile's provider is
  built at the first RAG call that needs it, never at start-up.
- **The planner, the Guardian, the `Evaluation` judges and the cognitive memory's analyses** stay
  on the default profile: they are host services, not crew roles (`CrewBuilder.WithPlanningLlm`
  keeps the hand in C#, with `.Planning()`, its calls metered like the host's own).

A section holding `Profiles` alone configures no default provider — the default is then the echo
provider, with the usual warning. `orkeon-host` can restrict which profiles its crews may name
(`Orkeon:Host:LlmProfiles`, below); the restriction applies to the RAG profile and to a manager
agent's too.

#### Studio writes this section

Orkeon Studio's model settings are the host's profiles (STUDIO-48). Each setting of the AI-model
tab that names a provider is the profile named after it by the folder-name rule of the teams —
« Claude » is `claude`, « Z.AI » is `z-ai` — and its card and its editor show what a crew writes,
`profile: claude`. Creating, editing, renaming or deleting the setting writes, moves or removes
its entry: every field the setting pins (`BaseUrl`, `Model`, `Temperature`, `TimeoutSeconds`,
`MaxTokens`, `Thinking`), never the key, and a key Studio does not model (`MaxRetries`,
`Grammar`) stays where it is. A setting without a model (the echo card), or whose name keeps no
ASCII letter or digit, is offered to no crew; `default`, a name another setting already answers
to, and a name that would take over an entry written by hand are refused.

- **Keys.** A launch from Studio — a run, a trial, the creation assistant — lays every setting over
  its child as `ORKEON_Llm__Profiles__<id>__*`, the key resolved like the default profile's, so it
  never depends on the file having been saved nor on which settings file it reads. `orkeon run`
  in a terminal reads the same file and needs the key alone, in
  `ORKEON_Llm__Profiles__<id>__ApiKey`; the editor names that variable in expert mode.
- **Entries written by hand** — no setting owns them — are listed read-only under the settings,
  and Studio never rewrites nor removes them. Ownership is the setting's name: an id is Studio's
  when a setting of `studio-model-profiles.json` answers to it.
- **The RAG's profile** (`Orkeon:Rag:LlmProfile`) is chosen on the same tab, in expert mode, among
  the profiles; it follows its setting through a rename and falls back to the default when the
  setting is deleted. Studio warns, before saving, about a file whose RAG profile names a profile
  the file does not define.

`orkeon-studio-config` shows the section and the RAG's profile read-only.

## Sections outside the `Orkeon:` prefix

| Section | Configures | Consumer / opt-in |
|---|---|---|
| `Llm` | Active LLM provider — the default profile (see above) | `RunnerHost`, the REPL (`LlmSettings.ReadDefault`, the same reader) |
| `Llm:Profiles:<name>` | Named LLM profiles a crew picks per agent or per task, same keys as `Llm` (see above); Orkeon Studio writes one per model setting, without its key | `RunnerHost`, the REPL (`AddOrkeonLlmProfiles(configuration)`) |
| `Llm:AvailableModels` | The model list a scripted `/model` REPL command can offer (string array, or one comma-separated string) | `AddOrkeonSessionTools(configuration)` |
| `Memory:Provider` | TYPE of the application-wide memory provider (`inmemory`, `redis`, `sqlite`, `chromadb`, `pinecone`, `lancedb`; unset → in-memory). Its connection is that provider's own section (`Orkeon:Redis`, `Orkeon:Sqlite`, … below). It is also where the memory of a named crew with `memory: true` and no `memoryProvider:` lives — see [Memory system](../architecture/memory-system.md#selection-by-configuration) | `AddOrkeonInfrastructure()` |
| `RateLimiting` | LLM request throttling: `GlobalRequestsPerMinute`, `ProviderRequestsPerMinute`, `AgentRequestsPerMinute`, `MaxConcurrentRequests`, `QueueLimit` | `AddOrkeonInfrastructure()` (`ILlmRateLimiter`) |
| `LlmLogging` | LLM exchange capture tuning: `FullEmbeddingLog` (default `true`), `LogStreamingExchanges` (`true`), `MaxBodyLengthChars` (`0` = no truncation) | `RunnerHost` → `AddLlmExchangeLogging(logDirectory, options)`, only when the run passes `--llm-log` |
| `PathSecurity` | Physical path validation: `DefaultWorkspaceRoot`, `AdditionalAllowedDirectories`, `AdditionalBlockedExtensions`, `ResolveSymlinks`, `MaxFileSizeBytes` | `AddOrkeonInfrastructure()` (`IPathValidator`) |
| `Telemetry` | OpenTelemetry export: `Enabled`, `OtlpEndpoint`, `ExportToConsole`, `PrometheusEndpoint`, `MaxMemoryMB` (the standard `OTEL_EXPORTER_OTLP_ENDPOINT` also works in the runners) | `AddOrkeonTelemetry(configuration)` — called by `AddOrkeonInfrastructure(configuration)` and `RunnerHost` |
| `A2A` | A2A server/client: `EnableServer`, `Port`, `Host`, `AgentName`, `AgentDescription`, `AgentVersion`, `Organization`, `ContactUrl`, `TimeoutSeconds`; `EnableServer` also registers the hosted service that starts the server with the generic host | opt-in `AddOrkeonA2A(configuration)`. `orkeon-host` reads the card's identity and `A2A:Security` here, and refuses `EnableServer`, `Host` and `Port` at start — its listener is `Orkeon:Host:A2A`, below; see [A2A conformance](./a2a-conformance.md#activation) |
| `A2A:Security` | `ClientCertificatePath`, `ClientCertificatePassword`, `TrustedCertificateAuthorities`, `TrustedClientCertificateThumbprints`, `RequireMutualTls`, `AllowedAuthSchemes` (`Bearer`, `ApiKey` — each needs its validator or the server refuses to start), `ApiKeySecretNames` (names of the secrets holding the accepted keys, read through `ISecretProvider`); client side `ClientAuthScheme` (`Bearer`/`ApiKey`) and `ClientCredentialSecretName` (the secret `A2AClient` sends as `Authorization`); bearer validators `A2A:Security:AzureAD` (`TenantId`, `ClientId`, `Authority`, `ValidIssuers`, `ValidAudiences`) and `A2A:Security:Oidc` (`Authority`, `ClientId`, `ValidAudiences`, `RequireHttpsMetadata`) | idem — see [Security](../architecture/security.md#a2a-mutual-tls) |
| `MCP` | MCP client connections (`MCP:Servers:<id>`), the switch `MCP:Enabled` (default `true`), and the optional MCP server — `MCP:EnableServer` (default `false`) + `MCP:Server` (`Name`, `Version`; the server exposes tools only) | `RunnerHost` (`orkeon run`, `orkeon-host`) when `MCP:Servers` declares at least one server and `MCP:Enabled` is not `false` — the servers are connected before the crew loads (STUDIO-21), and by `orkeon-host` once at startup, before its first message (GAP-11); library hosts call `AddOrkeonMcp(configuration)` or the `AddOrkeonInfrastructure(configuration)` overload — see [MCP integration](../architecture/mcp.md) |
| `Secrets:<NAME>` | Second stop of the secret chain after `ORKEON_<NAME>` (`ConfigurationSecretProvider`), e.g. `Secrets:TAVILY_API_KEY` for `web_search` | `AddOrkeonInfrastructure()` |
| `Evaluation` | `EnableLlmJudge` (default `false`): the default `IEvaluationSuite` also runs the coherence, fluency and groundedness LLM judges over the registered `IChatClient` | `AddOrkeonInfrastructure(configuration)`, or `AddOrkeonEvaluation(configuration)` (idempotent: a second call registers no evaluator twice) |
| `RaggableTree` | Codebase indexing: `Enabled`, `Embedding` (`Provider`, `Model`, `ApiKey`, `BaseUrl`, `Dimensions`, `MaxTextChars`) — nothing else: any other key fails the host at startup; what an index covers is set per `index_codebase` call | **opt-out in runner hosts**: `RunnerHost` registers it by default, `RaggableTree:Enabled = false` disables; library consumers call `AddRaggableTree(options)` explicitly |
| `ToolRateLimiting` | Per-tool rate limits: `GlobalToolRequestsPerMinute`, `DefaultToolRequestsPerMinute`, `ToolSpecificLimits` | opt-in `AddOrkeonToolRateLimiting()` (binds from the registered `IConfiguration`; see [opt-in subsystems](./opt-in-subsystems.md)) |
| `TokenBudget` | Token budgets: `MaxTokensPerAgent`, `MaxTokensPerCrew`, `MaxCostPerCrew` | idem |
| `Security:Audit` | Audit sinks: `Enabled`, `MinSeverity`, `EnabledCategories`, `AuditDirectory`, `RetentionDays` | `AddOrkeonInfrastructure()` |
| `Security:Url` | SSRF validation: `AllowedSchemes`, `BlockedPorts`, `AllowedDomains`, `BlockedDomains`, `BlockPrivateIPs`, `ResolveDNS` | `AddOrkeonInfrastructure()` (`IUrlValidator`) |
| `Security:Vault` | Secret vault chain: `AzureKeyVaultUri`, `UseAwsSecretsManager`, `DpapiSecretsDirectory`, `CacheTtl` | `AddOrkeonInfrastructure()` |
| `Security:Prompt` | The Guardian's input phase: `Policy` (`Block` default — High/Critical fails the task, lower warns; `Warn`; `None`), `CustomPatterns`, `EnableExfiltrationDetection` | `AddOrkeonInfrastructure()` — screens every agent turn's prompt; see [Security](../architecture/security.md) |
| `Security:ToolResults` | Tool-result screening: `Policy` (`Warn` default — tagged as data and reported; `Block` withholds High/Critical; `None`), `TrustedTools` (added to the `email_*` defaults). The length bound is not configured here: one rule, `AgentDefaults.ResolveMaxToolResultLength` | `AddOrkeonInfrastructure()` — applied to every tool result by `IToolInvocationPipeline` |
| `BRAVE_API_KEY` | Also read as a **configuration key** (not only an env var) to gate the Brave tool | `RunnerHost` |
| `Plugins` | Plugin directory discovery: `Directory` (default `/plugins`), `SearchPattern` (`*.dll`), `ContinueOnError`, `SharedAssemblyPrefixes` | opt-in `AddOrkeonPlugins(fileSystem, configuration)` — see [Plugins](../architecture/plugins.md) |

## `Orkeon:*` sections

### Core, orchestration, persistence

| Section | Configures | Consumer | Opt-in |
|---|---|---|---|
| `Orkeon:CrewFactory:StrictTools` | Fail crew loading on unknown tool names (runners default `true`) | `RunnerHost` → `CrewFactoryOptions` | — |
| `Orkeon:ExecutionState:Persistence` | Durable crew execution states (`Enabled`, `DeleteFromStoreOnArchive`) | `ScopedCrewExecutionStateManager` | `AddCrewExecutionStatePersistence(configuration)` — auto-called by `AddOrkeonInfrastructure(configuration)` when the section exists; requires an `IStateStore` |
| `Orkeon:Checkpointing:*` | Postgres state store (`ConnectionString`, `SchemaName` default `orkeon`, `AutoMigrate`, `MaxHistoryPerSession`) | `CheckpointingExtensions` | `AddOrkeonPostgresCheckpointing(configuration)` |
| `Orkeon:Consensus` | Consensual-mode voting: `VotingOptions`, `MaxVotingRounds`, `EnableDiscussion`, `FallbackStrategy`, `RoleWeights` | Infrastructure | — (registered by `AddOrkeonInfrastructure()`; the section gates behavior) |
| `Orkeon:CostTracking` | LLM cost tracking: `Enabled`, `DefaultCrewBudget`, `CustomPricings` | Infrastructure | — (registered by `AddOrkeonInfrastructure()`; the section gates behavior) |
| `Orkeon:TokenCounter` | Token estimation: `CharsPerToken`, `TokensPerMessage`, `TokensPerReply`, `SpecialTokenOverhead` | Infrastructure | idem |
| `Orkeon:Monitoring` | Monitoring backend: `MaxTraceHistory`, `MetricsRetentionMinutes`, `TraceSourcePrefix` | Infrastructure | `AddOrkeonMonitoring(configuration)` |

### Embeddings, vector search, memory

| Section | Configures | Consumer | Opt-in |
|---|---|---|---|
| `Orkeon:Embeddings` | Embedding provider selection (`Provider`, `Model`, `Dimension`, `BatchSize`, `EnableCache`) | `DefaultEmbeddingProviderResolver` | bound by `AddOrkeonVectorSearch(configuration)` (called by `AddOrkeonInfrastructure(configuration)`) |
| `Orkeon:EmbeddingCache` | Embedding cache (`SlidingExpirationMinutes`, `MaxCacheSizeBytes`) | idem | idem |
| `Orkeon:VectorSearch` | Vector search options (`DefaultMetric`, `DefaultTopK`, `DefaultMinScore`, `PreferVectorSearch`) | idem | idem |
| `Orkeon:Redis` | Redis memory provider: `ConnectionString` (default `localhost:6379`), `KeyPrefix` (default `orkeon:memory:`). The connection opens on first use | `MemoryProviderFactory` — every path selecting `redis`: `Memory:Provider`, a crew's `memoryProvider:`, `Orkeon:Rag:Provider`, `AddOrkeonRedisMemory` | bound by `AddOrkeonInfrastructure()` |
| `Orkeon:Sqlite` | SQLite memory provider: `ConnectionString` (default `Data Source=:memory:`; a file `Data Source` is a virtual path on a writable mount), `TableName`, `DefaultTopK`, `MinSimilarityScore` | `MemoryProviderFactory` — every path selecting `sqlite` | idem |
| `Orkeon:ChromaDb` | ChromaDB server: `BaseUrl`, `Tenant`, `Database`, `CollectionName`, `DefaultTopK` | `MemoryProviderFactory` — every path selecting `chromadb` | idem; `AddOrkeonChromaDb(configuration)` (called by `AddOrkeonInfrastructure(configuration)` when the section exists) also exposes the shared `ChromaDbMemoryProvider` by its class |
| `Orkeon:Pinecone` | Pinecone index: `ApiKey`, `IndexName`, `Host` (optional: without it, one `describe_index` call resolves the index host on first use), `Namespace` | `MemoryProviderFactory` — every path selecting `pinecone` | idem, `AddOrkeonPinecone(configuration)` |
| `Orkeon:LanceDb` | Remote LanceDB server: `Endpoint`, `ApiKey`, `Database`, `TableName`, `EmbeddingDimension`, `DistanceType`, `DefaultTopK`, `MinSimilarityScore`, `VectorWeight`, `FullTextWeight`, `CreateFullTextIndexOnInit` | `MemoryProviderFactory` — every path selecting `lancedb` | bound by `AddOrkeonInfrastructure()`; `AddOrkeonLanceDb(configuration)` adds the concrete class and `LanceDbMigrationService` |
| `Orkeon:CrewMemory` | What a crew with `memory: true` recalls before each task (`CrewMemoryOptions`): `RecallLimit` (5 memories; 0 recalls nothing), `MinScore` (0.6, cosine on the embedder's scale — measured on the local model), `MaxChars` (4,000 characters of memory content in all) — see [Memory system](../architecture/memory-system.md#what-a-crew-recalls) | `MemoryCoordinator` | bound by `AddOrkeonInfrastructure()` |
| `Orkeon:CognitiveMemory` | Cognitive memory layering (`EnableLlmAnalysis`, `EnableContradictionDetection`, `ContradictionCandidateCount`, `AnalysisModel`, `AnalysisTemperature`, `PruningThreshold`, `PruningMinAgeDays`, `RecencyHalfLifeHours`, `DefaultRecallOptions`) | Infrastructure | `AddOrkeonCognitiveMemory(configuration)` |
| `Orkeon:Encryption` | At-rest memory encryption (`Enabled`, `SecretName`, `KeySizeInBits`) | Infrastructure | — (registered by `AddOrkeonInfrastructure()`; the section gates behavior; the decorator is applied by the host). Key rotation stays opt-in: `AddOrkeonKeyRotation()` |

### RAG (`Orkeon:Rag`)

Details and semantics: [RAG pipeline](../architecture/rag-pipeline.md). Everything below
requires `AddOrkeonRag(configuration)` (`Orkeon.Rag.DependencyInjection`), which every runner host
(`orkeon run`, `orkeon-host`) calls.

| Section | Configures |
|---|---|
| `Orkeon:Rag:Profile` | Profile preset `fast` (default) / `balanced` / `quality` / `adaptive` / `corrective`; any `Orkeon:Rag` key overrides the preset key-by-key |
| `Orkeon:Rag:LlmProfile` | The host LLM profile (`Llm:Profiles:<name>`) the RAG subsystem calls — generation, query transformers, listwise reranker, corrective evaluator and groundedness checker, `llm` classifier, evaluation judge; unset or `default` is the default profile. An unknown name refuses the host start, listing the known ones; `orkeon rag eval --offline` ignores it. Chosen in Studio › Settings › AI model (expert) |
| `Orkeon:Rag:Provider` | TYPE of the RAG document-store provider (`RagStoreOptions` — a `MemoryProviderFactory` type alias), connected from that provider's own section (`Orkeon:Redis`, `Orkeon:Sqlite`, …); default is the ambient `IMemoryProvider` |
| `Orkeon:Rag:Collection` | Collection `rag_search` queries when the agent names none (unset: `default`); `rag_eval` uses it for a dataset that names no collection and brings no corpus |
| `Orkeon:Rag:Retrieval` (`TopK`, `CandidateK`, `MinScore`) | Retrieval stage bounds |
| `Orkeon:Rag:Retrieval:Hybrid` (`Enabled`, `RrfK`), `Orkeon:Rag:Retrieval:Mmr` (`Enabled`, `Lambda`) | Hybrid BM25+RRF retrieval, opt-in MMR |
| `Orkeon:Rag:Rerank` (`Enabled`, `Kind`, `TopN`) | Reranker selection and depth |
| `Orkeon:Rag:Context` (`MaxTokens`, `Ordering`) | Context assembly (anti-Lost-in-the-Middle `edges` ordering) |
| `Orkeon:Rag:Groundedness` (`Enabled`) | Groundedness hook |
| `Orkeon:Rag:Generation` (`SystemPrompt`, `Temperature`, `MaxOutputTokens`) | Cited generation stage |
| `Orkeon:Rag:Ingestion` (`DefaultChunkingStrategy` default `recursive`, `ManifestDirectory` default `/output/rag/manifests`) | Ingestion pipeline (`RagIngestionOptions`) |
| `Orkeon:Rag:QueryTransform` (`Mode`, `VariantCount`), `Orkeon:Rag:QueryRouting` (`Classifier`) | Query transformers (`multi-query`/`rag-fusion`/`hyde`), Adaptive-RAG routing (`heuristic` or `llm`) |
| `Orkeon:Rag:Corrective` (`MaxIterations`) | CRAG corrective graph bounds |
| `Orkeon:Rag:Corrective:WebFallback` (`Enabled`, `MaxResults`) + `Orkeon:Rag:WebFallback` (`Enabled`, `Endpoint`, `ApiKeyEnvVar`, `MaxResults`, `Timeout`, `SuspiciousAction`) | Web fallback — double opt-in, both `Enabled` off by default (policy + SearxNG transport) |

### Security, sandbox, tools

| Section | Configures | Consumer | Opt-in |
|---|---|---|---|
| `Orkeon:Security:PermissionGate` | Per-tool-call gate (`Enabled` default `false`, `Interactive`) | `ModePermissionGate` | `AddOrkeonPermissionGate(configuration)` — called by `RunnerHost`; no-op unless `Enabled = true` |
| `Orkeon:Dlp` | DLP policies / PII detection (`Enabled`, `DefaultAction`, `ChannelPolicies`) | Infrastructure | `AddOrkeonDlp()` |
| `Orkeon:Guardian` | Guard pipeline: `Enabled` (default `true`), `DefaultPolicy` (`InputGuardEnabled`, `ToolGuardEnabled`, `DelegationGuardEnabled`, `MaxDelegationDepth` — 5) | `GuardianPipeline` — the input phase of every agent turn, the tool and delegation phases of every tool call | — (on by default, registered by `AddOrkeonInfrastructure()`) |
| `Orkeon:CodeSandbox` (+ `:Docker`) | Secure code interpreter sandbox (`TimeoutSeconds`, `MaxMemoryBytes`, `MaxOutputBytes`, `AllowHostExecution`, `DefaultPermissions`, `SecurityOptions`; Docker: `ImageName`, `PullImageOnStartup`) | Infrastructure | — (registered by `AddOrkeonInfrastructure()`; the section gates behavior) |
| `Orkeon:Sandbox` | Sandbox file-system mount (`/sandbox`, Internal): `EphemeralRoot`, `CleanupOrphansOlderThan` (24 h), `VirtualPath` | `AddOrkeonFileSystem(...)` | — |
| `Orkeon:FileSystem` (`Mounts`) | VFS mounts (see [VFS compliance](../architecture/vfs-compliance.md)). An entry may carry an **id** — `<ulid>|<physical>:<virtual>:<rights>` (VFS-90): what a crew's `mounts:` block, Studio's team sidecar and `--mount-id` name it by; Studio writes one on every save. A CLI `--mount` on the same virtual root **replaces every entry of that root** for that run; on a new root it is appended (never merged, never dropped). One root declared twice is legitimate only when every entry of it carries an id — `--mount-id`, or the crew's `mounts:`, then selects one and the others are withdrawn for the run; with nothing selecting one the run is refused before any host builds (`'/output' is declared twice in <settings> (<idA>: <folderA>, <idB>: <folderB>) and nothing selects one. Pass --mount-id <id>, list '<id>|/output' under mounts: in the crew, or pass --mount <folder>:/output:rw to replace them all.`), and so is a root declared twice with an entry that has no id (`… and '<entry>' has no id. Give every entry an id …`) or an id carried by two entries. Every declared entry's base path is **whitelisted for `PathValidator`** without `--allow-external-mounts` — a declared folder is the machine owner's intent, so it is reachable even when it lies outside the process working directory; a withdrawn entry is not | `AddOrkeonFileSystem(...)` | — |
| `Orkeon:FileSystem` (`InternalMounts`) | Same grammar as `Mounts`, registered `MountVisibility.Internal`: resolvable by the VFS, **absent from `list_mounts`, from the agent prompt's mount table and from access-denied messages**. Where a host puts what the VFS must reach and no agent has any business addressing — the `--llm-log` directory lives here, and so does `/credentials`, the OAuth tokens of the e-mail accounts, when one is declared ([ADR-008](../adr/ADR-008-virtual-paths-are-the-only-currency.md)) | `AddOrkeonFileSystem(...)` | — |
| `Orkeon:Tools:Shell:AllowInterpreters` | Allow interpreters/mutating git in `ShellCommandTool` (**RCE-equivalent**, warning emitted) | `AddOrkeonCodeTools()` | config-only |
| `Orkeon:Tools:Shell:ExtraAllowedCommands` / `AllowedCommands` | Shell allowlist: additive / full replacement (replacement cancels `AllowInterpreters`) | idem | config-only |

### E-mail (`Orkeon:Tools:Email`)

Bound by `AddOrkeonEmailTools(configuration)` — the shared runner host and `orkeon-repl` call
it — and validated lazily: an account is validated the first time a tool or an `orkeon email`
command uses it, every problem reported at once, and a value the binder cannot even read (a
misspelt right, a port in words) sets that one account aside instead of failing the host — a
broken section never breaks a crew that sends no mail. Secrets are never values here, only the
**names** of the environment variables that hold them. Provider walkthroughs and the
key-by-key table: [E-mail tools](../guides/email.md).

| Section | Configures |
|---|---|
| `Orkeon:Tools:Email:DefaultAccount` | The account a call that names none uses (optional with a single account) |
| `Orkeon:Tools:Email:CredentialsDirectory` | Physical directory whose `email` subdirectory holds the OAuth tokens; a relative path is read from the settings file's directory. The runner mounts it at the internal root `/credentials` when an OAuth account is declared; default: `credentials` next to the per-user settings file |
| `Orkeon:Tools:Email:Screening:WithholdRejected` | Withhold the body of a message the prompt-injection screen rejects (default `false`: flag only) |
| `Orkeon:Tools:Email:Accounts:<name>` | One account. `Provider` (`Gmail`, `Outlook` or `Custom` — the default), `Address`, `DisplayName`, `Rights` (**mandatory** — `Read, Organize, Draft, Send, Delete, Purge`), `Incoming` (`Protocol` `Imap`, `Pop3` or `Graph`; `Host`, `Port`, `Security` `SslOnConnect`, `StartTls` or `None` — the last towards a loopback host only), `Outgoing` (`Protocol` `Smtp` or `Graph`; `Host`, `Port`, `Security`), `Auth` (`Method` `Password` or `OAuth2`; `Username`, `PasswordEnvVar`, `ClientId`, `ClientSecretEnvVar`, `Tenant`), `Send` (`AllowedRecipients` — an empty list allows nobody —, `MaxRecipients`, `MaxPerHour`), `TimeoutSeconds`, `SaveSentCopy`. The name holds letters, digits, `.`, `_` and `-`, starts with a letter or a digit (64 at most) |

### Scripting and CLI

| Section | Configures | Consumer |
|---|---|---|
| `Orkeon:Scripting:Limits` | Jint sandbox: `MemoryLimitBytes` (default 100 MB), `RecursionLimit` (64), `ExecutionTimeout` (30 s) | `Orkeon.Scripting` |
| `Orkeon:Scripting:Toolchain` | esbuild toolchain resolution (`EsbuildPath`, first in the lookup order; `EsbuildTimeout` 30 s) | `EsbuildTranspiler.Create` — `orkeon run`, the shared runner, `orkeon doctor`, `orkeon forge`, `*.cmd.ts` commands |
| `Orkeon:Cli:ScriptCommands` (+ `:Limits`) | TypeScript CLI command discovery (`Enabled`, `Directories`, `FailFastOnInvalidScript`, `EsbuildTranspile`, `MaxScripts` 50, `ContinueOnConflict`, `FallbackCommandName` `assistant`) + tighter CLI sandbox profile | `Orkeon.Cli.Commands.Scripting` |
| `Orkeon:Cli:ScriptHost` | Crew resolution for `<name>/crew.ork.ts`: `CrewDirectories`, `CrewFileName` (`crew.ork.ts`), `RunCrewTimeout` (10 min) | `ScriptHostFacade` |
| `Orkeon:Cli:ConsoleStreaming` | Streamed `ctx.llm.act` deltas on the REPL console (`Enabled` default `false`) | `AddLlmConsoleStreaming(configuration)` |
| `Orkeon:Cli:Session:ContextWindowTokens` | Context window used by `token_budget` and the TUI | `TokenBudgetTool`, ConsoleApp |
| `Orkeon:Cli:Tui:SpinnerVerbs` | TUI spinner verbs | ConsoleApp |

### Service host (`orkeon-host`)

| Section | Configures | Consumer |
|---|---|---|
| `Orkeon:Host` | The daemon: `Crews` (each `Name` — unique, case-insensitively — `Path`, `Profile:MaxConcurrentRuns` default 4 — chat and A2A runs counted together —, `Mounts` — the per-crew mount namespace —, `Description` — what the crew's A2A skill says it does), `RunTimeout` (30 min), `ShutdownGracePeriod` (20 s), `LlmProfiles` (the allow-list of `Llm:Profiles` the hosted crews may name — their agents, tasks and managers — and `Orkeon:Rag:LlmProfile` too; unset offers them all, `["default"]` the default alone; an entry naming an undefined profile, or a RAG profile the list leaves out, refuses the start) | `Orkeon.Host` — see [Service host](../architecture/service-host.md) |
| `Orkeon:Host:Discord` | Discord channel: `Enabled`, `TokenEnvironmentVariable` (`ORKEON_DISCORD_TOKEN`), `AllowedUserIds`, `GuildIds`, `ProgressInterval` (2 s), `Routes` (Discord channel id → crew name: a thread opened in that channel starts that crew), `DefaultCrew` (the crew an unrouted channel reaches; the first declared crew when unset). A route to an undeclared crew, a route key that is not a channel id, or an unknown `DefaultCrew` refuses the start | idem |
| `Orkeon:Host:A2A` | A2A server, off by default (GAP-23): `Enabled`, `Host` (`http://localhost`; `http://+` listens on every interface), `Port` (5002), `Crews` (the crews other agents may run, by name — one skill each, a task being a run of that crew; none by default). An enabled section that exposes no crew or a crew `Crews` does not declare, a malformed `Host` or `Port`, or a listener beyond the loopback while `A2A:Security` declares no authentication scheme nor mutual TLS refuses the start | idem — see [Service host](../architecture/service-host.md#5-other-agents-a2a) |

### Multi-modal

| Section | Configures | Opt-in |
|---|---|---|
| `Orkeon:MultiModal` | Vision/content validation (`Enabled`, `MaxImageSizeBytes` 20 MB, `SupportedImageFormats`, `SupportedAudioFormats`, `MaxAudioDurationSeconds`) — no image is resized | `AddOrkeonMultiModal(configuration)` |

---

> **See also**: [Opt-in subsystems](./opt-in-subsystems.md) ·
> [Hosting](./hosting.md) ·
> [Memory system](../architecture/memory-system.md) ·
> [RAG pipeline](../architecture/rag-pipeline.md) ·
> [Back to index](../INDEX.md)
