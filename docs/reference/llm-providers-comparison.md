> 🇫🇷 [Version française](../fr/reference/llm-providers-comparison.md)

# LLM Provider Comparison — Orkeon

> Derived from the source code (`src/core/Orkeon.Infrastructure/LLMs/`) and from each provider's
> declared `LlmProviderCapabilities`; campaign figures carry their own date.
> Legend: ✓ supported · ✗ absent · ◐ partial/generic. Every provider but Azure OpenAI has an
> archived real-execution campaign; OpenRouter and Mammouth AI since 2026-10-07.

| Provider | Base class | SSE streaming | Native tool calling | Multi-turn chat (tool roles) | System message | top_p / stop | GBNF grammar | response_format | thinking | Vision | reasoning_content round-trip | Prompt cache | Timing metrics | Polly resilience | API key sanitization |
|---|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| **OpenAI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Azure OpenAI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Anthropic** | HttpLlmProviderBase | ✓ native | ✓ | ✓ | ✓ (native, separate) | ✓ | ✗ | ✓ schema | ✓ toggle | ✓ | ✗ | ✓ explicit | ✗ | ✓ | ✓ |
| **DeepSeek** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ object | ✓ toggle | ✓ | ✓ | ✓ metrics | ✗ | ✓ | ✓ |
| **Z.AI (GLM)** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ object | ✓ toggle | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Together AI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ schema | ✗ | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Mistral AI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Qwen** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ object | ✓ budget | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Kimi / Moonshot** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ object | ✓ toggle | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Google Gemini** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Grok (x.AI)** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **MiniMax** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✗ (accepted but non-binding — measured) | ✗ (always-on inline, split out) | ✓ | ✓ | ◐ auto | ✗ | ✓ | ✓ |
| **HuggingFace** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ object | ✗ | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **OpenRouter** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ schema (per endpoint) | ✓ budget (`reasoning` object) | ✓ (per model) | ✗ | ◐ auto (+ `cache_write_tokens`) | ✗ (`usage.cost` exposed) | ✓ | ✓ |
| **Mammouth AI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✗ (accepted but non-binding — measured) | ✗ (`reasoning_effort` accepted, not declared yet) | ✓ (per model) | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Ollama** | HttpLlmProviderBase | ✓ | ✓ (`/api/chat`) | ✓ (`/api/chat`) | ✓ (prepend) | ✓ (`options`) | ◐ `Llm:Grammar` | ✓ schema | ✓ toggle | ✓ (`images`) | ✗ | ✗ | ◐ (`total_duration`, `eval_duration` on `/api/generate`) | ✓ | ✓ |

## How to read the capability columns

`response_format`, `thinking` and `Vision` are not hand-maintained here: each provider declares
an `LlmProviderCapabilities` value object, and `OpenAICompatibleProviderBase` translates it into
the OpenAI dialect once. Anthropic and Ollama write their own dialect (they do not extend the
OpenAI-compatible base); Qwen and OpenRouter override the thinking translation for DashScope's
fields and OpenRouter's `reasoning` object. The [response format guide](../guides/llm-response-format.md)
details the `response_format` surfaces.

- **`response_format`** — `object` means the API guarantees well-formed JSON; `schema` means it
  validates against a JSON Schema server-side. A schema sent to an `object`-only provider is
  **downgraded with a warning**, never in silence. **Anthropic is schema-only**: it has no
  equivalent of `json_object`, so a schema-less JSON request there is reported rather than sent.
- **`thinking`** — `effort` accepts a level hint only; `toggle` can also switch reasoning on and
  off; `budget` additionally accepts an explicit token budget (Qwen and OpenRouter only — Anthropic rejects
  `budget_tokens` with a 400 on the current generation). On the wire, Anthropic's toggle is
  written as `thinking: {type: adaptive|disabled}` — a payload detail, not a capability level.
- **Anything a provider does not support is reported.** An option declared in YAML on a provider
  that cannot honour it produces an actionable warning naming the option, the provider and the
  remedy.

## Notes

- **◐ prompt cache (auto)**: the vendor caches prompt prefixes implicitly and reports the hit;
  `OpenAICompatibleProviderBase.ParseSuccessResponse` reads `prompt_cache_hit/miss_tokens` and the
  OpenAI-standard `prompt_tokens_details.cached_tokens` generically. **✓ explicit** (Anthropic)
  means the cache does nothing until a `cache_control` breakpoint is placed — opt in with
  `LlmCacheConfig` / the YAML `cache:` block of an agent or of a task (`llm_override:`). **✓ metrics** (DeepSeek) adds the vendor's own
  `prompt_cache_hit_tokens` / `prompt_cache_miss_tokens` to the response metadata.
- **Anthropic SSE**: `ChatStreamingAsync` parses the Messages API event stream natively: tokens
  arrive as they are produced.
- **Ollama tool calling**: goes through `/api/chat` when the conversation declares tools, replays
  tool calls, or carries a non-text part; everything else keeps `/api/generate`. An audio or file
  part is refused there with `NotSupportedException`, as on OpenAI and Anthropic. The
  text-fallback protocol remains in charge for models without tool support.
- **Azure OpenAI**: two API shapes — the dated deployment URL (default) and the v1 GA surface
  (`api_version: v1`), which is the only path to the Responses API and to the non-OpenAI models
  Azure resells.
- **HuggingFace**: model identifiers accept a routing suffix (`:fastest` / `:cheapest` /
  `:preferred` / `:<partner>`) — the only cost and latency lever on Inference Providers.
- **top_p / stop**: written only when set, whatever the value — `temperature` too; nothing set,
  nothing written and the model applies its own default. Mistral writes `top_p: 1` when
  nothing sets one. Ollama writes them in its `options`, with `seed`. No dialect writes
  `frequency_penalty` or `presence_penalty`, and none but Ollama writes `seed`: setting one
  produces the structured warning naming it (event id `110`), never a silent drop.
- **GBNF grammar**: no vendor API documents a `grammar` field, so no provider declares the
  capability (`LlmProviderCapabilities.GbnfGrammar`). **◐ `Llm:Grammar`** means the field is
  written — on both of Ollama's endpoints, and by both payload builders of the OpenAI-compatible
  base — only when the settings say the endpoint behind `Llm:BaseUrl` takes it
  (`"Llm": { "Grammar": true }`, a llama.cpp-compatible server such as Docker Model Runner or
  `llama-server`). Otherwise a grammar is dropped with a structured warning naming that key.
  Anthropic's Messages API has no such field, whatever the key says. A `structured_output`
  deliverable sends its schema as a non-strict `json_schema` response format to a provider that
  declares **✓ schema** (see [LLM response format](../guides/llm-response-format.md)).
- **OpenAI `max_completion_tokens`**: OpenAI retired `max_tokens` on its current models (the
  2026-08-30 campaign failed ten modes on that one field), so the OpenAI dialect writes
  `max_completion_tokens` — accepted by the older generations too (verified on `gpt-4o-mini`
  the same day). The compatible vendors keep `max_tokens`: the retirement is OpenAI's alone.
- **Gemini `response_format`**: undocumented on the compat surface when first audited
  (2026-08-18) and undeclared then; measured live on 2026-08-30, the surface accepts
  `json_object` and `json_schema` and enforces the schema server-side; the provider
  declares `JsonSchema`.
- **DeepSeek vision**: arrived with `deepseek-v4-flash-vision-exp` (measured 2026-08-30) and
  is native on the Flash tier since V4.1 Flash (2026-09-10): the default `deepseek-flash`
  sees, the experimental companion is retired. Per provider vs per model as everywhere:
  `deepseek-v4-pro` stays text-only and answers an image with the vendor's own error.
  `deepseek-v4-flash` is a retired model's name the API "temporarily" routes to V4.1 Flash —
  the default moved to the vendor's own id on 2026-09-19.
- **MiniMax**: campaign-backed since 2026-08-30 (7/2/3, same day it was integrated). The
  reasoning arrives INLINE — every reply opens with a `<think>` block inside `content`, no
  separate field — and the dialect splits it out to `reasoning_content`, re-inlining it on
  replay as the vendor documents. `response_format` is accepted but NON-BINDING (a schema is
  ignored, `json_object` arrives fenced in markdown): the None declaration is a measurement
  (raw calls, 2026-08-30, recorded in the campaign catalog's MiniMax note — the archived M8
  shows ➖ because the None declaration keeps the option from ever being sent).
  Vision is per model: `MiniMax-M2` answers "I'm unable to view the image";
  `MiniMax-M3`, the default since 2026-11-11, reads the image (campaigns of 2026-09-21 and 2026-11-11).
- **Grok (x.AI)**: every declared capability is a live measurement — a full 12-mode campaign
  passed against `api.x.ai` through the generic OpenAI dialect before the provider class
  existed (2026-08-30, archived under `llmproviders-test/custom-endpoints/`). Keys carry the
  `xai-` prefix, which the factory infers.
- **OpenRouter**: the model marketplace (465 models on 2026-10-07) behind one key,
  integrated documentation-first and campaigned on 2026-10-07 (11/1 on
  `google/gemini-3.7-flash`, the red being M10; the default has been `google/gemini-3.8-flash` since 2026-11-11, 12/0/0). Identifiers
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
  `provider.require_parameters`: whether a schema can be silently ignored elsewhere is still
  open after the first campaign (M8 green on the three models played). The advanced routing body (`provider {…}`, `models[]`,
  `plugins[]`) is not exposed. The `sk-or-v1-` key prefix is confirmed on a
  real key (2026-10-07); the factory does not infer from it yet. The campaign also confirmed
  `usage.cost` on both paths, `message.reasoning` with `reasoning_details[]` on a reasoning
  model, and `openrouter/auto` echoing the model that answered.
- **Mammouth AI**: the French multi-model subscription whose included API credits drive
  Orkeon, integrated documentation-first and campaigned on 2026-10-07 (10/0/2 on
  `gemini-3.7-flash`: every exercised mode green; the default has been `gemini-3.8-flash` since 2026-11-11, 9/1/2 with M10 red). On three
  concordant clues (2026-09-18) the API is a LiteLLM proxy; nothing in the provider depends on
  it. Identifiers are the vendors' own bare strings (`gpt-5.6-sol`, `claude-sonnet-5`,
  `gemini-3.8-flash`), so the provider is reached by host (`api.mammouth.ai`) or by the
  explicit provider key `mammouth` (`LlmProviderFactory.Create("mammouth", …)`,
  `orkeon llm probe -p mammouth`) and never inferred from a model name — the same string
  without a base URL keeps going to the vendor. A run's settings carry no provider key, so a
  run reaches Mammouth through its `BaseUrl`. Only `messages`, `model`, `temperature`, `max_tokens`,
  `top_p` and `stream` are documented. Measured on 2026-10-07: tools are passed through,
  `response_format` is accepted but a strict schema does not bind (the MiniMax case, so it
  stays undeclared), `reasoning_effort` is accepted and the trace of a reasoning model comes
  back as `reasoning_content` (not declared yet — structured warning, never a silent drop); vision is declared from the vendor's own `text, image` model list. Prices are
  the vendor's upper bounds (`gemini-3.8-flash`, like `gemini-3.7-flash` before it, at 1.5 / 7.5 $/M, twice the direct price).
- **Anthropic identity-linked keys**: refuse every request without an `anthropic-workspace-id`
  header (2026-08-30). Set `LlmConfig.WorkspaceId` (CLI: `--workspace-id`); classic keys need
  nothing.
- **Polly** and **API key sanitization**: provided by `HttpLlmProviderBase` → active everywhere.

## API keys: the variable per provider

**A run takes the key the configuration resolves, else the variable the settings file
names**: `Llm:ApiKey` in the file, overridden by `ORKEON_Llm__ApiKey` in the
environment (`ORKEON_Llm__Profiles__<id>__ApiKey` for a profile); otherwise the variable the
section's `ApiKeyEnvVar` names — read in the process environment, then, on Windows, in the
user scope ([configuration](./configuration.md#the-api-key-apikey-apikeyenvvar)).
The vendor names below are the conventional ones: a settings file stores the *name* of the
variable and never the key, as the `ApiKeyEnvVar` of the `Llm` section and of a profile's
`Llm:Profiles:<id>` entry. So exporting `DEEPSEEK_API_KEY` is enough for a terminal
`orkeon run` on a file that names it: `"ApiKeyEnvVar": "DEEPSEEK_API_KEY"`. A file that names no
variable still reads `Llm:ApiKey` and `ORKEON_Llm__ApiKey` only.

| Provider | Variable (the conventional name for `ApiKeyEnvVar`) | Key issued at | Suggested timeout | Balance exposed by the API ([details](#account-balance-what-each-api-tells)) |
|---|---|---|---|---|
| OpenAI | `OPENAI_API_KEY` | `platform.openai.com/api-keys` | engine default (30 s) | ✗ (spend only, to an admin key) |
| Anthropic | `ANTHROPIC_API_KEY` | `console.anthropic.com` | engine default | ✗ (spend only, to an Admin API key) |
| DeepSeek | `DEEPSEEK_API_KEY` | `platform.deepseek.com` | **600 s** | ✓ `GET /user/balance` |
| Mistral AI | `MISTRAL_API_KEY` | `console.mistral.ai` | engine default | ✗ (usage only, to an Admin API key) |
| Google Gemini | `GEMINI_API_KEY` | `aistudio.google.com/apikey` | engine default | ✗ |
| Grok (x.AI) | `XAI_API_KEY` | `console.x.ai` | engine default | ◐ to a management key only |
| MiniMax | `MINIMAX_API_KEY` | `platform.minimax.io` | **600 s** | ✗ |
| Together AI | `TOGETHER_API_KEY` | `api.together.ai` | engine default | ✗ |
| Qwen | `DASHSCOPE_API_KEY` | `dashscope.console.aliyun.com` | engine default | ◐ to an Alibaba Cloud AccessKey only |
| Kimi (Moonshot) | `MOONSHOT_API_KEY` | `platform.moonshot.ai` | **600 s** | ✓ `GET /v1/users/me/balance` (USD on `.ai`, CNY on `.cn`) |
| HuggingFace | `HF_TOKEN` | `huggingface.co/settings/tokens` | engine default | ✗ |
| Z.AI (GLM) | `ZAI_API_KEY` | `z.ai/manage-apikey` | **600 s** | ✗ |
| OpenRouter | `OPENROUTER_API_KEY` | `openrouter.ai/keys` | engine default | ◐ per key (`GET /api/v1/key`); the account's credits to a management key only |
| Mammouth AI | `MAMMOUTH_API_KEY` | `mammouth.ai` — the vendor documents "from the API settings"; the exact page is confirmed with the first key | engine default | ✗ (a key's spend, answer undocumented) |
| Ollama · Docker Model Runner | none | — | engine default | — (local, no account) |
| OpenAI-compatible (`custom`) | none of its own: the settings name it through `ApiKeyEnvVar`, else `ORKEON_Llm__ApiKey` is read, the default key of every run | — | engine default | ✗ (unknown host) |
| Azure OpenAI | no dedicated variable: its per-resource endpoint makes it an OpenAI-compatible entry | — | — | ✗ (spend in Cost Management, to a Microsoft Entra identity) |

The suggested 600 s is not decorative. The four providers whose **default** model reasons
before it answers overrun the engine's 30 s, and the run of 2026-09-20 was lost to two Kimi
timeouts reported as an empty answer. A settings file needs `Llm:TimeoutSeconds` raised
explicitly.

Two names that are **not** this one, and get confused with it:

- `ORKEON_LLM_API_KEY` — the default of `orkeon llm probe -k` and `orkeon llm models -k`.
  Campaign tooling only; a run never reads it.
- `ORKEON_<NAME>` — the secret chain of the **tools**, not of the LLM
  (`EnvironmentSecretProvider`, then `Secrets:<NAME>` in the file): `ORKEON_TAVILY_API_KEY`
  for `web_search`, `BRAVE_API_KEY` read as-is for `brave_search`. See the
  [configuration reference](./configuration.md).

`orkeon init --api-key-env <name>` writes `Llm:ApiKeyEnvVar: <name>`
into the generated file — nothing for `ORKEON_Llm__ApiKey`, which the runtime reads natively — so
every run reads the variable it names.

## Account balance: what each API tells

What is left on the account behind a key, as each vendor's API tells it. Only three vendors
serve a balance to the inference key a run holds; for every other provider, the vendor's console
is the place to look — the "Key issued at" column above, behind `https://`. A balance request
goes to the key's own host: a key of Kimi's `.cn` platform is refused by the `.ai` host, and the
other way round. Each row was read on the vendor's own reference on 2026-09-24.

| Provider | Balance | Endpoint | What it returns | Key it takes | Source |
|---|---|---|---|---|---|
| DeepSeek | ✓ available | `GET /user/balance` | `balance_infos[]`, one entry per currency (`CNY`, `USD`): `total_balance`, `granted_balance`, `topped_up_balance` as decimal strings; `is_available` | the API key (Bearer) | api-docs.deepseek.com/api/get-user-balance |
| Kimi (Moonshot) | ✓ available | `GET /v1/users/me/balance` | `data.available_balance`, `voucher_balance`, `cash_balance` (negative in arrears) — US dollars on `api.moonshot.ai`, yuan on `api.moonshot.cn`; the answer names no currency | the API key (Bearer) of that platform | platform.kimi.ai/docs/api/balance · platform.kimi.com/docs/api/balance |
| OpenRouter | ◐ per key | `GET /api/v1/key` | `data.limit_remaining`: US dollars the key may still spend under its limit, `null` when it has none; `limit`, `usage`. The account's credits (`total_credits`, `total_usage`) are on `GET /api/v1/credits` | the API key for `/key`; a **management key** for `/credits`, which answers 403 to any other | openrouter.ai/docs/api/api-reference/api-keys/get-current-api-key · openrouter.ai/docs/api/api-reference/credits/get-remaining-credits |
| Grok (x.AI) | ◐ admin key required | `GET /v1/billing/teams/{team_id}/prepaid/balance` on `management-api.x.ai` | the prepaid balance, in US cents | a **management key** only | docs.x.ai/developers/rest-api-reference/management/billing |
| Qwen | ◐ admin key required | `QueryAccountBalance`, Alibaba Cloud billing OpenAPI | `AvailableAmount`, `AvailableCashAmount`, `CreditAmount`, `Currency` | an **Alibaba Cloud AccessKey** allowed `bss:DescribeAcccount` — never the DashScope key | alibabacloud.com/help/en/user-center/developer-reference/api-bssopenapi-2017-12-14-queryaccountbalance |
| OpenAI | ✗ not exposed | none — `GET /v1/organization/costs` reports spend | spend per day (`amount.value`, `currency`), not a balance | an admin key | developers.openai.com/cookbook/examples/completions_usage_api |
| Anthropic | ✗ not exposed | none — `GET /v1/organizations/cost_report` reports spend | spend in US cents, not a balance | Admin API credentials — "workspace API keys don't work" | platform.claude.com/docs/en/manage-claude/usage-cost-api |
| Mistral AI | ✗ not exposed | none — `GET /v1/admin/usage` reports usage | consumption per category for a month; the credit balance is on the console's Billing page only | an Admin API key | docs.mistral.ai/admin/admin-api/usage-metrics · docs.mistral.ai/admin/billing-usage/billing |
| Azure OpenAI | ✗ not exposed | none — Cost Management reports spend | spend per meter | a Microsoft Entra identity with Cost Management Reader, never the resource key | learn.microsoft.com/en-us/azure/foundry/concepts/manage-costs |
| Google Gemini | ✗ not exposed | none | the prepay balance is managed "directly within the Google AI Studio Billing tab" | — | ai.google.dev/gemini-api/docs/billing |
| Together AI | ✗ not exposed | none | the "Credit balance" card of the billing settings | — | docs.together.ai/docs/billing-credits · docs.together.ai/docs/billing-usage-limits |
| HuggingFace | ✗ not exposed | none | the billing page and the Inference Providers settings | — | huggingface.co/docs/inference-providers/pricing |
| Z.AI (GLM) | ✗ not exposed | none | the API reference lists no billing endpoint | — | docs.z.ai/llms.txt |
| MiniMax | ✗ not exposed | none for pay-as-you-go; `GET /v1/token_plan/remains` on `www.minimax.io` for a Token Plan | Token Plan only, answer undocumented | a Token Plan subscription key | platform.minimax.io/docs/token-plan/faq |
| Mammouth AI | ✗ not exposed | none — `GET /key/info`, documented with a placeholder host | "how much credits has been spent on a key": spend, answer undocumented | the API key | info.mammouth.ai/docs/api-quick-start |
| Ollama · Docker Model Runner | — not applicable | none | a local runtime has no account | — | — |

Two endpoints are documented without the shape of their answer: MiniMax's Token Plan `remains`
and Mammouth's `key/info`. An OpenAI-compatible endpoint (`custom`) exposes no balance anyone
knows of, and a local one has no account.

## Defaults and newer models — catalogue reviews of 2026-09-19, 2026-10-07 and 2026-11-11

Every vendor's own model and pricing pages were read on 2026-09-19, alongside the public
catalogues of the two aggregators (OpenRouter, 447 models; Mammouth, 100). The rule is the
one `LlmProviderDefaultModels` states: a default changes only when the vendor retires the
name; otherwise a newer model is a **candidate** until a campaign has archived an M1 on it.
The campaigns of 2026-10-07 and 2026-11-11 played the candidates again: the date in a verdict
says which campaign measured what, and the models they added to the candidates are in the
"Newer" column.

| Provider | Default (code) | Served on 2026-09-19 | Newer on the vendor's API | Verdict |
|---|---|---|---|---|
| OpenAI | `gpt-5.6-sol` | yes — 4 / 20 $/M through 2026-11-21, the replacement target of both 2026 deprecation waves | `gpt-6-astra` (10 / 50, effort `low`…`max` with no `none`, 2× billing above 272K input tokens); `gpt-6.1-terra` (listed after 2026-10-07) | stays — **campaigned 2026-09-21**: `gpt-6-astra` 10/2 with `temperature: 1` pinned; it has no `reasoning_effort: none`, so function tools on `/v1/chat/completions` are refused (`use /v1/responses`) — not a default until the dialect speaks Responses; `gpt-6-astra-pro` is an aggregator label, not an OpenAI id; **campaigned 2026-11-11** (after the fix that lets `reasoning_effort: none` reach the API again): `gpt-5.6-sol` 11/1, `gpt-6-sol` 11/1, `gpt-6-luna` 12/0/0, `gpt-6.1-terra` 12/0/0 — it takes `none`, unlike `gpt-6.1-sol`; `gpt-6-astra` 10/2 and `gpt-6.1-sol` 9/3 stay red on M2 and M5 (no `none`, tools need `/v1/responses`) |
| Anthropic | `claude-sonnet-5` | yes — 2 / 10 $/M made permanent, active until at least 2027-06-30 | `claude-fable-5-1` (2026-09-01, 10 / 50, thinking always on, forced `tool_choice` refused); `claude-fable-5-2` (2026-11-09, 1M context, 128K output) | stays — **campaigned 2026-09-21**: `claude-fable-5-1` 11/1 twice (the red is M6, the text-fallback protocol; native tools, schema, vision and cache all green) — viable, at five times the price; `claude-opus-5-fast` is a `speed` flag, not an id; `temperature` / `top_p` / `top_k` return 400 on every model from Opus 4.7 on; **campaigned 2026-11-11**: the default 12/12, `claude-fable-5-2` 12/12 — including the M6 that `claude-fable-5-1` still misses (11/1) |
| Azure OpenAI | deployment | — | — | nothing to default to |
| Ollama | `llama3.2` | yes — 3B, text only, no thinking, a year old | `qwen3.5:4b`, `gemma4:e4b`, `granite4.2:3b` (tools + thinking, same size class) | stays: a new default means a pull on every machine |
| Together AI | `zai-org/GLM-5.3-Flash` (was `meta-llama/Llama-3.3-70B-Instruct-Turbo`) | yes — 0.15 / 0.50 $/M (catalogue read of 2026-11-11); the former default is no longer served on serverless | `Qwen/Qwen3.5-9B`, `deepseek-ai/DeepSeek-V4.1-Flash`; Llama 4 left serverless | **changed 2026-11-11** — every call to the former default answered `Unable to access non-serverless model` (2/9/1), so the default moves to `zai-org/GLM-5.3-Flash`, Together's replacement target since 2026-09-19: 11/0/1 on the last run of the day (10/1/1 on the first, M10: no cached tokens reported; 11/0/1 on 2026-09-21), it reads the image, so the default sees and there is no separate vision companion; `Qwen/Qwen3.5-9B` 10/1/1 (M10), candidate |
| DeepSeek | `deepseek-flash` (was `deepseek-v4-flash`) | yes — V4.1 Flash, 0.30 / 1.20 $/M peak, native vision | it is the newest | **changed 2026-09-19**: the old name is a retired model "temporarily" routed here; **replayed 2026-09-21**: 12/12 twice — M2 and M9, red for five campaigns on `deepseek-v4-flash`, are green on `deepseek-flash`; **replayed 2026-11-11**: `deepseek-flash` 12/12 |
| Kimi | `kimi-k3.1-pro` (was `kimi-k2.6`) | yes — `kimi-k2.6` 0.95 / 4.00 $/M, no retirement date | `kimi-k3` (July 2026, 1M, 3 / 15, fixed sampling, no `thinking` field, always reasons); `kimi-k3.1-pro` and the `-highspeed` variants (listed 2026-11-11) | **changed 2026-11-11** — **campaigned 2026-09-21**: `kimi-k3` 12/12 then 11/1 (M2 on the flattened shape), reasoning trace, cache 0.97, bare JSON where k2.6 now fences its `json_object` — ready, at three times the price; `kimi-k2.5` and `moonshot-v1-*` retired 2026-08-31; docs now on `platform.kimi.ai`; **2026-11-11**: `kimi-k2.6` 10/1/1 (M8: its `json_object` still fenced), `kimi-k3` 12/12, `kimi-k3.1-pro` 12/12 — the new top model; **changed 2026-11-11**: the default moves to `kimi-k3.1-pro` (12/12 direct and on OpenRouter). The K3 family fixes `temperature`, `top_p` and its reasoning: `KimiLlmProvider` leaves `temperature`, `top_p` and `thinking` out of a K3 request and reports each, so a host that sets them gets a warning, not a 400 |
| Qwen | `qwen3.7-plus` | yes — still the Plus tier, one of the three recommended models | `qwen3.8-max` (= `qwen3.8-max-0902`), `qwen3.8-flash`; no `qwen3.8-plus` | stays — **campaigned 2026-09-21**: `qwen3.8-max` 12/12, `qwen3.8-flash` 11/1 (M2), both ready, and `qwen3.7-plus` is still 12/12; the 3.8 generation adds `preserve_thinking`; **2026-11-11**: `qwen3.7-plus`, `qwen3.8-flash` and `qwen3.8-max` 12/12 each |
| Mistral AI | `mistral-medium-2604` | yes — 1.50 / 7.50 $/M, not deprecated | nothing for chat since April (OCR 4.1 only) | stays; `devstral-*` / magistral ids sit in the deprecated table; **2026-11-11**: the default and `mistral-large-4` 12/12, `mistral-small-2603` 11/1 (M9) |
| HuggingFace | `meta-llama/Llama-3.1-8B-Instruct` | yes — but tools on one of its four routed providers, and `:fastest` may pick another | `Qwen/Qwen3.5-9B` (tools on three providers, from 0.10 / 0.15), `zai-org/GLM-5.3-Flash`, `deepseek-ai/DeepSeek-V4.1-Flash` | candidate Qwen3.5-9B — **campaigned 2026-09-21**: 10/0/2, it sees and its tools hold; its first pass returned an empty M1 after 4113 tokens of thinking on the router's 4096 fallback — pin `Llm:MaxTokens` (or turn thinking off) before making it a default. The routing still explains the default's moving reds (M2 and M5 both red on the same pass); **2026-11-11**: the default 7/3/2 (M2, M5, M9), `Qwen/Qwen3.5-9B` 9/1/2 (M2) |
| Z.AI | `glm-5.3-flashx` (was `glm-5.2`) | yes — `glm-5.2` still under "Latest Models", 1.40 / 4.40 $/M | `glm-5.3` (2026-08-18), `glm-5.3-flash` (0.15 / 0.50, image + video), `glm-5.3-flashx` | **changed 2026-11-11** — **campaigned 2026-09-21**: `glm-5.3-flash` 12/12 twice (it sees, where 5.2 and 5.3 answer `1210`; cache read) — the cleanest candidate in the fleet, at a ninth of the price; `glm-5.3` 10/2 (M2, M9) brings nothing over 5.2. The `Toggle` declaration still needs a per-model guard before a bump; **2026-11-11**: `glm-5.3-flashx` 12/12 again, `glm-5.3` 11/1 (M9), `glm-5.3-flash` 11/1 (M2), `glm-5.2` 10/2; **changed 2026-11-11**: the default moves to `glm-5.3-flashx` (12/12 on 2026-10-07 and on the first run of 2026-11-11, 11/1 with M10 red on the last; it sees and its cache is read); last runs of the day: `glm-5.3-flash` 12/0/0, `glm-5.3` 10/2 (M2, M9), `glm-5.2` 11/1 (M9). The 5.3 family cannot switch thinking off: `ZaiLlmProvider` does not send `thinking: disabled` to it and reports the option instead |
| Google Gemini | `gemini-3.8-flash` (was `gemini-3.7-flash`) | yes — `gemini-3.7-flash` "previous generation", no shutdown date, 0.75 / 3.75 $/M until 2026-12-31 then 1.50 / 7.50 | `gemini-3.8-flash` (GA 2026-09-02, same price, `minimal` thinking rejected); `gemini-4-argon`, `gemini-4-carbon` | first candidate for a bump — **campaigned 2026-09-21**: `gemini-3.8-flash` 11/0/1 twice, identical to 3.7 mode for mode on the direct transport; OpenRouter and Mammouth not played (no key); **2026-11-11**: the default 11/0/1, `gemini-3.8-flash` 12/0/0, `gemini-4-argon` and `gemini-4-carbon` 11/0/1 each; `gemini-omni-1.2-flash` is listed but Interactions-API only, so out of reach of this provider and not campaigned; **changed 2026-11-11 (full rerun)**: `gemini-3.8-flash` 12/0/0 direct and on OpenRouter (9/1/2 on Mammouth, the same M10 as 3.7), the default 11/0/1 — the default moves to `gemini-3.8-flash`, and OpenRouter and Mammouth follow it |
| Grok (x.AI) | `grok-4.6` | yes — recommended, 2 / 6 $/M below 200K prompt tokens, 4 / 12 above | none | stays (`grok-4.7` is listed since); **campaigned 2026-11-11**: `grok-4.6` and `grok-4.7` 12/12 |
| MiniMax | `MiniMax-M3` (was `MiniMax-M2`) | yes — `MiniMax-M2` listed as legacy, no retirement date | `MiniMax-M3` (2026-06-01, 1M, image + video input, same 0.30 / 1.20); `MiniMax-M3.1-Flash` | **changed 2026-11-11** — **campaigned 2026-09-21**: `MiniMax-M3` 9/1/2 then 8/1/3 — it reads the image (then the kit's vision companion), M2 red like M2, the inline `<think>` block split out as on M2, 128 cached tokens once; the reasoning-format question is answered; **2026-11-11**: `MiniMax-M3.1-Flash` 10/0/2 (M7 and M8 not declared by the provider), M9 green — it reads the image where `MiniMax-M3` reads `72`; `MiniMax-M3` 9/1/2 (M9), `MiniMax-M2` 8/2/2 (M2, M9); **changed 2026-11-11 (full rerun)**: `MiniMax-M3` 10/0/2 (it reads the image), `MiniMax-M2` 6/4/2 (M2, M5, M9, M10) — the default moves to `MiniMax-M3` and the vision companion is retired |
| OpenRouter | `google/gemini-3.8-flash` (was `google/gemini-3.7-flash`) | yes | `google/gemini-3.8-flash` (2026-09-02, same price) | follows the direct default — **campaigned 2026-10-07**: 11/1 on both (M10: cache fields returned with 0); **2026-11-11**: the default 12/0/0, `google/gemini-3.8-flash` 11/1/0 (M10), `moonshotai/kimi-k3.1-pro` 12/0/0; **changed 2026-11-11 (full rerun)**: `google/gemini-3.8-flash` 12/0/0, `google/gemini-3.7-flash` 11/1 (M10) — follows the direct default to 3.8 |
| Mammouth AI | `gemini-3.8-flash` (was `gemini-3.7-flash`) | yes | `gemini-3.8-flash` | follows the direct default — **campaigned 2026-10-07**: 10/0/2 on both, every exercised mode green; **2026-11-11**: 9/1/2 on both (M10: 0 cached tokens), `kimi-k3.1-pro` 10/0/2; **changed 2026-11-11 (full rerun)**: 9/1/2 on both again (M10) — follows the direct default to 3.8 |

`ModelPricingRegistry` follows the same pages: the GPT-5.6 family and `gpt-6-astra`, and, for
Anthropic (read 2026-10-30), Fable 5.1 / Fable 5 at 10 / 50, Opus 5.5 at 4 / 20, Opus 5 and Opus 4.5 to 4.8 at 5 / 25, Sonnet 5 at 2 / 10,
Haiku 5 at 0.10 / 0.50 and Haiku 4.5 at 1 / 5. Cache tariffs: a 5-minute write at 1.25x the prompt rate, a 1-hour write at 2x
(costed by the cost manager), a read at 0.1x (0.25 for Fable 5.1, 0.20 for Opus 5.5). The Haiku 5.5 tier above 100K prompt tokens
(0.50 / 2.50) is not modelled.

## Models served and their default options — catalogue read of 2026-11-11

> The latest result of every campaigned model, regenerated after each campaign, is on
> [LLM campaign results](llm-campaign-results.md); the scores below are the ones this
> catalogue read recorded.

The campaign of 2026-11-11 read each provider's own catalogue endpoint with the campaign key
(`orkeon llm models -p <provider>` returns the same identifiers), then played the protocol on
every default, on the candidates of the reviews, and on the current chat models the listing
showed that no campaign had met yet. Two providers were campaigned later the same day:
**Kimi** with a second key (the first was refused, 401), and **Z.AI** on a working key (the
first account had no balance: 429, `Insufficient balance or no resource package`). A score marked `(2026-10-07)` below was not replayed. Three kinds of facts come out of it, kept apart
on purpose:

- **what the endpoint publishes** about a model — context, output cap, default sampling,
  effort levels. The vendor's own data: read, not measured;
- **what the model dictates** — a value it refuses to see changed. No endpoint publishes
  that. It is learnt by calling the model (one pass on M1, M5 and M7 at the probe's
  defaults) and recorded in `requiredParams`;
- **what the campaign measured** on the twelve automated modes.

The full listing — 1 312 identifiers across fourteen providers, each with the options its
endpoint publishes — is
[`llmproviders-test/lib/served-models.json`](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/lib/served-models.json),
a dated snapshot the next campaign diffs against. Azure OpenAI and Ollama have no row: one
serves an account's deployments, the other what a machine pulled.

| Provider | Catalogue request | Identifiers | What it publishes per model |
|---|---|---|---|
| OpenAI | `GET api.openai.com/v1/models` | 96 | ids only |
| Anthropic | `GET api.anthropic.com/v1/models` | 15 | context, output cap, effort levels, thinking types, modalities |
| DeepSeek | `GET api.deepseek.com/models` | 2 | context, output cap, modalities, effort levels and default |
| Z.AI | `GET api.z.ai/api/paas/v4/models` | 12 | ids only |
| Kimi | `GET api.moonshot.ai/v1/models` | 6 | context, image/video input, reasoning |
| Google Gemini | `GET generativelanguage.googleapis.com/v1beta/models` | 62 | context, output cap, default temperature / topP / topK, maximum temperature, thinking |
| Mistral AI | `GET api.mistral.ai/v1/models` | 64 | context, default temperature, capabilities, aliases, deprecation |
| Qwen | `GET dashscope-intl.aliyuncs.com/compatible-mode/v1/models` | 101 | ids only |
| Grok (x.AI) | `GET api.x.ai/v1/language-models` | 8 | modalities, prices in $ per million tokens, long-context threshold, aliases |
| MiniMax | `GET api.minimax.io/v1/models` | 9 | ids only |
| Together AI | `GET api.together.xyz/v1/models` | 270 | type, context, prices in $ per million tokens |
| HuggingFace | `GET router.huggingface.co/v1/models` | 127 | modalities, and per routed provider: context, tools, structured output |
| OpenRouter | `GET openrouter.ai/api/v1/models` | 433 | context, output cap of the top endpoint, default parameters, reasoning (mandatory, default effort, supported efforts) |
| Mammouth AI | `GET api.mammouth.ai/v1/models` | 107 | context, output cap |

The chat models of the current generation, provider by provider. An em dash in the two size
columns means the endpoint does not publish the figure — for those models the documented
caps are the ones of the next section.

| Provider | Model | Context | Max output | Published by the endpoint | Dictated by the model (measured) | Campaign ✅/❌/➖ |
|---|---|---|---|---|---|---|
| OpenAI | `gpt-5.6-sol` *(default)* | — | — | — | `temperature` = `1` · `reasoning_effort` = `none` with function tools | 11/1/0 — ❌ M9 |
|  | `gpt-5.6-terra` | — | — | — | `temperature` = `1` · `reasoning_effort` = `none` with function tools | 11/1/0 — ❌ M9 (2026-10-07) |
|  | `gpt-5.6-luna` | — | — | — | `temperature` = `1` · `reasoning_effort` = `none` with function tools | 11/1/0 — ❌ M6 (2026-10-07) |
|  | `gpt-6-sol` | — | — | — | `temperature` = `1` · `reasoning_effort` = `none` with function tools | 11/1/0 — ❌ M9 |
|  | `gpt-6-luna` | — | — | — | `temperature` = `1` · `reasoning_effort` = `none` with function tools | 12/0/0 |
|  | `gpt-6.1-terra` | — | — | — | `temperature` = `1` · `reasoning_effort` = `none` with function tools | 12/0/0 |
|  | `gpt-6.1-sol` | — | — | — | `temperature` = `1` | 9/3/0 — ❌ M2, M5, M9 |
|  | `gpt-6-astra` | — | — | — | `temperature` = `1` | 10/2/0 — ❌ M2, M5 |
| Anthropic | `claude-sonnet-5` *(default)* | 1 000 000 | 128 000 | efforts `low` `medium` `high` `xhigh` `max` · thinking `adaptive` `disabled` | `temperature` = `1` | 12/0/0 |
|  | `claude-sonnet-5-5` | 1 000 000 | 128 000 | efforts `low` `medium` `high` `xhigh` `max` · thinking `adaptive` | `temperature` = `1` | 12/0/0 |
|  | `claude-haiku-5-5` | 1 000 000 | 128 000 | efforts `low` `medium` `high` `xhigh` `max` · thinking `adaptive` `disabled` | `temperature` = `1` | 12/0/0 |
|  | `claude-opus-5` | 1 000 000 | 128 000 | efforts `low` `medium` `high` `xhigh` `max` · thinking `adaptive` `disabled` | `temperature` = `1` | 12/0/0 (2026-10-07) |
|  | `claude-opus-5-5` | 1 000 000 | 128 000 | efforts `low` `medium` `high` `xhigh` `max` · thinking `adaptive` | `temperature` = `1` | 12/0/0 |
|  | `claude-fable-5-1` | 1 000 000 | 128 000 | efforts `low` `medium` `high` `xhigh` `max` · thinking `adaptive` | `temperature` = `1` | 11/1/0 — ❌ M6 |
|  | `claude-fable-5-2` | 1 000 000 | 128 000 | efforts `low` `medium` `high` `xhigh` `max` · thinking `adaptive` | `temperature` = `1` | 12/0/0 |
| DeepSeek | `deepseek-flash` *(default)* | 1 048 576 | 393 216 | default effort `high` (of `low` `high` `max`) · text + image | — | 12/0/0 |
|  | `deepseek-v4-pro` | 1 048 576 | 393 216 | default effort `high` (of `low` `high` `max`) · text | — | 11/1/0 — ❌ M9 (2026-10-07) |
| Z.AI | `glm-5.2` | — | — | — | — | 11/1/0 — ❌ M9 (10/2/0 earlier the same day) |
|  | `glm-5.3` | — | — | — | — | 10/2/0 — ❌ M2, M9 (11/1/0 earlier the same day) |
|  | `glm-5.3-flash` | — | — | — | — | 12/0/0 (11/1/0, M2 red, earlier the same day) |
|  | `glm-5.3-flashx` *(default)* | — | — | — | — | 11/1/0 — ❌ M10 (12/0/0 earlier the same day) |
| Kimi | `kimi-k2.6` | 262 144 | — | image + video + reasoning | `temperature` = `1` | 10/1/1 — ❌ M8 |
|  | `kimi-k3` | 1 048 576 | — | image + video + reasoning | `temperature` = `1` | 11/1/0 (M2) |
|  | `kimi-k3.1-pro` *(default)* | 1 048 576 | — | image + video + reasoning | `temperature` = `1` | 12/0/0 |
|  | `kimi-k3.1-pro-highspeed` | 1 048 576 | — | image + video + reasoning | `temperature` = `1` | 12/0/0 |
|  | `kimi-k2.7-code` | 262 144 | — | image + video + reasoning | `temperature` = `1` | 12/0/0 |
|  | `kimi-k2.7-code-highspeed` | 262 144 | — | image + video + reasoning | `temperature` = `1` | 12/0/0 |
| Google Gemini | `gemini-3.7-flash` | 1 048 576 | 65 536 | `temperature` 1 (max 2) · `topP` 0.95 · `topK` 64 | — | 11/0/1 |
|  | `gemini-3.8-flash` *(default)* | 1 048 576 | 65 536 | `temperature` 1 (max 2) · `topP` 0.95 · `topK` 64 | — | 12/0/0 |
|  | `gemini-4-argon` | 1 048 576 | 32 768 | `temperature` 1 (max 2) · `topP` 0.95 · `topK` 64 | — | 11/0/1 |
|  | `gemini-4-carbon` | 1 048 576 | 32 768 | `temperature` 1 (max 2) · `topP` 0.95 · `topK` 64 | — | 11/0/1 |
|  | `gemini-3.5-flash-lite` | 1 048 576 | 65 536 | `temperature` 1 (max 2) · `topP` 0.95 · `topK` 64 | — | 12/0/0 (2026-10-07) |
|  | `gemini-3.1-pro-preview` | 1 048 576 | 65 536 | `temperature` 1 (max 2) · `topP` 0.95 · `topK` 64 | — | 10/1/1 — ❌ M5 (2026-10-07) |
| Mistral AI | `mistral-medium-2604` *(default)* | 262 144 | — | `temperature` 1 · tools + reasoning + vision | `reasoning_effort` ∈ {`high`, `none`} | 12/0/0 |
|  | `mistral-large-4` | 1 048 576 | — | `temperature` 1 · tools + reasoning + vision | `reasoning_effort` ∈ {`high`, `none`} | 12/0/0 |
|  | `mistral-small-2603` | 262 144 | — | `temperature` 0.3 · tools + reasoning + vision | `reasoning_effort` ∈ {`high`, `none`} | 11/1/0 — ❌ M9 |
| Qwen | `qwen3.7-plus` *(default)* | — | — | — | — | 12/0/0 |
|  | `qwen3.7-flash` | — | — | — | — | 11/1/0 — ❌ M2 (2026-10-07) |
|  | `qwen3.7-max` | — | — | — | — | 11/1/0 — ❌ M9 (2026-10-07) |
|  | `qwen3.8-flash` | — | — | — | — | 12/0/0 |
|  | `qwen3.8-max` | — | — | — | — | 12/0/0 |
|  | `deepseek-v4.1-flash-1017` | — | — | — | — | 12/0/0 |
| Grok (x.AI) | `grok-4.6` *(default)* | — | — | text + image · 2 / 0.5 / 6 $/M in / cached / out | — | 12/0/0 |
|  | `grok-4.7` | — | — | text + image · 2 / 0.5 / 6 $/M in / cached / out | — | 12/0/0 |
| MiniMax | `MiniMax-M2` | — | — | — | — | 6/4/2 — ❌ M2, M5, M9, M10 |
|  | `MiniMax-M2.7` | — | — | — | — | 8/2/2 — ❌ M2, M9 (2026-10-07) |
|  | `MiniMax-M3` *(default)* | — | — | — | — | 10/0/2 |
|  | `MiniMax-M3.1-Flash` | — | — | — | — | 10/0/2 |
| Together AI | `zai-org/GLM-5.3-Flash` *(default)* | 1 048 575 | — | 0.15 / 0.5 $/M | — | 11/0/1 (10/1/1, M10 red, earlier the same day) |
|  | `Qwen/Qwen3.5-9B` | 262 144 | — | 0.17 / 0.25 $/M | — | 10/1/1 — ❌ M10 |
|  | `meta-llama/Llama-3.3-70B-Instruct-Turbo` *(former default)* | 131 072 | — | 1.04 / 1.04 $/M | — | 2/9/1 — no longer serverless |
| HuggingFace | `meta-llama/Llama-3.1-8B-Instruct` *(default)* | up to 131 072 | — | 3 routed provider(s), 1 with tools | — | 7/3/2 — ❌ M2, M5, M9 |
|  | `Qwen/Qwen3.5-9B` | up to 262 144 | — | 3 routed provider(s), 2 with tools | — | 9/1/2 — ❌ M2 |
| OpenRouter | `google/gemini-3.7-flash` | 1 048 576 | 65 536 | default effort `medium` · reasoning mandatory | — | 11/1/0 — ❌ M10 |
|  | `google/gemini-3.8-flash` *(default)* | 1 048 576 | 65 536 | default effort `medium` · reasoning mandatory | — | 12/0/0 |
|  | `moonshotai/kimi-k3.1-pro` | 1 048 576 | 943 718 | default effort `max` | — | 12/0/0 |
|  | `deepseek/deepseek-v4.1-flash` | 1 048 576 | 943 718 | default effort `high` | — | 4/0/0 (M5, M7, M8, M10) (2026-10-07) |
| Mammouth AI | `gemini-3.7-flash` | 1 048 576 | 65 536 | — | — | 9/1/2 — ❌ M10 |
|  | `gemini-3.8-flash` *(default)* | 1 048 576 | 65 536 | — | — | 9/1/2 — ❌ M10 |
|  | `kimi-k3.1-pro` | 1 048 576 | 943 718 | — | — | 10/0/2 |
|  | `deepseek-v4.1-flash` | 1 048 576 | 1 048 576 | — | — | 1/0/2 (M5, M7, M8) (2026-10-07) |

What the table does not say by itself:

- **OpenAI** — the 6 generation is listed (`gpt-6-sol`, `gpt-6-luna`, `gpt-6.1-sol`,
  `gpt-6.1-terra`, beside `gpt-6-astra`). `gpt-6-sol`, `gpt-6-luna`, `gpt-6.1-terra` and the
  two 5.6 tiers not replayed behave like the default: temperature 1 only, and function tools
  on `/v1/chat/completions` need `reasoning_effort: none`. `gpt-6.1-terra` takes `none`,
  unlike `gpt-6.1-sol`, and is 12/12 on its first run. `gpt-6.1-sol` behaves like
  `gpt-6-astra`: it has no `none`, so function tools stay refused there (M2 on the messages
  array, M5) until the dialect speaks `/v1/responses`. The first pass of 2026-11-11 (06:10)
  was red on M2 and M5 for every OpenAI model: Orkeon had been lowering the pinned `none` to
  `low` since #242, and the `gpt-5` row of the capability table caught the `gpt-5.6-*` ids.
  Fixed and replayed at 06:25: `gpt-5.6-sol` 11/1, `gpt-6-sol` 11/1, `gpt-6-luna` and
  `gpt-6.1-terra` 12/12; `gpt-6-astra` and `gpt-6.1-sol` stay red, as expected. M9 is the mode
  that moves at the mandated temperature: `29`, `26` and `18` were read for 73 on 2026-10-07,
  `23`, `43` and `23` on 2026-11-11 (`gpt-5.6-sol`, `gpt-6-sol`, `gpt-6.1-sol`).
- **Anthropic** — `claude-sonnet-5-5`, `claude-opus-5-5` and `claude-haiku-5-5` are listed,
  the last on the day of the campaign. The four models campaigned for the first time refuse a
  pinned temperature in the words `claude-sonnet-5` uses. `claude-opus-5-5` answers the
  probe's tool prompt with HTTP 200, an empty `content` and `stop_reason: "refusal"`
  (category `cyber`): the provider does not read `stop_reason` and returns an empty answer —
  its M5 and M6 reds are that refusal, not tool calling. `claude-haiku-5-5`'s M9 red is the
  probe's: the model answers `Seventy-three red`, the check looks for the digits. On
  2026-11-11 `claude-haiku-5-5` is 12/12 and `claude-opus-5-5` is 12/12 with the same prompt:
  the classifier no longer meets the probe's wording. `claude-fable-5-2` (listed 2026-11-09)
  refuses an explicit temperature like the rest of the 5 family and is 12/12 on its first run,
  including the M6 that `claude-fable-5-1` still misses (11/1).
- **Mistral AI** — the account is no longer rate-limited, so the default has a result again
  (11/1, its 2026-08-30 line). `mistral-large-4` reasons by default and streams
  `delta.content` as an array of typed chunks: the text stream threw on it (M3) and the chat
  stream dropped the trace and the first words of the answer. Both streams read the two
  shapes since 2026-10-07; replayed on that fix the model was 11/1, M3 green, and it is 12/12
  on 2026-11-11 (its published context went from 524 288 to 1 048 576). The default is 12/12
  too. `/v1/models` now also lists models of other vendors served by Mistral —
  `deepseek-v4-1-flash-1017` and `zai-glm-5-3-flash`, both 11/1 on their first run: the system
  message is lost on the messages-array shape of M2 while the flattened shape honours it.
- **Kimi** — played an hour after the others: the account was suspended for insufficient
  balance at first (HTTP 429, `exceeded_current_quota_error`, which the retry policy treats
  as a rate limit). `kimi-k2.6` still fences its `json_object` in markdown (M8, as on
  2026-09-21); `kimi-k2.7-code` is the one Kimi model with every mode green. It mandates
  temperature 1 too, which only its streams showed: on a buffered call the provider retries
  once with the mandated value, the two streaming paths do not. **2026-11-11**, with a
  second key (the first was refused, 401 `Incorrect API key provided`): the listing grows to six
  identifiers — `kimi-k3.1-pro` and the `-highspeed` variants of it and of `kimi-k2.7-code`.
  All three mandate temperature 1 as well (6/6 at 0: M2, M3, M4, M5, M9 and M10 refused), and
  are 12/12 once pinned; `kimi-k3` is 12/12 too, `kimi-k2.7-code` still 12/12. `kimi-k2.6`
  stays 10/1/1 — M8 still fenced, M10 reports no cache-token breakdown. `kimi-k3.1-pro` is
  12/12 through OpenRouter as well, 10/0/2 through Mammouth.
- **Z.AI** — `glm-5.3-flashx` is the second model of the family to pass every mode (on its first run of the day). The
  listing omits `glm-4.6v-flash`, which is served (the vision companion, M9 green the same
  day): a catalogue is not the whole offer. `glm-5.3-flashx` is 12/12 again on the first run, 11/1 on the last (M10 red); `glm-5.3` turns
  M2 green on the first run (11/1, M9 `1210` left) and red again on the last (10/2); `glm-5.3-flash`
  keeps M2 red on the first run, its system message ignored on both shapes — the model, not
  Orkeon — and is 12/12 on the last; `glm-5.2` is 10/2 then 11/1 (M9 `1210`; M10 `0 cached` on the first run).
  The first key of the day had no balance (429, code 1113), which the provider retried as a
  transient limit for 21 minutes (issue #328). `glm-5.3-flashx-legal` is new in the listing,
  not campaigned.
- **Qwen** — an international key is served by `dashscope-intl.aliyuncs.com` only; the
  compiled endpoint (`dashscope.aliyuncs.com`) answers it 401. Set `Llm:BaseUrl`. The
  international listing fell from 172 to 101 ids on 2026-11-11 (the qwen3 open-weight line and
  the qwen-vl / omni / tts families left it); `qwen3.7-plus`, `qwen3.8-flash` and
  `qwen3.8-max` are 12/12, and `deepseek-v4.1-flash-1017`, served there, is 12/12 on its
  first run.
- **Gemini** — `gemini-3.5-flash-lite` is the one Gemini model whose cache breakdown came
  back (4077 cached tokens); the M5 red of `gemini-3.1-pro-preview` did not reproduce in
  three replays (not replayed on 2026-11-11). `gemini-3.8-flash` is 12/12 on 2026-11-11 and
  the default 11/0/1. `gemini-4-argon` and `gemini-4-carbon` (1 048 576 context, 32 768
  output, thinking) are 11/0/1 each on their first run. `gemini-omni-1.2-flash` is listed by
  `/v1beta/models` but answers `This model only supports Interactions API` on the
  OpenAI-compatible surface: out of this provider's reach, not campaigned.
- **MiniMax** — `MiniMax-M3.1-Flash` is new in the listing (9 ids): 10/0/2 on its first run,
  M7 and M8 not declared by the provider, M9 green — it reads the image where `MiniMax-M3`
  misreads the number (`72`; 9/1/2 on the first run, 10/0/2 on the last: it reads the image
  there too). `MiniMax-M2` is 8/2/2 (M2, M9), then 6/4/2 on the last run (M2, M5, M9, M10).
- **Together AI** — `meta-llama/Llama-3.3-70B-Instruct-Turbo`, the former compiled default, is no
  longer served on serverless: every call answers `Unable to access non-serverless model`
  (2/9/1). The default moves to `zai-org/GLM-5.3-Flash` (11/0/1 on the last run of the day, 10/1/1
  on the first with M10 red: the endpoint reports no cached tokens); `Qwen/Qwen3.5-9B` is 10/1/1 (M10). The default sees, so there is
  no separate vision companion any more.
- **HuggingFace** — the default is 7/3/2 (M2, M5, M9) and `Qwen/Qwen3.5-9B` 9/1/2 (M2); the
  routing still decides how many of a model's providers carry tools.
- **OpenRouter and Mammouth AI** — first campaigns, both on the fleet's Gemini default: one
  model, three transports. Direct 11/0/1, OpenRouter 11/1/0, Mammouth 10/0/2 — the marketplace
  returns the cache fields with 0 where Google returns none (M10), and Mammouth's two ➖ are
  the capabilities the provider does not declare. OpenRouter's catalogue is the richest of the
  fleet: per model, the default parameters and whether reasoning is mandatory. Replayed on
  2026-11-11: direct 11/0/1 (and `gemini-3.8-flash` 12/12), OpenRouter 12/0/0 on the default
  and 11/1/0 on `gemini-3.8-flash` (M10), Mammouth 9/1/2 on both — its M10 now reads 0 cached
  tokens where it was green on 2026-10-07.
- **Also served, not campaigned** — among others `claude-fable-5`, `claude-opus-4-8`,
  `gemini-3.6-flash`, `gemini-3.5-flash`, `mistral-large-2512`, the `ministral` family,
  `grok-4.5`, `grok-4.3`, `MiniMax-M2.1`, `MiniMax-M2.5`, `glm-5.1`, `glm-5-turbo`,
  `qwen3.6-plus`, `qwen3.5-plus`. The snapshot lists them all.

## Output caps — the documented maximum per model

A request carries an output cap (`max_tokens`, `max_completion_tokens` at OpenAI, `num_predict`
at Ollama). Left unpinned, the cap is **the model's documented maximum**, from the
`LlmModelOutputLimits` catalogue in `Orkeon.Constants.Llm`: a fixed 4096 would let a reasoning
model spend the budget thinking and answer empty. A pinned value (`Llm:MaxTokens`, a
profile, a crew's `max_tokens`) always wins; a model the catalogue does not know keeps
the 4096 fallback. Only documented figures go in, each read on 2026-09-19:

| Provider | Model | Cap sent | Source | Note |
|---|---|---|---|---|
| OpenAI (and Azure deployments of the same ids) | `gpt-5.6-sol`, `gpt-6-astra` | 128 000 | developers.openai.com/api/docs/models | reasoning counts inside the cap |
| Anthropic | `claude-sonnet-5`, `claude-opus-5`, `claude-fable-5-1` (dated variants follow the family) | 128 000 | platform.claude.com/docs/en/about-claude/models/overview | thinking counts inside `max_tokens`; the field is required, an unknown Claude gets 4096 |
| Gemini | `gemini-3.7-flash`, `gemini-3.8-flash` (also under `google/…` on OpenRouter and bare on Mammouth) | 65 536 | ai.google.dev/gemini-api/docs/models | includes thought tokens; 65 537 is a 400 |
| DeepSeek | `deepseek-flash` (and the retired `deepseek-v4-flash*` names it routes) | 393 216 | api-docs.deepseek.com/api/create-chat-completion | "1 to 384K"; 384 000 on Mammouth |
| Kimi | `kimi-k3` | 131 072 | platform.kimi.ai/docs/guide/kimi-k3-quickstart | the vendor's own `max_completion_tokens` default; real bound 1M − prompt |
| Kimi | `kimi-k3.1-pro` | 131 072 | platform.kimi.ai/docs/guide/kimi-k3-quickstart | **a pin, not a documented cap**: the K3 default carried over to the default model of 2026-11-11; `kimi-k3` alone would not claim it (`.` is no separator), and the 4096 fallback starved K3 |
| Kimi | `kimi-k2.6` | 131 072 | platform.kimi.ai/docs/guide/troubleshooting | **a pin, not a documented cap**: the bound is 256K − prompt; a rejection drops the field on the retry |
| Qwen | `qwen3.7-plus`, `qwen3.8-flash`, `qwen3.8-max` | 131 072 | alibabacloud.com/help/en/model-studio | the chain of thought has its own `thinking_budget`; 65 500 on Mammouth |
| Z.AI | `glm-5.2`, `glm-5.3`, `glm-5.3-flash`, `glm-5.3-flashx` | 131 072 | docs.z.ai/guides/overview/concept-param | schema maximum; default 65 536 |
| Z.AI | `glm-4.6v-flash` | 32 768 | docs.z.ai/guides/vlm/glm-4.6v | — |
| MiniMax | `MiniMax-M2` | 131 072 | platform.minimax.io/docs/guides/models-intro | "128k (including CoT)"; **M3 is absent**, default since 2026-11-11 — the vendor publishes no output figure, so it runs on the 4096 fallback (512k appears only as a benchmark setting; 512 000 on Mammouth) |
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
surfaces unchanged.

## Per-model mandatory parameter values

Some models refuse a request unless a parameter carries one specific value. This is a
different animal from the per-model capability gaps above (a model lacking thinking or
vision): here the capability exists, but the model dictates the value, and the vendor answers
anything else with a 400. **The constraint is per model, never per provider** — the same
vendor ships models with opposite demands, so a provider-level pin is one campaign away from
breaking the sibling model. Every row below was measured live; nothing is inferred.

| Provider | Model | Parameter | Mandatory value | Vendor's own words | Measured |
|---|---|---|---|---|---|
| Kimi | `kimi-k2.6` | `temperature` | `1` | `invalid temperature: only 1 is allowed for this model` | 2026-08-03 |
| Kimi | `kimi-k2.7-code` | `temperature` | `1` — a buffered call is retried by the provider with that value, a stream is refused | `invalid temperature: only 1 is allowed for this model` | 2026-10-07 |
| Kimi | `kimi-k3.1-pro`, `kimi-k3.1-pro-highspeed`, `kimi-k2.7-code-highspeed` | `temperature` | `1` — every streaming mode and M2, M5, M9, M10 refused at 0; on the K3 family the provider now leaves the field out (with a warning) on every path | `invalid temperature: only 1 is allowed for this model` | 2026-11-11 |
| OpenAI | `gpt-5.6-sol` | `temperature` | `1` | `'temperature' does not support 0 with this model. Only the default (1) value is supported.` | 2026-08-30 |
| OpenAI | `gpt-5.6-sol` | `reasoning_effort` | `"none"` when the request carries function tools on `/v1/chat/completions` | `Function tools with reasoning_effort are not supported for gpt-5.6-sol in /v1/chat/completions. To use function tools, use /v1/responses or set reasoning_effort to 'none'.` | 2026-08-30 |
| OpenAI | `gpt-6-astra` | `temperature` | `1` | `'temperature' does not support 0 with this model. Only the default (1) value is supported.` | 2026-09-21 |
| OpenAI | `gpt-6-astra` | `reasoning_effort` | **no `"none"` exists** (`low`, `medium`, `high`, `xhigh`) — the Sol workaround is impossible, so function tools stay refused on `/v1/chat/completions` until the dialect speaks `/v1/responses` | `'reasoning_effort' does not support 'none' with this model. Supported values are: 'low', 'medium', 'high', and 'xhigh'.` — and without it, `Function tools with reasoning_effort are not supported for gpt-6-astra in /v1/chat/completions. To use function tools, use /v1/responses` | 2026-09-21 |
| Anthropic | `claude-sonnet-5` | `temperature` | `1`, or omit the field | `` `temperature` is deprecated for this model.`` (1 and omission pass; 0 and 0.7 do not) | 2026-08-30 |
| OpenAI | `gpt-5.6-terra`, `gpt-5.6-luna`, `gpt-6-sol`, `gpt-6-luna` | `temperature` and `reasoning_effort` | as `gpt-5.6-sol`: `1`, and `"none"` with function tools | the two sentences of `gpt-5.6-sol`, the model name aside | 2026-10-07 |
| OpenAI | `gpt-6.1-sol` | `temperature` and `reasoning_effort` | as `gpt-6-astra`: `1`, and **no `"none"` exists** | `'reasoning_effort' does not support 'none' with this model. Supported values are: 'low', 'medium', 'high', and 'xhigh'.` | 2026-10-07 |
| Anthropic | `claude-opus-5`, `claude-opus-5-5`, `claude-sonnet-5-5`, `claude-haiku-5-5` | `temperature` | `1`, or omit the field | `` `temperature` is deprecated for this model.`` | 2026-10-07 |
| Anthropic | `claude-fable-5-2` | `temperature` | `1`, or omit the field | refused like the rest of the 5 family — the sentence was not recorded for this model | 2026-11-11 |
| OpenAI | `gpt-6.1-terra` | `temperature` and `reasoning_effort` | as `gpt-5.6-sol`: `1`, and `"none"` with function tools — unlike `gpt-6.1-sol`, this model has a `none` | the two sentences of `gpt-5.6-sol`, the model name aside; 12/12 with those two pins | 2026-11-11 |
| Mistral | `mistral-large-4` | `reasoning_effort` | `high` or `none` only | `reasoning_effort low is not supported for this model, supported values: [<ReasoningEffort.high: 'high'>, <ReasoningEffort.none: 'none'>]` | 2026-10-07 |
| Mistral | `mistral-small-2603` | `reasoning_effort` | `high` or `none` only | `reasoning_effort='low' is not supported for this model. Must be one of (<ReasoningEffort.none: 'none'>, <ReasoningEffort.high: 'high'>)` | 2026-10-07 |
| Mistral | `mistral-medium-2604` | `reasoning_effort` | `high` or `none` only | `reasoning_effort low is not supported for this model, supported values: [<ReasoningEffort.high: 'high'>, <ReasoningEffort.none: 'none'>]` | 2026-08-30 |
| Mistral | `mistral-medium-2604` | `top_p` | explicit `1` when `temperature` is 0 and reasoning is on (omission is NOT 1 there) | `top_p must be 1 when using greedy sampling.` | 2026-08-30 |

On OpenAI, the GPT-5.6 and GPT-6 tiers take function tools on `/v1/chat/completions` only with
`reasoning_effort: none` (`gpt-6-astra` and `gpt-6.1-sol` have no `none`: tools need
`/v1/responses`). The pin must reach the API as `none`: on 2026-11-11 a first pass had Orkeon
lowering it to `low`, which turned M2 and M5 red on every OpenAI model until the fix.

A temperature nothing sets is not sent: a host that pins none runs these models on their
own default, which they accept — `orkeon init` writes none. A host that pins one must pin
the mandated value.

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
accepts it. Real-execution evidence is archived separately by the campaign kit
([`llmproviders-test/`](https://github.com/Orkeon/orkeon/tree/main/llmproviders-test), whose
README carries the campaign journal; the latest result per model is on
[LLM campaign results](llm-campaign-results.md)); run a campaign with
`orkeon llm probe --provider <name> --archive <dir>`, or the kit's `run-campaign.sh`
([validating a provider](../architecture/llm-providers.md#validating-a-provider-against-its-real-api)).
