# Campagne HuggingFace — `Qwen/Qwen3-VL-30B-A3B-Instruct`

|  |  |
|---|---|
| **Provider** | `huggingface` |
| **Modèle** | `Qwen/Qwen3-VL-30B-A3B-Instruct` |
| **Endpoint** | `router.huggingface.co` |
| **Horodatage (UTC)** | 2026-08-30T12:46:33Z |
| **Version Orkéon** | 1.0.0-rc.2 |
| **Commit** | `8942d304` |
| **Modes exercés** | M9 |
| **Température** | 0 |
| **Qualité de preuve** | sortie archivée |

## Résultats

| Mode | Protocole | Résultat | Détail | Durée |
|---|---|---|---|---|
| M9 | Vision / multimodal | ✅ | image read: number 73 and red background both named | 1608 ms |

✅ **Succès** — 1 mode(s) validé(s) · 1 ✅ · 0 ❌ · 0 ➖

> ➖ = mode non applicable à ce provider ou à ce modèle. Rien n'a été exercé, il n'y a
> donc rien à corriger — c'est une absence de capacité, pas un défaut.

## Points de vigilance (matrice §6.11)

- Le routeur résout l'identifiant vers le partenaire (le M9 de campagne répond au nom `Meta-Llama-3.1-8B-Instruct-Turbo`) : le défaut est texte seul, son M9 rouge est la preuve D-03, le compagnon `Qwen3-VL-30B-A3B-Instruct` apporte le complément.
- Le défaut compilé `meta-llama/Llama-3.1-8B-Instruct`, longtemps signalé inatteignable via le routeur, est INNOCENTÉ : M1 vivant mesuré le 2026-08-30 (20 tokens, 1.5 s). Le catalogue s'aligne sur lui — la campagne valide ce que la plateforme livre.
- G-01 fermé : l'ancien `api-inference.huggingface.co` est supprimé, le défaut est `router.huggingface.co/v1`.
- Exercer en plus un suffixe de routage (`:cheapest`, `:fastest`, `:<provider>`) — G-23.
- Le contexte et la sortie effectifs dépendent du fournisseur routé, pas du modèle : `GET /v1/models` donne les valeurs par fournisseur.

## À reporter dans la matrice

Journal (§7) :

```
| 2026-08-30 | HuggingFace `Qwen/Qwen3-VL-30B-A3B-Instruct` | campagne `llmproviders-test` | M9 | ✅ | `llmproviders-test/huggingface/2026-08-30-124631-Qwen_Qwen3-VL-30B-A3B-Instruct.md` | Sortie archivée |
```

Tableau modèles (§6.11) :

```
| `Qwen/Qwen3-VL-30B-A3B-Instruct` | | | M9 | ✅ | 2026-08-30 | 1.0.0-rc.2 | campagne 2026-08-30-124631-Qwen_Qwen3-VL-30B-A3B-Instruct.md |
```

<!-- orkeon-campaign-json -->
```json
{
  "provider": "huggingface",
  "model": "Qwen/Qwen3-VL-30B-A3B-Instruct",
  "endpointHost": "router.huggingface.co",
  "orkeonVersion": "1.0.0-rc.2",
  "commit": "8942d304",
  "timestampUtc": "2026-08-30T12:46:33Z",
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
      "elapsedMs": 1608
    }
  ]
}
```
