# Campaign DeepSeek — `deepseek-v4-flash-vision-exp`

|  |  |
|---|---|
| **Provider** | `deepseek` |
| **Model** | `deepseek-v4-flash-vision-exp` |
| **Endpoint** | `api.deepseek.com` |
| **Timestamp (UTC)** | 2026-09-07T06:15:43Z |
| **Orkeon version** | 1.0.0-rc.3 |
| **Commit** | `14e1d300` |
| **Modes exercised** | M9 |
| **Temperature** | 0 |
| **Proof quality** | archived output |

## Results

| Mode | Protocol | Result | Detail | Duration |
|---|---|---|---|---|
| M9 | Vision / multimodal | ❌ | image transited (red background named) but the number 73 was not read: 75 red | 1895 ms |

❌ **Failure** — 1 failed mode(s) · 0 ✅ · 1 ❌ · 0 ➖

> ➖ = mode not applicable to this provider or this model. Nothing was exercised, so there is
> nothing to fix — it is an absence of capability, not a defect.

## Points of attention (matrix §6.7)

- To **revalidate** despite the age of its proof: LLM-01 changed its default model and LLM-02 refactored it onto the capabilities base.
- M7 must confirm the `reasoning_content` round-trip — without it the API returns a 400.
- M8 must confirm the “word *json* absent from the prompt” guard.
- Long the only provider in the fleet without a vision model, until `deepseek-v4-flash-vision-exp` (measured 2026-08-30: it reads a base64 image). The default `deepseek-v4-flash` stays text-only: its red M9 is the D-03 proof, the vision companion brings the complementary fact.

## To carry into the matrix

Journal (§7):

```
| 2026-09-07 | DeepSeek `deepseek-v4-flash-vision-exp` | campaign `llmproviders-test` | M9 | ❌ | `llmproviders-test/deepseek/2026-09-07-061540-deepseek-v4-flash-vision-exp.md` | Archived output |
```

Model table (§6.7):

```
| `deepseek-v4-flash-vision-exp` | | | M9 | ❌ | 2026-09-07 | 1.0.0-rc.3 | campaign 2026-09-07-061540-deepseek-v4-flash-vision-exp.md |
```

<!-- orkeon-campaign-json -->
```json
{
  "provider": "deepseek",
  "model": "deepseek-v4-flash-vision-exp",
  "endpointHost": "api.deepseek.com",
  "orkeonVersion": "1.0.0-rc.3",
  "commit": "14e1d300",
  "timestampUtc": "2026-09-07T06:15:43Z",
  "temperature": 0,
  "passed": 0,
  "failed": 1,
  "notApplicable": 0,
  "modes": [
    {
      "mode": "M9",
      "outcome": "failed",
      "symbol": "\u274C",
      "detail": "image transited (red background named) but the number 73 was not read: 75 red",
      "elapsedMs": 1895
    }
  ]
}
```
