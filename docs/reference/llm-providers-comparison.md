> 🇫🇷 [Version française](../fr/reference/llm-providers-comparison.md)

# LLM Provider Comparison — Orkeon

> Status as of 2026-09-19, derived from the source code (`src/core/Orkeon.Infrastructure/LLMs/`)
> and from each provider's declared `LlmProviderCapabilities`.
> Legend: ✓ supported · ✗ absent · ◐ partial/generic · † not campaigned (declared from the
> vendor's documentation, pending the first real-execution campaign).

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
| **OpenRouter** † | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema (per endpoint) | ✓ budget (`reasoning` object) | ✓ (per model) | ✗ | ◐ auto (+ `cache_write_tokens`) | ✗ (`usage.cost` exposed) | ✓ | ✓ |
| **Mammouth AI** † | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ (undocumented) | ✗ (undocumented) | ✓ (per model) | ✗ | ◐ auto | ✗ | ✓ | ✓ |
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
- **DeepSeek vision**: arrived with `deepseek-v4-flash-vision-exp` (measured 2026-08-30) and
  is native on the Flash tier since V4.1 Flash (2026-09-10): the default `deepseek-flash`
  sees, the experimental companion is retired. Per provider vs per model as everywhere
  (D-03): `deepseek-v4-pro` stays text-only and answers an image with the vendor's own error.
  `deepseek-v4-flash` is a retired model's name the API "temporarily" routes to V4.1 Flash —
  the default moved to the vendor's own id on 2026-09-19.
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
- **OpenRouter** †: the model marketplace (445 models from 60 vendors on 2026-09-18) behind
  one key, integrated documentation-first (LLM-09) — no campaign archived yet. Identifiers
  are `vendor/model` (mandatory prefix), with the `:free` / `:nitro` / `:floor` suffixes and
  the `openrouter/auto` router slug (usable, refused as a default: the served model drifts —
  the `served_model` metadata says who answered). The reasoning trace comes back as
  `reasoning`, never `reasoning_content` (a `ReasoningFieldName` hook on the base); thinking
  travels as the `reasoning` request object (`enabled` / `effort` / `max_tokens`, so the
  Budget declaration is one of the transport — OpenRouter converts effort and budget into
  each other per model); the real charge arrives in `usage.cost` (the `cost` metadata) with
  its `upstream_inference_cost` / `is_byok` / `cache_write_tokens` / `reasoning_tokens`
  breakdown; two constant attribution headers (`HTTP-Referer`, `X-OpenRouter-Title`) name
  Orkeon. `json_schema` is honoured per endpoint and the provider does not send
  `provider.require_parameters`: whether a schema can be silently ignored elsewhere is the
  first campaign's question. The advanced routing body (`provider {…}`, `models[]`,
  `plugins[]`) is not exposed. The `sk-or-v1-` key prefix is documented by secondary sources
  only, so the factory does not infer from it yet.
- **Mammouth AI** †: the French multi-model subscription whose included API credits drive
  Orkeon, integrated documentation-first (LLM-09) — no campaign archived yet. On three
  concordant clues (2026-09-18) the API is a LiteLLM proxy; nothing in the provider depends on
  it. Identifiers are the vendors' own bare strings (`gpt-5.6-sol`, `claude-sonnet-5`,
  `gemini-3.7-flash`), so the provider is reached by host (`api.mammouth.ai`) or by
  `"Provider": "mammouth"` and never inferred from a model name — the same string without a
  base URL keeps going to the vendor. Only `messages`, `model`, `temperature`, `max_tokens`,
  `top_p` and `stream` are documented: `response_format` and thinking stay undeclared
  (structured warning, never a silent drop) until the first campaign measures them — the
  MiniMax rule; vision is declared from the vendor's own `text, image` model list. Prices are
  the vendor's upper bounds (`gemini-3.7-flash` at 1.5 / 7.5 $/M, twice the direct price).
- **Anthropic identity-linked keys**: refuse every request without an `anthropic-workspace-id`
  header (2026-08-30). Set `LlmConfig.WorkspaceId` (CLI: `--workspace-id`); classic keys need
  nothing.
- **Polly** and **API key sanitization**: provided by `HttpLlmProviderBase` → active everywhere.

## API keys: the variable per provider

**A run reads one key and one only**: `Llm:ApiKey` in the settings file, overridden by
`ORKEON_Llm__ApiKey` in the environment. The vendor names below are **not** read by the
engine — they are the convention Orkeon Studio's model-profile editor carries
(`LlmPresets.ProviderCatalogFor`): a profile stores the *name* of the variable and never the
key (`ModelProfile.KeyEnvName`), resolves it at launch, and lays the value over the child
process as `ORKEON_Llm__ApiKey`. Exporting `DEEPSEEK_API_KEY` and expecting a terminal
`orkeon run` to find it is the trap this table exists to close: outside Studio, export
`ORKEON_Llm__ApiKey`.

| Provider | Variable (Studio convention) | Key issued at | Timeout Studio pre-fills |
|---|---|---|---|
| OpenAI | `OPENAI_API_KEY` | `platform.openai.com/api-keys` | engine default (30 s) |
| Anthropic | `ANTHROPIC_API_KEY` | `console.anthropic.com` | engine default |
| DeepSeek | `DEEPSEEK_API_KEY` | `platform.deepseek.com` | **600 s** |
| Mistral AI | `MISTRAL_API_KEY` | `console.mistral.ai` | engine default |
| Google Gemini | `GEMINI_API_KEY` | `aistudio.google.com/apikey` | engine default |
| Grok (x.AI) | `XAI_API_KEY` | `console.x.ai` | engine default |
| MiniMax | `MINIMAX_API_KEY` | `platform.minimax.io` | **600 s** |
| Together AI | `TOGETHER_API_KEY` | `api.together.ai` | engine default |
| Qwen | `DASHSCOPE_API_KEY` | `dashscope.console.aliyun.com` | engine default |
| Kimi (Moonshot) | `MOONSHOT_API_KEY` | `platform.moonshot.ai` | **600 s** |
| HuggingFace | `HF_TOKEN` | `huggingface.co/settings/tokens` | engine default |
| Z.AI (GLM) | `ZAI_API_KEY` | `z.ai/manage-apikey` | **600 s** |
| OpenRouter | `OPENROUTER_API_KEY` | `openrouter.ai/keys` | engine default |
| Mammouth AI | `MAMMOUTH_API_KEY` | `mammouth.ai` — the vendor documents "from the API settings"; the exact page is confirmed with the first key | engine default |
| Ollama · Docker Model Runner | none | — | engine default |
| OpenAI-compatible (`custom`) | `ORKEON_Llm__ApiKey` | — | engine default |
| Azure OpenAI | no card by design: its per-resource endpoint makes it an OpenAI-compatible entry | — | — |

The pre-filled 600 s is not decorative. The four providers whose **default** model reasons
before it answers overrun the engine's 30 s, and the run of 2026-09-20 was lost to two Kimi
timeouts reported as an empty answer (LLM-11). Studio pre-fills the profile's timeout field;
a hand-written settings file needs `Llm:TimeoutSeconds` raised explicitly.

Three names that are **not** this one, and get confused with it:

- `ORKEON_LLM_API_KEY` — the default of `orkeon llm probe -k` and `orkeon llm models -k`.
  Campaign tooling only; a run never reads it.
- `orkeon init --api-key-env <name>` — writes **nothing** into the generated file. The name
  feeds init's own endpoint probe, after which init prints that the runtime reads
  `ORKEON_Llm__ApiKey` natively. The file it produces references no variable at all.
- `ORKEON_<NAME>` — the secret chain of the **tools**, not of the LLM
  (`EnvironmentSecretProvider`, then `Secrets:<NAME>` in the file): `ORKEON_TAVILY_API_KEY`
  for `web_search`, `BRAVE_API_KEY` read as-is for `brave_search`. See the
  [configuration reference](./configuration.md).

## Defaults and newer models — catalogue review of 2026-09-19

Every vendor's own model and pricing pages were read on 2026-09-19, alongside the public
catalogues of the two aggregators (OpenRouter, 447 models; Mammouth, 100). The rule is the
one `LlmProviderDefaultModels` states: a default changes only when the vendor retires the
name; otherwise a newer model is a **candidate** until a campaign has archived an M1 on it.

| Provider | Default (code) | Served on 2026-09-19 | Newer on the vendor's API | Verdict |
|---|---|---|---|---|
| OpenAI | `gpt-5.6-sol` | yes — 4 / 20 $/M through 2026-11-21, the replacement target of both 2026 deprecation waves | `gpt-6-astra` (10 / 50, effort `low`…`max` with no `none`, 2× billing above 272K input tokens) | stays — **campaigned 2026-09-21**: `gpt-6-astra` 10/2 with `temperature: 1` pinned; it has no `reasoning_effort: none`, so function tools on `/v1/chat/completions` are refused (`use /v1/responses`) — not a default until the dialect speaks Responses; `gpt-6-astra-pro` is an aggregator label, not an OpenAI id |
| Anthropic | `claude-sonnet-5` | yes — 2 / 10 $/M made permanent, active until at least 2027-06-30 | `claude-fable-5-1` (2026-09-01, 10 / 50, thinking always on, forced `tool_choice` refused) | stays — **campaigned 2026-09-21**: `claude-fable-5-1` 11/1 twice (the red is M6, the text-fallback protocol; native tools, schema, vision and cache all green) — viable, at five times the price; `claude-opus-5-fast` is a `speed` flag, not an id; `temperature` / `top_p` / `top_k` return 400 on every model from Opus 4.7 on |
| Azure OpenAI | deployment | — | — | nothing to default to |
| Ollama | `llama3.2` | yes — 3B, text only, no thinking, a year old | `qwen3.5:4b`, `gemma4:e4b`, `granite4.2:3b` (tools + thinking, same size class) | stays: a new default means a pull on every machine |
| Together AI | `meta-llama/Llama-3.3-70B-Instruct-Turbo` | yes — 1.04 / 1.04 $/M, not scheduled | `zai-org/GLM-5.3-Flash` (1M, tools + JSON, 0.15 / 0.50), `Qwen/Qwen3.5-9B`, `deepseek-ai/DeepSeek-V4.1-Flash`; Llama 4 left serverless | candidate GLM-5.3-Flash, a seventh of the price — **campaigned 2026-09-21**: 11/0/1, it reads the image (the first serverless vision model on Together, now the kit's vision companion) and its cache is read (8000 tokens, 0.99); `Qwen/Qwen3.5-9B` 10/0/2. Both ready; the bump is the owner's call |
| DeepSeek | `deepseek-flash` (was `deepseek-v4-flash`) | yes — V4.1 Flash, 0.30 / 1.20 $/M peak, native vision | it is the newest | **changed 2026-09-19**: the old name is a retired model "temporarily" routed here; **replayed 2026-09-21**: 12/12 twice — M2 and M9, red for five campaigns on `deepseek-v4-flash`, are green on `deepseek-flash` |
| Kimi | `kimi-k2.6` | yes — 0.95 / 4.00 $/M, no retirement date | `kimi-k3` (July 2026, 1M, 3 / 15, fixed sampling, no `thinking` field, always reasons) | stays — **campaigned 2026-09-21**: `kimi-k3` 12/12 then 11/1 (M2 on the flattened shape), reasoning trace, cache 0.97, bare JSON where k2.6 now fences its `json_object` — ready, at three times the price; `kimi-k2.5` and `moonshot-v1-*` retired 2026-08-31; docs now on `platform.kimi.ai` |
| Qwen | `qwen3.7-plus` | yes — still the Plus tier, one of the three recommended models | `qwen3.8-max` (= `qwen3.8-max-0902`), `qwen3.8-flash`; no `qwen3.8-plus` | stays — **campaigned 2026-09-21**: `qwen3.8-max` 12/12, `qwen3.8-flash` 11/1 (M2), both ready, and `qwen3.7-plus` is still 12/12; the 3.8 generation adds `preserve_thinking` |
| Mistral AI | `mistral-medium-2604` | yes — 1.50 / 7.50 $/M, not deprecated | nothing for chat since April (OCR 4.1 only) | stays; `devstral-*` / magistral ids sit in the deprecated table |
| HuggingFace | `meta-llama/Llama-3.1-8B-Instruct` | yes — but tools on one of its four routed providers, and `:fastest` may pick another | `Qwen/Qwen3.5-9B` (tools on three providers, from 0.10 / 0.15), `zai-org/GLM-5.3-Flash`, `deepseek-ai/DeepSeek-V4.1-Flash` | candidate Qwen3.5-9B — **campaigned 2026-09-21**: 10/0/2, it sees and its tools hold; its first pass returned an empty M1 after 4113 tokens of thinking on the router's 4096 fallback — pin `Llm:MaxTokens` (or turn thinking off) before making it a default. The routing still explains the default's moving reds (M2 and M5 both red on the rc.4 pass) |
| Z.AI | `glm-5.2` | yes — still under "Latest Models", 1.40 / 4.40 $/M | `glm-5.3` (2026-08-18), `glm-5.3-flash` (0.15 / 0.50, image + video), `glm-5.3-flashx` | stays — **campaigned 2026-09-21**: `glm-5.3-flash` 12/12 twice (it sees, where 5.2 and 5.3 answer `1210`; cache read) — the cleanest candidate in the fleet, at a ninth of the price; `glm-5.3` 10/2 (M2, M9) brings nothing over 5.2. The `Toggle` declaration still needs a per-model guard before a bump |
| Google Gemini | `gemini-3.7-flash` | yes — "previous generation", no shutdown date, 0.75 / 3.75 $/M until 2026-12-31 then 1.50 / 7.50 | `gemini-3.8-flash` (GA 2026-09-02, same price, `minimal` thinking rejected) | first candidate for a bump — **campaigned 2026-09-21**: `gemini-3.8-flash` 11/0/1 twice, identical to 3.7 mode for mode on the direct transport; OpenRouter and Mammouth not played (no key) |
| Grok (x.AI) | `grok-4.6` | yes — recommended, 2 / 6 $/M below 200K prompt tokens, 4 / 12 above | none | stays |
| MiniMax | `MiniMax-M2` | yes — listed as legacy, no retirement date | `MiniMax-M3` (2026-06-01, 1M, image + video input, same 0.30 / 1.20) | stays — **campaigned 2026-09-21**: `MiniMax-M3` 9/1/2 then 8/1/3 — it reads the image (now the kit's vision companion), M2 red like M2, the inline `<think>` block split out as on M2, 128 cached tokens once; the reasoning-format question is answered |
| OpenRouter | `google/gemini-3.7-flash` | yes | `google/gemini-3.8-flash` (2026-09-02, same price) | follows the direct default — not campaigned on 2026-09-21 (no key) |
| Mammouth AI | `gemini-3.7-flash` | yes | `gemini-3.8-flash` | follows the direct default — not campaigned on 2026-09-21 (no key) |

`ModelPricingRegistry` follows the same pages: the GPT-5.6 family and `gpt-6-astra`, Sonnet 5
at 2 / 10, Fable 5.1 / Fable 5 and Haiku 4.5.

## Output caps — the documented maximum per model (LLM-10)

A request carries an output cap (`max_tokens`, `max_completion_tokens` at OpenAI, `num_predict`
at Ollama). The engine used to send **4096 for every model**; a reasoning model spends that
budget thinking and answers empty, the tool-free retry that follows narrates the deliverable
instead of writing it, and the run goes green with no file (owner recette 2026-09-19,
`kimi-k3`). Since LLM-10 the cap left unpinned is **the model's documented maximum**, from the
`LlmModelOutputLimits` catalogue in `Orkeon.Constants.Llm`; a pinned value (`Llm:MaxTokens`, a
Studio profile, a crew's `max_tokens`) always wins; a model the catalogue does not know keeps
the 4096 fallback. Only documented figures go in, each read on 2026-09-19:

| Provider | Model | Cap sent | Source | Note |
|---|---|---|---|---|
| OpenAI (and Azure deployments of the same ids) | `gpt-5.6-sol`, `gpt-6-astra` | 128 000 | developers.openai.com/api/docs/models | reasoning counts inside the cap |
| Anthropic | `claude-sonnet-5`, `claude-opus-5`, `claude-fable-5-1` (dated variants follow the family) | 128 000 | platform.claude.com/docs/en/about-claude/models/overview | thinking counts inside `max_tokens`; the field is required, an unknown Claude gets 4096 |
| Gemini | `gemini-3.7-flash`, `gemini-3.8-flash` (also under `google/…` on OpenRouter and bare on Mammouth) | 65 536 | ai.google.dev/gemini-api/docs/models | includes thought tokens; 65 537 is a 400 |
| DeepSeek | `deepseek-flash` (and the retired `deepseek-v4-flash*` names it routes) | 393 216 | api-docs.deepseek.com/api/create-chat-completion | "1 to 384K"; 384 000 on Mammouth |
| Kimi | `kimi-k3` | 131 072 | platform.kimi.ai/docs/guide/kimi-k3-quickstart | the vendor's own `max_completion_tokens` default; real bound 1M − prompt |
| Kimi | `kimi-k2.6` | 131 072 | platform.kimi.ai/docs/guide/troubleshooting | **a pin, not a documented cap**: the bound is 256K − prompt; a rejection drops the field on the retry |
| Qwen | `qwen3.7-plus`, `qwen3.8-flash`, `qwen3.8-max` | 131 072 | alibabacloud.com/help/en/model-studio | the chain of thought has its own `thinking_budget`; 65 500 on Mammouth |
| Z.AI | `glm-5.2`, `glm-5.3`, `glm-5.3-flash` | 131 072 | docs.z.ai/guides/overview/concept-param | schema maximum; default 65 536 |
| Z.AI | `glm-4.6v-flash` | 32 768 | docs.z.ai/guides/vlm/glm-4.6v | — |
| MiniMax | `MiniMax-M2` | 131 072 | platform.minimax.io/docs/guides/models-intro | "128k (including CoT)"; **M3 is absent** — the vendor publishes no output figure (512k appears only as a benchmark setting; 512 000 on Mammouth) |
| xAI | `grok-4.6` | 128 000 | docs.x.ai/developers/rest-api-reference | no per-model cap; the vendor's own default when unset, reasoning excluded |
| Mistral | `mistral-medium-2604` (`mistral-medium-3-5`) | **none** | docs.mistral.ai/api/endpoint/chat | no output cap, only "prompt + max_tokens ≤ context": the field is left out and the model writes to its window |
| Together | any | 4096 (fallback) | docs.together.ai/docs/serverless-models | no per-model output cap, only prompt + `max_tokens` ≤ window. The window itself was the cap from 2026-09-19 to 2026-09-21, on the assumption that `context_length_exceeded_behavior: truncate` clamps it to window − prompt; the campaign of 2026-09-21 measured that two of three serverless engines refuse regardless (`max_new_tokens`, `context_length_exceeded`) and the third clamps on the buffered path only. The fallback holds, the flag is still sent, and the retry net drops the cap on those wordings; omitted, the field means 2048 (`finish_reason: length`) |
| Ollama | any | **none** (`num_predict` left out) | docs.ollama.com/modelfile | `-1, infinite generation` is the runtime's default: a local model writes to its context |
| HuggingFace, Docker Model Runner, anything else | — | 4096 (fallback) | — | the router's bound is the routed provider's context, which differs per route; pin `Llm:MaxTokens` |

Two things the catalogue is honest about. Where the vendor bounds the cap by `window − prompt`
(Kimi, Together, Mistral), a fixed value near the window fails on any real prompt — so those
rows are either a pin, the fallback, or nothing. And a cap the endpoint refuses (Qwen "Range of
max_tokens", Kimi "prompt tokens + max_tokens exceeds", Gemini `maxOutputTokens`, DeepSeek's
422, Together's engines with `max_new_tokens` / `context_length_exceeded`) is **retried once
without the field** when it came from the
catalogue, with a warning naming the model — a pinned value is the user's, and its rejection
surfaces unchanged. The Studio profile editor reads the same catalogue: the hint under the
« Maximum response » field says what an empty field means for the chosen model, and invites a
pin when the model is unknown.

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
| OpenAI | `gpt-6-astra` | `temperature` | `1` | `'temperature' does not support 0 with this model. Only the default (1) value is supported.` | 2026-09-21 |
| OpenAI | `gpt-6-astra` | `reasoning_effort` | **no `"none"` exists** (`low`, `medium`, `high`, `xhigh`) — the Sol workaround is impossible, so function tools stay refused on `/v1/chat/completions` until the dialect speaks `/v1/responses` | `'reasoning_effort' does not support 'none' with this model. Supported values are: 'low', 'medium', 'high', and 'xhigh'.` — and without it, `Function tools with reasoning_effort are not supported for gpt-6-astra in /v1/chat/completions. To use function tools, use /v1/responses` | 2026-09-21 |
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
; run a campaign with
`orkeon llm probe --provider <name> --archive <dir>`.
