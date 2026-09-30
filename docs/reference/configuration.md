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
until that launch starts. No file is ever generated: the composition is in-memory.

## LLM provider (`Llm` section)

The `Llm` section is read by `RunnerHost.RegisterLlmProvider` and turned into an
`ILlmProvider` via `ILlmProviderFactory`. **The provider is inferred automatically**, in
order: base-URL host patterns (e.g. `deepseek.com` → DeepSeek, `api.x.ai` → Grok,
`/engines/` → Docker Model Runner/OpenAI-compatible), then model-name patterns, then
API-key shape; default `openai`. Keys: `Model`, `BaseUrl`, `ApiKey` (prefer
`ORKEON_Llm__ApiKey` — the variable each provider's key conventionally lives in, and the three
names confused with it, are in the
[provider comparison](llm-providers-comparison.md#api-keys-the-variable-per-provider)), `Temperature`, `MaxTokens`, `TimeoutSeconds`, `MaxRetries` (default 10), and
`Thinking:{Enabled,Effort}` for thinking-capable providers. `TimeoutSeconds` defaults to 30 s,
too short for a model that thinks before it answers (Kimi K2.6, DeepSeek V4 and GLM do so by
default): set 600 s, or turn thinking off with `Thinking:Enabled = false`. A call that hits the
timeout is retried once, then fails its task with a message naming the setting — it is never
reported as an empty answer (LLM-11). `MaxTokens` is a **pin**: left
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

## Sections outside the `Orkeon:` prefix

| Section | Configures | Consumer / opt-in |
|---|---|---|
| `Llm` | Active LLM provider (see above) | `RunnerHost` |
| `Llm:AvailableModels` | The model list a scripted `/model` REPL command can offer (string array, or one comma-separated string) | `AddOrkeonSessionTools(configuration)` |
| `Memory:Provider`, `Memory:ConnectionString` | Application-wide memory provider selected through `MemoryProviderFactory` (`inmemory`, `redis`, `sqlite`, `chromadb`, `pinecone`, `lancedb`; unset → in-memory) — see [Memory system](../architecture/memory-system.md#selection-by-configuration) | `AddOrkeonInfrastructure()` |
| `RateLimiting` | LLM request throttling: `GlobalRequestsPerMinute`, `ProviderRequestsPerMinute`, `AgentRequestsPerMinute`, `MaxConcurrentRequests`, `QueueLimit` | `AddOrkeonInfrastructure()` (`ILlmRateLimiter`) |
| `LlmLogging` | LLM exchange capture tuning: `FullEmbeddingLog` (default `true`), `LogStreamingExchanges` (`true`), `MaxBodyLengthChars` (`0` = no truncation) | `RunnerHost` → `AddLlmExchangeLogging(logDirectory, options)`, only when the run passes `--llm-log` |
| `PathSecurity` | Physical path validation: `DefaultWorkspaceRoot`, `AdditionalAllowedDirectories`, `AdditionalBlockedExtensions`, `ResolveSymlinks`, `MaxFileSizeBytes` | `AddOrkeonInfrastructure()` (`IPathValidator`) |
| `Telemetry` | OpenTelemetry export: `Enabled`, `OtlpEndpoint`, `ExportToConsole`, `PrometheusEndpoint`, `MaxMemoryMB` (the standard `OTEL_EXPORTER_OTLP_ENDPOINT` also works in the runners) | `AddOrkeonTelemetry(configuration)` — called by `AddOrkeonInfrastructure(configuration)` and `RunnerHost` |
| `A2A` | A2A server/client: `EnableServer`, `Port`, `Host`, `AgentName`, `AgentDescription`, `AgentVersion`, `Organization`, `ContactUrl`, `TimeoutSeconds` (`Enabled` and `RemoteAgents` are bound but read by nothing) | opt-in `AddOrkeonA2A(configuration)` — no shipped host calls it; see [A2A conformance](./a2a-conformance.md#activation) |
| `A2A:Security` | `ClientCertificatePath`, `ClientCertificatePassword`, `TrustedCertificateAuthorities`, `TrustedClientCertificateThumbprints`, `RequireMutualTls`, `AllowedAuthSchemes` | idem |
| `MCP` | MCP client connections (`MCP:Servers:<id>`), the switch `MCP:Enabled` (default `true`), and the optional MCP server — `MCP:EnableServer` (default `false`) + `MCP:Server` (`Name`, `Version`, `ExposeResources`, `ExposePrompts`) | `RunnerHost` (`orkeon run`) when `MCP:Servers` declares at least one server and `MCP:Enabled` is not `false` — the servers are connected before the crew loads (STUDIO-21); library hosts call `AddOrkeonMcp(configuration)` or the `AddOrkeonInfrastructure(configuration)` overload — see [MCP integration](../architecture/mcp.md) |
| `Secrets:<NAME>` | Second stop of the secret chain after `ORKEON_<NAME>` (`ConfigurationSecretProvider`), e.g. `Secrets:TAVILY_API_KEY` for `web_search` | `AddOrkeonInfrastructure()` |
| `Evaluation` | Evaluation services: `EnableLlmJudge`, `DefaultRunsPerCase`, `RegressionThreshold` | `AddOrkeonEvaluation(configuration)` — `AddOrkeonInfrastructure()` calls it **without** a configuration, so the section is only read when the host calls it itself |
| `RaggableTree` | Codebase indexing: `Enabled`, `Exclude`, `RootAlias`, `IndexMode`, `EnrichWithLlm`, `IncludeStatements`, `Embedding` (`Provider`, `Model`, `ApiKey`, `BaseUrl`, `Dimensions`, `MaxTextChars`) | **opt-out in runner hosts**: `RunnerHost` registers it by default, `RaggableTree:Enabled = false` disables; library consumers call `AddRaggableTree(options)` explicitly |
| `Resilience` | `LlmMaxRetries`, `LlmTimeoutSeconds`, `CircuitBreakerThreshold`, `CircuitBreakerDurationSeconds`, `DatabaseMaxRetries`, `RedisMaxRetries` — **bound to `ResilienceOptions` but read by nothing**: the LLM retry budget and timeout are `Llm:MaxRetries` / `Llm:TimeoutSeconds` | `AddOrkeonInfrastructure()` |
| `ToolRateLimiting` | Per-tool rate limits: `GlobalToolRequestsPerMinute`, `DefaultToolRequestsPerMinute`, `ToolSpecificLimits` | opt-in `AddOrkeonToolRateLimiting()` (binds from the registered `IConfiguration`; see [opt-in subsystems](./opt-in-subsystems.md)) |
| `TokenBudget` | Token budgets: `MaxTokensPerAgent`, `MaxTokensPerCrew`, `MaxCostPerCrew` | idem |
| `Security:Audit` | Audit sinks: `Enabled`, `MinSeverity`, `EnabledCategories`, `AuditDirectory`, `RetentionDays` | `AddOrkeonInfrastructure()` |
| `Security:Url` | SSRF validation: `AllowedSchemes`, `BlockedPorts`, `AllowedDomains`, `BlockedDomains`, `BlockPrivateIPs`, `ResolveDNS` | `AddOrkeonInfrastructure()` (`IUrlValidator`) |
| `Security:Vault` | Secret vault chain: `AzureKeyVaultUri`, `UseAwsSecretsManager`, `DpapiSecretsDirectory`, `CacheTtl` | `AddOrkeonInfrastructure()` |
| `Security:Prompt`, `Security:ToolResults` | Prompt hardening (`Policy`, `CustomPatterns`, `EnableExfiltrationDetection`) and tool-result screening (`MaxToolResultLength`, `Policy`, `TrustedTools`) | registered by `AddOrkeonInfrastructure()`, but **no execution path calls** `PromptSanitizer`/`ToolResultSanitizer` — a host that wants them invokes them — see [Security](../architecture/security.md) |
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
| `Orkeon:ChromaDb` | ChromaDB server: `BaseUrl`, `Tenant`, `Database`, `CollectionName`, `DefaultTopK` | `AddOrkeonChromaDb` (concrete `ChromaDbMemoryProvider`) | auto-registered by `AddOrkeonInfrastructure(configuration)` **when the section exists**, or `AddOrkeonChromaDb(configuration)` |
| `Orkeon:Pinecone` | Pinecone index: `ApiKey`, `Environment`, `IndexName`, `Namespace` | `AddOrkeonPinecone` (concrete `PineconeMemoryProvider`) | idem, `AddOrkeonPinecone(configuration)` |
| `Orkeon:LanceDb` | Remote LanceDB server: `Endpoint`, `ApiKey`, `Database`, `TableName`, `EmbeddingDimension`, `DistanceType`, `DefaultTopK`, `MinSimilarityScore`, `VectorWeight`, `FullTextWeight`, `CreateFullTextIndexOnInit` | `VectorStoreExtensions` | `AddOrkeonLanceDb(configuration)` only (never auto) |
| `Orkeon:CognitiveMemory` | Cognitive memory layering (`EnableLlmAnalysis`, `EnableContradictionDetection`, `ContradictionCandidateCount`, `AnalysisModel`, `AnalysisTemperature`, `PruningThreshold`, `PruningMinAgeDays`, `RecencyHalfLifeHours`, `DefaultRecallOptions`) | Infrastructure | `AddOrkeonCognitiveMemory(configuration)` |
| `Orkeon:Encryption` | At-rest memory encryption (`Enabled`, `SecretName`, `KeySizeInBits`) | Infrastructure | — (registered by `AddOrkeonInfrastructure()`; the section gates behavior; the decorator is applied by the host). Key rotation stays opt-in: `AddOrkeonKeyRotation()` |

### RAG (`Orkeon:Rag`)

Details and semantics: [RAG pipeline](../architecture/rag-pipeline.md). Everything below
requires the opt-in `AddOrkeonRag(configuration)` (`Orkeon.Rag.DependencyInjection`).

| Section | Configures |
|---|---|
| `Orkeon:Rag:Profile` | Profile preset `fast` (default) / `balanced` / `quality` / `adaptive` / `corrective`; any `Orkeon:Rag` key overrides the preset key-by-key |
| `Orkeon:Rag:Provider`, `Orkeon:Rag:ConnectionString`, `Orkeon:Rag:ProviderOptions` | Dedicated RAG document-store provider (`RagStoreOptions` — a `MemoryProviderFactory` type alias plus its options); default is the ambient `IMemoryProvider` |
| `Orkeon:Rag:Collection` | Default collection name |
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
| `Orkeon:Guardian` | Guard pipeline (`Enabled`, `DefaultPolicy`, `AuditEnabled`, `LogViolations`) | `AddOrkeonGuardian()` | — (registered by `AddOrkeonInfrastructure()`, but **no execution path runs the pipeline**: the host invokes `GuardianPipeline` itself) |
| `Orkeon:Auth:AzureAD`, `Orkeon:Auth:OIDC` | Auth providers (`TenantId`, `ClientId`, `Authority`, `ValidIssuers`, `ValidAudiences`; OIDC: `Authority`, `ClientId`, `ValidAudiences`, `RequireHttpsMetadata`) | `AddOrkeonAuth()` | — (options registered by `AddOrkeonInfrastructure()`; nothing in the execution path authenticates with them) |
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
| `Orkeon:DefaultLlmProvider` | Default provider name for the scripting `ctx.llm` namespace | `Orkeon.Scripting` |
| `Orkeon:Scripting:Limits` | Jint sandbox: `MemoryLimitBytes` (default 100 MB), `RecursionLimit` (64), `ExecutionTimeout` (30 s) | `Orkeon.Scripting` |
| `Orkeon:Scripting:Toolchain` | esbuild toolchain resolution (`EsbuildPath`, `EsbuildTimeout` 30 s) | `Orkeon.Scripting` |
| `Orkeon:Cli:ScriptCommands` (+ `:Limits`) | TypeScript CLI command discovery (`Enabled`, `Directories`, `FailFastOnInvalidScript`, `EsbuildTranspile`, `MaxScripts` 50, `ContinueOnConflict`, `FallbackCommandName` `assistant`) + tighter CLI sandbox profile | `Orkeon.Cli.Commands.Scripting` |
| `Orkeon:Cli:ScriptHost` | Crew resolution for `<name>/crew.ork.ts`: `CrewDirectories`, `CrewFileName` (`crew.ork.ts`), `RunCrewTimeout` (10 min) | `ScriptHostFacade` |
| `Orkeon:Cli:ConsoleStreaming` | Streamed `ctx.llm.act` deltas on the REPL console (`Enabled` default `false`) | `AddLlmConsoleStreaming(configuration)` |
| `Orkeon:Cli:Session:ContextWindowTokens` | Context window used by `token_budget` and the TUI | `TokenBudgetTool`, ConsoleApp |
| `Orkeon:Cli:Tui:SpinnerVerbs` | TUI spinner verbs | ConsoleApp |

### Service host (`orkeon-host`)

| Section | Configures | Consumer |
|---|---|---|
| `Orkeon:Host` | The daemon: `Crews` (each `Name`, `Path`, `Profile:MaxConcurrentRuns` default 4, `Mounts` — the per-crew mount namespace), `RunTimeout` (30 min), `ShutdownGracePeriod` (20 s) | `Orkeon.Host` — see [Service host](../architecture/service-host.md) |
| `Orkeon:Host:Discord` | Discord channel: `Enabled`, `TokenEnvironmentVariable` (`ORKEON_DISCORD_TOKEN`), `AllowedUserIds`, `GuildIds`, `ProgressInterval` (2 s) | idem |

### Multi-modal

| Section | Configures | Opt-in |
|---|---|---|
| `Orkeon:MultiModal` | Vision/content validation (`Enabled`, `MaxImageSizeBytes` 20 MB, `MaxImageDimension` 2048, `AutoResizeImages`, `SupportedImageFormats`, `SupportedAudioFormats`, `MaxAudioDurationSeconds`) | `AddOrkeonMultiModal(configuration)` |

---

> **See also**: [Opt-in subsystems](./opt-in-subsystems.md) ·
> [Hosting](./hosting.md) ·
> [Memory system](../architecture/memory-system.md) ·
> [RAG pipeline](../architecture/rag-pipeline.md) ·
> [Back to index](../INDEX.md)
