# Campaign HuggingFace — `Qwen/Qwen3-VL-30B-A3B-Instruct`

|  |  |
|---|---|
| **Provider** | `huggingface` |
| **Model** | `Qwen/Qwen3-VL-30B-A3B-Instruct` |
| **Endpoint** | `router.huggingface.co` |
| **Timestamp (UTC)** | 2026-11-11T07:44:06Z |
| **Commit** | `f6f6f35fc` |
| **Modes exercised** | M9 |
| **Temperature** | 0 |
| **Proof quality** | archived output |

## Results

| Mode | Protocol | Result | Detail | Duration |
|---|---|---|---|---|
| M9 | Vision / multimodal | ✅ | image read: number 73 and red background both named | 1744 ms |

✅ **Success** — 1 mode(s) validated · 1 ✅ · 0 ❌ · 0 ➖

> ➖ = mode not applicable to this provider or this model. Nothing was exercised, so there is
> nothing to fix — it is an absence of capability, not a defect.

## Points of attention (matrix §6.11)

Kept current in `llmproviders-test/lib/catalog.json`, `providers.huggingface.notes`.

## To carry into the matrix

Journal (§7):

```
| 2026-11-11 | HuggingFace `Qwen/Qwen3-VL-30B-A3B-Instruct` | campaign `llmproviders-test` | M9 | ✅ | `llmproviders-test/huggingface/2026-11-11-074404-Qwen_Qwen3-VL-30B-A3B-Instruct.md` | Archived output |
```

Model table (§6.11):

```
| `Qwen/Qwen3-VL-30B-A3B-Instruct` | | | M9 | ✅ | 2026-11-11 | f6f6f35fc | campaign 2026-11-11-074404-Qwen_Qwen3-VL-30B-A3B-Instruct.md |
```

<!-- orkeon-campaign-json -->
```json
{
  "provider": "huggingface",
  "model": "Qwen/Qwen3-VL-30B-A3B-Instruct",
  "endpointHost": "router.huggingface.co",
  "commit": "f6f6f35fc",
  "timestampUtc": "2026-11-11T07:44:06Z",
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
      "elapsedMs": 1744
    }
  ]
}
```
