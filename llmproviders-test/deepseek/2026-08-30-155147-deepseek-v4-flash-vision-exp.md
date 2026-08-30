# Campagne DeepSeek — `deepseek-v4-flash-vision-exp`

|  |  |
|---|---|
| **Provider** | `deepseek` |
| **Modèle** | `deepseek-v4-flash-vision-exp` |
| **Endpoint** | `api.deepseek.com` |
| **Horodatage (UTC)** | 2026-08-30T15:51:50Z |
| **Version Orkéon** | 1.0.0-rc.2 |
| **Commit** | `6388cecb` |
| **Modes exercés** | M9 |
| **Température** | 0 |
| **Qualité de preuve** | sortie archivée |

## Résultats

| Mode | Protocole | Résultat | Détail | Durée |
|---|---|---|---|---|
| M9 | Vision / multimodal | ✅ | image read: number 73 and red background both named | 1854 ms |

✅ **Succès** — 1 mode(s) validé(s) · 1 ✅ · 0 ❌ · 0 ➖

> ➖ = mode non applicable à ce provider ou à ce modèle. Rien n'a été exercé, il n'y a
> donc rien à corriger — c'est une absence de capacité, pas un défaut.

## Points de vigilance (matrice §6.7)

- À **revalider** malgré son ancienneté de preuve : LLM-01 a changé son modèle par défaut et LLM-02 l'a refactoré sur le socle de capacités.
- M7 doit confirmer le round-trip de `reasoning_content` — sans lui l'API renvoie un 400.
- M8 doit confirmer la garde « mot *json* absent du prompt ».
- Longtemps seul provider du parc sans modèle vision, jusqu'à `deepseek-v4-flash-vision-exp` (mesuré 2026-08-30 : il lit une image base64). Le défaut `deepseek-v4-flash` reste texte seul : son M9 rouge est la preuve D-03, le compagnon vision apporte le fait complémentaire.

## À reporter dans la matrice

Journal (§7) :

```
| 2026-08-30 | DeepSeek `deepseek-v4-flash-vision-exp` | campagne `llmproviders-test` | M9 | ✅ | `llmproviders-test/deepseek/2026-08-30-155147-deepseek-v4-flash-vision-exp.md` | Sortie archivée |
```

Tableau modèles (§6.7) :

```
| `deepseek-v4-flash-vision-exp` | | | M9 | ✅ | 2026-08-30 | 1.0.0-rc.2 | campagne 2026-08-30-155147-deepseek-v4-flash-vision-exp.md |
```

<!-- orkeon-campaign-json -->
```json
{
  "provider": "deepseek",
  "model": "deepseek-v4-flash-vision-exp",
  "endpointHost": "api.deepseek.com",
  "orkeonVersion": "1.0.0-rc.2",
  "commit": "6388cecb",
  "timestampUtc": "2026-08-30T15:51:50Z",
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
      "elapsedMs": 1854
    }
  ]
}
```
