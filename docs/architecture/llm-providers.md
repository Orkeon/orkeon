> 🇫🇷 [Version française](../fr/architecture/llm-providers.md)

# LLM Providers

`HttpLlmProviderBase` (`Orkeon.Infrastructure.LLMs.Base`) provides the abstract base for all LLM providers. It integrates HTTP handling (`IHttpClientFactory`), Polly resilience policies (retry, circuit breaker, timeout), and JSON serialization.

Implemented providers:

| Provider | Class | Namespace |
|----------|-------|-----------|
| OpenAI | `OpenAIProvider` (via `OpenAICompatibleProviderBase`) | `Orkeon.Infrastructure.LLMs` |
| Anthropic | `AnthropicLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Azure OpenAI | `AzureOpenAILlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Groq | `GroqLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Ollama | `OllamaLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Together AI | `TogetherAiLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| DeepSeek | `DeepSeekLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| HuggingFace | `HuggingFaceLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Kimi | `KimiLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Qwen | `QwenLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Mistral AI | `MistralLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Z.AI (Zhipu GLM) | `ZaiLlmProvider` | `Orkeon.Infrastructure.LLMs` |

Generic adapters (`ChatClientToLlmProviderAdapter`, `LlmProviderToChatClientAdapter`) are available in `Orkeon.Infrastructure.LLMs.Adapters` to integrate other providers compatible with the `IChatClient` interface.

`LlmProviderFactory` (`Orkeon.Infrastructure.LLMs`) automatically resolves the provider from the `LlmConfig` (detection by URL, model name, or API key).

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
