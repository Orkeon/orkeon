> 🇫🇷 [Version française](../fr/architecture/llm-providers.md)

# LLM Providers

The providers on this page serve the `Generation` category. Embedding, reranking and decision providers (TypeSafe) are listed in [Model providers](./model-providers.md); a decision model judges an LLM call through a decision profile ([Decision models](./decision-models.md)).

`HttpLlmProviderBase` (`Orkeon.Infrastructure.LLMs.Base`) provides the abstract base for all LLM providers. It integrates HTTP handling (`IHttpClientFactory`), the Polly retry policies, and JSON serialization.

**Retry budget.** `Llm:MaxRetries` (10 by default) covers the failures that come back in seconds — request errors, 5xx, 429. A buffered call that hits `Llm:TimeoutSeconds` is retried **once** (`ResilienceDefaults.LlmTimeoutRetries`: every attempt costs the whole timeout), then the provider answers with a failure naming the setting and the two ways out (a longer timeout, thinking off); a caller's cancellation is never retried. A failed call travels as `LlmResponse.Error` and, through the chat-client adapter, as an exception — it is never mistaken for an empty answer, so the agent loop fails the task at once with the provider's reason instead of retrying without tools (LLM-11).

**Rate-limit window.** A 429, or a 529 that carries a reset header, is not retried on the backoff ladder: the call waits until the window resets, on the buffered path and on the streaming connect alike (`RateLimitResetHeaders`). It reads `Retry-After` (seconds or an HTTP date), `retry-after-ms` (milliseconds, sent by OpenAI and TypeSafe), `anthropic-ratelimit-requests-reset` and `anthropic-ratelimit-tokens-reset` (RFC 3339) and `x-ratelimit-reset-requests` and `x-ratelimit-reset-tokens` (OpenAI durations such as `6m0s`), and the longest wins. The wait never exceeds `Llm:MaxRateLimitWait` (5 minutes by default, separate from the 30-second cap of the 5xx ladder): a reset beyond it fails the call at once with the reason (`LlmRateLimitException`) instead of parking an unattended run. Each wait is announced to `ILlmRetryObserver` with the reason `rate-limit window` and the delay. A 529 without a header keeps the ladder.

**A streamed call is bounded the same way (LLM-12).** A streamed request is sent with `ResponseHeadersRead`, and `HttpClient.Timeout` stops counting once the headers are in: the body used to be read on the caller's token alone, so a model that thinks for minutes before its first token hung a Studio run past any `Llm:TimeoutSeconds`. Every streaming path (the OpenAI dialect, Azure, Anthropic, Ollama — chat and text streams) now runs under a `StreamReadBudget`: `Llm:TimeoutSeconds` bounds the whole call, headers and body, and the optional `Llm:StreamIdleSeconds` bounds the silence between two lines of the body (unset by default). An elapsed bound ends the chat stream with a `Completed` event whose response carries `error` (the sentence naming the setting) and `error_type` (`TaskCanceledException` for the whole-call bound, `StreamIdleTimeout` for the idle one) over whatever content had arrived, with no `RawResponseBody`; the text stream throws an `HttpRequestException` with the same sentence. A stream the provider closes without its finish marker (`finish_reason` or `[DONE]`, `message_stop`, `done: true`) is a failed call too when no answer text and no tool call arrived (`error_type` `StreamTruncated`, the sentence saying how much reasoning was streamed); with some answer it is served as it came, flagged `stream_truncated` in its metadata. The chat-client adapter fails a streamed turn on any `error`, partial content or not; the headers phase re-sends an elapsed `HttpClient.Timeout` once (`LlmTimeoutRetries`), like the buffered path.

**Thinking is replayed unchanged (Anthropic).** The assistant turn of a tool call carries its `thinking` and `redacted_thinking` blocks (text, `signature`, `data`) as the provider sent them, buffered or streamed, in `LlmMessage.RawThinkingBlocks`; the next request re-emits them first, before the turn's `text` and `tool_use` blocks, which the API requires for extended thinking with tools. Other providers ignore them. A tool call without `id`, `name` or an object `arguments` is refused with an error that names its index, its name and the field.

Implemented providers:

| Provider | Class | Namespace |
|----------|-------|-----------|
| OpenAI | `OpenAIProvider` (via `OpenAICompatibleProviderBase`) | `Orkeon.Infrastructure.LLMs` |
| Anthropic | `AnthropicLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Azure OpenAI | `AzureOpenAILlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Ollama | `OllamaLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Together AI | `TogetherAiLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| DeepSeek | `DeepSeekLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| HuggingFace | `HuggingFaceLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Kimi | `KimiLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Qwen | `QwenLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Mistral AI | `MistralLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Z.AI (Zhipu GLM) | `ZaiLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Google Gemini | `GeminiLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Grok (x.AI) | `GrokLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| MiniMax | `MiniMaxLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| OpenRouter (aggregator) | `OpenRouterLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Mammouth AI (aggregator) | `MammouthLlmProvider` | `Orkeon.Infrastructure.LLMs` |

Fourteen of them extend `OpenAICompatibleProviderBase` (itself an `HttpLlmProviderBase`);
Anthropic and Ollama extend `HttpLlmProviderBase` directly, because their APIs are not
OpenAI-shaped. Docker Model Runner and any other OpenAI-compatible server have no class of
their own: they are driven by `OpenAIProvider` through their base URL. Endpoints, default
models and provider keys are shared constants in `Orkeon.Constants.Llm`
(`LlmProviderEndpoints`, `LlmProviderDefaultModels`, `LlmProviderKeys`,
`LlmModelOutputLimits`).

Generic adapters (`ChatClientToLlmProviderAdapter`, `LlmProviderToChatClientAdapter`, `ChatClientToBasicLlmProviderAdapter`) are available in `Orkeon.Infrastructure.LLMs.Adapters` to integrate other providers compatible with the `IChatClient` interface. A Microsoft Agent Framework agent can also serve as a model (`AIAgentLlmProvider`, [ADR-010](../adr/ADR-010-agent-framework-interop.md)).

### Provider resolution

`LlmProviderFactory` (`Orkeon.Infrastructure.LLMs`) has two entry points:

- `Create(providerType, config)` takes an explicit provider key (`LlmProviderKeys`):
  `openai`, `anthropic`, `ollama`, `azure-openai` / `azure`, `together` / `togetherai`,
  `qwen`, `deepseek`, `kimi` / `moonshot`, `mistral`, `huggingface` / `hf`, `gemini` /
  `google`, `grok` / `xai`, `minimax`, `zai` / `glm` / `zhipu`, `openrouter`, `mammouth`, and
  `custom` / `docker-model-runner`, which build the OpenAI dialect on the configured base URL.
  An unknown key throws `NotSupportedException`. This is the path of
  `orkeon llm probe --provider`.
- `Create(config)` infers the provider — this is what a run does, since the `Llm` section of
  the settings file carries no provider key. The first rule that matches wins:
  1. **With a base URL, the address alone decides** (`LlmEndpointReading`, the one reading the
     run, the CLI and Studio share; the model and the key are not consulted):

     | Address | Provider |
     |---|---|
     | a known vendor host — `azure` / `.cognitiveservices.`, `together.xyz`, the DashScope hosts, `deepseek.com`, `moonshot.cn` / `moonshot.ai`, `mistral.ai`, `generativelanguage.googleapis.com`, `api.x.ai`, `api.minimax.io` / `api.minimaxi.com`, `huggingface.co` / `hf.co`, `api.z.ai` / `bigmodel.cn`, `openrouter.ai`, `mammouth.ai`, `openai.com`, `anthropic.com` | that vendor |
     | `/engines/` in the path, `model-runner.docker.internal` or port `12434` | Docker Model Runner (OpenAI dialect) |
     | port `11434` | Ollama, at its native root (a trailing `/v1` or `/api` is dropped) |
     | `anthropic` in the host or in the path | Anthropic |
     | any other address | OpenAI-compatible server (`custom`), at the path written |
     | a vendor host under an `/anthropic` path, or an address that is not an absolute `http(s)` URL | refused at start, with the setting's path and the fix |

     A server is Ollama only on port `11434`, wherever it runs. A `custom` address
     with no path (`http://host:8000`) is read as an OpenAI-compatible server answering at its
     root and is reported once per section (`LlmEndpointNotice.Silent`, a warning on stderr and in
     the log): add the path of its routes, often `/v1`.
  2. **Model prefix** (only without an address) — `gpt`, `claude`, `llama` / `codellama` (Ollama), `mistral` /
     `ministral` (the bare `mistral` and `mistral:<tag>` go to Ollama), `qwen`, `deepseek`,
     `moonshot`, `glm`, `gemini`, `grok`, `minimax`, `openrouter/`. Mammouth is never
     inferred from a model name: its identifiers are the vendors' own.
  3. **API key prefix** — `hf_` → HuggingFace, `xai-` → Grok.
  4. Otherwise **OpenAI**.

A section that names no model skips rule 2. **The model a call runs on** is then resolved by
the provider, the same way for all sixteen (`HttpLlmProviderBase.ResolveModel`): the call's own
when its configuration names one, else the model the provider is configured with — its
profile's —, else its `DefaultModel` (its `LlmProviderDefaultModels` entry; Azure has none of
its own and falls back to OpenAI's as a deployment name, so name yours). A configuration that
names no model (`LlmConfig.OnProfile()`, an empty `Model`) never reaches the wire empty, and
never carries another vendor's model.

**The configuration a call runs on** follows the same rule, field by field
(`LlmConfig.InheritFrom`, applied by `HttpLlmProviderBase.EffectiveConfig` in the sixteen
providers): a configuration passed with a call **completes** the one the provider was built with,
it does not replace it. Every field the call leaves unset — null, an empty or blank string, no
stop sequence — is the provider's: the key, `BaseUrl`, `TimeoutSeconds` (nullable: unset
everywhere, 30 s), Azure's `ApiVersion`, Anthropic's `WorkspaceId`, `Thinking`, `MaxTokens`,
`Temperature` and `TopP` (nullable too since GAP-36), `Seed`, `SystemMessage`, `ResponseFormat`,
`Cache`, `Tools`, the grammar and the model; custom parameters merge, the call's keys winning.
Every field the call sets wins. The settings that cannot be unset — the penalties (0, every
vendor's default) and the tool mode — are the caller's, and `MaxRetries` and `Grammar` are read
from the provider's configuration when it is built. A call cannot unset what its provider sets;
it overrides it (`Thinking = { Enabled = false }`, `LlmResponseFormat.Text()`). The chat client
adapter registered without a base configuration starts from the provider's own
(`ILlmProvider.BaseConfig`). Before, the providers took a call's configuration whole
(`config ?? Config`): the planner, the cognitive memory, the context-window and RaggableTree
summarizers and the agent loops outside the chat client lost the key — "API key is required" —,
the endpoint and the timeout of the provider they reached (GAP-29).

**Sampling: what is set is sent, what is not set is not** (GAP-36). `Temperature` and `TopP`
are null when nothing sets them — not the call, not the agent, not the task, not the profile —
and then nothing reaches the wire: the model applies its own default (often 1; the Modelfile's
on Ollama). Set, they are sent whatever their value. The engine used to take 0.7 and 1.0 for
"not set": an agent asking for exactly those ran on its profile's, and the 0.7 it pinned on every
call nobody configured is refused by the default models of OpenAI (`gpt-5.6-sol`) and Anthropic
(`claude-sonnet-5`). One measured exception: Mistral's dialect writes `top_p: 1` when nothing sets
one (`AlwaysEmitTopP`). The penalties, the seed and the stop sequences of an agent's configuration
travel through the chat client too; on the wire, Ollama writes `top_p`, `seed` and `stop` in its
`options`, and a dialect that has no field for a penalty or a seed says so with the structured
warning below, never in silence. A temperature, `top_p` or penalty is sent as written, up to six
significant digits (the `float` of `ChatOptions` holds no more): `0.7`, never `0.699999988079071`,
under any current culture.

Every provider the factory builds is wrapped in `CallModeLlmProvider`, then, by
`LlmProviderEntrance`, in `MeteredLlmProvider` (always; it reports to the host's `ILlmUsageSink`
when one is registered), `TracedLlmProvider` and, when the host registered an `ILlmRateLimiter`,
`RateLimitedLlmProvider` (see [Decorators and registration](#decorators-and-registration)), and
returned behind an `LlmProviderAdapter`.

## Declared capabilities

Every provider declares a `LlmProviderCapabilities` value object (Domain, exposed on
`ILlmProvider`; a provider overrides `HttpLlmProviderBase.DeclaredCapabilities`, which defaults
to `LlmProviderCapabilities.Unknown`): `ResponseFormat`
(`None`/`JsonObject`/`JsonSchema`), `Thinking` (`None`/`EffortOnly`/`Toggle`/`Budget`), `EffortLevels`
(the effort levels the field knows, `low`, `medium`, `high` by default, the five with `xhigh` and `max` for
Anthropic and OpenRouter, `max` without `xhigh` for DeepSeek and Z.ai: a level asked that is not declared is sent as the
highest declared level below it, with a warning in the provider's log), `Vision`, `ExplicitPromptCaching`,
`RequiresJsonKeywordInPrompt`, `ReplaysReasoningContent`, `RunsOwnTools` (declared by the Microsoft Agent Framework
bridge alone: it is an agent that runs its own tools).
One capability is not the vendor's to declare: `GbnfGrammar`, which no vendor API documents, is
switched on by the configuration the provider is built with (`Llm:Grammar`,
`LlmConfig.GrammarEnabled`) for a llama.cpp-compatible server behind the OpenAI-compatible
providers or Ollama — `ILlmProvider.Capabilities` is the declaration plus that switch.
And one is no vendor's at all: `ReplaysPrompt`, declared by the echo provider alone
(`UndefinedLlmProvider`, the model of a host without an `Llm` section), says that what it returns
is its prompt, never a model's answer — the crew's planner then skips its call with a warning
instead of reading the prompt back as a plan (GAP-31). The decorators a provider is wrapped in
(`MeteredLlmProvider`, `RateLimitedLlmProvider`) pass the capabilities through.
`OpenAICompatibleProviderBase` translates the declaration into the OpenAI dialect once
(vision payloads, `response_format`, thinking, the `CapabilityMismatchHint` diagnostics);
Anthropic and Ollama write their own dialects, and Qwen overrides the hook for DashScope's
thinking fields. An option a provider cannot honour produces a structured warning (event id
`110`, `Option '…' was declared but … does not support it — it was not sent`) — never a
silent drop; since GAP-36 that includes `frequency_penalty` and `presence_penalty` on every
dialect, and `seed` wherever the dialect does not write it (all but Ollama). `CapabilityMismatchHint` (OpenAI-compatible providers and Ollama) covers the
per-model side: when a vendor refuses a
capability the provider declares (a text-only model sent an image, a model without thinking
or tools), the error says whose assumption was wrong. The mirror-image case —
a constraint only the **server** can state — has its own seam:
`OpenAICompatibleProviderBase.TryAdaptRejectedPayload` gives a provider one chance to adapt
a payload the API rejected with a 4xx and re-send it once (generate and chat paths;
streaming never retries). Kimi uses it for Moonshot's `invalid temperature: only 1 is
allowed for this model` — the mandated value is read from the rejection itself (which
models mandate it is decided server-side, a hard-coded list would drift) and the
substitution is logged as a structured warning; only a temperature that was set and sent is
replaced, since one nothing sets is not sent (GAP-36). Two more dialect seams serve the
aggregators (LLM-09): `ReasoningFieldName` names the vendor field the reasoning trace is
read from (`reasoning_content` by default, `reasoning` on OpenRouter — the Orkeon metadata
key stays `reasoning_content`), and `usage.cost` becomes the `cost` metadata wherever a
vendor bills in the response — buffered or streamed — with `cost_currency` beside it when the
provider states its vendor's billing currency (`CostCurrency`: `USD` on OpenRouter, whose
credits are dollars; no other provider states one). From there the charge travels as billed:
the chat client adapter carries it onto the `ChatResponse` (`AdditionalProperties`, also on
its streaming path), the agent loop reports it on the usage event
(`CostUsageEvent.Cost`, null when the vendor billed nothing — a free model's `0` stays
`0`), the scripting facade does the same for `ctx.llm.*`, and the run's `cost.updated`
relays it with `costSource: "vendor"` ([the run event bus](run-event-bus.md)).
`CostBudgetManager` prices from its registry only a call nobody priced, for its own
budgets; that estimate never reaches the wire. A chunk carrying a root-level `error` after
the HTTP 200 ends a stream the way a pre-stream refusal does — `error` metadata on the chat stream, an
`HttpRequestException` on the token stream — never as a clean completion. All 16 providers
are `IStreamingLlmProvider`s. The chat client adapter's streaming path
(`GetStreamingResponseAsync`, GAP-32) is built like its buffered one — the same messages,
roles, tools and options — and reads the provider's chat stream (`ChatStreamingAsync`): the
text as it arrives, then one last update carrying the tool calls (native or of the text
protocol), the provider's usage, the finish reason, the model, the vendor's cost and the
reasoning to replay. Folded, the updates are the response the buffered call returns, so a
streamed agent turn calls its tools and is metered exactly, never estimated; a provider that
does not stream answers it buffered. A tool call is replayed to the provider with every
property it carried, buffered or streamed, each call with its own: the adapter keeps the
provider's `tool_calls[i]` element on its `FunctionCallContent` and replays it whole, and the
merge of streamed fragments keeps what a fragment adds beyond `index`, `id`, `type` and
`function` — Gemini's thought signature (`extra_content.google.thought_signature`), which
Gemini requires back, travels that way (LLM-14); a call that carried nothing else is replayed
as `id`, `type` and `function`, as before. The streams themselves had to say what the buffered
answers say: Anthropic now assembles the `tool_use` blocks it streams; MiniMax splits its
leading `<think>` block out of the content deltas (`LeadingReasoningTag`), so the text a stream
carries is the answer its final response keeps; and on the native protocol, the OpenAI dialect
and Anthropic give a streamed answer the body their buffered answer carries, in which the
text-protocol fallback reads a call the model wrote as text. The per-provider matrix lives in
[the provider comparison](../reference/llm-providers-comparison.md).

### Dialect seams of the OpenAI-compatible base

What differs between the compatible vendors is expressed through a handful of protected
members, not through copies of the payload builder:

| Seam | Default | Overridden by |
|---|---|---|
| `ApplyProviderSpecificOptions` | writes `thinking` / `reasoning_effort` and `response_format` from the declared capabilities | Qwen (`enable_thinking`, `thinking_budget`), OpenRouter (the `reasoning` request object), Together AI (adds `context_length_exceeded_behavior: truncate`) |
| `MaxTokensFieldName` | `max_tokens` | OpenAI (`max_completion_tokens`) |
| `AlwaysEmitTopP` | `top_p` written only when set | Mistral (`top_p: 1` when nothing sets one) |
| `SplitReasoningFromContent` / `EnrichAssistantMessage` | nothing split; `reasoning_content` replayed when `ReplaysReasoningContent` is declared | MiniMax (inline `<think>` block split out, re-inlined on replay) |
| `ReasoningFieldName` | `reasoning_content` | OpenRouter (`reasoning`) |
| `CostCurrency` | none | OpenRouter (`USD`) |
| `TryAdaptRejectedPayload` | re-sends once without the output cap when the catalogue's cap was refused | Kimi (the mandated temperature, then the base rule) |
| `BuildEndpoint` | `{baseUrl}/chat/completions` | Azure OpenAI (dated deployment URL, or the v1 surface with `api_version: v1`) |

## Prompt-cache tokens and TTL

`LlmResponse` carries three cache counts: `CacheHitTokens` (read from the cache), `CacheWriteTokens`
(written to it — Anthropic's `cache_creation_input_tokens`) and `CacheMissTokens` (input not served from
the cache). `PromptTokens` already includes the written and read tokens: a write is never counted twice.
`MeteredLlmProvider` records them once per call in the `CostUsageEvent`; `CostBudgetManager` keeps per model
the prompt, completion, cache-write, cache-read and reasoning tokens and the cost (`CostReport.ModelUsageByModel`), and
`session_stats` returns them in `by_model_tokens[]` beside the unchanged `by_model` cost map. A cache or reasoning
count that no call reported is null in `ModelUsage`, never 0 (a call that reported none adds nothing to the others:
null + 5 = 5), and its key is absent from the `by_model_tokens[]` entry; 0 means the provider measured nothing. A model whose
`ModelPricing` declares `CacheWritePricePerMillion` / `CacheReadPricePerMillion` is priced as
`(prompt - write - read) x prompt rate + write x write rate + read x read rate + completion x completion rate`;
without those rates the cost is unchanged. Every built-in Anthropic entry declares them (#257): a write at
1.25x the prompt rate, a read at 0.1x unless the vendor states another figure (Opus 5.5 and Sonnet 5.5 read at
0.20 $/M, Fable 5.1 at 0.25 $/M). The write rate is the 5-minute TTL's: the 1-hour TTL writes at 2x the prompt
rate, but the usage event carries no TTL, so a 1-hour write is costed at the 5-minute rate. Haiku 5.5 is priced
at its rate for a prompt up to 100K tokens; a longer prompt bills more, which the registry does not model.

The cache TTL is `LlmCacheConfig.Ttl` (`5m` or `1h`). A call can carry it as the option
`LlmChatOptionsKeys.CacheTtl` (`orkeon:cache_ttl`), which `ChatOptionsComposer` fills from the agent's
`llm.cache.ttl`; the chat client adapter folds it into the call's `Cache.Ttl` and Anthropic emits it in
`cache_control`. A call TTL with no cache configuration that places a breakpoint is itself the request: the
adapter marks the system prompt with that lifetime (`LlmCacheConfig.SystemPrompt()`), since a lifetime on
nothing would cache nothing. The OpenAI-compatible providers and Ollama cannot honour it and log the structured unhonoured-option
warning (event 110, `cache_ttl`) instead of dropping it silently.

The request is opt-in, from YAML (`llm.cache: { system, tools, ttl }`) or from a script:
`agentBuilder().cache({ systemPrompt, tools, ttl })` and the `cache` key of `LlmConfig.with({...})` map onto the
same `LlmCacheConfig` (`LlmCacheConfig.Request`). The TTL is `5m` or `1h`, anything else is refused in both, and a
script's `ttl` without `systemPrompt` or `tools` is refused as caching nothing. An object that marks nothing
(`cache: {}`, `cache({})`) is an explicit Off (`LlmCacheConfig.Off`): it turns an inherited cache off, the host
default included, where a cache left out inherits it.

A host sets a default for every agent with `Llm:Cache` (`SystemPrompt`, `Tools`, `Ttl`), and a profile with
`Llm:Profiles:<name>:Cache`. It is validated as the YAML block is (`LlmCacheConfig.Request`): a `Ttl` other than
`5m` or `1h`, or one with neither `SystemPrompt` nor `Tools`, refuses the load and names the key. It applies only
to a provider whose cache is explicit (`LlmProviderCapabilities.ExplicitPromptCaching`, Anthropic): elsewhere the
provider's `BaseConfig` drops it, so it neither reaches the call nor warns. An agent's or task's own cache, an
explicit Off included, always wins. The procedural `ctx.llm` calls of a script agent run with the cache and the
effort its builder set (`.cache(...)`, `.effort(...)`, or the `LlmConfig` given to `.llm(...)`), a call's own
options winning.

## Decorators and registration

- **`MeteredLlmProvider`** — the one place LLM usage is measured: it reports every call of
  the provider it wraps to the host's `ILlmUsageSink`, with the vendor's own cost when the
  answer carries one and an estimate flagged as such otherwise. `LlmProviderFactory` wraps
  every provider it builds; a provider the factory does not build (the echo provider of a
  host without an `Llm` section, a Microsoft Agent Framework agent, a test double) is
  registered with `services.AddOrkeonLlmProvider(sp => …, baseConfig)`, which exposes it as
  `ILlmProvider`, `IBasicLlmProvider` and `IChatClient` over one metered instance.
  `AddOrkeonInfrastructure()` registers no model of its own: `orkeon run`, `orkeon-host` and
  `orkeon-repl` register theirs from the `Llm` section — or the echo provider when there is
  none — and a container that registers none fails at its first LLM resolution, naming the
  missing service. Its former fallback, a keyless OpenAI provider, was the REPL's chat client
  (GAP-29).
- **`RateLimitedLlmProvider`** — the host's `RateLimiting` at the entrance of each provider:
  every call takes one lease of the `ILlmRateLimiter` (global, per provider, concurrency) for
  the time of the call, a stream for its whole enumeration. It is applied with the meter, around
  it, at the points that meter a provider — the factory, `AddOrkeonLlmProvider` and
  `AddOrkeonLlmProfile`, the profile registry's `ForProvider`, the manager's resolver, the planner
  of a C# crew (`LlmProviderEntrance`) — so every model call of the host is limited once, the
  scripts' `ctx.llm.*` included. A provider already limited is left as it is; a call made inside
  another limited call takes no lease (under `MaxConcurrentRequests: 1` it would wait on
  itself); a provider that runs its own tools (`RunsOwnTools`, the Microsoft Agent Framework
  bridge) is not limited itself — what it calls of Orkeon's model is. A refusal is retried five
  times on its `RetryAfter`, then the call fails with the limiter's reason (GAP-38).
- **Call mode** — `CallModeLlmProvider` decides how each call of a provider built from a setting
  reaches the model, for every caller (agent loop, manager, planner, judges, RAG). The setting's
  `CallMode` key (`Llm:CallMode`, `Llm:Profiles:<name>:CallMode`) is `Auto` (absent or empty),
  `Buffer` or `Stream`, case ignored; another value fails the load. `Buffer` answers whole;
  `Stream` and `Auto` stream on the wire when the provider streams natively and fold the events
  into the same `LlmResponse` (text, tool calls, finish reason, usage), so the meter and the
  limiter still see one call. In `Auto`, a clear refusal of streaming before any event (status
  404, 405, 415 or 501, or 400/422 whose body names the stream, or a provider that declares no
  streaming) falls back to a buffered call for the rest of the run (`LlmCallModeScope`, opened
  per crew run), with one warning; a 401, 403, 429, a timeout or a stream cut after its first
  event stays a failure. In `Stream`, a refusal fails the call naming `Auto` and `Buffer`. It sits
  inside the meter and the limiter, applied by `LlmProviderFactory` (STUDIO-71). See also: Studio offers the mode in its settings and shows the mode of the current call in the status bar — [The live token counter](studio.md#the-live-token-counter-studio-71).
- **LLM exchange logging** — `LlmLoggingDelegatingHandler` (`Orkeon.Infrastructure.Logging`)
  captures every HTTP exchange (headers and payload, sanitized by `LogSanitizer`: credential
  headers redacted by name, secrets in bodies by pattern) as JSON Lines plus a structured
  log summary. `services.AddLlmExchangeLogging(logDirectory, options)` injects it into every
  `IHttpClientFactory` client; the `IHttpClientBuilder` overload targets one named client.
  `LlmLoggingOptions`: `MaxBodyLengthChars` (0 = no truncation),
  `LogStreamingExchanges` (true; a stream passes through to the caller as it arrives and is
  logged once it ends, as its rebuilt answer, or the raw text received when it was cut),
  `FullEmbeddingLog` (true; false shortens embedding arrays to a preview). The runners switch
  it on with `--llm-log` / `--llm-log-path` and read the options from the `LlmLogging`
  settings section; the files go to the internal `/llm-logs` mount, never visible to agents.
  It is a debugging facility: the capture holds full prompts.

## Validating a provider against its real API

Every unit test in this area speaks to a mocked HTTP handler, which proves Orkeon sends what
we believe it sends — not that the vendor accepts it. The second proof is produced by the
campaign kit in [`llmproviders-test/`](https://github.com/Orkeon/orkeon/tree/main/llmproviders-test):

```bash
llmproviders-test/run-campaign.sh --provider ollama --model llama3.2   # zero-cost first run
llmproviders-test/run-campaign.sh --all --config providers.local.json --dry-run
```

It drives `orkeon llm probe` over modes M1–M10, M12 and M13 of the provider
test protocol (maintainers' internal matrix) and archives
one Markdown report per campaign. `orkeon llm models -p <provider> --filter 'gpt-5.6-*'`
lists what a provider currently serves, so a campaign never depends on a hand-maintained
model list.

---

> **See also**: [Provider comparison](../reference/llm-providers-comparison.md) · [LLM response format](../guides/llm-response-format.md) · [Multi-modal content](../guides/multimodal.md) · [Local models](../guides/local-models.md) · [Memory system](./memory-system.md) · [Security](./security.md) · [Back to index](../INDEX.md)
