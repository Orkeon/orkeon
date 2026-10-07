# Campaign OpenRouter — `deepseek/deepseek-v4.1-flash`

|  |  |
|---|---|
| **Provider** | `openrouter` |
| **Model** | `deepseek/deepseek-v4.1-flash` |
| **Endpoint** | `openrouter.ai` |
| **Timestamp (UTC)** | 2026-10-07T18:14:35Z |
| **Orkeon version** | 1.0.0-rc.4 |
| **Commit** | `77ac8a969` |
| **Modes exercised** | M5, M7, M8, M10 |
| **Temperature** | 0 |
| **Proof quality** | archived output |

## Results

| Mode | Protocol | Result | Detail | Duration |
|---|---|---|---|---|
| M5 | Native tool calling | ✅ | orkeon_probe_lookup(city=Lyon) called, result accepted | 1785 ms |
| M7 | Thinking / reasoning | ✅ | reasoning trace returned, tokens=161 | 13006 ms |
| M8 | response_format JSON | ✅ | valid JSON returned | 1186 ms |
| M10 | Context cache | ✅ | second call: 8064 cached token(s), ratio=1.00 | 1173 ms |

✅ **Success** — 4 mode(s) validated · 4 ✅ · 0 ❌ · 0 ➖

> ➖ = mode not applicable to this provider or this model. Nothing was exercised, so there is
> nothing to fix — it is an absence of capability, not a defect.

## Points of attention (matrix §6.17)

- Sixteenth sheet, WITHOUT execution proof: integrated on 2026-09-18 from the vendor documentation and cold calls (LLM-09), no key available. The first campaign is the pending proof — the Mistral lesson (compiled default never served) applies in full until a live M1 is archived.
- Marketplace: 445 models from 60 vendors on 2026-09-18, `vendor/model` identifiers are mandatory (`anthropic/claude-sonnet-5`), suffixes `:free` / `:nitro` / `:floor`, router slugs `openrouter/auto` (refused as a default: the served model drifts, price -1). The default is the fleet's Gemini default under its marketplace id — one model, three transports: a red here with a green in direct (2026-08-30) is a fact about the transport.
- Cheap text-only alternative for the transport comparison: `deepseek/deepseek-v4-flash` on M7/M8.
- Question 1 — the `sk-or-v1-` key prefix is documented by secondary sources only: confirm it on the first key before the factory infers from it (the `xai-` pattern); the inference line waits for that.
- Question 2 — `message.reasoning` / `delta.reasoning` really populated on a reasoning model (`deepseek/deepseek-v4-flash`), and the shape of `reasoning_details[]` for a later sheet.
- Question 3 — `usage.cost` present on the buffered path AND in the final `usage` chunk of a stream (it arrives unasked; `stream_options.include_usage` is documented deprecated, no effect).
- Question 4 — M8: a `json_schema` WITHOUT `provider.require_parameters` — does OpenRouter route to an endpoint that ignores it (the MiniMax case)? If so the JsonSchema declaration is too optimistic and D-09 reopens for that one field.
- Question 5 — M10: `prompt_tokens_details.cached_tokens` and `cache_write_tokens` on the second request over an 8k prefix.
- Question 6 — `: OPENROUTER PROCESSING` keep-alive comment lines observed at least once in the `LlmLoggingDelegatingHandler` logs, with no effect on the content.
- Question 7 — the `model` echoed behind `openrouter/auto` (one request, for the documentation).

## To carry into the matrix

Journal (§7):

```
| 2026-10-07 | OpenRouter `deepseek/deepseek-v4.1-flash` | campaign `llmproviders-test` | M5, M7, M8, M10 | ✅ | `llmproviders-test/openrouter/2026-10-07-181417-deepseek_deepseek-v4.1-flash.md` | Archived output |
```

Model table (§6.17):

```
| `deepseek/deepseek-v4.1-flash` | | | M5, M7, M8, M10 | ✅ | 2026-10-07 | 1.0.0-rc.4 | campaign 2026-10-07-181417-deepseek_deepseek-v4.1-flash.md |
```

<!-- orkeon-campaign-json -->
```json
{
  "provider": "openrouter",
  "model": "deepseek/deepseek-v4.1-flash",
  "endpointHost": "openrouter.ai",
  "orkeonVersion": "1.0.0-rc.4",
  "commit": "77ac8a969",
  "timestampUtc": "2026-10-07T18:14:35Z",
  "temperature": 0,
  "passed": 4,
  "failed": 0,
  "notApplicable": 0,
  "modes": [
    {
      "mode": "M5",
      "outcome": "passed",
      "symbol": "\u2705",
      "detail": "orkeon_probe_lookup(city=Lyon) called, result accepted",
      "elapsedMs": 1785
    },
    {
      "mode": "M7",
      "outcome": "passed",
      "symbol": "\u2705",
      "detail": "reasoning trace returned, tokens=161",
      "elapsedMs": 13006
    },
    {
      "mode": "M8",
      "outcome": "passed",
      "symbol": "\u2705",
      "detail": "valid JSON returned",
      "elapsedMs": 1186
    },
    {
      "mode": "M10",
      "outcome": "passed",
      "symbol": "\u2705",
      "detail": "second call: 8064 cached token(s), ratio=1.00",
      "elapsedMs": 1173
    }
  ]
}
```
