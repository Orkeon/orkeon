> 🇫🇷 [Version française](../fr/architecture/security.md)

# Security, resilience and plugins

## Security

The framework integrates several security layers:

- **Tools**: `ToolAccessPolicy` (whitelist/blacklist/unrestricted) per agent, `IPathValidator` for path traversal protection, `IUrlValidator` for SSRF protection, `HttpHeaderSanitizer`. Tool names specified in YAML configurations are validated at creation time via `IToolRegistry` — no unregistered tool can be assigned to an agent.
- **Code**: `CodeSecurityAnalyzer` and `CodeSandbox` (`Orkeon.Infrastructure.Sandbox`) for secure C# code execution
- **Data**: `IDatabaseSecurityPolicy` for SQL/NoSQL query validation, `SensitiveDataDetector` and `DlpInterceptor` (`Orkeon.Infrastructure.Security.DLP`). The DLP subsystem is **opt-in** (`AddOrkeonDlp()`, see [Opt-in subsystems](../reference/opt-in-subsystems.md))
- **Encryption**: `IEncryptionProvider` with an AES implementation, applicable to memory providers via the Decorator pattern. Key rotation is **opt-in** (`AddOrkeonKeyRotation()`)
- **Audit**: `AuditLogger`, `ProvenanceTracker` (`Orkeon.Infrastructure.Security.Sinks`)
- **Compliance**: `ComplianceChecker` and `NistComplianceReporter` (`Orkeon.Infrastructure.Compliance`). NIST reporting is **opt-in** (`AddOrkeonNistCompliance()`)

### Tool resolution in YAML

When a Crew is created from YAML, each tool name in `agents[].tools[]` is resolved via `IToolRegistry.GetToolByNameAsync(toolName)`. If a tool does not exist:

- `CrewFactory` throws a validation exception specifying the missing tools
- Agent creation is blocked — assigning an invalid tool is impossible
- An error message lists the tools available in the registry

This guarantees that no agent can operate with non-existent capabilities.

### A2A mutual TLS

The opt-in A2A subsystem (`AddOrkeonA2A()`) enforces the security posture declared in `A2ASecurityOptions`:

- **Server** (`RequireMutualTls = true`): an incoming client certificate is **authenticated, not merely required** — it must chain to one of the configured `TrustedCertificateAuthorities` (X509 chain built with `CustomRootTrust`, which also covers the validity window) or match a pinned `TrustedClientCertificateThumbprints` entry. Self-signed certificates that are not pinned are rejected (403). Starting the server with `RequireMutualTls` and no trust anchor **throws** (fail-closed): presence and date validity alone are not authentication. Revocation is not checked — the trust anchors are assumed to be private CAs without CRL/OCSP endpoints.
- **Client**: `ValidateServerCertificate = true` (default) keeps full TLS validation. Configuring `TrustedCertificateAuthorities` pins trust to private CAs; this pinning only vouches for an **unknown chain** (`RemoteCertificateChainErrors`) — a host-name mismatch or a missing certificate is never bypassed. The explicit opt-out `ValidateServerCertificate = false` accepts any certificate, **logs a security warning**, and is strictly for local development.
- Real mutual TLS termination requires an OS-level HTTPS binding for `HttpListener` — see [Known limitations](../reference/limitations.md).

## Resilience

`RetryPolicy`, `CircuitBreaker` and `TimeoutPolicy` (`Orkeon.Infrastructure.Resilience`) provide Polly-based resilience mechanisms, used by the LLM providers and the HTTP tools.

## Checkpointing

`CheckpointManager` and `ResumeEngine` (`Orkeon.Application.Services.Checkpointing`) enable saving and resuming the state of crew executions. Useful for long workflows that must survive restarts.

Since R3.8, the **execution states** themselves (`ICrewExecutionStateManager`: status, progress, output) can also be persisted in the same state stores (`IStateStore` — in-memory, SQLite via `AddOrkeonSqliteCheckpointing`, PostgreSQL) for recovery after a crash. Opt-in via `AddCrewExecutionStatePersistence(...)` or the `Orkeon:ExecutionState:Persistence` section; default: in-memory only. See [Opt-in subsystems](../reference/opt-in-subsystems.md).

## Plugin system

`IOrkeonPlugin`, `PluginAssemblyDiscovery`, `PluginLoader`/`PluginLoadContext` and `IPluginRegistry` (`Orkeon.Plugins`) provide a plugin system for extending the framework with custom tools and providers: directory-based discovery through the VFS, isolated loading via a collectible `AssemblyLoadContext`, opt-in DI activation `AddOrkeonPlugins(...)`.

> ⚠️ **Trust boundary**: loading a plugin executes arbitrary code with the host process privileges — no sandbox in v1, and the `AssemblyLoadContext` is not a security boundary. Only load trusted plugins. Details and operating rules: [Plugin system](./plugins.md).

---

> **See also**: [LLM providers](./llm-providers.md) · [Events and CQRS](./domain-events.md) · [Back to index](../INDEX.md)
