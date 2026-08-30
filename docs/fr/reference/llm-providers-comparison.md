> 🇬🇧 [English version](../../reference/llm-providers-comparison.md)

# Comparatif des fournisseurs LLM — Orkeon

> État au 2026-08-30, dérivé du code source (`src/core/Orkeon.Infrastructure/LLMs/`)
> et des `LlmProviderCapabilities` déclarées par chaque fournisseur.
> Légende : ✓ supporté · ✗ absent · ◐ partiel/générique.

| Fournisseur | Classe de base | Streaming SSE | Tool calling natif | Chat multi-tours (rôles tool) | Message système | top_p / stop | Grammaire GBNF | response_format | thinking | Vision | reasoning_content round-trip | Cache prompt | Métriques timing | Résilience Polly | Sanitization clé API |
|---|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| **OpenAI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Azure OpenAI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Anthropic** | HttpLlmProviderBase | ✓ natif | ✓ | ✓ | ✓ (natif, séparé) | ✓ | ✗ | ✓ schema | ✓ toggle | ✓ | ✗ | ✓ explicite | ✗ | ✓ | ✓ |
| **DeepSeek** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ object | ✓ toggle | ✓ | ✓ | ✓ métriques | ✗ | ✓ | ✓ |
| **Z.AI (GLM)** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ object | ✓ toggle | ✓ | ✗ | ✓ métriques | ✗ | ✓ | ✓ |
| **Together AI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema | ✗ | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Mistral AI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Qwen** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ object | ✓ budget | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Kimi / Moonshot** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ object | ✓ toggle | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Google Gemini** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Grok (x.AI)** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
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
- **OpenAI `max_completion_tokens`** : OpenAI a retiré `max_tokens` de ses modèles actuels (la
  campagne du 2026-08-30 a perdu dix modes sur ce seul champ), le dialecte OpenAI écrit donc
  `max_completion_tokens` — accepté aussi par les générations antérieures (vérifié sur
  `gpt-4o-mini` le même jour). Les fournisseurs compatibles gardent `max_tokens` : le retrait
  n'appartient qu'à OpenAI.
- **`response_format` Gemini** : non documenté sur la surface compat au premier audit
  (2026-08-18) et non déclaré alors ; mesuré en réel le 2026-08-30, la surface accepte
  `json_object` et `json_schema` et valide le schéma côté serveur — le provider déclare
  désormais `JsonSchema`.
- **Vision DeepSeek** : arrivée avec `deepseek-v4-flash-vision-exp` (mesuré 2026-08-30).
  Déclaré par fournisseur, réel par modèle comme partout (D-03) : le défaut
  `deepseek-v4-flash` reste texte seul et répond à une image par l'erreur du vendeur.
- **Grok (x.AI)** : chaque capacité déclarée est une mesure en réel — une campagne complète de
  12 modes est passée contre `api.x.ai` via le dialecte OpenAI générique avant même que la
  classe du provider existe (2026-08-30, archivée sous `llmproviders-test/custom-endpoints/`).
  Les clés portent le préfixe `xai-`, que la factory infère.
- **Clés identity-linked Anthropic** : refusent toute requête sans en-tête
  `anthropic-workspace-id` (2026-08-30). Renseigner `LlmConfig.WorkspaceId`
  (CLI : `--workspace-id`) ; les clés classiques n'en ont pas besoin.
- **Polly** et **Sanitization clé API** : fournis par `HttpLlmProviderBase` → actifs partout.

## Valeurs de paramètres obligatoires, par modèle

Certains modèles refusent une requête tant qu'un paramètre ne porte pas une valeur précise.
C'est autre chose que les écarts de capacité par modèle ci-dessus (D-03 — un modèle sans
thinking ou sans vision) : ici la capacité existe, mais le modèle dicte la valeur, et le
vendeur répond 400 à tout le reste. **La contrainte est par modèle, jamais par fournisseur** —
le même vendeur sert des modèles aux exigences opposées, un épinglage au niveau fournisseur
est donc à une campagne de casser le modèle voisin. Chaque ligne ci-dessous est mesurée en
réel ; rien n'est inféré.

| Fournisseur | Modèle | Paramètre | Valeur obligatoire | Les mots du vendeur | Mesuré |
|---|---|---|---|---|---|
| Kimi | `kimi-k2.6` | `temperature` | `1` | `invalid temperature: only 1 is allowed for this model` | 2026-08-03 |
| OpenAI | `gpt-5.6-sol` | `temperature` | `1` | `'temperature' does not support 0 with this model. Only the default (1) value is supported.` | 2026-08-30 |
| OpenAI | `gpt-5.6-sol` | `reasoning_effort` | `"none"` quand la requête porte des function tools sur `/v1/chat/completions` | `Function tools with reasoning_effort are not supported for gpt-5.6-sol in /v1/chat/completions. To use function tools, use /v1/responses or set reasoning_effort to 'none'.` | 2026-08-30 |
| Anthropic | `claude-sonnet-5` | `temperature` | `1`, ou omettre le champ | `` `temperature` is deprecated for this model.`` (1 et l'omission passent ; 0 et 0.7 non) | 2026-08-30 |
| Mistral | `mistral-medium-2604` | `reasoning_effort` | `high` ou `none` seulement | `reasoning_effort low is not supported for this model, supported values: [<ReasoningEffort.high: 'high'>, <ReasoningEffort.none: 'none'>]` | 2026-08-30 |
| Mistral | `mistral-medium-2604` | `top_p` | `1` explicite quand `temperature` vaut 0 et que le raisonnement est actif (l'omission n'y vaut PAS 1) | `top_p must be 1 when using greedy sampling.` | 2026-08-30 |

Le contre-exemple qui rend le registre par-modèle : `gpt-4o-mini` — même fournisseur que
`gpt-5.6-sol` — rejette `reasoning_effort` tout court (`Unrecognized request argument
supplied: reasoning_effort`, mesuré le même jour). Un « toujours none » au niveau fournisseur
le casserait.

Jumeau lisible par machine : `llmproviders-test/lib/catalog.json`, clé `requiredParams` (par
fournisseur, indexée par identifiant de modèle) — les scripts de campagne la résolvent
automatiquement et l'en-tête du rapport imprime les valeurs réellement utilisées. Dans le code
applicatif, les mêmes valeurs s'expriment par `LlmConfig` (`Temperature`,
`Thinking = { Effort = "none" }`) ; sur une mauvaise valeur, l'erreur du vendeur revient
attribuée (`CapabilityMismatchHint` nomme le réglage thinking sur le refus
tools-avec-raisonnement).

## Ce que cette table ne prouve pas

Chaque ligne est adossée à des tests unitaires qui vérifient le payload émis — contre un **handler
HTTP mocké**. Un mock prouve qu'Orkeon envoie ce que nous croyons envoyer ; il ne prouve pas que
le fournisseur l'accepte. Les preuves d'exécution réelle sont suivies séparément dans le journal
de la matrice de tests (`backstage/features/drafts/LLM-PROVIDERS-TEST-MATRIX.md` §7) ; lancez une
campagne avec `orkeon llm probe --provider <name> --archive <dir>`.
