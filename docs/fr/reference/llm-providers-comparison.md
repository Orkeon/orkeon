> 🇬🇧 [English version](../../reference/llm-providers-comparison.md)

# Comparatif des fournisseurs LLM — Orkeon

> État au 2026-07-27, dérivé du code source (`src/core/Orkeon.Infrastructure/LLMs/`)
> et des `LlmProviderCapabilities` déclarées par chaque fournisseur.
> Légende : ✓ supporté · ✗ absent · ◐ partiel/générique.

| Fournisseur | Classe de base | Streaming SSE | Tool calling natif | Chat multi-tours (rôles tool) | Message système | top_p / stop | Grammaire GBNF | response_format | thinking | Vision | reasoning_content round-trip | Cache prompt | Métriques timing | Résilience Polly | Sanitization clé API |
|---|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| **OpenAI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Azure OpenAI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Anthropic** | HttpLlmProviderBase | ✓ natif | ✓ | ✓ | ✓ (natif, séparé) | ✓ | ✗ | ✓ schema | ✓ toggle | ✓ | ✗ | ✓ explicite | ✗ | ✓ | ✓ |
| **DeepSeek** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ object | ✓ toggle | ✗ | ✓ | ✓ métriques | ✗ | ✓ | ✓ |
| **Z.AI (GLM)** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ object | ✓ toggle | ✓ | ✗ | ✓ métriques | ✗ | ✓ | ✓ |
| **Groq** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✓ | ✓ | ✓ |
| **Together AI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema | ✗ | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Mistral AI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Qwen** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ object | ✓ budget | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Kimi / Moonshot** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ object | ✓ toggle | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Google Gemini** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ (non déclaré — surface compat non documentée) | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **HuggingFace** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ object | ✗ | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Ollama** | HttpLlmProviderBase | ✓ | ✓ (`/api/chat`) | ✓ (`/api/chat`) | ✓ (prepend) | ✗ | ✓ | ✓ schema | ✓ toggle | ✓ (`images`) | ✗ | ✗ | ✗ | ✓ | ✓ |

## Comment lire les colonnes de capacités

`response_format`, `thinking` et `Vision` ne sont pas maintenus à la main ici : chaque fournisseur
déclare un value object `LlmProviderCapabilities`, et `OpenAICompatibleProviderBase` le traduit une
seule fois dans le dialecte OpenAI. Anthropic, Ollama et Qwen surchargent le hook parce que leurs
API parlent leur propre dialecte.

- **`response_format`** — `object` signifie que l'API garantit un JSON bien formé ; `schema`
  signifie qu'elle valide contre un JSON Schema côté serveur. Un schéma envoyé à un fournisseur
  qui ne supporte que `object` est **rétrogradé avec un avertissement**, jamais en silence.
  **Anthropic est schema-only** : il n'a pas d'équivalent de `json_object`, donc une demande JSON
  sans schéma y est signalée plutôt qu'envoyée.
- **`thinking`** — `effort` n'accepte qu'une indication de niveau ; `toggle` peut en plus activer
  et désactiver le raisonnement ; `budget` accepte en outre un budget de tokens explicite (Qwen
  uniquement — Anthropic rejette `budget_tokens` avec un 400 sur la génération actuelle). Sur le
  fil, le toggle d'Anthropic s'écrit `thinking: {type: adaptive|disabled}` — un détail de payload,
  pas un niveau de capacité.
- **Tout ce qu'un fournisseur ne supporte pas est signalé.** Une option déclarée en YAML sur un
  fournisseur qui ne peut pas l'honorer produit un avertissement actionnable nommant l'option, le
  fournisseur et le remède. C'était le vrai défaut relevé par l'audit du 2026-07-27 : pas le
  câblage manquant, mais son invisibilité.

## Notes

- **◐ cache prompt (auto)** : le fournisseur met en cache les préfixes de prompt implicitement et
  rapporte le hit ; `OpenAICompatibleProviderBase.ParseSuccessResponse` lit génériquement
  `prompt_cache_hit/miss_tokens` et le champ standard OpenAI
  `prompt_tokens_details.cached_tokens`. **✓ explicite** (Anthropic) signifie que le cache ne fait
  rien tant qu'un point d'arrêt `cache_control` n'est pas posé — opt-in via `LlmCacheConfig` / le
  bloc YAML `cache:`.
- **SSE Anthropic** : `ChatStreamingAsync` parse nativement le flux d'événements de la Messages
  API depuis LLM-05 ; auparavant il retombait sur une émulation bufferisée, donc aucun token
  n'arrivait tôt.
- **Tool calling Ollama** : passe par `/api/chat` dès que la conversation déclare des outils,
  rejoue des appels d'outils, ou transporte une image ; tout le reste conserve `/api/generate`
  (streaming NDJSON, GBNF). Le protocole de repli textuel reste en charge pour les modèles sans
  support des outils.
- **Azure OpenAI** : deux formes d'API — l'URL de déploiement datée (par défaut) et la surface
  v1 GA (`api_version: v1`), seule voie vers la Responses API et vers les modèles non-OpenAI
  qu'Azure revend.
- **HuggingFace** : les identifiants de modèle acceptent un suffixe de routage (`:fastest` /
  `:cheapest` / `:preferred` / `:<partner>`) — le seul levier de coût et de latence sur Inference
  Providers.
- **top_p / stop** : Ollama n'expose que `temperature` + `num_predict` (= max_tokens).
- **Polly** et **Sanitization clé API** : fournis par `HttpLlmProviderBase` → actifs partout.

## Ce que cette table ne prouve pas

Chaque ligne est adossée à des tests unitaires qui vérifient le payload émis — contre un **handler
HTTP mocké**. Un mock prouve qu'Orkeon envoie ce que nous croyons envoyer ; il ne prouve pas que
le fournisseur l'accepte. Les preuves d'exécution réelle sont suivies séparément dans le journal
de la matrice de tests (`backstage/features/drafts/LLM-PROVIDERS-TEST-MATRIX.md` §7) ; lancez une
campagne avec `orkeon llm probe --provider <name> --archive <dir>`.
