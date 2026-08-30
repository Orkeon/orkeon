> 🇫🇷 [Version française](../fr/reference/llm-providers-comparison.md)

# LLM Provider Comparison — Orkeon

> Status as of 2026-08-30, derived from the source code (`src/core/Orkeon.Infrastructure/LLMs/`)
> and from each provider's declared `LlmProviderCapabilities`.
> Legend: ✓ supported · ✗ absent · ◐ partial/generic.

| Provider | Base class | SSE streaming | Native tool calling | Multi-turn chat (tool roles) | System message | top_p / stop | GBNF grammar | response_format | thinking | Vision | reasoning_content round-trip | Prompt cache | Timing metrics | Polly resilience | API key sanitization |
|---|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| **OpenAI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Azure OpenAI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Anthropic** | HttpLlmProviderBase | ✓ native | ✓ | ✓ | ✓ (native, separate) | ✓ | ✗ | ✓ schema | ✓ toggle | ✓ | ✗ | ✓ explicit | ✗ | ✓ | ✓ |
| **DeepSeek** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ object | ✓ toggle | ✓ | ✓ | ✓ metrics | ✗ | ✓ | ✓ |
| **Z.AI (GLM)** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ object | ✓ toggle | ✓ | ✗ | ✓ metrics | ✗ | ✓ | ✓ |
| **Together AI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema | ✗ | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Mistral AI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Qwen** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ object | ✓ budget | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Kimi / Moonshot** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ object | ✓ toggle | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Google Gemini** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Grok (x.AI)** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **MiniMax** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ (accepted but non-binding — measured) | ✗ (always-on inline, split out) | ✓ | ✓ | ◐ auto | ✗ | ✓ | ✓ |
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
  `budget_tokens` with a 400 on the current generation). On the wire, Anthropic's toggle is
  written as `thinking: {type: adaptive|disabled}` — a payload detail, not a capability level.
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
- **OpenAI `max_completion_tokens`**: OpenAI retired `max_tokens` on its current models (the
  2026-08-30 campaign failed ten modes on that one field), so the OpenAI dialect writes
  `max_completion_tokens` — accepted by the older generations too (verified on `gpt-4o-mini`
  the same day). The compatible vendors keep `max_tokens`: the retirement is OpenAI's alone.
- **Gemini `response_format`**: undocumented on the compat surface when first audited
  (2026-08-18) and undeclared then; measured live on 2026-08-30, the surface accepts
  `json_object` and `json_schema` and enforces the schema server-side, so the provider now
  declares `JsonSchema`.
- **DeepSeek vision**: arrived with `deepseek-v4-flash-vision-exp` (measured 2026-08-30). Per
  provider vs per model as everywhere (D-03): the default `deepseek-v4-flash` stays text-only
  and answers an image with the vendor's own error.
- **MiniMax**: campaign-backed since 2026-08-30 (7/2/3, same day it was integrated). The
  reasoning arrives INLINE — every reply opens with a `<think>` block inside `content`, no
  separate field — and the dialect splits it out to `reasoning_content`, re-inlining it on
  replay as the vendor documents. `response_format` is accepted but NON-BINDING (a schema is
  ignored, `json_object` arrives fenced in markdown): the None declaration is a measurement
  (raw calls, 2026-08-30, recorded in the campaign catalog's MiniMax note — the archived M8
  shows ➖ because the None declaration keeps the option from ever being sent).
  Vision is per model (D-03): `MiniMax-M2` answers "I'm unable to view the image", and the
  VL family does not appear on the platform's `/models` — no companion declarable yet.
- **Grok (x.AI)**: every declared capability is a live measurement — a full 12-mode campaign
  passed against `api.x.ai` through the generic OpenAI dialect before the provider class
  existed (2026-08-30, archived under `llmproviders-test/custom-endpoints/`). Keys carry the
  `xai-` prefix, which the factory infers.
- **Anthropic identity-linked keys**: refuse every request without an `anthropic-workspace-id`
  header (2026-08-30). Set `LlmConfig.WorkspaceId` (CLI: `--workspace-id`); classic keys need
  nothing.
- **Polly** and **API key sanitization**: provided by `HttpLlmProviderBase` → active everywhere.

## Per-model mandatory parameter values

Some models refuse a request unless a parameter carries one specific value. This is a
different animal from the per-model capability gaps above (D-03 — a model lacking thinking or
vision): here the capability exists, but the model dictates the value, and the vendor answers
anything else with a 400. **The constraint is per model, never per provider** — the same
vendor ships models with opposite demands, so a provider-level pin is one campaign away from
breaking the sibling model. Every row below was measured live; nothing is inferred.

| Provider | Model | Parameter | Mandatory value | Vendor's own words | Measured |
|---|---|---|---|---|---|
| Kimi | `kimi-k2.6` | `temperature` | `1` | `invalid temperature: only 1 is allowed for this model` | 2026-08-03 |
| OpenAI | `gpt-5.6-sol` | `temperature` | `1` | `'temperature' does not support 0 with this model. Only the default (1) value is supported.` | 2026-08-30 |
| OpenAI | `gpt-5.6-sol` | `reasoning_effort` | `"none"` when the request carries function tools on `/v1/chat/completions` | `Function tools with reasoning_effort are not supported for gpt-5.6-sol in /v1/chat/completions. To use function tools, use /v1/responses or set reasoning_effort to 'none'.` | 2026-08-30 |
| Anthropic | `claude-sonnet-5` | `temperature` | `1`, or omit the field | `` `temperature` is deprecated for this model.`` (1 and omission pass; 0 and 0.7 do not) | 2026-08-30 |
| Mistral | `mistral-medium-2604` | `reasoning_effort` | `high` or `none` only | `reasoning_effort low is not supported for this model, supported values: [<ReasoningEffort.high: 'high'>, <ReasoningEffort.none: 'none'>]` | 2026-08-30 |
| Mistral | `mistral-medium-2604` | `top_p` | explicit `1` when `temperature` is 0 and reasoning is on (omission is NOT 1 there) | `top_p must be 1 when using greedy sampling.` | 2026-08-30 |

The counter-example that makes the registry per-model: `gpt-4o-mini` — same provider as
`gpt-5.6-sol` — rejects `reasoning_effort` outright (`Unrecognized request argument supplied:
reasoning_effort`, measured the same day). A provider-wide "always send none" would break it.

Machine-readable twin: `llmproviders-test/lib/catalog.json`, key `requiredParams` (per
provider, keyed by model id) — the campaign scripts resolve it automatically and the report
header prints the values actually used. In application code, express the same values through
`LlmConfig` (`Temperature`, `Thinking = { Effort = "none" }`); on the wrong value the vendor's
error comes back attributed (`CapabilityMismatchHint` names the thinking knob on the
tools-with-reasoning refusal).

## What this table does not prove

Every row is backed by unit tests asserting the emitted payload — against a **mocked HTTP
handler**. A mock proves Orkeon sends what we believe it sends; it does not prove the vendor
accepts it. Real-execution evidence is tracked separately in the test matrix journal
(`backstage/features/drafts/LLM-PROVIDERS-TEST-MATRIX.md` §7); run a campaign with
`orkeon llm probe --provider <name> --archive <dir>`.
