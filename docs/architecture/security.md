> 🇫🇷 [Version française](../fr/architecture/security.md)

# Security, resilience and plugins

## Security

The framework integrates several security layers:

- **Tools**: `ToolAccessPolicy` (whitelist/blacklist/unrestricted) per agent, `IPathValidator` for path traversal protection, `IUrlValidator` for SSRF protection, `HttpHeaderSanitizer`. Tool names specified in YAML configurations are validated at creation time via `IToolRegistry`; under `Orkeon:CrewFactory:StrictTools` (the runners' default) no unregistered tool can be assigned to an agent — the library default is lenient (skip + Warning).
- **Code**: `RoslynCodeSecurityAnalyzer` (`ICodeSecurityAnalyzer`) and the `ICodeSandbox` implementations — `DockerSandbox`, `HostProcessRunner` behind `LazyProbingCodeSandbox` (`Orkeon.Infrastructure.Sandbox`) — for secure C# code execution
- **Data**: `IDatabaseSecurityPolicy` for SQL/NoSQL query validation, `PiiDetector` and the five `IDlpInterceptor` implementations (tool output, delegation, log, memory, external output — `Orkeon.Infrastructure.Security.Dlp`). The DLP subsystem is **opt-in** (`AddOrkeonDlp()`, see [Opt-in subsystems](../reference/opt-in-subsystems.md))
- **Encryption**: `IEncryptionProvider` with an AES implementation, applicable to memory providers via the Decorator pattern. Key rotation is **opt-in** (`AddOrkeonKeyRotation()`)
- **Audit**: `AuditLogger` (`Orkeon.Infrastructure.Security`) with its sinks (`Orkeon.Infrastructure.Security.Sinks`); `ProvenanceTracker` lives on the RAG side (`Orkeon.Rag.Validation`)
- **Compliance**: `NistComplianceReportGenerator` (`INistComplianceReporter`, `Orkeon.Infrastructure.Compliance`). NIST reporting is **opt-in** (`AddOrkeonNistCompliance()`)

### Tool resolution in YAML

When a Crew is created from YAML, each tool name in `agents[].tools[]` is resolved via `IToolRegistry.GetToolByNameAsync(toolName)`. What happens when a tool does not exist depends on `CrewFactoryOptions.StrictTools`:

- **Strict mode** (`Orkeon:CrewFactory:StrictTools=true` — the default for `orkeon run` and the runners): `CrewFactory` throws, naming the missing tools and listing the registry's available names. No agent can operate with non-existent capabilities.
- **Lenient mode** (the library default, `false`): the tool is skipped with a Warning log and the crew loads without it — a host embedding the library that wants the guarantee must turn strict mode on.

### A2A mutual TLS

The opt-in A2A subsystem (`AddOrkeonA2A()`) enforces the security posture declared in `A2ASecurityOptions`:

- **Server** (`RequireMutualTls = true`): an incoming client certificate is **authenticated, not merely required** — it must chain to one of the configured `TrustedCertificateAuthorities` (X509 chain built with `CustomRootTrust`, which also covers the validity window) or match a pinned `TrustedClientCertificateThumbprints` entry. Self-signed certificates that are not pinned are rejected (403). Starting the server with `RequireMutualTls` and no trust anchor **throws** (fail-closed): presence and date validity alone are not authentication. Revocation is not checked — the trust anchors are assumed to be private CAs without CRL/OCSP endpoints.
- **Client**: full TLS validation always applies — there is deliberately **no accept-any-certificate opt-out**. Configuring `TrustedCertificateAuthorities` pins trust to private CAs (a self-signed or local-dev server must pin its CA this way); the pinning only vouches for an **unknown chain** (`RemoteCertificateChainErrors`) — a host-name mismatch or a missing certificate is never bypassed.
- Real mutual TLS termination requires an OS-level HTTPS binding for `HttpListener` — see [Known limitations](../reference/limitations.md).

### RAG web fallback & prompt injection

The corrective RAG pipeline can optionally fall back to a web search (`WebSearchDocumentRetriever`, `Orkeon.Rag.WebFallback`) when local retrieval fails. Web content is the textbook vector for **indirect prompt injection**: a page crafted so that, once retrieved and inserted into an LLM prompt, its text is interpreted as instructions — hijacking the agent, leaking the conversation, or triggering tool calls.

The defense is layered; no single layer is trusted on its own:

- **Strict opt-in**: the fallback is off by default behind **two** independent switches — the transport (`Orkeon:Rag:WebFallback:Enabled`, wired by `AddOrkeonRagWebFallback(configuration)`) and the corrective-graph policy (`Orkeon:Rag:Corrective:WebFallback:Enabled`); both must be on for a web document to ever reach the graph. Enabling it without configuring an endpoint keeps it inert and logs a warning — there is no silent egress. The API key is read from an environment variable (`ApiKeyEnvVar`), never from configuration files.
- **Deterministic validation**: every downloaded page passes through `PromptInjectionDocumentValidator` (`Orkeon.Rag.Validation`) — pure heuristics, no LLM, testable offline. Detected signals: model-addressed directives (EN + FR: "ignore previous instructions", "you are now…", "system:", « nouvelles instructions : »…), chat-template control tokens (`<|im_start|>`, `<<SYS>>`, `[INST]`…) with per-occurrence accumulation, hidden HTML markup (`display:none`, `visibility:hidden`, near-zero opacity/font-size, `aria-hidden`), massive HTML comments, exfiltration vectors (auto-loading markdown images with encoded query payloads, percent-encoded link payloads, base64 data URIs) and long base64 blobs. The per-document verdict is `Clean | Suspicious | Rejected` with reasons and incriminated spans.
- **Traceability, no silent cleaning**: `Rejected` documents never leave the retriever and are logged with their reasons and URL. `Suspicious` documents are — per `SuspiciousAction` — either discarded (logged) or kept **flagged** in metadata (`injection_verdict`, `injection_reasons`, `injection_risk_score`). Content is never rewritten: the validator detects, it does not sanitize. On the ingestion path the same validator sits in the `DataValidationPipeline` (`IDataValidator` chain) with quarantine and provenance tracking.
- **Fail-quiet on the network, fail-loud on security**: search HTTP errors and timeouts degrade to an empty result list with a warning (the corrective graph simply proceeds without web context); rejections are always warnings with full reasons.

**Honest limits**: these heuristics are pattern-based and can be evaded — paraphrased directives, languages other than English/French, novel encodings or split payloads will pass. Conversely, legitimate technical documents *about* prompts or CSS may score `Suspicious` (they are flagged, never silently dropped). A content validator is a filter, **not a privilege boundary**: the real containment is architectural — retrieved text must stay data (never executed as instructions), agents consuming web content should run with least-privilege tools, and sensitive actions must not be triggerable by retrieved content alone.

## Resilience

`ResiliencePolicies` (`Orkeon.Infrastructure.Resilience`) exposes the Polly-based policies — `GetRetryPolicy`, `GetCircuitBreakerPolicy`, `GetTimeoutPolicy`, `GetCombinedPolicy`, `GetLlmApiPolicy` — used by the LLM providers and the HTTP tools.

## Checkpointing

`CheckpointManager` and `ResumeEngine` (`Orkeon.Application.Services.Checkpointing`) enable saving and resuming the state of crew executions. Useful for long workflows that must survive restarts.

Since R3.8, the **execution states** themselves (`ICrewExecutionStateManager`: status, progress, output) can also be persisted in the same state stores (`IStateStore` — in-memory, SQLite via `AddOrkeonSqliteCheckpointing`, PostgreSQL) for recovery after a crash. Opt-in via `AddCrewExecutionStatePersistence(...)` — or the `Orkeon:ExecutionState:Persistence` section, which only takes effect through the `AddOrkeonInfrastructure(IConfiguration)` overload (the shipped runners use the parameterless one, so for them the explicit call is the route); default: in-memory only. See [Opt-in subsystems](../reference/opt-in-subsystems.md).

## Plugin system

`IOrkeonPlugin`, `PluginAssemblyDiscovery`, `PluginLoader`/`PluginLoadContext` and `IPluginRegistry` (`Orkeon.Plugins`) provide a plugin system for extending the framework with custom tools and providers: directory-based discovery through the VFS, isolated loading via a collectible `AssemblyLoadContext`, opt-in DI activation `AddOrkeonPlugins(...)`.

> ⚠️ **Trust boundary**: loading a plugin executes arbitrary code with the host process privileges — no sandbox in v1, and the `AssemblyLoadContext` is not a security boundary. Only load trusted plugins. Details and operating rules: [Plugin system](./plugins.md).

---

> **See also**: [LLM providers](./llm-providers.md) · [Events and CQRS](./domain-events.md) · [Back to index](../INDEX.md)
