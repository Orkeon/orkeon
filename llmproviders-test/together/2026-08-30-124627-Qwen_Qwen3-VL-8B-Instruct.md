# Campagne Together AI — `Qwen/Qwen3-VL-8B-Instruct`

|  |  |
|---|---|
| **Provider** | `together` |
| **Modèle** | `Qwen/Qwen3-VL-8B-Instruct` |
| **Endpoint** | `api.together.xyz` |
| **Horodatage (UTC)** | 2026-08-30T12:46:30Z |
| **Version Orkéon** | 1.0.0-rc.2 |
| **Commit** | `8942d304` |
| **Modes exercés** | M9 |
| **Température** | 0 |
| **Qualité de preuve** | sortie archivée |

## Résultats

| Mode | Protocole | Résultat | Détail | Durée |
|---|---|---|---|---|
| M9 | Vision / multimodal | ❌ | Together AI API error: BadRequest - {
  "id": "oyGk652-2kFHot-a333e6076c7fc6ca",
  "error": {
    "message": "Unable to access non-serverless model Qwen/Qwen3-VL-8B-Instruct. Please visit https://api.together.ai/models/Qwen/Qwen3-VL-8B-Instruct to create and start a new dedicated endpoint for the model.",
    "type": "invalid_request_error",
    "param": null,
    "code": "model_not_available"
  }
} — the request carried an image and Together AI declares vision support, but that is declared per provider while models differ: 'Qwen/Qwen3-VL-8B-Instruct' may be text-only. Try a vision model, or send text only. | 1987 ms |

❌ **Échec** — 1 mode(s) en échec · 0 ✅ · 1 ❌ · 0 ➖

> ➖ = mode non applicable à ce provider ou à ce modèle. Rien n'a été exercé, il n'y a
> donc rien à corriger — c'est une absence de capacité, pas un défaut.

## Points de vigilance (matrice §6.6)

- Le défaut `Llama-3.3-70B-Instruct-Turbo` est texte seul : son M9 rouge est la preuve D-03 (campagne 2026-08-30), le compagnon `Qwen3-VL-8B-Instruct` apporte le fait complémentaire.
- Endpoint et modèle par défaut à jour. Un passage M1–M5 suffit.
- Together sert DeepSeek-V4-Pro à 512 K là où DeepSeek le sert à 1 M — écart de service à vérifier en M11.

## À reporter dans la matrice

Journal (§7) :

```
| 2026-08-30 | Together AI `Qwen/Qwen3-VL-8B-Instruct` | campagne `llmproviders-test` | M9 | ❌ | `llmproviders-test/together/2026-08-30-124627-Qwen_Qwen3-VL-8B-Instruct.md` | Sortie archivée |
```

Tableau modèles (§6.6) :

```
| `Qwen/Qwen3-VL-8B-Instruct` | | | M9 | ❌ | 2026-08-30 | 1.0.0-rc.2 | campagne 2026-08-30-124627-Qwen_Qwen3-VL-8B-Instruct.md |
```

<!-- orkeon-campaign-json -->
```json
{
  "provider": "together",
  "model": "Qwen/Qwen3-VL-8B-Instruct",
  "endpointHost": "api.together.xyz",
  "orkeonVersion": "1.0.0-rc.2",
  "commit": "8942d304",
  "timestampUtc": "2026-08-30T12:46:30Z",
  "temperature": 0,
  "passed": 0,
  "failed": 1,
  "notApplicable": 0,
  "modes": [
    {
      "mode": "M9",
      "outcome": "failed",
      "symbol": "\u274C",
      "detail": "Together AI API error: BadRequest - {\n  \u0022id\u0022: \u0022oyGk652-2kFHot-a333e6076c7fc6ca\u0022,\n  \u0022error\u0022: {\n    \u0022message\u0022: \u0022Unable to access non-serverless model Qwen/Qwen3-VL-8B-Instruct. Please visit https://api.together.ai/models/Qwen/Qwen3-VL-8B-Instruct to create and start a new dedicated endpoint for the model.\u0022,\n    \u0022type\u0022: \u0022invalid_request_error\u0022,\n    \u0022param\u0022: null,\n    \u0022code\u0022: \u0022model_not_available\u0022\n  }\n} \u2014 the request carried an image and Together AI declares vision support, but that is declared per provider while models differ: \u0027Qwen/Qwen3-VL-8B-Instruct\u0027 may be text-only. Try a vision model, or send text only.",
      "elapsedMs": 1987
    }
  ]
}
```
