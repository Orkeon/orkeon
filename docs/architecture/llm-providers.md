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

Every provider the factory builds is wrapped in `MeteredLlmProvider` (see
[Decorators and registration](#decorators-and-registration)) and returned behind an
`LlmProviderAdapter`.

## Declared capabilities

Every provider declares a `LlmProviderCapabilities` value object (Domain, exposed on
`ILlmProvider`; the base defaults to `LlmProviderCapabilities.Unknown`): `ResponseFormat`
(`None`/`JsonObject`/`JsonSchema`), `Thinking` (`None`/`EffortOnly`/`Toggle`/`Budget`),
`Vision`, `ExplicitPromptCaching`, `RequiresJsonKeywordInPrompt`, `ReplaysReasoningContent`.
`OpenAICompatibleProviderBase` translates the declaration into the OpenAI dialect once
(vision payloads, `response_format`, thinking, the `CapabilityMismatchHint` diagnostics);
Anthropic and Ollama write their own dialects, and Qwen overrides the hook for DashScope's
thinking fields. An option a provider cannot honour produces a structured warning (event id
`110`, `Option '…' was declared but … does not support it — it was not sent`) — never a
silent drop. `CapabilityMismatchHint` (OpenAI-compatible providers and Ollama) covers the
per-model side: when a vendor refuses a
capability the provider declares (a text-only model sent an image, a model without thinking
or tools), the error says whose assumption was wrong. The mirror-image case —
a constraint only the **server** can state — has its own seam:
`OpenAICompatibleProviderBase.TryAdaptRejectedPayload` gives a provider one chance to adapt
a payload the API rejected with a 4xx and re-send it once (generate and chat paths;
streaming never retries). Kimi uses it for Moonshot's `invalid temperature: only 1 is
allowed for this model` — the mandated value is read from the rejection itself (which
models mandate it is decided server-side, a hard-coded list would drift) and the
substitution is logged as a structured warning. Two more dialect seams serve the
aggregators (LLM-09): `ReasoningFieldName` names the vendor field the reasoning trace is
read from (`reasoning_content` by default, `reasoning` on OpenRouter — the Orkeon metadata
key stays `reasoning_content`), and `usage.cost` becomes the `cost` metadata wherever a
vendor bills in the response — buffered or streamed — with `cost_currency` beside it when the
provider states its vendor's billing currency (`CostCurrency`: `USD` on OpenRouter, whose
credits are dollars; no other provider states one). From there the charge travels as billed:
the chat client adapter carries it onto the `ChatResponse` (`AdditionalProperties`, also on
its streamed fallback), the agent loop reports it on the usage event
(`CostUsageEvent.Cost`, null when the vendor billed nothing — a free model's `0` stays
`0`), the scripting facade does the same for `ctx.llm.*`, and the run's `cost.updated`
relays it with `costSource: "vendor"` ([the run event bus](run-event-bus.md)).
`CostBudgetManager` prices from its registry only a call nobody priced, for its own
budgets; that estimate never reaches the wire. A chunk carrying a root-level `error` after
the HTTP 200 ends a stream the way a pre-stream refusal does — `error` metadata on the chat stream, an
`HttpRequestException` on the token stream — never as a clean completion. All 16 providers
are `IStreamingLlmProvider`s. The per-provider matrix lives in
[the provider comparison](../reference/llm-providers-comparison.md).

### Dialect seams of the OpenAI-compatible base

What differs between the compatible vendors is expressed through a handful of protected
members, not through copies of the payload builder:

| Seam | Default | Overridden by |
|---|---|---|
| `ApplyProviderSpecificOptions` | writes `thinking` / `reasoning_effort` and `response_format` from the declared capabilities | Qwen (`enable_thinking`, `thinking_budget`), OpenRouter (the `reasoning` request object), Together AI (adds `context_length_exceeded_behavior: truncate`) |
| `MaxTokensFieldName` | `max_tokens` | OpenAI (`max_completion_tokens`) |
| `AlwaysEmitTopP` | `top_p` omitted when it equals 1 | Mistral (always written) |
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
- **`RateLimitedLlmProvider`** — routes every call through the `ILlmRateLimiter` (the
  `RateLimiting` settings block). It wraps the provider handed to the scripting engine,
  whose `ctx.llm.*` calls bypass the orchestrator's own throttling; it is not meant as a
  global decorator, or the YAML path would be throttled twice.
- **LLM exchange logging** — `LlmLoggingDelegatingHandler` (`Orkeon.Infrastructure.Logging`)
  captures every HTTP exchange (headers and payload, sanitized by `LogSanitizer`: credential
  headers redacted by name, secrets in bodies by pattern) as JSON Lines plus a structured
  log summary. `services.AddLlmExchangeLogging(logDirectory, options)` injects it into every
  `IHttpClientFactory` client; the `IHttpClientBuilder` overload targets one named client,
  and `AddLlmExchangeFileLogging(logDirectory)` keeps the JSONL capture without the console
  summary. `LlmLoggingOptions`: `MaxBodyLengthChars` (0 = no truncation),
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
