> 🇬🇧 [English version](../../reference/llm-providers-comparison.md)

# Comparatif des fournisseurs LLM — Orkeon

> État au 2026-09-19, dérivé du code source (`src/core/Orkeon.Infrastructure/LLMs/`)
> et des `LlmProviderCapabilities` déclarées par chaque fournisseur.
> Légende : ✓ supporté · ✗ absent · ◐ partiel/générique · † non campagné (déclaré depuis la
> documentation du vendeur, en attente de la première campagne en exécution réelle — les
> détails datés sont dans [la version anglaise](../../reference/llm-providers-comparison.md)).

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
| **MiniMax** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ (accepté mais non contraignant — mesuré) | ✗ (toujours actif, inline, extrait) | ✓ | ✓ | ◐ auto | ✗ | ✓ | ✓ |
| **HuggingFace** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ object | ✗ | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **OpenRouter** † | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ schema (par endpoint) | ✓ budget (objet `reasoning`) | ✓ (par modèle) | ✗ | ◐ auto (+ `cache_write_tokens`) | ✗ (`usage.cost` exposé) | ✓ | ✓ |
| **Mammouth AI** † | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ (non documenté) | ✗ (non documenté) | ✓ (par modèle) | ✗ | ◐ auto | ✗ | ✓ | ✓ |
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
- **Vision DeepSeek** : arrivée avec `deepseek-v4-flash-vision-exp` (mesuré 2026-08-30),
  native sur le palier Flash depuis V4.1 Flash (2026-09-10) : le défaut `deepseek-flash`
  voit, le compagnon expérimental est retiré. Déclaré par fournisseur, réel par modèle
  comme partout (D-03) : `deepseek-v4-pro` reste texte seul et répond à une image par
  l'erreur du vendeur. `deepseek-v4-flash` est le nom d'un modèle retiré que l'API route
  « temporairement » vers V4.1 Flash — le défaut est passé à l'identifiant du vendeur le
  2026-09-19.
- **MiniMax** : adossé à sa campagne depuis le 2026-08-30 (7/2/3, le jour même de son
  intégration). Le raisonnement arrive EN LIGNE — chaque réponse ouvre sur un bloc
  `<think>` dans `content`, sans champ séparé — et le dialecte l'extrait vers
  `reasoning_content`, en le réinjectant au replay comme la doc du vendeur l'exige.
  `response_format` est accepté mais NON contraignant (schéma ignoré, `json_object` en
  clôture markdown) : la déclaration None est une mesure (appels bruts, 2026-08-30,
  consignée dans la note MiniMax du catalogue de campagne — le M8 archivé montre ➖ parce
  que la déclaration None empêche l'option d'être envoyée). Vision par modèle (D-03) :
  `MiniMax-M2` répond « I'm unable to view the image », et la famille VL n'apparaît pas au
  `/models` de la plateforme — pas de compagnon déclarable en l'état.
- **Grok (x.AI)** : chaque capacité déclarée est une mesure en réel — une campagne complète de
  12 modes est passée contre `api.x.ai` via le dialecte OpenAI générique avant même que la
  classe du provider existe (2026-08-30, archivée sous `llmproviders-test/custom-endpoints/`).
  Les clés portent le préfixe `xai-`, que la factory infère.
- **OpenRouter** † : la place de marché (445 modèles de 60 vendeurs le 2026-09-18) derrière
  une seule clé, intégrée documentation d'abord (LLM-09) — aucune campagne archivée encore.
  Les identifiants sont `vendeur/modèle` (préfixe obligatoire), avec les suffixes `:free` /
  `:nitro` / `:floor` et le slug routeur `openrouter/auto` (utilisable, refusé comme défaut :
  le modèle servi dérive — la métadonnée `served_model` dit qui a répondu). La trace de
  raisonnement revient dans `reasoning`, jamais `reasoning_content` (hook `ReasoningFieldName`
  du socle) ; le thinking voyage dans l'objet de requête `reasoning` (`enabled` / `effort` /
  `max_tokens`, la déclaration Budget est donc celle du transport — OpenRouter convertit
  effort et budget l'un en l'autre selon le modèle) ; le coût réel arrive dans `usage.cost`
  (métadonnée `cost`) avec sa ventilation `upstream_inference_cost` / `is_byok` /
  `cache_write_tokens` / `reasoning_tokens` ; deux en-têtes d'attribution constants
  (`HTTP-Referer`, `X-OpenRouter-Title`) nomment Orkeon. `json_schema` est honoré par endpoint
  et le provider n'envoie pas `provider.require_parameters` : savoir si un schéma peut être
  ignoré en silence ailleurs est la question de la première campagne. Le routage avancé
  (`provider {…}`, `models[]`, `plugins[]`) n'est pas exposé. Le préfixe de clé `sk-or-v1-`
  n'est documenté que par des sources secondaires : la factory n'en infère rien encore.
- **Mammouth AI** † : l'abonnement multi-modèles français dont les crédits API inclus pilotent
  Orkeon, intégré documentation d'abord (LLM-09) — aucune campagne archivée encore. À trois
  indices concordants (2026-09-18) l'API est un proxy LiteLLM ; rien dans le provider n'en
  dépend. Les identifiants sont les chaînes nues des vendeurs (`gpt-5.6-sol`,
  `claude-sonnet-5`, `gemini-3.7-flash`) : le provider se cible par hôte (`api.mammouth.ai`)
  ou par `"Provider": "mammouth"` et n'est jamais inféré d'un nom de modèle — la même chaîne
  sans base URL continue d'aller chez le vendeur. Seuls `messages`, `model`, `temperature`,
  `max_tokens`, `top_p` et `stream` sont documentés : `response_format` et le thinking
  restent non déclarés (avertissement structuré, jamais un drop silencieux) tant que la
  première campagne ne les a pas mesurés — la règle MiniMax ; la vision est déclarée depuis la
  liste `text, image` du vendeur. Les tarifs sont les bornes hautes du vendeur
  (`gemini-3.7-flash` à 1,5 / 7,5 $/M, le double du direct).
- **Clés identity-linked Anthropic** : refusent toute requête sans en-tête
  `anthropic-workspace-id` (2026-08-30). Renseigner `LlmConfig.WorkspaceId`
  (CLI : `--workspace-id`) ; les clés classiques n'en ont pas besoin.
- **Polly** et **Sanitization clé API** : fournis par `HttpLlmProviderBase` → actifs partout.

## Défauts et modèles plus récents — revue des catalogues du 2026-09-19

Les pages modèles et tarifs de chaque vendeur ont été lues le 2026-09-19, avec les catalogues
publics des deux agrégateurs (OpenRouter, 447 modèles ; Mammouth, 100). La règle est celle
qu'énonce `LlmProviderDefaultModels` : un défaut ne change que si le vendeur retire le nom ;
sinon un modèle plus récent est un **candidat** tant qu'une campagne n'a pas archivé un M1.

| Provider | Défaut (code) | Servi le 2026-09-19 | Plus récent sur l'API du vendeur | Verdict |
|---|---|---|---|---|
| OpenAI | `gpt-5.6-sol` | oui — 4 / 20 $/M jusqu'au 2026-11-21, cible de remplacement des deux vagues de dépréciation 2026 | `gpt-6-astra` (10 / 50, effort `low`…`max` sans `none`, facturation ×2 au-delà de 272K tokens d'entrée) | reste — **campagné le 2026-09-21** : `gpt-6-astra` 10/2 avec `temperature: 1` épinglée ; il n'a pas de `reasoning_effort: none`, les function tools sur `/v1/chat/completions` sont donc refusés (`use /v1/responses`) — pas un défaut tant que le dialecte ne parle pas Responses ; `gpt-6-astra-pro` est une étiquette d'agrégateur, pas un id OpenAI |
| Anthropic | `claude-sonnet-5` | oui — 2 / 10 $/M rendu définitif, actif au moins jusqu'au 2027-06-30 | `claude-fable-5-1` (2026-09-01, 10 / 50, thinking toujours actif, `tool_choice` forcé refusé) | reste — **campagné le 2026-09-21** : `claude-fable-5-1` 11/1 deux fois (le rouge est M6, le protocole texte de repli ; outils natifs, schéma, vision et cache verts) — viable, à cinq fois le prix ; `claude-opus-5-fast` est un drapeau `speed`, pas un id ; `temperature` / `top_p` / `top_k` renvoient 400 sur tout modèle à partir d'Opus 4.7 |
| Azure OpenAI | déploiement | — | — | rien à défauter |
| Ollama | `llama3.2` | oui — 3B, texte seul, sans thinking, vieux d'un an | `qwen3.5:4b`, `gemma4:e4b`, `granite4.2:3b` (outils + thinking, même gabarit) | reste : un nouveau défaut impose un pull sur chaque machine |
| Together AI | `meta-llama/Llama-3.3-70B-Instruct-Turbo` | oui — 1,04 / 1,04 $/M, pas de retrait programmé | `zai-org/GLM-5.3-Flash` (1M, outils + JSON, 0,15 / 0,50), `Qwen/Qwen3.5-9B`, `deepseek-ai/DeepSeek-V4.1-Flash` ; Llama 4 a quitté le serverless | candidat GLM-5.3-Flash, sept fois moins cher — **campagné le 2026-09-21** : 11/0/1, il lit l'image (premier modèle vision serverless chez Together, désormais compagnon vision du kit) et son cache est lu (8000 tokens, 0,99) ; `Qwen/Qwen3.5-9B` 10/0/2. Tous deux prêts ; la montée est au propriétaire |
| DeepSeek | `deepseek-flash` (était `deepseek-v4-flash`) | oui — V4.1 Flash, 0,30 / 1,20 $/M en pointe, vision native | c'est le plus récent | **changé le 2026-09-19** : l'ancien nom est un modèle retiré routé « temporairement » ici ; **rejoué le 2026-09-21** : 12/12 deux fois — M2 et M9, rouges pendant cinq campagnes sur `deepseek-v4-flash`, sont verts sur `deepseek-flash` |
| Kimi | `kimi-k2.6` | oui — 0,95 / 4,00 $/M, sans date de retrait | `kimi-k3` (juillet 2026, 1M, 3 / 15, échantillonnage figé, pas de champ `thinking`, raisonne toujours) | reste — **campagné le 2026-09-21** : `kimi-k3` 12/12 puis 11/1 (M2 sur la forme aplatie), trace de raisonnement, cache 0,97, JSON nu là où k2.6 clôture désormais son `json_object` — prêt, à trois fois le prix ; `kimi-k2.5` et `moonshot-v1-*` retirés le 2026-08-31 ; docs désormais sur `platform.kimi.ai` |
| Qwen | `qwen3.7-plus` | oui — toujours le palier Plus, l'un des trois modèles recommandés | `qwen3.8-max` (= `qwen3.8-max-0902`), `qwen3.8-flash` ; pas de `qwen3.8-plus` | reste — **campagné le 2026-09-21** : `qwen3.8-max` 12/12, `qwen3.8-flash` 11/1 (M2), tous deux prêts, et `qwen3.7-plus` est toujours 12/12 ; la génération 3.8 ajoute `preserve_thinking` |
| Mistral AI | `mistral-medium-2604` | oui — 1,50 / 7,50 $/M, non déprécié | rien pour le chat depuis avril (OCR 4.1 seulement) | reste ; les ids `devstral-*` / magistral sont dans la table des dépréciés |
| HuggingFace | `meta-llama/Llama-3.1-8B-Instruct` | oui — mais outils sur un seul de ses quatre fournisseurs routés, et `:fastest` peut en choisir un autre | `Qwen/Qwen3.5-9B` (outils sur trois fournisseurs, dès 0,10 / 0,15), `zai-org/GLM-5.3-Flash`, `deepseek-ai/DeepSeek-V4.1-Flash` | candidat Qwen3.5-9B — **campagné le 2026-09-21** : 10/0/2, il voit et ses outils tiennent ; son premier passage a rendu un M1 vide après 4113 tokens de réflexion sur le repli 4096 du routeur — épingler `Llm:MaxTokens` (ou couper le thinking) avant d'en faire un défaut. Le routage explique toujours les rouges mouvants du défaut (M2 et M5 rouges ensemble au passage rc.4) |
| Z.AI | `glm-5.2` | oui — toujours sous « Latest Models », 1,40 / 4,40 $/M | `glm-5.3` (2026-08-18), `glm-5.3-flash` (0,15 / 0,50, image + vidéo), `glm-5.3-flashx` | reste — **campagné le 2026-09-21** : `glm-5.3-flash` 12/12 deux fois (il voit, là où 5.2 et 5.3 répondent `1210` ; cache lu) — le candidat le plus net du parc, à un neuvième du prix ; `glm-5.3` 10/2 (M2, M9) n'apporte rien sur 5.2. La déclaration `Toggle` demande toujours une garde par modèle avant une montée |
| Google Gemini | `gemini-3.7-flash` | oui — « génération précédente », sans date d'arrêt, 0,75 / 3,75 $/M jusqu'au 2026-12-31 puis 1,50 / 7,50 | `gemini-3.8-flash` (GA 2026-09-02, même prix, thinking `minimal` refusé) | premier candidat à une montée — **campagné le 2026-09-21** : `gemini-3.8-flash` 11/0/1 deux fois, identique à 3.7 mode pour mode sur le transport direct ; OpenRouter et Mammouth non joués (pas de clé) |
| Grok (x.AI) | `grok-4.6` | oui — recommandé, 2 / 6 $/M sous 200K tokens de prompt, 4 / 12 au-delà | aucun | reste |
| MiniMax | `MiniMax-M2` | oui — listé « legacy », sans date de retrait | `MiniMax-M3` (2026-06-01, 1M, entrée image + vidéo, mêmes 0,30 / 1,20) | reste — **campagné le 2026-09-21** : `MiniMax-M3` 9/1/2 puis 8/1/3 — il lit l'image (désormais compagnon vision du kit), M2 rouge comme M2, le bloc `<think>` inline découpé comme sur M2, 128 tokens en cache une fois ; la question du format de raisonnement est tranchée |
| OpenRouter | `google/gemini-3.7-flash` | oui | `google/gemini-3.8-flash` (2026-09-02, même prix) | suit le défaut direct — non campagné le 2026-09-21 (pas de clé) |
| Mammouth AI | `gemini-3.7-flash` | oui | `gemini-3.8-flash` | suit le défaut direct — non campagné le 2026-09-21 (pas de clé) |

`ModelPricingRegistry` suit les mêmes pages : la famille GPT-5.6 et `gpt-6-astra`, Sonnet 5 à
2 / 10, Fable 5.1 / Fable 5 et Haiku 4.5.

## Plafonds de sortie — le maximum documenté par modèle (LLM-10)

Une requête porte un plafond de sortie (`max_tokens`, `max_completion_tokens` chez OpenAI,
`num_predict` chez Ollama). Le moteur envoyait **4096 pour tous les modèles** ; un modèle
raisonneur dépense ce budget à réfléchir et répond vide, la relance sans outils qui suit raconte
le livrable au lieu de l'écrire, et le run passe vert sans fichier (recette propriétaire du
2026-09-19, `kimi-k3`). Depuis LLM-10, le plafond non épinglé est **le maximum documenté du
modèle**, lu dans le catalogue `LlmModelOutputLimits` d'`Orkeon.Constants.Llm` ; une valeur
épinglée (`Llm:MaxTokens`, un profil Studio, le `max_tokens` d'une crew) gagne toujours ; un
modèle inconnu du catalogue garde le repli 4096. N'y entrent que des chiffres documentés, chacun
lu le 2026-09-19 :

| Fournisseur | Modèle | Plafond envoyé | Source | Note |
|---|---|---|---|---|
| OpenAI (et les déploiements Azure des mêmes ids) | `gpt-5.6-sol`, `gpt-6-astra` | 128 000 | developers.openai.com/api/docs/models | le raisonnement compte dans le plafond |
| Anthropic | `claude-sonnet-5`, `claude-opus-5`, `claude-fable-5-1` (les variantes datées suivent la famille) | 128 000 | platform.claude.com/docs/en/about-claude/models/overview | la réflexion compte dans `max_tokens` ; le champ est obligatoire, un Claude inconnu reçoit 4096 |
| Gemini | `gemini-3.7-flash`, `gemini-3.8-flash` (aussi sous `google/…` chez OpenRouter et nu chez Mammouth) | 65 536 | ai.google.dev/gemini-api/docs/models | jetons de réflexion inclus ; 65 537 fait un 400 |
| DeepSeek | `deepseek-flash` (et les noms retirés `deepseek-v4-flash*` qu'il route) | 393 216 | api-docs.deepseek.com/api/create-chat-completion | « 1 à 384K » ; 384 000 chez Mammouth |
| Kimi | `kimi-k3` | 131 072 | platform.kimi.ai/docs/guide/kimi-k3-quickstart | le défaut `max_completion_tokens` du fournisseur ; borne réelle 1M − prompt |
| Kimi | `kimi-k2.6` | 131 072 | platform.kimi.ai/docs/guide/troubleshooting | **un épinglage, pas un plafond documenté** : la borne est 256K − prompt ; un rejet retire le champ au rejeu |
| Qwen | `qwen3.7-plus`, `qwen3.8-flash`, `qwen3.8-max` | 131 072 | alibabacloud.com/help/en/model-studio | la chaîne de pensée a son propre `thinking_budget` ; 65 500 chez Mammouth |
| Z.AI | `glm-5.2`, `glm-5.3`, `glm-5.3-flash` | 131 072 | docs.z.ai/guides/overview/concept-param | maximum du schéma ; défaut 65 536 |
| Z.AI | `glm-4.6v-flash` | 32 768 | docs.z.ai/guides/vlm/glm-4.6v | — |
| MiniMax | `MiniMax-M2` | 131 072 | platform.minimax.io/docs/guides/models-intro | « 128k (CoT compris) » ; **M3 est absent** — le fournisseur ne publie aucun chiffre de sortie (512k n'apparaît que comme réglage de benchmark ; 512 000 chez Mammouth) |
| xAI | `grok-4.6` | 128 000 | docs.x.ai/developers/rest-api-reference | pas de plafond par modèle ; le défaut du fournisseur quand le champ manque, raisonnement exclu |
| Mistral | `mistral-medium-2604` (`mistral-medium-3-5`) | **aucun** | docs.mistral.ai/api/endpoint/chat | pas de plafond de sortie, seulement « prompt + max_tokens ≤ contexte » : le champ est omis et le modèle écrit jusqu'à sa fenêtre |
| Together | tous | 4096 (repli) | docs.together.ai/docs/serverless-models | aucun plafond de sortie par modèle, seulement prompt + `max_tokens` ≤ fenêtre. La fenêtre elle-même a été le plafond du 2026-09-19 au 2026-09-21, sur l'hypothèse que `context_length_exceeded_behavior: truncate` la ramène à fenêtre − prompt ; la campagne du 2026-09-21 a mesuré que deux des trois moteurs serverless refusent quand même (`max_new_tokens`, `context_length_exceeded`) et que le troisième ne tronque que sur le chemin bufferisé. Le repli tient, le drapeau part toujours, et le filet de rejeu retire le plafond sur ces formulations ; omis, le champ vaut 2048 (`finish_reason: length`) |
| Ollama | tous | **aucun** (`num_predict` omis) | docs.ollama.com/modelfile | `-1, génération infinie` est le défaut du runtime : un modèle local écrit jusqu'à son contexte |
| HuggingFace, Docker Model Runner, tout le reste | — | 4096 (repli) | — | la borne du routeur est le contexte du fournisseur routé, qui change selon la route ; épinglez `Llm:MaxTokens` |

Deux choses que le catalogue dit franchement. Là où le fournisseur borne le plafond par
« fenêtre − prompt » (Kimi, Together, Mistral), une valeur fixe proche de la fenêtre échoue sur
tout vrai prompt — ces lignes sont donc un épinglage, le repli, ou rien. Et un plafond que le
point d'accès refuse (Qwen « Range of max_tokens », Kimi « prompt tokens + max_tokens exceeds »,
Gemini `maxOutputTokens`, le 422 de DeepSeek, les moteurs Together avec `max_new_tokens` /
`context_length_exceeded`) est **rejoué une
fois sans le champ** quand il vient du catalogue, avec un avertissement qui nomme le modèle —
une valeur épinglée appartient à l'utilisateur, et son rejet remonte tel quel. L'éditeur de
profil de Studio lit le même catalogue : l'indication sous le champ « Réponse maximale » dit ce
qu'un champ vide signifie pour le modèle choisi, et invite à épingler quand le modèle est
inconnu.

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
| OpenAI | `gpt-6-astra` | `temperature` | `1` | `'temperature' does not support 0 with this model. Only the default (1) value is supported.` | 2026-09-21 |
| OpenAI | `gpt-6-astra` | `reasoning_effort` | **aucun `"none"` n'existe** (`low`, `medium`, `high`, `xhigh`) — le contournement Sol est impossible, les function tools restent refusés sur `/v1/chat/completions` tant que le dialecte ne parle pas `/v1/responses` | `'reasoning_effort' does not support 'none' with this model. Supported values are: 'low', 'medium', 'high', and 'xhigh'.` — et sans lui, `Function tools with reasoning_effort are not supported for gpt-6-astra in /v1/chat/completions. To use function tools, use /v1/responses` | 2026-09-21 |
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
de la matrice de tests ; lancez une
campagne avec `orkeon llm probe --provider <name> --archive <dir>`.
