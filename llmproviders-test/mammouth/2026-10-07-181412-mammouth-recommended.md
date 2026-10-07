# Campaign Mammouth AI — `mammouth-recommended`

|  |  |
|---|---|
| **Provider** | `mammouth` |
| **Model** | `mammouth-recommended` |
| **Endpoint** | `api.mammouth.ai` |
| **Timestamp (UTC)** | 2026-10-07T18:14:15Z |
| **Orkeon version** | 1.0.0-rc.4 |
| **Commit** | `77ac8a969` |
| **Modes exercised** | M1 |
| **Temperature** | 0 |
| **Proof quality** | archived output |

## Results

| Mode | Protocol | Result | Detail | Duration |
|---|---|---|---|---|
| M1 | Simple chat | ✅ | 6 char(s), tokens=19 | 1811 ms |

✅ **Success** — 1 mode(s) validated · 1 ✅ · 0 ❌ · 0 ➖

> ➖ = mode not applicable to this provider or this model. Nothing was exercised, so there is
> nothing to fix — it is an absence of capability, not a defect.

## Points of attention (matrix §6.18)

- Sixteenth sheet's twin, WITHOUT execution proof: integrated on 2026-09-18 from the vendor documentation and cold calls (LLM-09), no key available. The first campaign is the pending proof — the Mistral lesson applies in full until a live M1 is archived.
- French multi-model subscription (12 / 24 / 72 € per month with 2 / 4 / 10 $ of API credits included, pay-as-you-go from the API settings); the API is, on three concordant clues, a LiteLLM proxy — what is undocumented is PROBABLY passed through to the upstream. Bare vendor identifiers (`gpt-5.6-sol`, `claude-sonnet-5`, `gemini-3.7-flash`): always name the provider by baseUrl or by key, never by model name.
- The included credits (2 $ on Starter) cover a full campaign of the harness on `gemini-3.7-flash` (M1–M10, M12, M13). `mammouth-recommended` is refused as a default (routed alias). Cheap text-only alternative: `deepseek-v4-flash` on M5/M7/M8.
- Question 1 — `tools`: passed through (M5 green) or ignored (M6, the text fallback, takes over)?
- Question 2 — `response_format`: refused, accepted-non-binding (MiniMax) or binding — and with a schema? The declaration rises accordingly, as Gemini's did.
- Question 3 — `reasoning_effort`: accepted? And in WHICH field does the trace come back — LiteLLM often normalises to `reasoning_content`, in which case EffortOnly is declared without a line of parsing.
- Question 4 — M9 on the default (`gemini-3.7-flash` sees in direct).
- Question 5 — M10: does the proxy relay `prompt_tokens_details.cached_tokens`?
- Question 6 — M12: the shape of the 429 and the presence of `Retry-After` (quotas are not published).
- Question 7 — the shape of the key: for the documentation, never for an inference, and for `LogSanitizer` — if it is neither `sk-…` nor covered by the `Bearer` pattern alone, its prefix joins `VendorPrefixedKeyPattern` and `LogSanitizerTests`.
- Question 8 — `mammouth-recommended`: which model answers (`model` echo), one request.

## To carry into the matrix

Journal (§7):

```
| 2026-10-07 | Mammouth AI `mammouth-recommended` | campaign `llmproviders-test` | M1 | ✅ | `llmproviders-test/mammouth/2026-10-07-181412-mammouth-recommended.md` | Archived output |
```

Model table (§6.18):

```
| `mammouth-recommended` | | | M1 | ✅ | 2026-10-07 | 1.0.0-rc.4 | campaign 2026-10-07-181412-mammouth-recommended.md |
```

<!-- orkeon-campaign-json -->
```json
{
  "provider": "mammouth",
  "model": "mammouth-recommended",
  "endpointHost": "api.mammouth.ai",
  "orkeonVersion": "1.0.0-rc.4",
  "commit": "77ac8a969",
  "timestampUtc": "2026-10-07T18:14:15Z",
  "temperature": 0,
  "passed": 1,
  "failed": 0,
  "notApplicable": 0,
  "modes": [
    {
      "mode": "M1",
      "outcome": "passed",
      "symbol": "\u2705",
      "detail": "6 char(s), tokens=19",
      "elapsedMs": 1811
    }
  ]
}
```
