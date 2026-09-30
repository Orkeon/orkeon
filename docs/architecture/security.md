> 🇫🇷 [Version française](../fr/architecture/security.md)

# Security, resilience and plugins

## Security

The framework integrates several security layers:

- **Tools**: `ToolAccessPolicy` (whitelist/blacklist/unrestricted) per agent, checked when a tool is attached to the agent; `IPathValidator` for path traversal protection, `IUrlValidator` for SSRF protection, `HttpHeaderSanitizer`. Tool names specified in YAML configurations are validated at creation time via `IToolRegistry`; under `Orkeon:CrewFactory:StrictTools` (the runners' default) no unregistered tool can be assigned to an agent — the library default is lenient (skip + Warning).
- **File system**: every framework I/O goes through `IFileSystemService` — mounts, per-mount `FileAccessRights` (Read/Write/Create/Delete), physical paths redacted from error messages; the Roslyn analyzer `Orkeon.Compliance.Vfs` forbids direct `System.IO` in framework code ([VFS compliance](./vfs-compliance.md)). Underneath, `PathValidator` (`PathSecurity` section) refuses traversal, sensitive system paths and dangerous extensions: `AdditionalAllowedDirectories`, `AdditionalBlockedExtensions`, `ResolveSymlinks` (default `true`), `MaxFileSizeBytes` (default 50 MB), `DefaultWorkspaceRoot`.
- **Network (SSRF)**: `UrlValidator` (`Security:Url` section) allows only `http`/`https` (`AllowedSchemes`), blocks private and loopback addresses (`BlockPrivateIPs`, default `true`) after DNS resolution (`ResolveDNS`, default `true`), refuses the ports of common internal services (22, 23, 25, 110, 143, 445, 3306, 5432, 6379, 27017 — `BlockedPorts`) and honours `AllowedDomains`/`BlockedDomains`. It is consulted by the HTTP tool bases, `http_api`, the scraping tools, `WebPageLoader` and the RAG web fallback.
- **Shell**: `ShellCommandTool` runs a read-only allowlist by default (`ls`, `cat`, `pwd`, `which`, `grep`, `wc`, `echo`, `dir`, `type`, `where`, and `git` restricted to `status`/`log`/`diff`/`show`); interpreters (`dotnet`, `npm`, `node`, `find`) and mutating git only come back with `Orkeon:Tools:Shell:AllowInterpreters` (RCE-equivalent, warning logged) — see [Opt-in subsystems](../reference/opt-in-subsystems.md).
- **Code**: `RoslynCodeSecurityAnalyzer` (`ICodeSecurityAnalyzer`) and the `ICodeSandbox` implementations — `DockerSandbox`, `HostProcessRunner` behind `LazyProbingCodeSandbox` (`Orkeon.Infrastructure.Sandbox`) — for C# code execution by `SecureCodeInterpreterTool`. `DockerSandbox` runs `docker run --rm --cap-drop=ALL --security-opt=no-new-privileges --pids-limit=…` with a memory cap, `--network=none` unless the request allows network access and `--read-only` (+ a `noexec` tmpfs) unless it allows writes; the container still runs as root inside its namespace, without capabilities. `HostProcessRunner` provides **no** OS isolation, so the gate is fail-closed: without Docker, execution is refused unless the operator sets `Orkeon:CodeSandbox:AllowHostExecution = true` (RCE-equivalent — trusted code only). Other keys: `TimeoutSeconds` (30), `MaxMemoryBytes` (256 MB), `MaxOutputBytes` (50 000); `Orkeon:CodeSandbox:Docker` holds `ImageName` (`mcr.microsoft.com/dotnet/sdk:10.0-alpine`) and `PullImageOnStartup`.
- **Data**: `IDatabaseSecurityPolicy` (`Orkeon.Tools.Abstractions.Data`) for SQL query validation in the relational database tools, `PiiDetector` and the five `IDlpInterceptor` implementations (tool output, delegation, log, memory, external output — `Orkeon.Infrastructure.Security.Dlp`). The DLP subsystem is **opt-in** (`AddOrkeonDlp()`, see [Opt-in subsystems](../reference/opt-in-subsystems.md))
- **Output validation**: the six `IOutputValidator`s (`LengthValidator`, `FormatValidator`, `SchemaValidator`, `CompletenessValidator`, `ContentSafetyValidator`, `PiiDetectionValidator`) form the `OutputValidationPipeline` the execution orchestrator runs on a task's output when the task declares output expectations, with correction retries.
- **Encryption**: `IEncryptionProvider` with an AES-256-GCM implementation (`AesEncryptionProvider`, section `Orkeon:Encryption`: `Enabled` — default `false` —, `SecretName` — default `orkeon-encryption-key`, resolved through `ISecretProvider` —, `KeySizeInBits` — 256), applicable to any memory provider via the `EncryptedMemoryProviderDecorator` — nothing wraps a provider automatically, the host composes the decorator. Key rotation is **opt-in** (`AddOrkeonKeyRotation()`)
- **Secrets**: `ISecretProvider` is a chain — `ORKEON_<NAME>` environment variables, then the `Secrets` configuration section, then, when `Security:Vault` configures them, Azure Key Vault (`AzureKeyVaultUri`), AWS Secrets Manager (`UseAwsSecretsManager`, with an `IAmazonSecretsManager` registered by the host) and DPAPI on Windows (`DpapiSecretsDirectory`); `CacheTtl` caches vault reads. `LogSanitizer` masks secrets in logged LLM errors and exchanges.
- **LLM rate limiting**: `LlmRateLimiter` (`RateLimiting` section: `GlobalRequestsPerMinute` 60, `ProviderRequestsPerMinute` 30, `AgentRequestsPerMinute` 20, `MaxConcurrentRequests`, `QueueLimit` 5) gates every model call of the execution orchestrator. Per-tool rate limits and token budgets are opt-in (`AddOrkeonToolRateLimiting()`).
- **Permission gate**: `ModePermissionGate` (`IPermissionGate`) asks before each tool call of the scripted runtime (`.ork.ts`, `ctx.llm.act`) according to a mode (`default`, `acceptEdits`, `plan`, `bypassPermissions`) and the tool's declared `ToolAccess` (Read/Edit/Execute); off unless `Orkeon:Security:PermissionGate:Enabled = true` — see [Opt-in subsystems](../reference/opt-in-subsystems.md).
- **Audit**: `AuditLogger` (`Orkeon.Infrastructure.Security`, section `Security:Audit`: `Enabled`, `MinSeverity`, `EnabledCategories`, `RetentionDays` — 90) with its sinks (`Orkeon.Infrastructure.Security.Sinks`: `StructuredLogAuditSink` and `InMemoryAuditSink` registered by default, `JsonFileAuditSink` for a host that wants files under `AuditDirectory`); `ProvenanceTracker` lives on the RAG side (`Orkeon.Rag.Validation`)
- **Compliance**: `NistComplianceReportGenerator` (`INistComplianceReporter`, `Orkeon.Infrastructure.Compliance`). NIST reporting is **opt-in** (`AddOrkeonNistCompliance()`)

### What runs on its own, and what the host calls

`AddOrkeonInfrastructure()` registers more security components than the execution path
consumes. Registered does not mean enforced:

| Component | On the execution path? |
|---|---|
| Tool resolution (`StrictTools`), `ToolAccessPolicy`, VFS rights + `PathValidator`, `UrlValidator`, shell allowlist, sandbox gate, output validation pipeline, `LlmRateLimiter`, secret chain | **Yes** — enforced wherever they apply, with no host code |
| Permission gate | Yes for the scripted runtime once enabled; YAML crews do not consult it |
| `GuardianPipeline` (`Orkeon:Guardian` — `InputGuard`, `OutputGuard`, `ToolGuard`, `DelegationGuard`), `IPromptSanitizer` + `PromptShieldBuilder` (`Security:Prompt`), `ToolResultSanitizer` (`Security:ToolResults`) | **No** — registered building blocks (`AddOrkeonGuardian()` binds `Orkeon:Guardian` and wires the four guards into the pipeline; the sanitizers are registered with the security core; all of it is called by `AddOrkeonInfrastructure()`); no orchestrator, agent loop or tool calls them. A host that wants them resolves `GuardianPipeline` / `IPromptSanitizer` / `ToolResultSanitizer` and invokes them itself. The audit trail is fed by the Guardian pipeline, so it stays empty unless the host runs it |
| `AuthenticationGuard`, `AzureAdAuthProvider`, `OidcAuthProvider` (`Orkeon:Auth:AzureAD`, `Orkeon:Auth:OIDC`) | **No** — `AddOrkeonAuth()` (called by `AddOrkeonInfrastructure()`) binds the options and registers the guard, but no `IAuthenticationProvider` is registered and the guard is not in the Guardian pipeline: a host that authenticates registers a provider and adds the guard itself |
| DLP interceptors, NIST reporting, key rotation, tool rate limiting | Opt-in, and host-invoked once registered — see [Opt-in subsystems](../reference/opt-in-subsystems.md) |

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

- **Strict opt-in**: the fallback is off by default behind **two** independent switches — the transport (`Orkeon:Rag:WebFallback:Enabled` + an `Endpoint`, registered by `AddOrkeonRag(configuration)`) and the corrective-graph policy (`Orkeon:Rag:Corrective:WebFallback:Enabled`); both must be on for a web document to ever reach the graph. Enabling it without configuring an endpoint keeps it inert and logs a warning — there is no silent egress. The API key is read from an environment variable (`ApiKeyEnvVar`), never from configuration files.
- **Deterministic validation**: every downloaded page passes through `PromptInjectionDocumentValidator` (`Orkeon.Rag.Validation`) — pure heuristics, no LLM, testable offline. Detected signals: model-addressed directives (EN + FR: "ignore previous instructions", "you are now…", "system:", « nouvelles instructions : »…), chat-template control tokens (`<|im_start|>`, `<<SYS>>`, `[INST]`…) with per-occurrence accumulation, hidden HTML markup (`display:none`, `visibility:hidden`, near-zero opacity/font-size, `aria-hidden`), massive HTML comments, exfiltration vectors (auto-loading markdown images with encoded query payloads, percent-encoded link payloads, base64 data URIs) and long base64 blobs. The per-document verdict is `Clean | Suspicious | Rejected` with reasons and incriminated spans.
- **Traceability, no silent cleaning**: `Rejected` documents never leave the retriever and are logged with their reasons and URL. `Suspicious` documents are — per `SuspiciousAction` — either discarded (logged) or kept **flagged** in metadata (`injection_verdict`, `injection_reasons`, `injection_risk_score`). Content is never rewritten: the validator detects, it does not sanitize. On the ingestion path the same validator sits in the `DataValidationPipeline` (`IDataValidator` chain) with quarantine and provenance tracking.
- **Fail-quiet on the network, fail-loud on security**: search HTTP errors and timeouts degrade to an empty result list with a warning (the corrective graph simply proceeds without web context); rejections are always warnings with full reasons.

**Honest limits**: these heuristics are pattern-based and can be evaded — paraphrased directives, languages other than English/French, novel encodings or split payloads will pass. Conversely, legitimate technical documents *about* prompts or CSS may score `Suspicious` (they are flagged, never silently dropped). A content validator is a filter, **not a privilege boundary**: the real containment is architectural — retrieved text must stay data (never executed as instructions), agents consuming web content should run with least-privilege tools, and sensitive actions must not be triggerable by retrieved content alone.

### E-mail tools

The e-mail family (`Orkeon.Tools.Email` — [guide](../guides/email.md)) reads mail written by
strangers and acts on a real mailbox, so its defense does not rest on the model's judgment:

- **The model only picks an account name.** Servers, credentials and rights are the operator's
  configuration (`Orkeon:Tools:Email`). A secret is the *name* of an environment variable,
  never a value in that configuration, and never a tool argument — tool arguments are logged.
- **Rights are declared per account, and mandatory.** `Read`, `Organize`, `Draft`, `Send`,
  `Delete`, `Purge`: an account that declares none is refused, and every call is checked
  against the right its operation needs. Each tool also declares its `ToolAccess` (Read, Edit,
  Execute — `email_send` and `email_delete` are Execute), which is what the permission gate reads.
- **Sending fails closed.** `email_send` reaches only the addresses `Send:AllowedRecipients`
  allows (an address, `*@domain` or `*`; an empty list allows nobody), checked on To, Cc and
  Bcc by address, never by display name. The SMTP envelope is passed explicitly — the checked
  list, never derived from headers, so no `Resent-*` header can widen it — `From` is forced to
  the account, and `Send:MaxRecipients` / `Send:MaxPerHour` cap the volume. Over Graph, which
  reads the envelope from the headers, a message naming anyone outside the checked list is
  refused. `email_draft` is the human-review path: the agent writes, a person sends.
- **Received content is untrusted.** Every search page and read result opens with a notice
  that it is data, never instructions. `email_read` and `email_parser` carry the verdict of
  the same `PromptInjectionDocumentValidator` as the web fallback above, run on the
  **rendered** text — what the agent sees, not the raw HTML or MIME. Text the HTML hides from
  a human reader through the inline declarations the renderer knows (`display:none`,
  `font-size:0`…) or the `hidden` attribute is left out and flagged `hidden_content`; a CSS
  class, a 1-pixel font or white on white is not detected, and a plain-text part — what the
  agent reads when there is one — is not compared with the HTML. The default policy flags;
  `Screening:WithholdRejected` withholds the body of a rejected message.
- **Tokens stay out of the agents' reach.** OAuth tokens are written by `orkeon email login`
  into the internal root `/credentials`, mounted only when an OAuth account is declared and
  reached through `PrivilegedFileSystemAccess`, which no agent-facing tool resolves
  ([VFS compliance](./vfs-compliance.md)). The tools never start an interactive sign-in, and a
  Graph paging link is followed only while it still points at `graph.microsoft.com` over
  HTTPS, so the bearer token never leaves for another host. Transport is TLS
  (`SslOnConnect` or `StartTls`) — `None` is accepted towards a loopback test server only, and
  no option accepts an invalid certificate.
- **Forged crews get no mailbox.** `orkeon forge` removes the twelve mailbox tools from the
  catalogue of the crews it tries on its bench (`email_parser`, which reads a file, stays).

**Honest limits.** The screen is the pattern-based detector described above: it flags, and a
paraphrased injection passes it. The boundary is the account's rights, the fail-closed
allow-list and drafts. Every account is visible to every crew and every `.ork.ts` script that
resolves the same settings file — scripts call `tools.email*` directly, and the crews hosted
by `orkeon-host` share the host's settings — so declare accounts in the crew's own
`appsettings.json` (a run resolves one settings file; keep accounts out of an
`appsettings.json` in the directory runs start from and out of `Orkeon__Tools__Email__…`
environment variables, which the .NET host reads for every run), grant the fewest rights, prefer
`email_draft`, and do not give an untrusted crew both e-mail `Read` and an outbound channel
(`http_api`, the web tools): a message could ask the agent to carry the mailbox out. The token
files are plain JSON: shielded from the VFS tools, **not** from a shell or code tool running
as the same operating-system user.

## Resilience

`ResiliencePolicies` (`Orkeon.Infrastructure.Resilience`) exposes the Polly-based policies. Two are on the execution path:

- `GetLlmApiPolicy` — built by `HttpLlmProviderBase` for every LLM provider: retries transient HTTP failures (5xx, 408, network errors) and 429, honouring a `Retry-After` header capped at 30 s; the retry budget is `Llm:MaxRetries` (default 10) on a ladder that is linear for the first two retries then ×3, every wait capped at 30 s. A call that hits the HTTP timeout is retried **once**, then fails naming the setting (LLM-11).
- `GetRedisRetryPolicy` — exponential backoff (2^n s, 3 attempts) on Redis errors (connection and timeout included), used by `RedisMemoryProvider`.

`GetRetryPolicy`, `GetCircuitBreakerPolicy`, `GetTimeoutPolicy`, `GetCombinedPolicy` and `GetDatabaseRetryPolicy` are helpers for host code; no framework component uses them. The `Resilience` configuration section (`ResilienceOptions`: `LlmMaxRetries`, `LlmTimeoutSeconds`, `CircuitBreakerThreshold`, `CircuitBreakerDurationSeconds`, `DatabaseMaxRetries`, `RedisMaxRetries`) is bound by `AddOrkeonInfrastructure()` but **read by nothing** today — set `Llm:MaxRetries` and `Llm:TimeoutSeconds` instead.

## Checkpointing

`CheckpointManager` and `ResumeEngine` (`Orkeon.Application.Services.Checkpointing`) enable saving and resuming the state of crew executions. Useful for long workflows that must survive restarts.

Since R3.8, the **execution states** themselves (`ICrewExecutionStateManager`: status, progress, output) can also be persisted in the same state stores (`IStateStore` — in-memory, SQLite via `AddOrkeonSqliteCheckpointing`, PostgreSQL) for recovery after a crash. Opt-in via `AddCrewExecutionStatePersistence(...)` — or the `Orkeon:ExecutionState:Persistence` section, which only takes effect through the `AddOrkeonInfrastructure(IConfiguration)` overload (the shipped runners use the parameterless one, so for them the explicit call is the route); default: in-memory only. See [Opt-in subsystems](../reference/opt-in-subsystems.md).

## Plugin system

`IOrkeonPlugin`, `PluginAssemblyDiscovery`, `PluginLoader`/`PluginLoadContext` and `IPluginRegistry` (`Orkeon.Plugins`) provide a plugin system for extending the framework with custom tools and providers: directory-based discovery through the VFS, isolated loading via a collectible `AssemblyLoadContext`, opt-in DI activation `AddOrkeonPlugins(...)`.

> ⚠️ **Trust boundary**: loading a plugin executes arbitrary code with the host process privileges — no sandbox in v1, and the `AssemblyLoadContext` is not a security boundary. Only load trusted plugins. Details and operating rules: [Plugin system](./plugins.md).

---

> **See also**: [LLM providers](./llm-providers.md) · [Events and CQRS](./domain-events.md) · [Back to index](../INDEX.md)
