> 🇫🇷 [Version française](../fr/arkeon/llm-providers-comparatif.md)

# LLM Provider Comparison — Orkeon

> Status as of 2026-06-12, derived from the source code (`src/core/Orkeon.Infrastructure/LLMs/`).
> Legend: ✓ supported · ✗ absent · ◐ partial/generic.

| Provider | Base class | SSE streaming | Native tool calling | Multi-turn chat (tool roles) | System message | top_p / stop | GBNF grammar | response_format JSON | thinking / reasoning_effort | reasoning_content round-trip | Prompt cache metrics | Timing metrics | Polly resilience | API key sanitization |
|---|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| **DeepSeek** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ | ✓ | ✓ |
| **OpenAI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ | ✗ | ✗ | ◐ | ✗ | ✓ | ✓ |
| **Groq** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ | ✗ | ✗ | ◐ | ✓ | ✓ | ✓ |
| **Together AI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ | ✗ | ✗ | ◐ | ✗ | ✓ | ✓ |
| **Qwen** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ | ✗ | ✗ | ◐ | ✗ | ✓ | ✓ |
| **Kimi / Moonshot** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ | ✗ | ✗ | ◐ | ✗ | ✓ | ✓ |
| **Mistral AI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ | ✗ | ✗ | ◐ | ✗ | ✓ | ✓ |
| **HuggingFace** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ | ✗ | ✗ | ◐ | ✗ | ✓ | ✓ |
| **Anthropic** | HttpLlmProviderBase | ✓ | ✓ | ✓ | ✓ (native, separate) | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✓ | ✓ |
| **Ollama** | HttpLlmProviderBase | ✓ | ✗ | ◐ (concat) | ✓ (prepend) | ✗ | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | ✓ | ✓ |
| **Azure OpenAI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ | ✗ | ✗ | ◐ | ✗ | ✓ | ✓ |

## Notes

- **◐ prompt cache** (OpenAI-compat): `OpenAICompatibleProviderBase.ParseSuccessResponse` generically parses `prompt_cache_hit_tokens` / `prompt_cache_miss_tokens`, but only DeepSeek actually emits them on the API side.
- **◐ multi-turn chat** (Ollama): no `ChatAsync` override → fallback to the prompt concatenation of `HttpLlmProviderBase`, without `tool` / `tool_call_id` roles.
- **Azure OpenAI** (R10.7): now built on `OpenAICompatibleProviderBase` with the native OpenAI tool calling strategy wired by `LlmProviderFactory`; the deployment-based endpoint (`/openai/deployments/{model}/chat/completions?api-version=…`) and `api-key` authentication are preserved.
- **top_p / stop**: Ollama only exposes `temperature` + `num_predict` (= max_tokens).
- **GBNF grammar**: injected in the `BuildRequestPayload` (prompt) path, for llama.cpp/vLLM-compatible backends and Ollama.
- **Polly** and **API key sanitization**: provided by `HttpLlmProviderBase` → active on all providers.

## DeepSeek specifics (the most complete)

The only provider to wire up:

- the `reasoning_content` round-trip (re-emitted verbatim on every turn, mandatory in thinking mode otherwise HTTP 400);
- the `thinking` block (`enabled`/`disabled`) + `reasoning_effort`;
- `response_format: json_object` with a logged guardrail if the prompt does not contain "json";
- the context caching metrics (`prompt_cache_hit/miss_tokens`, billed at 1/10 of the input price).

Test coverage: `DeepSeekCacheMetricsTests`, `DeepSeekReasoningRoundTripTests`, `DeepSeekResponseFormatPayloadTests`, `DeepSeekResponseFormatStreamingTests`, `DeepSeekThinkingPayloadTests`.
