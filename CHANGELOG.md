# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed — transcript errors: one actionable line, never a stringified stack

A crew failure travels as
`PromiseRejectedException(ObjectWrapper(AggregateException(HttpRequestException(SocketException))))`
and its `Message` embeds the full stringified stack — which the REPL used to dump
verbatim into the transcript (the live `/analyze` incident: ~40 lines of .NET frames
for one DNS hiccup). New `ConciseErrors` helper (`Orkeon.Cli.Scripting`) unwraps the
wrapper layers (JS rejection → carried CLR exception, `AggregateException` flatten,
`TargetInvocationException`) and keeps the first line of the root cause —
`✗ analyze failed: Resource temporarily unavailable (api.moonshot.ai:443)`. Applied at
every transcript-facing site (`ScriptHostFacade` crew failures, `ScriptCommand`
dispatch/completed rejections, `CommandDispatchService` instance failures); the full
exception still goes to the logs at each site.

### Added — LLM streaming: connect-phase retry for transient failures

The buffered HTTP path runs under the Polly `GetLlmApiPolicy`, but
`SendStreamingRequestAsync` was a single bare `SendAsync` — one transient socket
failure killed the whole turn. It now retries the CONNECT/headers phase itself
(3 attempts, 0.5 s/1 s backoff, `Retry-After` honoured capped at 30 s) on transport
errors, client-side connect timeouts, and retriable statuses (408/429/5xx). Once
headers are handed to the caller, a mid-stream failure is never retried — replaying a
partially-consumed stream is the caller's decision. Non-transient statuses (401…)
return immediately, unretried.

### Fixed — TUI: a line typed before the runner's first read was silently dropped

The split-pane input field is live from the first frame, but the scripted-commands
runner only starts reading after its startup script load (57 commands ≈ 30–60 s of
discovery → esbuild → evaluate). A line submitted in that window was echoed to the
transcript and then **discarded** — the live "que fait-on ?" incident: the free-text
request looked accepted and nothing ever happened. `ReplPaneView` now buffers
type-ahead submissions in a FIFO queue and delivers them to subsequent
`ReadLineAsync` calls — the same type-ahead semantics a plain terminal gives for
free. Pinned by three `ReplPaneViewTests` (buffered delivery, FIFO order, live read
still wins).

### Fixed — scripted commands: `async dispatch` / `async completed` now awaited deterministically

`ScriptCommand` unwrapped an async `dispatch`'s promise with the synchronous
`UnwrapIfPromise` (blocking the engine-lock thread until settlement) and the
`completed` drain did the same. Both now await `UnwrapIfPromiseAsync` — same pattern
as the sync-handler path — and surface a rejected dispatch/completed promise as the
same `Error: …` console line as a thrown one, instead of relying on Jint's blocking
unwrap semantics. Pinned by a dispatch-with-pending-promise integration test (the
`/assistant` shape since B-5: await session state, then post).

### Added — end-to-end progress channel for long CLI operations

A `ProgressBroker` singleton (`Orkeon.Cli.Scripting.Progress`, registered by
`AddScriptCommands`) now carries a live `{label, step/total | percent, message}`
snapshot from whoever is doing long work to whoever renders it. Three publishers:
`ctx.progress(...)` handles from command scripts (which also stamp the ambient
`CommandInstance.ReportProgress` — the field `ps`/`inspect` exposed since design §6 but
nothing ever wrote); a new host tool **`progress_report`** so CREW scripts — which have
no `ctx.progress` — can report through the `tools` global (`Tools.progressReport`);
and an optional `IProgress<IndexBuildProgress>` hook on `RaggableEnrichmentServices`
notified by `RaggableTreeBuilder` per phase and per parsed file (null by default —
zero cost when unwired; the ConsoleApp routes it to the broker as "Indexing codebase").
The TUI status line renders the snapshot as
`✳ Compacting conversation… ▰▰▰▱▱▱▱▱▱▱ 34% (12s)` (indeterminate operations show
elapsed + message instead of a bar), including for background crews that run detached
from the REPL's own turn; the agents pane swaps a running row's intent for its live
progress. `ProgressAmbient` (AsyncLocal) links detached `post`/`postWork` flows to
their instance; completion clears the broker slot by ticket so one instance can never
erase a newer operation's bar.

### Added — `spinnerVerbs`: configurable status-line verbs + spinner animation

The status line's verb rotation ("thinking verbs" in the tweakcc vocabulary) is now
configurable: `TerminalGuiOptions.SpinnerVerbs` seeds a boot-time list (bound from
`Orkeon:Cli:Tui:SpinnerVerbs` in the ConsoleApp), and the live
`TuiIntegration.SpinnerVerbs` delegate — wired by the ConsoleApp to exp07's `/config`
layers (`config_map` session state over `/workspace/.orkeon/config.json`) — wins over
it without a restart. The verb re-draws every fifteen seconds on long turns
(`StatusLineFormatter.VerbFor`), the leading glyph animates through spinner frames
(`✢ ✳ ✶ ✻`, ASCII `| / - \`) at four steps per second (`SpinnerFrame`), and the
status-line timer tightened from 1 s to 250 ms accordingly. Defaults unchanged: the
six Orkéon gerunds.

### Fixed — agents pane: live work only, finished agents leave immediately

`TuiFidelityWiring.BuildAgentRows` collapsed every non-running instance to `idle`, so a
finished crew was indistinguishable from a stuck one (the exact confusion of the
2026-08-06 captures). Per the user ruling that followed — `idle` means *waiting*, not
*finished* — the pane now shows LIVE work only: a finished agent's row disappears at
once (no retention window, no terminal badges; `ps`/`inspect` stay the audit trail),
and `idle` never renders at all. `● main` (filled bullet, no metrics) appears only
while at least one delegated agent runs — with nothing delegated the pane collapses,
matching the reference. Delegated rows render hollow (`○`) with the live
`elapsed · ↓ tokens` pair.

### Added — `ILlmUsageSink`: per-call LLM usage events, per-agent token attribution

New Application port `ILlmUsageSink` (mirror of `ILlmDeltaSink`, same plumbing chain
`JsEngineFactory → crewBuilder → JsCrew → JsLlmFacade`): every `ctx.llm.*` path —
`complete`, `chat`, `extract`, `decide`, `stream` (both variants), and each `act`
iteration (buffered or streamed, counted exactly once) — reports a `CostUsageEvent`
carrying crew/agent/provider/model and the token split (a total-only response lands on
`CompletionTokens` so `Prompt + Completion == TokensUsed` — the pricing registry then
prices that total at the output rate, a deliberate upper bound: conservative for
budgets, an overestimate for cost reporting on split-less providers; a response with
no usage at all reports nothing — "no usage" ≠ "zero tokens"). `extract` reports
before its JSON parse, so a prose reply that throws still counts the paid tokens. Nothing fed `ICostBudgetManager`
before this: the REPL's session token readout summed an event stream no one produced.
`AddScriptCommands` registers `InstanceAttributingUsageSink`, which forwards to the
cost manager (fixing that readout and `/cost`) AND credits the `CommandInstance`
ambient at call time (`ProgressAmbient`, the progress channel's AsyncLocal) — so the
agents pane's `↓ NN.Nk tokens` is now that agent's real usage (`—` only when truly
unattributable). `CommandInstanceView` gains `tokens` (visible to `ps`/`inspect` and
the F4 detail; typed in `orkeon-cli.d.ts`). A sink that throws degrades to unobserved
usage, never to a failed LLM call.

### Added — agents pane: keyboard + mouse selection (F4)

The pane stays non-focusable at rest (its first live launch proved a focusable
read-only pane steals the prompt focus), but **F4** now enters an explicit selection
mode: ↑/↓ move a chevron cursor, **Enter** prints the instance's detail into the
transcript (state, intent, elapsed, progress, result/error — via the new
`TuiIntegration.DescribeAgent` delegate over `dispatch.get(ticket)`), **Esc** hands
focus back to the prompt. A mouse click selects a row without stealing focus; a
double-click opens the same detail. The hint bar advertises `f4 agents` only while the
pane has rows. (F4, not Ctrl+A: the focused panes' select-all already owns Ctrl+A and
global bindings fire before view dispatch.)

### Added — `ActOptions.system`: a real system prompt for scripted `act()` agents

`ctx.llm.act(prompt, { system })` now seeds a `role:"system"` message as the first
message of the tool-calling conversation (re-sent on every loop iteration). Until now
`act()` always sent a single user message, so a scripted agent could not have a system
prompt at all — identity and tool policy travelled inside the user turn with user-level
authority (the same authority as tool results, which also come back as user turns), and
the providers' native system handling (Anthropic top-level `system`, `cache_control`;
`PrependConfiguredSystemMessage` on the OpenAI-compatible providers) never fired. The
conversation-level message wins over `LlmConfig.SystemMessage` on every provider; the
option omitted keeps the historical single-user-message shape byte for byte.
(`JsLlmFacade.ResolveSystem`, `Typings/context.d.ts`.)

### Added — hybrid code search + edit↔search freshness in the RaggableTree (RAG×Tree)

`codebase_search` (and `IRaggableStore.SemanticSearchAsync`) fuses an embedding cosine
ranking with a **code-aware BM25** (camelCase/snake_case sub-tokens + whole identifier)
via Reciprocal Rank Fusion — `SemanticQuery.Mode` (`Hybrid` default / `Vector` /
`Lexical`), `SearchHit.MatchOrigin`. Pure vector missed `getUserById` when the query
said "fetch user", and an exact identifier could rank below prose; each half now covers
the other's blind side. With no embedder wired, `Hybrid` degrades to `Lexical` instead
of the historical silent empty (explicit `Vector` keeps that contract). The BM25/RRF
implementations are Analysis-native twins of the RAG's (`Bm25CodeIndex`, `RankFusion`) —
the dependency must keep pointing Rag → Analysis, never back.

Freshness (an agent that EDITS files invalidates its own index): `FileWriteTool` gains
an optional `IIndexInvalidation` hook (dirty-marking, O(1), same precedent as the
citation validator); `IndexFreshnessService` reindexes the dirty set ∪ the git
working-tree changes (shell edits) BEFORE a read tool answers — grouped, single-flight,
2 s clean-probe debounce, failure degrades to the stale index and keeps the debt.
`codebase_search` reports `refreshed_files`; `index_status` reports `dirty_count`/
`dirty_paths`. `InMemoryRaggableStore` gains a `ReaderWriterLockSlim`: a search running
DURING an incremental reindex no longer risks `InvalidOperationException` on the mutated
dictionaries. The freshness pass is the first real producer on `IRaggableTreeEventBus`;
`IGitDiffProvider` (never registered before — `incremental_reindex`'s commit-range path
could not resolve it) and the bus are now registered by `AddRaggableTree`, and
`IGitDiffProvider` gains `GetWorkingTreeChangesAsync`.

Orkeon.ConsoleApp wires `AddOrkeonRag` + `AddOrkeonRagTools`: `rag_search`/`rag_ingest`/
`rag_eval` are available to the scripted REPL, and `rag_search`'s `raggable-tree`
collection inherits the hybrid + freshness path. Live-validated end to end (exp07 probe,
8/8): exact identifier ranks `hybrid`, write→search round-trip reports
`refreshed_files: 1`, RAG routing serves the code index.

### Changed — `ctx.llm.stream` now asks for usage and carries the reasoning channel (SCR-24)

`stream` went through `GenerateStreamingAsync`; it now goes through `ChatStreamingAsync`. Both read the same SSE stream, and the difference is what they ask for: the chat path sends `stream_options: { include_usage: true }`, without which most providers emit no usage chunk at all — so a streamed call had **no token accounting**. Measured on exp02 round-41, whose two streamed requests were `{"model":…,"stream":true}` with no `stream_options`; they carried usage only because Moonshot volunteers it, and the same round on OpenAI would have reported nothing. The calls that stream are the long, expensive ones.

The plain path also dropped `delta.reasoning_content`, so a thinking model's stream is silent for as long as it thinks — round-41's deliverable 13 spent 22 673 of its 32 627 completion tokens reasoning, most of a nine-minute call in which "no chunk yet" and "the stream died" were the same observation.

- `stream(prompt)` keeps yielding strings — no contract change. What the chunks cannot carry is exposed on the returned object: **`usage`** (`{ promptTokens, completionTokens, tokensUsed, cacheHitTokens, model }`, `null` while the stream runs and `null` for good when the provider reported nothing) and **`reasoningChunks`**. Read them after the loop.
- Deliberately NOT callbacks. A callback has to be invoked from the stream's own thread, and Jint's `Engine` is single-threaded: the first cut of this did exactly that, and exp02's round-42 — seven area writers streaming concurrently — died of a `NullReferenceException` inside `ScriptFunction.Call`, with all seven writers falling back to a placeholder and the script stopping silently after assembling its document. CLR state read through interop runs on the engine's own thread.
- Reasoning progress is logged by the facade through the host logger every 200 deltas: a script cannot log it for itself, because while the model reasons its loop body never runs.
- `usage` is populated on the non-streaming fallback too, so "this provider does not stream" and "this provider reported no usage" stay distinguishable.

### Added — `rag.retrieve` / `IRagRetrievalCapable`: retrieval without the generation nobody asked for (SCR-24)

A caller that wants the retrieved passages rather than prose was still charged for a full grounded generation, because `IRagPipeline` exposed only `QueryAsync`. Measured on exp02's gap round (2026-08-04): seven `rag.query` calls whose generated answers were discarded **by design** cost 14 748 completion tokens — 68 % of them reasoning tokens — and 394 s of wall time, on top of retrieval that had already produced every citation the caller used. The generation stage is the expensive half of a RAG call and it is optional far more often than the API shape suggested.

- **`IRagRetrievalCapable` (`Orkeon.Rag.Abstractions`)** — opt-in capability, same shape as `IHybridSearchCapable`: `RetrieveAsync` runs `transform → retrieve → fuse → rerank → assemble` and stops. Declared as a separate interface rather than added to `IRagPipeline` because not every executor can honour it — the corrective graph interleaves evaluation with generation, so "retrieval only" is not a prefix of its run — and a caller must be able to ask instead of discovering the answer through an exception.
- **`StagedRagPipeline`** implements it; `QueryAsync` and `RetrieveAsync` now share one `RetrieveCoreAsync`, so the two cannot drift. The returned `RagAnswer` keeps the same shape (empty `Text`, populated `Citations`) and the trace carries a `generate` step saying the stage was skipped on purpose — an absent step would read as a trace from an older pipeline.
- **`rag.retrieve(question, options)`** in the scripting DSL, same signature as `rag.query`. On a pipeline that is not retrieval-capable it throws rather than falling back to `QueryAsync`: a silent fallback would charge exactly what the caller asked to avoid, with no way to tell.

### Fixed — the `system` role was flattened into the user message on every OpenAI-compatible provider (SCR-24)

`HttpLlmProviderBase.ChatAsync` flattens messages into one prompt shaped `"{role}: {content}"` per line, and `OpenAICompatibleProviderBase` took the structured chat path only when tools, tool-call metadata or a vision payload were present. A plain `system` + `user` conversation — the shape of the entire RAG generation stage, and of every LLM judge, retrieval evaluator and groundedness checker in this repository, none of which declares a tool — therefore reached the provider as a single `user` message whose text began with `system: `. A multi-turn history was concatenated the same way, so an assistant turn arrived as something the user claimed the assistant had said. Evidence: exp02 round-41's exchange log, all seven RAG generations sent as `messages: [{ role: "user", content: "system: You are a retrieval-augmented assistant…" }]`.

- Every `ChatAsync` call with at least one message now takes the structured path. A lone user message was affected too (it went out as `"user: Hello."`), so no case is left on the flattening path; an empty or null array still delegates to the base, which turns it into an empty single prompt.
- `grammar` (GBNF) was emitted only by the single-prompt builder and is now written by both, so an option cannot appear or vanish with the number of messages sent.
- Around 400 mocked provider tests missed this: they assert on the response, and a mocked handler answers whatever it is sent. The new `OpenAICompatibleProviderBaseRoleFidelityTests` read the outgoing payload on the cases with no tool and no image, which was the remaining blind spot.

### Added — Declared provider capabilities, structured outputs and thinking on all 12 providers (LLM-02, LLM-03, LLM-04)

The audit's central finding was not that the wiring was missing but that its absence was **invisible**: `LlmResponseFormat` and `LlmThinkingConfig` cascaded correctly from crew → agent → task → script → call-site, yet only DeepSeek wrote `response_format` and only DeepSeek and Z.AI wrote `thinking`. On the ten other providers a YAML declaration was silently dropped. Closes G-15, G-16 and G-19.

- **`LlmProviderCapabilities` (Domain)** — each provider declares what its API really supports: `ResponseFormat` (`None` | `JsonObject` | `JsonSchema`), `Thinking` (`None` | `EffortOnly` | `Toggle` | `Budget`), `Vision`, `ExplicitPromptCaching`, `RequiresJsonKeywordInPrompt`, `ReplaysReasoningContent`. Exposed on `ILlmProvider` as a **default-implemented** member (like `BaseConfig`), so third-party providers and test doubles keep compiling, and a provider that declares nothing gets nothing written on its behalf.
- **The end of silent drops** — an option the caller declared that the provider cannot honour now produces an actionable warning naming the option, the provider and the remedy. This is the actual fix for G-15/G-16; the wiring below is the easy half.
- **The OpenAI dialect is written once.** `OpenAICompatibleProviderBase.ApplyProviderSpecificOptions` went from a no-op hook to a capability-driven implementation. DeepSeek and Z.AI held two near-identical copies of the thinking translation and two of the `reasoning_content` extraction; both were refactored onto the base and shrank to what is genuinely theirs (DeepSeek's cache counters, Z.AI's `reasoning_tokens`). Their 33 + 15 existing tests pass unmodified.
- **Structured outputs on all 12 providers (G-15)** — `LlmResponseFormat` gains an optional `Schema` (`LlmJsonSchema { Name, Schema, Strict }`) and a `JsonSchema(...)` factory; `Type` is untouched, so existing configurations behave identically. Dialects: `response_format` (OpenAI-compatible family, with `json_schema` where the vendor validates it), `output_config.format` (Anthropic), `format` (Ollama, which also accepts a full schema). A schema handed to a provider that only guarantees well-formed JSON is **downgraded to `json_object` with a warning**, never in silence.
- ⚠️ **Anthropic has no schema-less JSON mode.** `output_config.format` takes exactly `type` (always `json_schema`) and `schema` — there is no equivalent of `json_object`, and `name`/`strict` do not belong there (`strict` exists, but on individual tools). A `json_object` request on Anthropic is therefore **reported**, not sent in a shape the API would reject. Supply a schema, or state the shape in the prompt.
- ⚠️ **The YAML allow-list is lifted.** `YamlCrewMapper` accepted only `text` and `json_object` and downgraded everything else to `null` — which would have discarded `json_schema` before it reached any provider. Unknown values now travel to the provider **with a warning**: a new vendor value works without a framework release, and a typo surfaces as a provider error rather than as nothing at all. Declare a schema with a `response_schema:` block (`name`, `schema`, `strict`) next to `response_format: json_schema`; a `response_schema:` on its own implies `json_schema`. The scripting DSL gains `withResponseSchema(name, schema, strict?)` on both the agent and task builders.
- **Thinking on all 12 providers (G-16, G-19)** — four dialects: `reasoning_effort` (+ the `thinking` block where the API has an explicit toggle) on the OpenAI-compatible family, `thinking: {type: "adaptive"}` + `output_config.effort` on Anthropic, `think` (boolean or effort level) on Ollama, `enable_thinking` + `thinking_budget` on Qwen's DashScope dialect. `LlmThinkingConfig` gains `BudgetTokens`, mapped **only** to Qwen — the `budget_tokens` shape found in many older sources is rejected with an HTTP 400 by the current Claude generation, so it is reported rather than sent.
- **`reasoning_content` extraction is generic**; its **replay** stays DeepSeek-only, driven by `ReplaysReasoningContent` — it is an API constraint of theirs (HTTP 400 without it), not a property of reasoning models, and Z.AI documents the opposite.
- Ollama's `format` and `think` are wired **on the current `/api/generate` path**: contrary to what the audit reported, both are accepted there, and only native tool calling actually requires the `/api/chat` migration.

### Added — `orkeon llm probe`, the provider campaign harness (LLM-08/C1)

Roughly 400 unit tests cover the LLM providers and **every one of them speaks to a mocked HTTP handler**. A mock proves the framework sends what we believe it sends; it cannot prove the vendor accepts it. Before this, only DeepSeek had archived real-execution traces.

- `orkeon llm probe --provider <name> [--model …] [--base-url …] [--modes M1,M8] [--archive <dir>]` runs the matrix's protocol modes against a live provider and prints a report ready to paste into the matrix journal, evidence level included. Currently exercises **M1** (single prompt), **M2** (multi-turn + system), **M3** (text streaming), **M4** (chat streaming), **M7** (thinking), **M8** (response format), **M12** (typed error on an invalid model) and **M13** (cancellation). M5/M6 (tool calling), M9 (vision), M10 (cache) and M11 (long context) need per-provider assets or a deliberately expensive call and are not covered yet — the harness names what it does not run rather than implying full coverage.
- **The API key is never a command-line argument**: it is read from an environment variable (`--api-key-env`, default `ORKEON_LLM_API_KEY`) and never appears in the report or the archive.
- A mode that a provider's declared capabilities make inapplicable is reported as such, not as a failure; a mode that throws is recorded as a finding rather than aborting the campaign, since the framework's contract is a typed error response, never a throw. A campaign with any failed mode exits non-zero.
- **The campaigns themselves are not run here.** They consume real credits on real accounts, so the decision to spend belongs to whoever owns them; the remaining LLM-08 work (running the campaigns, replaying experiment 08, filling the matrix) is unblocked but outstanding.

### Added — Azure v1 GA API and native Ollama tool calling (LLM-07)

The two gaps that needed a pipeline migration rather than a payload field. Closes G-06, G-10 and G-25.

- **Azure v1 GA API (G-06, G-25)** — `BuildEndpoint` was hardcoded to the dated, deployment-based shape (`/openai/deployments/{deployment}/chat/completions?api-version=2024-02-01`). Azure has served a **v1 GA** surface since August 2025 — `{endpoint}/openai/v1/chat/completions`, no `api-version` — which is the only path to the Responses API and to the non-OpenAI models Azure resells (DeepSeek, Grok); all of it was unreachable. Select it with `api_version: v1` (typed property or custom parameter). **The dated shape stays the default on purpose**: switching it would silently change the URL of every existing deployment-based configuration.
- ⚠️ **Ollama tool calling is now native (G-10)** — the provider targeted `/api/generate`, which has no tool support, so tool calling went through the text-fallback protocol. It now reaches `/api/chat` whenever the conversation declares tools, replays tool calls, or carries an image, and `LlmProviderFactory` wires it with the native OpenAI strategy. The response is reshaped once — Ollama returns `message.tool_calls` with `arguments` as a JSON **object** and no `choices` array, which is precisely why the framework's single parser could not read it — into the OpenAI body (`arguments` as a JSON **string**, positional call ids since Ollama issues none). Everything else keeps `/api/generate`, so NDJSON streaming and GBNF `grammar` are untouched and the text fallback remains in charge for models without tool support.
- **Ollama vision** — images travel in a bare base64 `images` array rather than as OpenAI content parts. Remote image URLs cannot be forwarded (the server never fetches them), so an image referenced only by URL is skipped rather than silently dropped.
- `docs/reference/limitations.md` no longer describes a limit that has been lifted; its Ollama entry now states what actually remains (no `/api/chat` streaming, bytes-only images, text fallback for tool-less models).

### Added — Anthropic parity, vision exposure and HuggingFace routing (LLM-05, LLM-06)

- **Native SSE chat streaming on Anthropic (G-20)** — `ChatStreamingAsync` used to fall back to the base class's buffered emulation, which waits for the whole answer before emitting anything: no token ever arrived early on Claude. It now parses the Messages API event stream directly, emitting `ContentDelta`, `ReasoningDelta` (extended thinking) and a terminal `Completed` whose response is indistinguishable from the buffered one.
- **Anthropic cache metrics (G-21)** — `cache_creation_input_tokens` and `cache_read_input_tokens` feed the typed `CacheHitTokens` / `CacheMissTokens`, so `CacheHitRatio` is finally computable on Claude. The three input counters do not overlap, so `PromptTokens` is their sum; absent counters stay `null` (unmeasured, not zero).
- **Explicit prompt caching (G-17)** — new `LlmCacheConfig` value object, cascaded like `LlmThinkingConfig` (crew → agent → task → call-site, `cache:` block in YAML). Anthropic's cache is explicit: without a `cache_control` breakpoint **nothing is cached**, so a long system prompt is re-billed in full on every turn — Orkeon read the metrics but never placed a breakpoint. Off by default, since a breakpoint changes what the vendor stores and how the call is billed. Marking the system prompt promotes it to the content-block form the API requires; the tools breakpoint lands on the last tool, which covers the whole catalogue.
- **Vision exposed on the 8 remaining capable providers (G-18)** — Azure, Groq, Together, Mistral, Qwen, Kimi, Z.AI and HuggingFace declare the capability and inherit the `image_url` composition from the base; no per-provider override. Text-only calls emit exactly the payload they did before (asserted). Ollama stays out on purpose — it takes a base64 `images` array rather than OpenAI content parts, which belongs with the rest of its request-shape work. DeepSeek has no vision model and degrades an image message to its text fallback.
- **HuggingFace provider-selection suffixes (G-23)** — `:fastest` / `:cheapest` / `:preferred` and partner pinning (`:groq`) are the only cost and latency lever on Inference Providers. The suffix already travelled to the wire; what was missing was any way to know it was wrong. Added `WithRoutingPolicy` / `WithPartner` helpers and validation that **reports** an unrecognised suffix while still forwarding it — the partner list moves faster than this framework releases.
- **Guard on DeepSeek's Anthropic-dialect endpoint (G-22)** — `api.deepseek.com/anthropic` speaks the Messages API, but host inference matched `deepseek.com` and routed it to the OpenAI-compatible provider, producing malformed requests whose error surfaced far from its cause. It now fails immediately with a message naming the cause and the fix.
- **G-24 (Groq server-side tools) is ruled out with a motive, not left pending.** `browser_search` and `code_interpreter` execute on Groq's side, so they pass through neither `IBaseTool`, nor the agent loop, nor the framework's validation, rate limiting and telemetry. Exposing them is a tooling-architecture decision — how they coexist with Orkeon's tool registry and what gets traced — not a provider-support gap. The same reasoning covers the other vendors' server-side tools.

### Changed — LLM provider defaults, host routing and the Azure config guard (LLM-01)

First sheet of the LLM provider remediation plan (`backstage/tasks/LLM-00-PLAN.md`), closing gaps G-01→G-05, G-07→G-09, G-11, G-12, G-14 and defect D-01 of the 2026-07-27 audit. **Four providers out of twelve failed with their out-of-the-box configuration**; four more targeted a superseded generation.

- ⚠️ **Default models changed — this changes the behaviour of every configuration that does not specify a model.** Each identifier was confirmed on the vendor's official documentation on 2026-07-27: OpenAI `gpt-4` → **`gpt-5.6-sol`** (`gpt-4` reaches end of life 2026-10-23 and caps context at 8 192 tokens), Anthropic `claude-3-5-sonnet-20241022` → **`claude-sonnet-5`** (retired 2025-10-28), DeepSeek `deepseek-chat` → **`deepseek-v4-flash`** (retired 2026-07-24), Kimi `moonshot-v1-8k` → **`kimi-k2.6`** (the `moonshot-v1-*` series sunsets 2026-08-31), Qwen `qwen-turbo` → **`qwen3.7-plus`** (absent from the catalogue), Mistral `mistral-large-latest` → **`mistral-medium-3-5-26-04`**. `LlmDefaults.DefaultModelName` — the fallback of `LlmConfig.Model` itself — moves with the OpenAI default. Pin a model explicitly to keep the previous behaviour.
- ⚠️ **Default endpoints changed** — HuggingFace `api-inference.huggingface.co` → **`router.huggingface.co`** (the old host is gone, so the provider could not work at all), Kimi `api.moonshot.cn` → **`api.moonshot.ai`** (the mainland host was the default for every account, including international ones; set `BaseUrl` explicitly for a mainland account).
- **Every default now lives in `ProviderDefaults`** — Groq and HuggingFace held theirs inline — and a pinning test asserts the model and endpoint of all twelve providers, read from live instances rather than from constants. Drift becomes a failing test, not a silent change.
- **International hosts are routed to their dedicated provider** instead of falling back to the generic OpenAI one: `api.moonshot.ai` (G-11), `dashscope-intl.aliyuncs.com` and the per-workspace `*.maas.aliyuncs.com` hosts (G-12). `router.huggingface.co` was already matched by the existing `huggingface.co` rule — the audit's G-13 was a false positive, and is now pinned by a test.
- **Mistral routing trap fixed (G-14)** — `InferFromModel` routed *every* `mistral*` model to Ollama, so a cloud identifier such as `mistral-medium-3-5-26-04` was sent to `localhost:11434`. Only the bare `mistral` and its Ollama tags (`mistral:7b`) stay local; versioned identifiers, including `ministral-*`, now reach the Mistral cloud. Setting `BaseUrl` still wins over model-name inference.
- **Azure configuration guard aligned across all four entry points (D-01)** — `ChatStreamingAsync` was not overridden, so with a missing `BaseUrl` it reached `BuildEndpoint` and threw a `NullReferenceException` where the other paths returned a typed error. It now emits the standard `Completed` event carrying that same error. `GenerateStreamingAsync` still ends in an empty stream — `IAsyncEnumerable<string>` has no error channel — but logs it instead of failing silently.
- **Cost tracking follows** — `gpt-5.6-sol` / `-terra` / `-luna` and `claude-opus-5` / `claude-sonnet-5` registered in `ModelPricingRegistry`; `gpt-4` keeps its own entry so configurations that pin it still produce a real cost.
- **`LlmConfig.Gpt4()` → `LlmConfig.WithDefaultModel()`** (and `Gpt4WithSecret` → `WithDefaultModelSecret`). These factories have always returned the *platform default* model, not literally `gpt-4`; with the default moved, the old names became actively misleading. The former names remain as `[Obsolete]` forwarders with identical behaviour — pass `"gpt-4"` to `LlmConfig.Create` if you really want that model.

### Fixed — post-RAG coherence audit (2026-07-26)

- **`LlmProviderToChatClientAdapter` honors the standard `ChatOptions.ResponseFormat`** — the adapter only read the `LlmChatOptionsKeys.ResponseFormat` AdditionalProperties key stashed by the orchestrator, so callers built on plain Microsoft.Extensions.AI options (RAG retrieval evaluator, groundedness checker, query complexity classifier setting `ChatResponseFormat.Json`) never reached providers wiring `response_format` (e.g. DeepSeek `json_object`). The adapter now falls back to `ChatOptions.ResponseFormat` when the key is absent (the explicit key keeps priority) — the JSON constraint was already enforced by strict prompts + tolerant parsing, this makes the API-level guarantee real on the providers that support it.
- **The corrective mechanism test now locks the rank-1 claim** — `CorrectiveRagMechanismSlowTests` asserted only that `notes-power.md` was cited; it now also asserts it is the **first** citation, matching the wording in the RAG-06 task sheet and the eval README.
- Documentation drift cleanup: version references aligned on `src/Directory.Build.props` (0.9.2-beta) across `README(.fr).md`, `CLAUDE.md`, `limitations.md`, `publication-matrix.md`; `publication-matrix.md` workflow narrative matched to reality (all NuGet pack/push lives in `publish.yml` → GitHub Packages; nothing on NuGet.org); `CONTRIBUTING(.fr).md` no longer claims a CI parity gate that was never wired; ADR-006 amended with the RAG-06 decisions (CRAG on `StateGraph`, web-fallback policy/transport split, `corrective` preset, no separate `rag-adr.md`); `docs/INDEX.md` links `rag-pipeline.md`; `limitations.md` + `opt-in-subsystems.md` document the ONNX requirement (`balanced`/`quality`/`adaptive`) and the double-opt-in web fallback; French mirror `docs/fr/architecture/rag-pipeline.md` added; eval README header corrected to 9 cases / 12 documents; `Orkeon.Tools.Rag` NuGet description lists its three tools; stale "lands with RAG-0x" comments rewritten in delivered code.

### Added — Corrective RAG & vitrine (RAG-06): CRAG graph on `StateGraph`, `corrective` profile, opt-in web fallback, examples

The showcase piece of the RAG plan (guide §8): Corrective RAG built on Orkeon's own Graph orchestration mode — the corrective engine *is* a Domain `StateGraph`, RAG demonstrates the `Graph` mode and vice versa — plus the vitrine layer (docs, three runnable examples, final all-profile evaluation).

- **CRAG graph pipeline (C1)** — `CorrectiveRagPipeline` (`Orkeon.Rag.Corrective`), an `IRagPipeline` whose execution is a `StateGraph<RagGraphState>` (immutable record state) with conditional edges and controlled cycles: `retrieve` → `evaluate` (`IRetrievalEvaluator` → `RetrievalVerdict` `Correct | Incorrect | Ambiguous`) → per verdict `generate` / `refine` (decompose-then-recompose, never empties the working set) / `rewrite_query` (vocabulary-gap rewrite, loops back to `retrieve`) → `generate` (original question, same `[n]` rank-based markers, token budget and `edges` layout as the staged pipeline) → `check_groundedness` (`IGroundednessChecker`; ungrounded → re-loop). Every node run is traced as `corrective:<node>` with the iteration ordinal; verdicts, rewritten probes and loop count land in `RagTrace.Verdicts` / `QueryVariants` / `Iterations`. Evaluator and checker are LLM-backed when an `IChatClient` is registered (`LlmRetrievalEvaluator` / `LlmGroundednessChecker`, constrained via `LlmResponseFormat` — `json_object` where wired, e.g. DeepSeek — tolerant JSON parsing elsewhere), deterministic heuristics with a warning otherwise. `AddOrkeonCorrectiveRag(configuration)` (idempotent `TryAdd`, called by `AddOrkeonRag`).
- **Double loop bound (C1)** — `Orkeon:Rag:Corrective:MaxIterations` (default 3) bounds both the rewrite cycle and the groundedness re-loop, and the graph engine's own `CircuitBreakerPolicy` is explicitly derived from that budget as a second, independent layer. Exhaustion → best-effort generation with the best available chunks, traced; a tripped breaker is caught, traced (`corrective:circuit_breaker`) and degraded — the pipeline never throws for a loop condition and can never loop forever (circuit-breaker test included).
- **Opt-in web fallback + anti-injection (C1 security, 6C)** — after rewriting is exhausted the graph may fire `web_fallback`, gated by **two** separate off-by-default switches: `Orkeon:Rag:Corrective:WebFallback` (pipeline policy, Abstractions) and `Orkeon:Rag:WebFallback` (transport — `WebSearchDocumentRetriever`, SearxNG-compatible JSON search, `ApiKeyEnvVar` only, `AddOrkeonRagWebFallback`). Every downloaded page passes `PromptInjectionDocumentValidator` (deterministic heuristics — model-addressed directives EN+FR, chat-template control tokens, hidden HTML, exfiltration vectors; verdict `Clean | Suspicious | Rejected`): `Rejected` never leaves the retriever, `Suspicious` is flagged or discarded per `SuspiciousAction`, content is never rewritten. Threat model + honest limits (pattern-based, evadable) in `docs/architecture/security.md`. Web chunks carry `ScoreOrigin = "web"`.
- **`corrective` profile + Adaptive lift (C2, 6D)** — `RagProfile.Corrective` / `corrective` in `RagProfilePresets` and the resolver (same `IRagPipeline` façade; the profile selects the executor). Preset: hybrid BM25 + RRF (rewritten probes need the lexical leg), **no linear rerank stage** (the graph corrects by looping — no ONNX package needed), `Groundedness.Enabled = false` on purpose (the graph runs its native `check_groundedness` node whenever a checker is registered). The `adaptive` profile's `Iterative` route now delegates to the memoized `corrective` pipeline — the RAG-05 documented fallback to `quality` is **lifted** (`route` step traces `delegate=corrective`).
- **Vitrine (C3, 6B)** — `docs/architecture/rag-pipeline.md` completed (CRAG topology, verdicts, double bound, web fallback, ADR pointers); three new runnable offline examples `examples/rag/hybrid-retrieval` (BM25+RRF vs vector-only), `examples/rag/custom-reranker` (host `IReranker` via `IRerankerRegistrar`), `examples/rag/crew-yaml` (crew `rag:`/`knowledge:` blocks) + scripting variant `examples/scripting/08-rag.ork.ts`; all four `examples/rag/*` projects in `Orkeon.Examples.sln`; `examples/INDEX.md` regenerated.
- **Final evaluation, published honestly (C3)** — `orkeon rag eval --compare fast,balanced,quality,corrective,adaptive` (2026-07-26, offline): fast/balanced/quality/adaptive at 0.89 recall@5 / 0.89 MRR; **`corrective` at 0.78 / 0.64 — worse than `quality` offline, and documented as such**: the extractive stub degrades the graph's LLM nodes (pseudo-random verdicts from the tolerant parser, degenerate rewrite probe shared by all cases), so the offline row measures the loop's guard rails, not rewrite quality — full per-case analysis in `examples/rag/eval/README.md`. The causal end-to-end proof of the mechanism (verdict `Incorrect` → rewrite → `notes-power.md` cited on the seeded q-007 case that `quality` misses, same store and embeddings) is `CorrectiveRagMechanismSlowTests` (scripted LLM for the two roles CI cannot provide, labelled). CI gate unchanged (`balanced`, `correctif` cases excluded, gated aggregates 1.00/1.00).

### Added — RAG query translation & adaptive routing (RAG-05): transformers, MMR, classifier, Adaptive profile

Stage 1 of the pipeline (query transformation, guide §6) plus Adaptive-RAG routing (guide §8.4), measured with the RAG-04 harness.

- **Query transformers (C1)** — `IQueryTransformer { Name, Kind, TransformAsync }` with retrieval semantics per `QueryTransformKind`: `MultiQueryTransformer` (`multi-query`, **Union** — one LLM call produces N phrasings, retrieval runs per query, rankings merged by chunk-id union keeping the original store scores, max on duplicates), `RagFusionTransformer` (`rag-fusion`, **Fusion** — same variants, per-query rankings fused by Reciprocal Rank Fusion, same k as the hybrid stage), `HydeTransformer` (`hyde`, **Replacement** — a hypothetical document is embedded as the retrieval probe INSTEAD of the question; generation and citations always use the ORIGINAL user question), `IdentityQueryTransformer` (`none`). Factory pre-populated via `AddOrkeonQueryTransforms()` (chat client resolved lazily; unknown names fail loudly); options `Orkeon:Rag:QueryTransform` (`Mode` default `none`, `VariantCount` default 3). Unusable/failing LLM responses degrade to `[original]` with a warning — never an exception.
- **Pipeline integration (5D)** — the `StagedRagPipeline` transform stage resolves the configured transformer and wires retrieve/fuse per `Kind` (union / rrf / replacement probe). The transform step traces `transformer`, `kind`, `variants` (count) and the truncated `variant_n` texts; `RagTrace.QueryVariants` carries the full produced texts (original excluded); the fuse step traces its `method` (`union` / `rrf` / `dedup`).
- **MMR diversification (C2, opt-in)** — `MaximalMarginalRelevance.Select` applied after fusion/dedup and before rerank when `Orkeon:Rag:Retrieval:Mmr:Enabled` is set (`Lambda` default 0.7): re-orders the fused candidates by `λ·relevance − (1−λ)·redundancy`. Candidate embeddings are **not** recomputed — the documented lexical (Jaccard) fallback with min-max-normalised scores is the pipeline path; the embedding overload exists for callers that already hold them. Traced on the fuse step (`mmr`, `mmr_lambda`, `mmr_in`/`mmr_out`).
- **Query-complexity classifier (C3)** — `IQueryComplexityClassifier.ClassifyAsync(query) → QueryRoute { NoRetrieval, SingleShot, Iterative }`: `HeuristicQueryComplexityClassifier` (default — deterministic rules, zero LLM) and `LlmQueryComplexityClassifier` (constrained JSON, tolerant parsing, SingleShot fallback with warning). `AddOrkeonQueryRouting(configuration)`, options `Orkeon:Rag:QueryRouting:Classifier` (`heuristic` | `llm`; `llm` without a chat client falls back to heuristic with a warning).
- **`Adaptive` profile (C3/5D)** — `RagProfile.Adaptive` / `adaptive` in `RagProfilePresets` and the resolver: the `AdaptiveRagPipeline` classifies first, then `NoRetrieval` → direct LLM answer (no retrieval, empty citations), `SingleShot` → delegates to the memoized **balanced** pipeline, `Iterative` → **documented fallback to `quality`** until the corrective/iterative engine ships with RAG-06. The decision is always traced: `RagTrace.Route` + a first `route` step (`route`, `classifier`, `delegate`, fallback detail). `Orkeon:Rag:Profile=adaptive` routes the default pipeline through the resolver.
- **Measured comparison** (golden dataset, offline, heuristic judge, 2026-07-26): fast / balanced / quality / adaptive all at 0.89 recall@5 / 0.89 MRR (4 / 217 / 166 / 141 ms/case). **Adaptive equals balanced on this dataset by construction**: the heuristic classifier routes all 9 golden questions to SingleShot → balanced (each has exactly one interrogative word, a single `?`, and fewer than 25 words); the ms/case delta is a warm-ONNX-session artifact of run order, not a quality gain. Offline honesty: `--offline` swaps in a deterministic extractive chat client, so the LLM-backed transformers (multi-query / rag-fusion / hyde) and the `llm` classifier have no real LLM to call — the measured path is `Mode=none` + heuristic routing; the transformer/MMR levers are wired and unit-tested, their quality delta will be measured with a real `IChatClient` and/or the RAG-06 corpus growth.

### Added — RAG quality phase (RAG-04): evaluation harness, hybrid retrieval, reranking, profiles

Measured before proclaimed — the whole phase is driven by an offline CI-runnable evaluation harness (local BGE embeddings, embedded ONNX cross-encoder, deterministic extractive generation, labelled judge — zero network, zero API key).

- **Evaluation harness (C1, plan §9)** — versioned golden dataset `examples/rag/eval/golden.yaml` (7 cases over a 10-document corpus, incl. the seeded hard-retrieval case `q-007` tagged `correctif`), deterministic retrieval metrics (recall@k, precision@k, MRR), generation metrics via LLM-judge with deterministic heuristic fallback (the mode used is **always labelled**), `IRagEvaluator`/`IRagEvalHarness`, agent tool `rag_eval`, CLI `orkeon rag eval --dataset … [--profile|--compare] [--offline] [--min-recall --min-mrr]`, dedicated workflow `.github/workflows/rag-eval.yml`.
- **Hybrid retrieval (C2, plan §5)** — in-process `Bm25Index` + `ReciprocalRankFusion` (RRF k=60) behind the `HybridSearchDocumentStore` decorator (works over all 6 memory providers; in-process index memory documented), native provider hybrid preferred via the new `IHybridSearchCapable` Domain capability (LanceDB), native scores via `IScoredVectorSearch` (ChromaDB/Pinecone/LanceDB).
- **Reranking (C3, plan §7)** — opt-in packages `Orkeon.Rag.Onnx` (cross-encoder runtime, ms-marco-MiniLM-L-6-v2, Apache-2.0) + `Orkeon.Rag.Onnx.Model` (int8 weights **embedded** — guaranteed offline, no download ever) registered with `AddOrkeonOnnxReranker()`; `LlmListwiseReranker` (universal fallback over the 12 providers) and `NoopReranker`; default cascade CandidateK 50 → TopN 5.
- **Staged pipeline + profiles (C4, plan §5.1–5.2)** — `StagedRagPipeline` replaces `LinearRagPipeline` (breaking rename, beta window): transform (hook `none`, RAG-05) → retrieve (CandidateK, per-query hybrid) → fuse (RRF/dedup) → rerank (named, `RerankerFactory`) → assemble (token budget + **anti-Lost-in-the-Middle `edges` ordering**: ranks 1, 3, 5… open the context, …6, 4, 2 close it — best two chunks at the extremities, rank-stable `[n]` markers) → cited generation → optional groundedness hook (checker ships with RAG-06); every stage traced in `RagAnswer.Trace`. `RagProfile { Fast, Balanced, Quality }` + `RagProfilePresets` → **`RagOptions` v2** bound on `Orkeon:Rag` (profile = preset, configuration = per-key override; defaults in `Orkeon.Domain.Constants.Rag.RagDefaults`); `ProfileRagPipelineResolver` builds and memoizes one pipeline per profile (unknown names fail loudly listing `fast, balanced, quality, default`). Default profile is `fast` (deviation from plan §5.2's `balanced`: balanced requires the opt-in ONNX package; opt in with one key `Orkeon:Rag:Profile=balanced`). The store is now always wrapped in the hybrid decorator (ingestion feeds BM25; disabled default mode = strict passthrough); `RetrievalQuery.Hybrid` toggles fusion per query.
- **Measured comparison** (golden dataset, offline, heuristic judge, 2026-07-26): fast 0.89 recall@5 / 0.89 MRR / 3 ms/case; balanced 0.89 / 0.89 / 139 ms/case; quality 0.89 / 0.89 / 105 ms/case (9 cases, incl. two exact-identifier lookups q-008/q-009 that vector-only also resolves at this corpus scale). The three profiles tie **on this dataset by construction**: the eight regular cases are saturated by plain vector retrieval (1.00/1.00 each, `correctif` excluded ⇒ gated aggregates 1.00/1.00 for all profiles), and the seeded `q-007` defeats the cross-encoder too (measured score of the relevant `notes-power.md`: 0.0000, dead last; decoy `faq-battery.md`: 0.9997) — vocabulary bridging is exactly the RAG-05/RAG-06 lever (plan §9.1: `correctif` must fail until Corrective). The CI gate runs on the **balanced** profile and the fast/balanced/quality table is published in the workflow step summary.

### Changed — **BREAKING: RAG subsystem extraction (RAG-02, no shims)**

The RAG feature set is promoted to a first-rank subsystem (`src/rag/` — `Orkeon.Rag.Abstractions` contracts + `Orkeon.Rag` implementations, agent tools in `src/tools/Orkeon.Tools.Rag`; see [ADR-006](docs/adr/ADR-006-rag-subsystem.md)). The legacy namespaces `Orkeon.Application.Interfaces.Rag.*`, `Orkeon.Application.Rag.*`, `Orkeon.Application.Interfaces.Knowledge.*` and `Orkeon.Infrastructure.Knowledge.*` are **removed without `[Obsolete]` shims** (assumed break, decision 2026-07-25, `0.9.x-beta` window).

Opt-in wiring: `services.AddOrkeonRag(configuration)` (namespace `Orkeon.Rag.DependencyInjection`, self-sufficient `TryAdd*`, default `IDocumentStore` = `MemoryProviderDocumentStore` over the ambient `IMemoryProvider`) + `services.AddOrkeonRagTools()` (`Orkeon.Tools.Rag.DependencyInjection`, registers `rag_search`). Neither is called by `AddOrkeonInfrastructure()`.

Migration table (old type → new type):

| Old (removed) | New |
|---|---|
| `Orkeon.Infrastructure.Knowledge.RagTool` (`rag_search`) | `Orkeon.Tools.Rag.RagSearchTool` (`rag_search` — same name, schema `question`/`top_k`/`collection`, and output format `answer` + `Sources:` block; `collection = "raggable-tree"` still routes to `IRaggableStore`) |
| `AddOrkeonRag` (Infrastructure `RagServiceExtensions`) + `AddOrkeonKnowledge` + `AddOrkeonRagValidation` | `AddOrkeonRag(configuration)` (`Orkeon.Rag.DependencyInjection.RagServiceCollectionExtensions` — loaders + ingestion validation + pipelines + factories) + `AddOrkeonRagTools()` |
| `Orkeon.Application.Interfaces.Rag.IRagPipeline` (`ExecuteAsync(question, RagOptions)` → `RagResult`) | `Orkeon.Rag.Abstractions.Interfaces.IRagPipeline` (`QueryAsync(RagQuery)` → `RagAnswer` with citations + trace) |
| `Orkeon.Application.Rag.RagPipeline` + `ChatClientResponseGenerator` | `Orkeon.Rag.Pipeline.StagedRagPipeline` (named `LinearRagPipeline` until RAG-04/C4) |
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
| `RagOptions` / `RagPipelineOptions` / `RagDefaults` / `KnowledgeContext` / `KnowledgeItem` / `RagTypes` (`RagResult`, `RetrievalOptions`…) | `Orkeon.Rag.Abstractions.Models.*` (`RagQuery`, `RagAnswer`, `Citation`, `ScoredChunk`, `RetrievalQuery`…) + `Orkeon.Rag.Abstractions.Options.RagOptions` v2 (section `Orkeon:Rag`, RAG-04/C4) / `RagIngestionOptions` (section `Orkeon:Rag:Ingestion`); defaults in `Orkeon.Domain.Constants.Rag.RagDefaults` |
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
