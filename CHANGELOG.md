# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed — **BREAKING: RAG subsystem extraction (RAG-02, no shims)**

The RAG feature set is promoted to a first-rank subsystem (`src/rag/` — `Orkeon.Rag.Abstractions` contracts + `Orkeon.Rag` implementations, agent tools in `src/tools/Orkeon.Tools.Rag`; see [ADR-006](docs/adr/ADR-006-rag-subsystem.md)). The legacy namespaces `Orkeon.Application.Interfaces.Rag.*`, `Orkeon.Application.Rag.*`, `Orkeon.Application.Interfaces.Knowledge.*` and `Orkeon.Infrastructure.Knowledge.*` are **removed without `[Obsolete]` shims** (assumed break, decision 2026-07-25, `0.9.x-beta` window).

Opt-in wiring: `services.AddOrkeonRag(configuration)` (namespace `Orkeon.Rag.DependencyInjection`, self-sufficient `TryAdd*`, default `IDocumentStore` = `MemoryProviderDocumentStore` over the ambient `IMemoryProvider`) + `services.AddOrkeonRagTools()` (`Orkeon.Tools.Rag.DependencyInjection`, registers `rag_search`). Neither is called by `AddOrkeonInfrastructure()`.

Migration table (old type → new type):

| Old (removed) | New |
|---|---|
| `Orkeon.Infrastructure.Knowledge.RagTool` (`rag_search`) | `Orkeon.Tools.Rag.RagSearchTool` (`rag_search` — same name, schema `question`/`top_k`/`collection`, and output format `answer` + `Sources:` block; `collection = "raggable-tree"` still routes to `IRaggableStore`) |
| `AddOrkeonRag` (Infrastructure `RagServiceExtensions`) + `AddOrkeonKnowledge` + `AddOrkeonRagValidation` | `AddOrkeonRag(configuration)` (`Orkeon.Rag.DependencyInjection.RagServiceCollectionExtensions` — loaders + ingestion validation + pipelines + factories) + `AddOrkeonRagTools()` |
| `Orkeon.Application.Interfaces.Rag.IRagPipeline` (`ExecuteAsync(question, RagOptions)` → `RagResult`) | `Orkeon.Rag.Abstractions.Interfaces.IRagPipeline` (`QueryAsync(RagQuery)` → `RagAnswer` with citations + trace) |
| `Orkeon.Application.Rag.RagPipeline` + `ChatClientResponseGenerator` | `Orkeon.Rag.Pipeline.LinearRagPipeline` |
| `KnowledgeService` (ingestion side) | `Orkeon.Rag.Pipeline.DefaultIngestionPipeline` (`IIngestionPipeline`) |
| `IKnowledgeService` (Application port) | `Orkeon.Rag.Abstractions.Interfaces.IDocumentStore` (storage/search) + `IIngestionPipeline` (ingestion) + `IRagPipeline` (query) |
| `TextFileLoader` / `CsvDocumentLoader` / `HtmlDocumentLoader` / `PdfDocumentLoader` / `DocumentLoaderFactory` (`Infrastructure.Knowledge.Loaders`) | `Orkeon.Rag.Loaders.*` (same names, `IDocumentLoader` over `SourceDescriptor` → `RagDocument`) |
| `WebPageLoader` (`Infrastructure.Knowledge.Loaders`) | `Orkeon.Rag.Loaders.WebPageLoader` (typed `HttpClient`, kinds `url`/`web`) |
| `RecursiveTextChunker` / `SentenceChunker` (+ tool-local chunker copies) | `Orkeon.Rag.Chunking.*` — `IChunkingStrategy` implementations `recursive`, `sentence`, `structural`, `semantic` |
| `ContentIntegrityValidator` / `PromptInjectionDocumentValidator` / `DataValidationPipeline` / `ProvenanceTracker` / `IQuarantineStore` + `InMemoryQuarantineStore` (`Infrastructure.Knowledge.Validation`) | `Orkeon.Rag.Validation.*` (same names — validation stays on the ingestion path) |
| `AnalysisEmbeddingProviderAdapter` (`Infrastructure.LLMs.Embeddings`) | `Orkeon.Rag.Embeddings.AnalysisEmbeddingProviderAdapter` |
| `SimpleEmbeddingService` (hash-based, `[Obsolete]`) | **Removed without replacement.** The default `IEmbeddingService` now adapts the `IEmbeddingProvider` port (`EmbeddingProviderServiceAdapter`): local BGE → remote `Orkeon:Embeddings` → fail-fast at first use. Semantic agent selection inherits the real chain. |
| `HybridScorer` (`Infrastructure.Knowledge.Retrieval`, dead code) | **Removed without replacement** (hybrid search capability lives in `Orkeon.Domain.Memory.IHybridSearchCapable`) |
| `FileKnowledgeSource` / `DirectoryKnowledgeSource` / `WebKnowledgeSource` / `DatabaseKnowledgeSource` (`Infrastructure.Knowledge.Sources`) | **Removed without replacement** — describe sources with `SourceDescriptor` and run them through `IIngestionPipeline` (`IngestionRequest`). The Domain contract `Orkeon.Domain.Knowledge.IKnowledgeSource` remains (no framework implementations). |
| `RagOptions` / `RagPipelineOptions` / `RagDefaults` / `KnowledgeContext` / `KnowledgeItem` / `RagTypes` (`RagResult`, `RetrievalOptions`…) | `Orkeon.Rag.Abstractions.Models.*` (`RagQuery`, `RagAnswer`, `Citation`, `ScoredChunk`, `RetrievalQuery`…) + `Orkeon.Rag.Pipeline.LinearRagPipelineOptions` / `RagIngestionOptions` (sections `Orkeon:Rag:Pipeline` / `Orkeon:Rag:Ingestion`) |
| `ResearchFindings` (was in `Orkeon.Application.Rag`) | **Kept** (not RAG) — moved to `Orkeon.Application.Services.Generic` |

## [0.9.2-beta] - 2026-07-24

First version actually published to GitHub Packages since `0.9.1-beta` (2026-07-04): the intermediate `v0.9.1-beta.rc*` tags re-packed the unchanged `0.9.1-beta` version from `Directory.Build.props`, so `--skip-duplicate` silently skipped every push. This release bumps the props version so the feed picks up everything below.

### Added

- **`IFileSystemScope`** (`Orkeon.Domain.FileSystem`) — ambient per-scope mount override for the VFS.
- **`ILlmDeltaSink`** (`Orkeon.Application.Interfaces.Ports`) — streaming delta sink port for LLM output.

### Security

- **A2A mTLS server now authenticates the client certificate instead of merely checking its dates** (SEC-012, R9.1). With `RequireMutualTls = true`, an incoming certificate must chain to one of `A2ASecurityOptions.TrustedCertificateAuthorities` (X509 `CustomRootTrust` chain — also covers validity dates, removing the last `DateTime.Now` in `src/`) or match the new `TrustedClientCertificateThumbprints` pin list; unpinned self-signed certificates are rejected (403). Starting the server with `RequireMutualTls` and no trust anchor now **throws** (fail-closed) — previously any date-valid certificate passed the guard. Revocation is not checked (private CAs without CRL/OCSP assumed).
- **A2A client-side CA pinning no longer bypasses host-name validation** (SEC-011, R9.1). `A2ASecurityHandlerFactory` only vouches for `RemoteCertificateChainErrors` (private CA unknown to the OS store); `RemoteCertificateNameMismatch`/`RemoteCertificateNotAvailable` are never accepted. The explicit `ValidateServerCertificate = false` opt-out now logs a security warning (local development only).
- **A2A mTLS handler is built once and cached instead of per call** (ANT-018, R9.2). On every A2A call (`send`, `sendSubscribe`, `cancel`, `status`) the client used to re-read the PFX through the VFS, re-import the `X509Certificate2` (never disposed) and create a fresh handler+client — a full mTLS handshake per call. `A2ASecurityHandlerFactory` now returns a pooled `SocketsHttpHandler` (`SslOptions.ClientCertificates`, `PooledConnectionLifetime` 2 min) cached lazily by `A2AClient`; per-call clients wrap it with `disposeHandler: false`. `A2AClient` is now `IDisposable` and disposes the handler and the imported certificate exactly once. Certificate rotation requires a new client instance (options snapshot at construction).

### Changed

- **Public API reshaped to .NET design-guideline conformance; 8 API-shape rules frozen as build errors** (R11.7 / maintainer decision D1 = "fix everything", **breaking, 0.9.0-beta**). The API-shape analyzer family was driven to zero across all `src/` with no `severity = none` carve-out:
  - **Exposed collections are read-only** (CA1002/CA2227/CA1819): `List<T>`/`T[]` properties and returns become `IReadOnlyList<T>`; settable collection properties become `init`/get-only. Deserialization-safe by construction — `IConfiguration`-bound options use `Collection<T>` get-only, YamlDotNet DTOs keep a settable `Collection<T>?`, System.Text.Json DTOs use `IReadOnlyList<T>` init; graph/builder state keeps a private mutable backing field exposed read-only.
  - **URLs are `System.Uri`** (CA1054/CA1056): string URL parameters and properties across `IHttpClient`, `IA2AClient`, `LlmConfig.BaseUrl`, options and tool DTOs now use `System.Uri`.
  - **Cross-language-safe naming** (CA1716): interface/virtual parameters and members renamed off reserved keywords (`ISpecification<T>.And/Or/Not` → `AndWith/OrWith/Negate`, `IAgentRegistrationStore.Get` → `GetById`), and the **`Orkeon.Domain.Shared` namespace renamed to `Orkeon.Domain.SharedKernel`** solution-wide (it collided with the VB `Shared` keyword).
  - **Getters and nesting** (CA1024/CA1034): getter methods (`GetX()`) become properties; public nested types are un-nested (with a qualifying rename where the bare name was generic, e.g. `OrkeonDiagnostics.Tags` → `OrkeonDiagnosticTags`) or made `internal` when they are implementation details.

  All changes are behaviour-preserving (order, JSON wire format, and config/YAML binding verified empirically per layer). `dotnet build Orkeon.sln` stays at 0 warning / 0 error. Note: this is a deliberate breaking change to the public surface, taken inside the 0.9.0-beta window before the first NuGet tag; some test assertions (URL equality, null-argument exception types, read-only collections) are updated accordingly and validated on the Windows test run.
- **Redundant default-value initializers removed and `System.Random` audited across `src/`; CA1805 + CA5394 frozen as build errors** (R11.5, zero-warning campaign hygiene wave). The 89 src fields/auto-properties explicitly initialized to their default value (`= 0/false/null/default/new()`) had the redundant initializer removed (CA1805, mechanical, no behaviour change). The 3 src `System.Random` uses flagged by CA5394 are all provably non-security (a SHA256-seeded deterministic pseudo-embedding fallback, retry-backoff jitter, and a simulated research-confidence score) and now carry a tight justified `#pragma warning disable CA5394`; the rule is frozen as `error` so any new `Random` use trips the build and gets a security review. CA1822 (mark members static) was intentionally deferred — several flagged members are exposed to JS scripts via Jint instance reflection and making them static would break the scripting API. `dotnet build Orkeon.sln` stays at 0 warning / 0 error.
- **Logging converted to the `LoggerMessage` source generator across all `src/`; CA1848 + CA1873 frozen as build errors** (R11.3, zero-warning campaign wave B3). The 113 src `ILogger.Log*` call sites flagged by CA1848 (use the LoggerMessage delegates) are now `[LoggerMessage]` source-generated partial methods (per-class EventIds, message templates and structured placeholder names preserved verbatim, exceptions passed as method arguments), and the 34 CA1873 sites (arguments evaluated even when the level is disabled) are fixed by that conversion or by hoisting the expensive expression into a local inside an `IsEnabled` guard. The single dynamic-level audit sink uses cached `LoggerMessage.Define` delegates. Both rules are locked as `error` for `src/**`. Behaviour is unchanged (same levels, templates, args, exceptions); tests and `examples/` keep their occurrences and are not frozen. `dotnet build Orkeon.sln` stays at 0 warning / 0 error.
- **Externally-visible method parameters are null-guarded across all `src/` and CA1062 frozen as a build error** (R11.2, zero-warning campaign wave B2). Every externally-visible method that dereferenced a reference parameter without checking it for null now guards it — 605 unique src sites get `ArgumentNullException.ThrowIfNull(param)` as their first statement (the netstandard2.0 analyzer project uses the classic `if (x is null) throw` form), and CA1062 is locked as `error` for `src/**` so no public entry point can ship unguarded. Body-only edits, no signature/behaviour change: expression-bodied methods became block bodies, constructor-initializer dereferences use `(param ?? throw …)`, and the contractually-nullable null-tolerant JS-facing logging shims coalesce (`?? JsValue.Undefined`) instead of throwing (a throw there would have regressed their documented null-tolerance). Tests and `examples/` (separate solution / test doubles) keep their occurrences and are not frozen. `dotnet build Orkeon.sln` stays at 0 warning / 0 error.
- **Globalisation analyzer family resorbed to zero across all `src/` and frozen as build errors** (R11.1, zero-warning campaign wave B1). The culture-sensitive string-operation rules CA1307/CA1310 (`StringComparison`), CA1305/CA1304 (`IFormatProvider`/`CultureInfo`), CA1311 (culture-aware case) and CA1308 (`ToLower`) are fixed at 363 unique sites and locked as `error` for `src/**` in `.editorconfig`, so framework code can never silently reintroduce one. Arbitrage "Ordinal default + protect ToLower": comparisons → `StringComparison.Ordinal` (`OrdinalIgnoreCase` for identifier/key matching), formatting → `CultureInfo.InvariantCulture`, and the 91 `ToLowerInvariant()` sites that *produce* a wire/storage/switch value keep their lowercase form under a tight justified `#pragma warning disable CA1308` (6 comparison-only sites rewritten to ordinal-ignore-case). The same six rules are neutralised (`severity = none`) in `tests/.editorconfig` — test assertions compare literal values where ordinal is already the default — a documented arbitrage, not a suppression. These rules are outside the default analysis set, so enabling them as errors enforces them in the normal build without `AnalysisMode=All`; `dotnet build Orkeon.sln` stays at 0 warning / 0 error.
- **Pinned Terminal.Gui to 2.0.1** (was 2.1.0). v2.1.0 shipped 2026-05-08 with a major API redesign + rendering bugs (gray-on-gray default scheme, focus on read-only widgets, missing `FakeDriver` for tests). 2.0.1 is the last stable release before that redesign. Same code compiles unchanged (API surface compatible). The xUnit `ModuleInitializer` crash (TUI-12) exists in both versions, so view-touching tests stay skipped.

### Fixed

- **Null-argument contracts reconciled with the R11.2 guards across Domain/Application/Scripting tests** (R11.2 follow-up, surfaced by the full Windows test run). Six tests and one value object that still encoded pre-guard behaviour are aligned with the `ArgumentNullException.ThrowIfNull` guards added in R11.2: `ValidationResult.Combine`, `TypedTaskContext.Transform`, `AgentMapper.ToDto`/`CreateFromRequest` and `SequentialCrewOrchestrator.KickoffAsync` now correctly reject null (the orchestrator's `CrewInput` has no empty form, so a null input is genuinely invalid — the test that expected "graceful" handling now expects the throw). `TaskDescription.From` is the one behavioural fix: the R11.2 `ThrowIfNull` had fragmented its validation so a null value surfaced `ArgumentNullException` ("Value cannot be null") instead of the value object's unified `ArgumentException` ("… cannot be null or whitespace") used for empty/whitespace — it now validates null/empty/whitespace uniformly in one guard (still CA1062-clean). A telemetry test (`OtelTests.tool_call_emits_a_span_with_tool_name_tag`) was also made robust against the process-global `ActivityListener` picking up a concurrent test's tool-call span, by filtering on the unique `tool.name` tag like the sibling crew/agent span tests already do. `dotnet build Orkeon.sln` stays at 0 warning / 0 error.
- **Tool URL parameters typed as `System.Uri` no longer break the agent tool contract** (R11.7-D1 follow-up). The D1 API-shape wave (CA1054/CA1056) converted tool request/response URL properties to `System.Uri`, but the typed-tool schema generator mapped `Uri` to JSON type `"object"` — so every tool call carrying a string URL was rejected before execution with `Parameter 'url' has invalid type. Expected: object` (≈30 direct failures cascading across `web_scrape`, `scrape_element`, `http_api`, `cache_search` and `arcadedb_query`). Fixed at a single point rather than reverting the DTOs: `ToolSchemaGenerator` now maps `Uri` to `"string"` (format `uri`) like `Guid`/`DateTime`, and a new tolerant `UriTolerantConverter` (registered only in the component pipeline's options) round-trips `Uri`↔string — empty/whitespace maps to `null` (so a tool's own "URL cannot be empty" validation reports a friendly error instead of an opaque JSON failure), relative values are accepted (`UriKind.RelativeOrAbsolute`, e.g. a host-substring filter), and writes use `OriginalString` to preserve the exact URL with no trailing-slash canonicalisation. The `Uri` typing (and the frozen CA1054/CA1056 errors) are kept. The same `string`→`Uri?` conversion had also silently flipped the required `url` parameter of `web_scrape`/`scrape_element`/`http_api` to optional in the generated schema (a non-nullable `string` infers required; a nullable `Uri?` infers optional); those three inputs are marked `[FieldSchema(IsRequired = true)]` to restore the pre-D1 schema while keeping the property nullable for the tool's own emptiness check (`cache_search`'s URL substring filter stays optional; `arcadedb_query`'s `bolt_uri` was already required). Also corrected three test concerns surfaced by the same Windows run: the `MockHttpMessageHandler`/`TestHttpMessageHandler` doubles now capture a **buffered clone** of each request so post-send body assertions survive the provider's R10.2 content disposal (was `ObjectDisposedException` ×27), the `IUrlValidator` SSRF stub records `OriginalString` instead of the canonicalised form, and three `LlmBasedManager` null-argument tests now expect the `ArgumentNullException` the R11.2 guards correctly throw (was `NullReferenceException`). `dotnet build Orkeon.sln` stays at 0 warning / 0 error; the test suite is re-run on Windows.
- **Conversation roles are matched case-insensitively everywhere** (SML-009, R12.5). The HTTP providers compared `msg.Role == "assistant"` (case-sensitive) while `ConversationPolicy` used `OrdinalIgnoreCase`, so a mixed-case role like `"Assistant"` was serialized one way and policy-matched another. A new `LlmRoles` (canonical lowercase wire values + a single `Is`/`IsX` helper) now routes every comparison and message construction in the Anthropic/OpenAI-compatible providers, `ConversationPolicy`, and the `LlmMessage` factories. This fixes a real bug where a mixed-case `"Tool"` orphan survived history trimming and produced the orphan `tool_result` the trim exists to prevent. The scripting default models (`LlmNamespaceBinding`) were de-duplicated (they were defined twice in the same file); reconciling them with `ProviderDefaults` is a maintainer product decision (the values differ).
- **`release.yml` and `ci.yml` no longer publish divergent NuGet perimeters** (OSS-011, R8.3). `release.yml` was missing `Orkeon.Infrastructure` even though the README documents installing it; both workflows now pack the same three core libraries. The published set is documented in `docs/reference/publication-matrix.md` (promotion of the tools family / `orkeon` tool is held until decision D3 so the scripting twins' names are not locked into NuGet before a possible rename). Stale metadata fixed: the `CONTRIBUTING` clone/upstream URLs (`Orkeon/orkeon`), the `Orkeon.ConsoleApp` local `1.0.0` version override (now inherits `0.9.0-beta`), and the obsolete `+orkeon` coverage-filter comment.
- **`HttpRequestMessage`/`HttpResponseMessage` are disposed on the LLM request path** (ANT-006, R10.2). The 20 per-call CA2000 leaks in the Anthropic/OpenAI-compatible/Ollama providers and `HttpClientAdapter` are fixed by real lifetime: `using var` for non-streaming request/response, dispose-after-send for the streaming request (the response escapes but the request body is already transmitted), and the previously-leaked cloned retry request in `ExecuteHttpRequestAsync`. The 11 `LlmProviderFactory` sites are ownership transfers (the provider is wrapped in the returned adapter; its `Dispose` is a no-op) — collapsed into one generic `Adapt<T>` helper carrying a single justified suppression.
- **Semantic memory recall no longer silently returns empty on the default configuration** (MAT-017, R10.1). `IMemoryProvider.SearchSimilarAsync` was a default interface method returning empty, and `InMemoryProvider`'s real cosine implementation was unreachable through the interface (C# does not re-map derived members onto a base-implemented interface). `SearchSimilarAsync` is now an **abstract member of `MemoryProviderBase`** (compiler-enforced for every provider), the four affected providers re-list the interface, and Redis/ChromaDB/Pinecone — which had **no** implementation at all — gained real vector searches (server-side query for Chroma/Pinecone, client-side cosine for Redis). A reflection test locks the interface map for future providers.
- **`ICodeSandbox` resolution no longer launches a `docker version` process under the DI singleton lock** (ORG-012, R10.3). The availability probe lives in a memoized lazy decorator (`LazyProbingCodeSandbox`) and runs at the first `ExecuteAsync` (async, cancellable); the probe process is now killed on timeout/cancellation. Fail-closed semantics of the sandbox gate are preserved byte-for-byte. An architecture test bans `GetAwaiter().GetResult()`/`.Result` in `src/` DI factories (single documented exemption: plugins).
- **Script command loading no longer blocks the DI thread** (ANT-002, R10.3). `ScriptCommandRegistry` resolution is pure wiring; discovery + esbuild transpilation + Jint evaluation are deferred to a memoized task awaited at runner startup (failures are not memoized — next call retries).
- **Script `ctx.services.get("tools")` no longer materializes a new set of transient disposable tools per call** (ANT-005, R10.4). The whitelist resolves the tool set once (thread-safe lazy) and serves a fresh shallow copy of the same instances — unbounded memory growth in long REPL sessions is gone.
- **`MemoryProviderFactory` no longer creates bare `HttpClient`s** (ANT-013, R10.5). Chroma/Pinecone/LanceDB clients use `SocketsHttpHandler` with `PooledConnectionLifetime` (2 min) so rotating cloud endpoints are re-resolved; ChromaDB and Pinecone providers gained the missing `Dispose` (the client was never released) and all three providers are now `IDisposable`.
- **`CrewOutput.TokensUsed` is real telemetry for all 6 process types** (MAT-004, R10.8). Parallel/Hierarchical/Consensual/Autonomous now record token usage (new thread-safe `TokenUsageTally`), Graph propagates its existing internal count (success and circuit-breaker paths), and the prompt/completion split is extracted from `UsageDetails` instead of being discarded. **Breaking (0.9.0-beta)**: `TokensUsed` is now nullable — `null` means "not measured", never a fabricated `TokenUsage(0,0,0)`; the persisted checkpoint projection records `TokensMeasured` so the distinction survives round-trips.
- **Application placeholders implemented or removed** (MAT-018, R10.9). `CrewConfigurationMapper.ToConfiguration` exports agents and tasks for real (round-trip tested; **breaking**: the caller now provides the materialized entities); `CrewValidator` validates LLM configs (model + canonical numeric bounds); `CrewPlanner` consumes its previously-ignored `strategy` parameter; the stub `AgentPlannerService` is explicitly documented and logs at use. **Removed**: `IMemoryCoordinator.ClearTemporaryMemoriesAsync` (no production caller, no "temporary" marker in the model — the no-op could not be made honest).
- **`AddOrkeonA2A()`/`AddOrkeonInfrastructure()` call order no longer matters for the A2A agent directory** (ANT-019, R9.3). `IAgentRepository` is now registered with `TryAddScoped` by the infrastructure: when A2A ran first, its `SharedStoreAgentRepository` upgrade used to be silently won back by the later `AddScoped` (per-scope empty directory — ANT-001's failure mode, no crash). Side effect: a host repository registered **before** the Orkeon extensions is no longer shadowed by the in-memory default; hosts that override `IAgentRepository` **after** `AddOrkeonInfrastructure()` without `Replace` keep the last-wins behaviour as before.
- **Ctrl+C in TUI cancels the current command instead of being intercepted as SIGINT** (TUI-20) — `Console.TreatControlCAsInput = true` is set after `Application.Init` so Ctrl+C reaches Terminal.Gui's input loop. The .NET runtime's `Console.CancelKeyPress` hook is skipped in TUI mode (would race with the keystroke path). A global `Application.KeyDown` handler in `TerminalGuiHost` is the source of truth: 1×Ctrl+C requests `IInteractiveRunner.RequestCommandCancellation` (with REPL-pane feedback `⏹  Cancellation requested...`); 2×Ctrl+C within 2s force-quits the TUI as an escape hatch. `RunOneShotAsync` now accepts an `externalCt` parameter so `VerifyCommand` propagates the per-command CT into the inner crew kickoff.
- **Ctrl+Q during a running command shows a confirmation dialog** — uses `MessageBox.Query` (Terminal.Gui's idiomatic modal). The previous custom `QuitConfirmDialog` exposed a v2 runnable-stack race that swallowed the post-dialog `Application.RequestStop`.
- **Inner-host logs (RunOneShotAsync) leaked to stdout in TUI mode** (TUI-19) — when `verify` spawned a child host with `AddSimpleConsole`, those writes hit `System.Console.Out` (commandeered by Terminal.Gui's alt-screen) and dumped on shutdown. New `Orkeon.Cli.Abstractions.Logging.AmbientLoggerProvider` is published by the TUI host on Initialize; `RunnerExecution.ConfigureVerboseLogging` resolves it via reflection (no project coupling) and substitutes it for `AddSimpleConsole` when present. Inner host logs now flow into the logs pane.
- **Default `LoggerFactory` minimum level too restrictive in TUI** — runner now sets `LogLevel.Trace` upstream so all entries reach `TerminalGuiLoggerProvider`; visible filtering is owned by the provider (toggled at runtime via F2 / Shift+F2 in the status bar).
- **Gray-on-gray invisible text + missing show/hide logs** — Terminal.Gui default scheme paints fg and bg in the same gray, so panes appear empty until you select with the mouse. New `Orkeon.Cli.TerminalGui.Layout.SchemeFactory` builds explicit white-on-black schemes wired into LogsPaneView/ReplPaneView/SplitPaneToplevel. `Ctrl+G` now toggles the logs pane visibility (REPL fills the screen when hidden).
- **Initial focus landed on the read-only logs pane instead of the prompt input** (TUI-16) — `_history.CanFocus = false` removes it from the focus cycle, plus an `Initialized` hook on `ReplPaneView` calls `_input.SetFocus()` once the view is laid out so typing works immediately without clicking.
- **Status bar shortcuts swallowed by focused TextField** (TUI-17) — every Shortcut now sets `BindKeyToApplication = true` so keystrokes route at app level regardless of focus.
- **Ctrl+Q hung the host when the runner was blocked in `Console.ReadLine`** (TUI-17) — sync-over-async wrapper passed `CancellationToken.None`. `ReplPaneView.CancelPendingRead()` is invoked from the host's finally block to forcibly complete the pending TCS with `null` so the runner's loop sees `ReadLine() == null` and exits.
- **Hosts hung when the runner ignored cancellation during a long command** — added a 2s grace period after `linkedCts.Cancel()`; if the REPL task doesn't honour cancellation within the window, it's abandoned (orphaned task finishes in background, host exits cleanly).
- **Terminal.Gui split-pane silently exited at startup** (TUI-14) — `TerminalGuiHost(options, loggerProvider)` formed a cycle with the `TerminalGuiLoggerProvider` DI factory; `.Host.Build()` exited with code 0 before any UI was rendered. `TerminalGuiHost` now takes only `TerminalGuiOptions`; the status bar is built and attached from the `TerminalGuiLoggerProvider` factory once both exist. Also: explicit `DOTNET` driver on Linux/macOS (TUI-13), since the default driver emits no output in WSL.

### Added

- **Bilingual documentation parity gate** (OSS-012, R8.4). `scripts/check-docs-parity.sh` fails CI when any `docs/**.md` lacks its `docs/fr/` mirror (or vice versa), or when a root `README`/`CONTRIBUTING`/`CODE_OF_CONDUCT`/`SECURITY` `.md` lacks its `.fr.md` pair; wired as the `docs-parity` job. The rule is documented in both `CONTRIBUTING` files.
- **Blocking npm vulnerability audit** (DEP-010, R8.6). `dependency-audit.yml` gains an `npm-vulnerability-audit` job (`npm ci --dry-run` integrity + `npm audit --audit-level=high` over the esbuild bootstrap), so a CVE on `esbuild`/`@esbuild/*` fails CI instead of being an ignorable Dependabot PR. NuGet lockfile pinning was considered and deferred (bump friction outweighs the reproducibility gain already covered by Dependabot + the blocking audit).
- **Native tool calling for Azure OpenAI** (FON-011, R10.7). `AzureOpenAILlmProvider` is rebased on `OpenAICompatibleProviderBase` (its hand-rolled pipeline only read `choices[0].message.content`) and the factory passes the OpenAI strategy: `tools`/`tool_choice` are injected and `tool_calls` parsed natively, with the text fallback kept as the safety net. Ollama stays on the text protocol — its `/api/generate` pipeline is prompt completion and the native-tools `/api/chat` response format is incompatible with the OpenAI parser; documented in `docs/reference/limitations.md`.
- **`runCrew` script calls are bounded by a configurable timeout** (ANT-007/ANT-010, R10.10). Default 10 minutes (`ScriptHostFacadeOptions.RunCrewTimeout`, ≤ 0 disables): an infinite crew no longer freezes the REPL; scripts get a clear `TimeoutException`. `IConsoleAdapter` gains `ReadLineAsync`/`ReadKeyAsync` as non-breaking default interface methods, with real TUI overrides on the existing TCS machinery. The assumed-blocking design (Jint is synchronous) is documented in the scripting guide (EN+FR).
- **Terminal.Gui split-pane console (`Orkeon.Cli.TerminalGui`)** — new `IConsoleAdapter` + `ILoggerProvider` that route REPL I/O and `ILogger` writes into separate panes (logs on top, REPL on bottom), driven by Terminal.Gui v2.0.1. All 4 interactive runners (`ClaimVerifierRunner`, `MainMenuRunner`, `QaRunner`, plus the new `--ui` flag in `Orkeon.ConsoleApp` + `examples/runners/interactive-claim-verification`) accept `--ui tui|plain|auto`, default `auto` (TUI when interactive TTY, plain in CI/pipe via `TtyDetector`). Wire-up: `services.AddOrkeonCliTerminalGui()`. Full keybindings: Ctrl+L (clear logs), Ctrl+K (clear REPL), Ctrl+F (find), Ctrl+G (toggle logs pane), Ctrl+↑/↓ (resize split), F2 (more log details) / Shift+F2 (less log details), Ctrl+C (cancel current command — 2× to force-quit), Ctrl+Q (quit, with confirmation dialog if a command is running). See `project/tasks/README-TUI-CONSOLE.md` for the full spec and TUI-18 for the 2.1.x re-evaluation follow-up.
- **`IInteractiveRunner` interface** in `Orkeon.Cli.Abstractions.Runners` exposes `IsCommandRunning` + `RequestCommandCancellation()`. `InteractiveRunnerBase` implements it; the TUI uses it to drive Ctrl+C cancellation and Ctrl+Q confirmation dialogs.
- **`AmbientLoggerProvider`** in `Orkeon.Cli.Abstractions.Logging` — process-wide ambient `ILoggerProvider` registry letting child hosts re-route their logs into a parent TUI's pane without project coupling (resolved via reflection by `RunnerExecution.ConfigureVerboseLogging`). Wrapped in a non-owning `LeasedLoggerProvider` so child host disposal doesn't kill the parent's provider.
- **TUI key event diagnostic** (`examples/runners/tui-keytest`) — small standalone runner that boots Terminal.Gui and logs every keystroke arriving at `Application.KeyDown` to `/tmp/tui-keytest.log`. Used to diagnose terminal-specific keystroke routing issues (TUI-20). Run with `dotnet run --project examples/runners/tui-keytest`.
- **Virtual FileSystem v2.2** — enumeration + streaming surface on `IFileSystemService` (`EnumerateFilesAsync`, `OpenReadStreamAsync`, `TryReadAllBytesAsync`, `TryReadAllTextAsync`, `GetEntryKindAsync`). `FileSystemDiscoverer` now goes through the VFS instead of raw `System.IO`.
- **Virtual paths across the RaggableTree pipeline** — `RaggableNode.FilePath` → `VirtualFilePath`, propagated through adapters, tools, DTOs (`SourceSlice`, `SymbolSourceResponse`), serializer (bumped to v2.0), and the store. Builder reads source via `IFileSystemService.TryReadAllTextAsync`.
- **Index introspection tools** — `index_status` and `is_path_indexed` let agents check which virtual roots are indexed and whether a given virtual path is covered (longest-match on overlapping roots). `IRaggableStore.GetIndexedRoots()` exposes the underlying list.

### Removed / Breaking

- **`ImprovedAgentExecutionService` renamed to `AgentExecutionService`** (SML-008, R12.4) — it is the only implementation of `IAgentExecutionService`, so the "Improved" qualifier was meaningless. The value object `Version` (which shadowed `System.Version`) is renamed `SemanticVersion`. Internal/pre-freeze renames in the 0.9.0-beta window. The dead `JsonToolCallParser` (`[Obsolete]`, no usage, no DI registration) and the empty `JsAgentInstance` placeholder are removed.
- **`--prebuild-index` CLI flag** removed from the standard runner. Agents now call `index_codebase(root_path="/src")` themselves (optionally gated by `is_path_indexed`). See `project/features/filesystem-sandbox/DECISIONS-2026-04-18.md` §5 for the motivation.
- **Serialization format bump** — RaggableTree on-disk cache goes from v1.0 to v2.0 (field rename `FilePath` → `VirtualFilePath`). Existing caches will fail to load and need to be rebuilt.

## [0.9.0-beta] - 2026-03-27

### Added

- **Typed pipeline architecture**: `ComponentBase<TRequest, TResponse>` as the core abstraction for all components, replacing `Dictionary<string, object>` signatures throughout the codebase
- **`ToolBase<TReq, TRes>`** generic tool base class with typed request/response, YAML defaults merging, and output filtering
- **`EvaluatorBase<TInput, TResult>`** and **`FlowStepBase<TInput, TOutput>`** typed base classes bridging interfaces with the typed pipeline
- **15+ built-in tools**: FileRead, FileWrite, WebScrape, HttpApi, JSON, CSV, PDF, XML, Database, GitHub, CodeExecution, and more in dedicated `Orkeon.Tools.*` projects
- **`SimpleCrewOrchestrator`** replacing the Akka.NET actor model with straightforward async/await orchestration
- **Semantic agent selection** using embedding-based similarity to match tasks to the most suitable agent
- **Strongly typed configurations**: `AgentConfiguration`, `TaskContext`, `LlmConfig`, and related value objects throughout Domain and Application layers
- **5 LLM providers**: OpenAI, Ollama, Anthropic, Azure OpenAI, and Groq — all HTTP-based implementations extending `HttpLlmProviderBase`
- **Memory providers**: Redis (with vector search), SQLite (long-term persistence), and InMemory (for development and testing)
- **Fluent Builder API**: `AgentBuilder`, `TaskBuilder`, `CrewBuilder`, and `FluentBuilderFactory` for ergonomic agent/crew construction
- **YAML configuration support**: full round-trip export/import for agents, tasks, crews, and tool schemas; `[FieldSchema]`, `[ComponentContract]`, and related attributes for schema generation
- **`ToolSchemaGenerator`**: auto-generates JSON/YAML schemas from typed `[FieldSchema]` attributes, with `$ref`-based nested type extraction
- **Tool validation framework**: security validation, rate limiting, and telemetry hooks on every tool execution
- **Batch tool execution** for parallel tool operations
- **Structured tool calling protocol** (JSON-based) with `ToolCallRequest<TParameters>` and `FunctionCallInfo`
- **CQRS pipeline**: commands and queries for Agent, Crew, and Task aggregates; `ValidatingCommandHandler` decorator; `UnitOfWork` integration for post-persistence domain event dispatch
- **Strongly-typed entity IDs**: 28 concrete `EntityId<T>` types (ULID-based) — `AgentId`, `TaskId`, `CrewId`, etc. — replacing primitive string identifiers
- **A2A (Agent-to-Agent) communication protocol**: `AgentCard`, discovery, `AgentCommunicationClient/Server`, `TaskRouter`, mTLS support, and DI integration (subsequently renamed to `AgentCommunication/`)
- **Session checkpointing**: `IStateStore`, `CheckpointManager`, `ResumeEngine` with three backing stores; time-travel checkpoint history with fork, replay, and diff
- **Cognitive memory system**: LLM-powered remember/recall with LanceDB embedded vector store support
- **Enterprise auth**: Azure AD and OIDC integration, claims-based authorization
- **Memory encryption at-rest**: AES-256-GCM encrypted Redis and SQLite decorators with key rotation
- **DLP (Data Loss Prevention)**: `PiiDetector` with 5-channel interceptors and per-channel policy
- **RAG data validation**: integrity checks, injection detection, provenance tracking, and quarantine
- **Agent kill switch**: `IAgentLifecycleManager` for controlled agent termination
- **`InMemoryAgentMemoryStoreRepository`** (Infrastructure) for fast in-process agent memory
- **E2E test project** (`Orkeon.Infrastructure.Tests`) with 10+ integration tests; `IConfiguration` wired into test DI container
- **XML documentation** on all public types across all projects (CS1591 enforcement enabled)
- **`ToolCallRequest<TParameters>`** generic typed tool protocol
- **Manual mock library** for LLM, Knowledge, Memory, Security, MCP, Process, and Tool interfaces — replaces Moq across the entire test suite
- **Clean Architecture + DDD audit reports** (ADRs) documenting architectural decisions and conformance

### Changed

- **Complete rename: CrewAI → Arkeon → Orkeon** across the entire codebase (solution file, namespaces, projects, docs, HTML, scripts, and examples)
- **Infrastructure layer redesigned** without Akka.NET: simple HTTP-based implementations, direct `async/await` service calls, standard dependency injection replacing the actor model
- **Domain encapsulation hardened**: private/internal constructors on all value objects and aggregate roots; `Restore()` factory methods for persistence; `IReadOnlyList<T>` replacing mutable `List<T>` on domain types
- **15+ anemic domain types converted** to immutable records (`init`-only properties)
- **Value objects refactored**: `AgentSkill`, `AgentCapability`, `AgentSelectionResult`, `CrewVariables`, `DomainValueObjects.cs` split into per-feature files; renamed duplicates (`MemoryEntity`, `TypedTaskContext`, `DelegationToolParameters`)
- **Domain reorganized feature-first**: Builders, Templates, Callbacks, DomainEvents, and ValueObjects moved to bounded-context folders; `TrainingScenario` moved to Training BC; `A2A/` renamed to `AgentCommunication/`
- **Application layer reorganized** feature-first: DTOs co-located with feature folders; dead infrastructure port interfaces removed; `KickoffAsync` extracted from `Crew` aggregate to the Application orchestrator
- **Infrastructure layer reorganized** by feature/BC: Persistence separated per-aggregate
- **`MemoryRelevanceRanker` renamed** to `MemoryRelevanceService`
- **`AgentStep` string ID replaced** with typed `AgentStepId`
- **`IRepository` simplified**: `GetAllAsync` and `IPredicateRepository` removed (AP4/R45)
- **`IAgent.Tools` typed** as `IReadOnlyList<IBaseTool>` (previously untyped)
- **CQRS handlers wired** into the ConsoleApp via the standard pipeline; DI lifetimes aligned
- **Test suite migrated** from Moq to manual mocks and from FluentAssertions to xUnit `Assert`; test methods renamed to `Should_When` convention; Handler-level directory organization
- **Trading tools migrated** to typed generic pipeline `TradingToolBase<TRequest, TResponse>`; tool definitions extracted to YAML
- **Examples updated** to use Fluent Builder API and Docker Model Runner LLM configuration
- **ComponentBase JSON serialization** extracted from Domain to Infrastructure (N1)
- **`ShouldRetain`/`ShouldPromoteToLongTerm`** made internal to enforce aggregate boundary (R35/R8)
- **`EntityMemory`/`EpisodicMemory`** made internal to the aggregate (R8/R35)
- **`MemoryItem` mutation methods** made internal (R35)
- **`AgentCapabilities`/`CrewOutput` constructors** privatized (R28)
- **`CrewInput` constructor** made internal (R10/R28)
- **`ToolResult`/`ToolUsageMetrics`** converted to init-only properties (R25)
- **Validation pipeline** wired in: `CreateTaskCommandValidator` handles `AgentId` validation; `ValidatingCommandHandler` decorates the CQRS chain
- **`UnitOfWork` try/finally guard** added to ensure domain events are dispatched after persistence even on exceptions (R21/R39)
- **`ITaskRepository`** scoped documented; `SaveChangesAsync` removed from repository, delegated to `IUnitOfWork`

### Fixed

- Infrastructure `ChatClient` adapter no longer overrides caller-supplied LLM configuration
- Empty task output in process strategies handled gracefully
- `IMemoryScope` and `IMemoryProviderFactory` registered in DI (defaults to `NullMemoryScope`)
- LLM DI registration corrected across all examples
- LLM `BaseUrl` changed from `host.docker.internal` to `localhost` in examples
- Missing `AgentBuilder`/`CrewTaskBuilder` `using` directives in email-management and research-assistant examples
- ClassicTrading example: raw strings wrapped with Value Object factories; `Agent.AgentId` renamed to `Agent.Id`; namespace corrections for `LlmConfig` and `ILlmProvider`
- Duplicate `MemoryProviderConfigDto` removed (kept in `Memory/`, removed from `Common/`)
- Duplicate `MemoryRelevanceRanker` stale reference cleaned up (R43/R50)
- Domain event dispatch order standardized; DI lifetimes aligned (N5/N6)
- `PromptShieldBuilder` tests fixed after `AgentBackstory` VO migration (null backstory handling)
- Post-refactoring compilation errors resolved across Infrastructure project
- CS4014 warning: async Timer callback wrapped with `try/catch` and discarded correctly
- Null safety and structured logging fixes (CS8604, CA1873) across Application and Infrastructure
- Code quality fixes: cognitive complexity (S3776), unused parameters (S1172), collapsed ifs (S1066), assertion improvements (xUnit2013, xUnit2032), and more

### Removed

- **Akka.NET dependency** and all actor-model code (cluster sharding, distributed data, CRDT, `CollaborationActor`)
- **All TODO comments** from the codebase
- **`sonar-project.properties`** file (caused scanner conflicts; all parameters now passed via CLI)
- **Legacy `CodeInterpreterTool`** (replaced by `SecureCodeInterpreterTool`)
- **`GetAllAsync` and `IPredicateRepository`** from `IRepository<T>` (AP4/R45)
- **5 dead infrastructure port interfaces** from Application layer (R16)
- **Moq and FluentAssertions** package references from all test projects
- **`[Obsolete]` `FunctionCall` dictionary property** replaced by `FunctionCallInfo` (T17)
- **Telemetry infrastructure** (`StartSpan` → migrated to `StartActivity`; unused telemetry packages removed)
- **Direct Domain usings** from ConsoleApp services (R51)

---

## [0.1.0-alpha] - 2025-09-21

Initial public development snapshot. Core domain model established in C# following Clean Architecture principles, as an independent reimplementation inspired by the CrewAI library.

### Added

- Initial solution structure: `Domain`, `Application`, `Infrastructure`, `ConsoleApp` projects
- Core domain entities: `Agent` (Worker, Manager, Observer, Human), `Crew`, `CrewTask`
- `IBaseTool` interface and initial tool implementations
- `ILlmProvider` with OpenAI and Ollama HTTP implementations
- Basic memory abstractions (`IMemoryProvider`)
- Initial Akka.NET actor-based agent execution (later replaced)
- Communication protocols: Direct, Broadcast, Consensus, Feedback
- Redis memory provider with vector search
- SQLite persistence for long-term memory
- ClassicTrading example (agent crew for trading workflows)
- Standalone mode (no Redis required)
- Console application entry point

[Unreleased]: https://github.com/Orkeon/orkeon/compare/v0.9.0-beta...HEAD
[0.9.0-beta]: https://github.com/Orkeon/orkeon/compare/v0.1.0-alpha...v0.9.0-beta
[0.1.0-alpha]: https://github.com/Orkeon/orkeon/releases/tag/v0.1.0-alpha
