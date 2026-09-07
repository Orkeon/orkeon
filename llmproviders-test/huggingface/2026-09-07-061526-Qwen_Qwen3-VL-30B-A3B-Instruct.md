# Campaign HuggingFace — `Qwen/Qwen3-VL-30B-A3B-Instruct`

|  |  |
|---|---|
| **Provider** | `huggingface` |
| **Model** | `Qwen/Qwen3-VL-30B-A3B-Instruct` |
| **Endpoint** | `router.huggingface.co` |
| **Timestamp (UTC)** | 2026-09-07T06:15:28Z |
| **Orkeon version** | 1.0.0-rc.3 |
| **Commit** | `14e1d300` |
| **Modes exercised** | M9 |
| **Temperature** | 0 |
| **Proof quality** | archived output |

## Results

| Mode | Protocol | Result | Detail | Duration |
|---|---|---|---|---|
| M9 | Vision / multimodal | ✅ | image read: number 73 and red background both named | 1890 ms |

✅ **Success** — 1 mode(s) validated · 1 ✅ · 0 ❌ · 0 ➖

> ➖ = mode not applicable to this provider or this model. Nothing was exercised, so there is
> nothing to fix — it is an absence of capability, not a defect.

## Points of attention (matrix §6.11)

- The router resolves the identifier to the partner (the campaign M9 answers under the name `Meta-Llama-3.1-8B-Instruct-Turbo`): the default is text-only, its red M9 is the D-03 proof, the companion `Qwen3-VL-30B-A3B-Instruct` brings the complement.
- The compiled default `meta-llama/Llama-3.1-8B-Instruct`, long reported unreachable through the router, is CLEARED: live M1 measured on 2026-08-30 (20 tokens, 1.5 s). The catalog aligns with it — the campaign validates what the platform delivers.
- G-01 closed: the old `api-inference.huggingface.co` is removed, the default is `router.huggingface.co/v1`.
- Also exercise a routing suffix (`:cheapest`, `:fastest`, `:<provider>`) — G-23.
- The effective context and output depend on the routed provider, not on the model: `GET /v1/models` gives the values per provider.

## To carry into the matrix

Journal (§7):

```
| 2026-09-07 | HuggingFace `Qwen/Qwen3-VL-30B-A3B-Instruct` | campaign `llmproviders-test` | M9 | ✅ | `llmproviders-test/huggingface/2026-09-07-061526-Qwen_Qwen3-VL-30B-A3B-Instruct.md` | Archived output |
```

Model table (§6.11):

```
| `Qwen/Qwen3-VL-30B-A3B-Instruct` | | | M9 | ✅ | 2026-09-07 | 1.0.0-rc.3 | campaign 2026-09-07-061526-Qwen_Qwen3-VL-30B-A3B-Instruct.md |
```

<!-- orkeon-campaign-json -->
```json
{
  "provider": "huggingface",
  "model": "Qwen/Qwen3-VL-30B-A3B-Instruct",
  "endpointHost": "router.huggingface.co",
  "orkeonVersion": "1.0.0-rc.3",
  "commit": "14e1d300",
  "timestampUtc": "2026-09-07T06:15:28Z",
  "temperature": 0,
  "passed": 1,
  "failed": 0,
  "notApplicable": 0,
  "modes": [
    {
      "mode": "M9",
      "outcome": "passed",
      "symbol": "\u2705",
      "detail": "image read: number 73 and red background both named",
      "elapsedMs": 1890
    }
  ]
}
```
