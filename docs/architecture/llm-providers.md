> 🇫🇷 [Version française](../fr/architecture/llm-providers.md)

# LLM Providers

`HttpLlmProviderBase` (`Orkeon.Infrastructure.LLMs.Base`) provides the abstract base for all LLM providers. It integrates HTTP handling (`IHttpClientFactory`), Polly resilience policies (retry, circuit breaker, timeout), and JSON serialization.

**Retry budget.** `Llm:MaxRetries` (10 by default) covers the failures that come back in seconds — request errors, 5xx, 429. A buffered call that hits `Llm:TimeoutSeconds` is retried **once** (`ResilienceDefaults.LlmTimeoutRetries`: every attempt costs the whole timeout), then the provider answers with a failure naming the setting and the two ways out (a longer timeout, thinking off); a caller's cancellation is never retried. A failed call travels as `LlmResponse.Error` and, through the chat-client adapter, as an exception — it is never mistaken for an empty answer, so the agent loop fails the task at once with the provider's reason instead of retrying without tools (LLM-11).

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
  `google`, `grok` / `xai`, `minimax`, `zai` / `glm` / `zhipu`, `openrouter`, `mammouth`.
  An unknown key throws `NotSupportedException`. This is the path of
  `orkeon llm probe --provider`.
- `Create(config)` infers the provider — this is what a run does, since the `Llm` section of
  the settings file carries no provider key. The first rule that matches wins:
  1. **Base URL, known hosts** — `/engines/` or `model-runner.docker.internal` (Docker Model
     Runner, OpenAI dialect — checked first, before the localhost rule), `azure` /
     `.cognitiveservices.`, `together.xyz`, the DashScope hosts, `deepseek.com` (refused when
     the path is `/anthropic`), `moonshot.cn` / `moonshot.ai`, `mistral.ai`,
     `generativelanguage.googleapis.com`, `api.x.ai`, `api.minimax.io` / `api.minimaxi.com`,
     `huggingface.co` / `hf.co`, `api.z.ai` / `bigmodel.cn`, `openrouter.ai`, `mammouth.ai`.
  2. **Base URL, generic** — contains `openai` → OpenAI, `anthropic` → Anthropic,
     `localhost` or `11434` → Ollama.
  3. **Model prefix** — `gpt`, `claude`, `llama` / `codellama` (Ollama), `mistral` /
     `ministral` (the bare `mistral` and `mistral:<tag>` go to Ollama), `qwen`, `deepseek`,
     `moonshot`, `glm`, `gemini`, `grok`, `minimax`, `openrouter/`. Mammouth is never
     inferred from a model name: its identifiers are the vendors' own.
  4. **API key prefix** — `hf_` → HuggingFace, `xai-` → Grok.
  5. Otherwise **OpenAI**.

A section that names no model skips rule 3. **The model a call runs on** is then resolved by
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
warning below, never in silence.

Every provider the factory builds is wrapped in `MeteredLlmProvider` (see
[Decorators and registration](#decorators-and-registration)) and returned behind an
`LlmProviderAdapter`.

## Declared capabilities

Every provider declares a `LlmProviderCapabilities` value object (Domain, exposed on
`ILlmProvider`; a provider overrides `HttpLlmProviderBase.DeclaredCapabilities`, which defaults
to `LlmProviderCapabilities.Unknown`): `ResponseFormat`
(`None`/`JsonObject`/`JsonSchema`), `Thinking` (`None`/`EffortOnly`/`Toggle`/`Budget`),
`Vision`, `ExplicitPromptCaching`, `RequiresJsonKeywordInPrompt`, `ReplaysReasoningContent`.
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
does not stream answers it buffered. The streams themselves had to say what the buffered
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
- **`RateLimitedLlmProvider`** — routes every call through the `ILlmRateLimiter` (the
  `RateLimiting` settings block). It wraps the provider handed to the scripting engine,
  whose `ctx.llm.*` calls bypass the orchestrator's own throttling; it is not meant as a
  global decorator, or the YAML path would be throttled twice.
- **LLM exchange logging** — `LlmLoggingDelegatingHandler` (`Orkeon.Infrastructure.Logging`)
  captures every HTTP exchange (headers and payload, sanitized by `LogSanitizer`: credential
  headers redacted by name, secrets in bodies by pattern) as JSON Lines plus a structured
  log summary. `services.AddLlmExchangeLogging(logDirectory, options)` injects it into every
  `IHttpClientFactory` client; the `IHttpClientBuilder` overload targets one named client.
  `LlmLoggingOptions`: `MaxBodyLengthChars` (0 = no truncation),
  `LogStreamingExchanges` (true; a stream is captured as its request only),
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
