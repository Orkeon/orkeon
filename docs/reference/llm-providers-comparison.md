> 🇫🇷 [Version française](../fr/reference/llm-providers-comparison.md)

# LLM Provider Comparison — Orkeon

> Status as of 2026-07-27, derived from the source code (`src/core/Orkeon.Infrastructure/LLMs/`)
> and from each provider's declared `LlmProviderCapabilities`.
> Legend: ✓ supported · ✗ absent · ◐ partial/generic.

| Provider | Base class | SSE streaming | Native tool calling | Multi-turn chat (tool roles) | System message | top_p / stop | GBNF grammar | response_format | thinking | Vision | reasoning_content round-trip | Prompt cache | Timing metrics | Polly resilience | API key sanitization |
|---|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| **OpenAI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Azure OpenAI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Anthropic** | HttpLlmProviderBase | ✓ native | ✓ | ✓ | ✓ (native, separate) | ✓ | ✗ | ✓ schema | ✓ adaptive | ✓ | ✗ | ✓ explicit | ✗ | ✓ | ✓ |
| **DeepSeek** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ object | ✓ toggle | ✗ | ✓ | ✓ metrics | ✗ | ✓ | ✓ |
| **Z.AI (GLM)** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ object | ✓ toggle | ✓ | ✗ | ✓ metrics | ✗ | ✓ | ✓ |
| **Groq** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✓ | ✓ | ✓ |
| **Together AI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema | ✗ | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Mistral AI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Qwen** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ object | ✓ budget | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Kimi / Moonshot** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ object | ✓ toggle | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Google Gemini** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ (undeclared — compat surface undocumented) | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **HuggingFace** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ object | ✗ | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Ollama** | HttpLlmProviderBase | ✓ | ✓ (`/api/chat`) | ✓ (`/api/chat`) | ✓ (prepend) | ✗ | ✓ | ✓ schema | ✓ toggle | ✓ (`images`) | ✗ | ✗ | ✗ | ✓ | ✓ |

## How to read the capability columns

`response_format`, `thinking` and `Vision` are not hand-maintained here: each provider declares
an `LlmProviderCapabilities` value object, and `OpenAICompatibleProviderBase` translates it into
the OpenAI dialect once. Anthropic, Ollama and Qwen override the hook because their APIs speak
their own dialect.

- **`response_format`** — `object` means the API guarantees well-formed JSON; `schema` means it
  validates against a JSON Schema server-side. A schema sent to an `object`-only provider is
  **downgraded with a warning**, never in silence. **Anthropic is schema-only**: it has no
  equivalent of `json_object`, so a schema-less JSON request there is reported rather than sent.
- **`thinking`** — `effort` accepts a level hint only; `toggle` can also switch reasoning on and
  off; `budget` additionally accepts an explicit token budget (Qwen only — Anthropic rejects
  `budget_tokens` with a 400 on the current generation).
- **Anything a provider does not support is reported.** An option declared in YAML on a provider
  that cannot honour it produces an actionable warning naming the option, the provider and the
  remedy. This was the actual defect the 2026-07-27 audit found: not the missing wiring, but its
  invisibility.

## Notes

- **◐ prompt cache (auto)**: the vendor caches prompt prefixes implicitly and reports the hit;
  `OpenAICompatibleProviderBase.ParseSuccessResponse` reads `prompt_cache_hit/miss_tokens` and the
  OpenAI-standard `prompt_tokens_details.cached_tokens` generically. **✓ explicit** (Anthropic)
  means the cache does nothing until a `cache_control` breakpoint is placed — opt in with
  `LlmCacheConfig` / the YAML `cache:` block.
- **Anthropic SSE**: `ChatStreamingAsync` parses the Messages API event stream natively since
  LLM-05; it used to fall back to a buffered emulation, so no token arrived early.
- **Ollama tool calling**: goes through `/api/chat` when the conversation declares tools, replays
  tool calls, or carries an image; everything else keeps `/api/generate` (NDJSON streaming, GBNF).
  The text-fallback protocol remains in charge for models without tool support.
- **Azure OpenAI**: two API shapes — the dated deployment URL (default) and the v1 GA surface
  (`api_version: v1`), which is the only path to the Responses API and to the non-OpenAI models
  Azure resells.
- **HuggingFace**: model identifiers accept a routing suffix (`:fastest` / `:cheapest` /
  `:preferred` / `:<partner>`) — the only cost and latency lever on Inference Providers.
- **top_p / stop**: Ollama only exposes `temperature` + `num_predict` (= max_tokens).
- **Polly** and **API key sanitization**: provided by `HttpLlmProviderBase` → active everywhere.

## What this table does not prove

Every row is backed by unit tests asserting the emitted payload — against a **mocked HTTP
handler**. A mock proves Orkeon sends what we believe it sends; it does not prove the vendor
accepts it. Real-execution evidence is tracked separately in the test matrix journal
(`backstage/features/drafts/LLM-PROVIDERS-TEST-MATRIX.md` §7); run a campaign with
`orkeon llm probe --provider <name> --archive <dir>`.
