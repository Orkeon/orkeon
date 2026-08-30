> 🇫🇷 [Version française](../fr/architecture/llm-providers.md)

# LLM Providers

`HttpLlmProviderBase` (`Orkeon.Infrastructure.LLMs.Base`) provides the abstract base for all LLM providers. It integrates HTTP handling (`IHttpClientFactory`), Polly resilience policies (retry, circuit breaker, timeout), and JSON serialization.

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

Generic adapters (`ChatClientToLlmProviderAdapter`, `LlmProviderToChatClientAdapter`, `ChatClientToBasicLlmProviderAdapter`) are available in `Orkeon.Infrastructure.LLMs.Adapters` to integrate other providers compatible with the `IChatClient` interface.

`LlmProviderFactory` (`Orkeon.Infrastructure.LLMs`) automatically resolves the provider from the `LlmConfig` (detection by URL, model name, or API key).

## Declared capabilities

Every provider declares a `LlmProviderCapabilities` value object (Domain, exposed on
`ILlmProvider`; the base defaults to `LlmProviderCapabilities.Unknown`): `ResponseFormat`
(`None`/`JsonObject`/`JsonSchema`), `Thinking` (`None`/`EffortOnly`/`Toggle`/`Budget`),
`Vision`, `ExplicitPromptCaching`, `RequiresJsonKeywordInPrompt`, `ReplaysReasoningContent`.
`OpenAICompatibleProviderBase` translates the declaration into the OpenAI dialect once
(vision payloads, `response_format`, thinking, the `CapabilityMismatchHint` diagnostics);
Anthropic, Ollama and Qwen override the hook for their own dialects. An option a provider
cannot honour produces a structured warning — never a silent drop. The mirror-image case —
a constraint only the **server** can state — has its own seam:
`OpenAICompatibleProviderBase.TryAdaptRejectedPayload` gives a provider one chance to adapt
a payload the API rejected with a 4xx and re-send it once (generate and chat paths;
streaming never retries). Kimi uses it for Moonshot's `invalid temperature: only 1 is
allowed for this model` — the mandated value is read from the rejection itself (which
models mandate it is decided server-side, a hard-coded list would drift) and the
substitution is logged as a structured warning. All 14 providers are
`IStreamingLlmProvider`s, and `RateLimitedLlmProvider` decorates any of them. The
per-provider matrix lives in [the provider comparison](../reference/llm-providers-comparison.md).

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

> **See also**: [Memory system](./memory-system.md) · [Security](./security.md) · [Back to index](../INDEX.md)
