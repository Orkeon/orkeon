> 🇬🇧 [English version](../../reference/llm-providers-comparison.md)

# Comparatif des fournisseurs LLM — Orkeon

> Dérivé du code source (`src/core/Orkeon.Infrastructure/LLMs/`) et des `LlmProviderCapabilities`
> déclarées par chaque fournisseur ; les chiffres de campagne portent leur propre date.
> Légende : ✓ supporté · ✗ absent · ◐ partiel/générique. Chaque fournisseur sauf Azure OpenAI
> a une campagne en exécution réelle archivée ; OpenRouter et Mammouth AI depuis le 2026-10-07.

| Fournisseur | Classe de base | Streaming SSE | Tool calling natif | Chat multi-tours (rôles tool) | Message système | top_p / stop | Grammaire GBNF | response_format | thinking | Vision | reasoning_content round-trip | Cache prompt | Métriques timing | Résilience Polly | Sanitization clé API |
|---|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| **OpenAI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Azure OpenAI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Anthropic** | HttpLlmProviderBase | ✓ natif | ✓ | ✓ | ✓ (natif, séparé) | ✓ | ✗ | ✓ schema | ✓ toggle (adaptatif, désactivation par modèle) | ✓ | ✗ | ✓ explicite | ✗ | ✓ | ✓ |
| **DeepSeek** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ object | ✓ toggle | ✓ | ✓ | ✓ métriques | ✗ | ✓ | ✓ |
| **Z.AI (GLM)** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ object | ✓ toggle | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Together AI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ schema | ✓ toggle (objet `reasoning`) | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Mistral AI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Qwen** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ object | ✓ budget | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Kimi / Moonshot** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ object | ✓ toggle | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Google Gemini** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Grok (x.AI)** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ schema | ✓ effort | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **MiniMax** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✗ (accepté mais non contraignant — mesuré) | ✓ toggle (désactivation seule ; M3.1-Flash raisonne toujours) | ✓ | ✓ | ◐ auto | ✗ | ✓ | ✓ |
| **HuggingFace** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ object | ✓ toggle (`chat_template_kwargs`) | ✓ | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **OpenRouter** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✓ schema (par endpoint) | ✓ budget (objet `reasoning`) | ✓ (par modèle) | ✗ | ◐ auto (+ `cache_write_tokens`) | ✗ (`usage.cost` exposé) | ✓ | ✓ |
| **Mammouth AI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ◐ `Llm:Grammar` | ✗ (accepté mais non contraignant — mesuré) | ✓ effort (`reasoning_effort`) | ✓ (par modèle) | ✗ | ◐ auto | ✗ | ✓ | ✓ |
| **Ollama** | HttpLlmProviderBase | ✓ | ✓ (`/api/chat`) | ✓ (`/api/chat`) | ✓ (prepend) | ✓ (`options`) | ◐ `Llm:Grammar` | ✓ schema | ✓ toggle | ✓ (`images`) | ✗ | ✗ | ◐ (`total_duration`, `eval_duration` sur `/api/generate`) | ✓ | ✓ |

## Comment lire les colonnes de capacités

`response_format`, `thinking` et `Vision` ne sont pas maintenus à la main ici : chaque fournisseur
déclare un value object `LlmProviderCapabilities`, et `OpenAICompatibleProviderBase` le traduit une
seule fois dans le dialecte OpenAI. Anthropic et Ollama écrivent leur propre dialecte (ils
n'étendent pas la base compatible OpenAI) ; Qwen et OpenRouter surchargent la traduction du
thinking pour les champs de DashScope et l'objet `reasoning` d'OpenRouter. Le
[guide du format de réponse](../guides/llm-response-format.md) détaille les surfaces de
`response_format`.

- **`response_format`** — `object` signifie que l'API garantit un JSON bien formé ; `schema`
  signifie qu'elle valide contre un JSON Schema côté serveur. Un schéma envoyé à un fournisseur
  qui ne supporte que `object` est **rétrogradé avec un avertissement**, jamais en silence.
  **Anthropic est schema-only** : il n'a pas d'équivalent de `json_object`, donc une demande JSON
  sans schéma y est signalée plutôt qu'envoyée.
- **`thinking`** — `effort` n'accepte qu'une indication de niveau ; `toggle` peut en plus activer
  et désactiver le raisonnement ; `budget` accepte en outre un budget de tokens explicite (Qwen
  et OpenRouter uniquement — Anthropic rejette `budget_tokens` avec un 400 sur la génération actuelle). Sur le
  fil, le toggle d'Anthropic s'écrit `thinking: {type: adaptive|disabled}` — un détail de payload,
  pas un niveau de capacité.
- **Tout ce qu'un fournisseur ne supporte pas est signalé.** Une option déclarée en YAML sur un
  fournisseur qui ne peut pas l'honorer produit un avertissement actionnable nommant l'option, le
  fournisseur et le remède.

## Notes

- **◐ cache prompt (auto)** : le fournisseur met en cache les préfixes de prompt implicitement et
  rapporte le hit ; `OpenAICompatibleProviderBase.ParseSuccessResponse` lit génériquement
  `prompt_cache_hit/miss_tokens` et le champ standard OpenAI
  `prompt_tokens_details.cached_tokens`. **✓ explicite** (Anthropic) signifie que le cache ne fait
  rien tant qu'un point d'arrêt `cache_control` n'est pas posé — opt-in via `LlmCacheConfig` / le
  bloc YAML `cache:` d'un agent ou d'une task (`llm_override:`). **✓ métriques** (DeepSeek) ajoute les `prompt_cache_hit_tokens` /
  `prompt_cache_miss_tokens` du vendeur aux métadonnées de la réponse.
- **SSE Anthropic** : `ChatStreamingAsync` parse nativement le flux d'événements de la Messages
  API : les tokens arrivent au fil de leur production.
- **Tool calling Ollama** : passe par `/api/chat` dès que la conversation déclare des outils,
  rejoue des appels d'outils, ou transporte une part non textuelle ; tout le reste conserve
  `/api/generate`. Une part audio ou fichier y est refusée par `NotSupportedException`, comme
  chez OpenAI et Anthropic. Le protocole de repli textuel reste en charge pour les modèles sans
  support des outils.
- **Azure OpenAI** : deux formes d'API — l'URL de déploiement datée (par défaut) et la surface
  v1 GA (`api_version: v1`), seule voie vers la Responses API et vers les modèles non-OpenAI
  qu'Azure revend.
- **HuggingFace** : les identifiants de modèle acceptent un suffixe de routage (`:fastest` /
  `:cheapest` / `:preferred` / `:<partner>`) — le seul levier de coût et de latence sur Inference
  Providers.
- **top_p / stop** : écrits seulement s'ils sont fixés, quelle que soit la valeur — `temperature`
  aussi ; rien de fixé, rien d'écrit, et le modèle applique son propre défaut. Mistral
  écrit `top_p: 1` quand rien n'en fixe. Ollama les écrit dans ses `options`, avec `seed`. Aucun
  dialecte n'écrit `frequency_penalty` ni `presence_penalty`, et aucun autre qu'Ollama n'écrit
  `seed` : en fixer un produit l'avertissement structuré qui le nomme (event id `110`), jamais un
  abandon silencieux.
- **Grammaire GBNF** : aucune API de vendeur ne documente de champ `grammar`, donc aucun
  fournisseur ne déclare la capacité (`LlmProviderCapabilities.GbnfGrammar`). **◐ `Llm:Grammar`**
  signifie que le champ n'est écrit — sur les deux points d'accès d'Ollama, et par les deux
  constructeurs de charge utile de la base compatible OpenAI — que lorsque les réglages disent que
  le point d'accès derrière `Llm:BaseUrl` l'accepte (`"Llm": { "Grammar": true }`, un serveur
  compatible llama.cpp comme Docker Model Runner ou `llama-server`). Sinon la grammaire est
  abandonnée avec un avertissement structuré qui nomme cette clé. La Messages API d'Anthropic n'a
  pas ce champ, quoi que dise la clé. Un livrable `structured_output` envoie son schéma comme
  format de réponse `json_schema` non strict à un fournisseur qui déclare **✓ schema** (voir
  [Format de réponse LLM](../guides/llm-response-format.md)).
- **OpenAI `max_completion_tokens`** : OpenAI a retiré `max_tokens` de ses modèles actuels (la
  campagne du 2026-08-30 a perdu dix modes sur ce seul champ), le dialecte OpenAI écrit donc
  `max_completion_tokens` — accepté aussi par les générations antérieures (vérifié sur
  `gpt-4o-mini` le même jour). Les fournisseurs compatibles gardent `max_tokens` : le retrait
  n'appartient qu'à OpenAI.
- **`response_format` Gemini** : non documenté sur la surface compat au premier audit
  (2026-08-18) et non déclaré alors ; mesuré en réel le 2026-08-30, la surface accepte
  `json_object` et `json_schema` et valide le schéma côté serveur ; le provider déclare
  `JsonSchema`.
- **Vision DeepSeek** : arrivée avec `deepseek-v4-flash-vision-exp` (mesuré 2026-08-30),
  native sur le palier Flash depuis V4.1 Flash (2026-09-10) : le défaut `deepseek-flash`
  voit, le compagnon expérimental est retiré. Déclaré par fournisseur, réel par modèle
  comme partout : `deepseek-v4-pro` reste texte seul et répond à une image par
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
  que la déclaration None empêche l'option d'être envoyée). Vision par modèle :
  `MiniMax-M2` répond « I'm unable to view the image » ; `MiniMax-M3`, le défaut depuis le 2026-11-11, lit l'image
  (campagnes du 2026-09-21 et du 2026-11-11).
- **Grok (x.AI)** : chaque capacité déclarée est une mesure en réel — une campagne complète de
  12 modes est passée contre `api.x.ai` via le dialecte OpenAI générique avant même que la
  classe du provider existe (2026-08-30, archivée sous `llmproviders-test/custom-endpoints/`).
  Les clés portent le préfixe `xai-`, que la factory infère.
- **OpenRouter** : la place de marché (465 modèles le 2026-10-07) derrière une seule clé,
  intégrée documentation d'abord et campagnée le 2026-10-07 (11/1 sur
  `google/gemini-3.7-flash`, le rouge étant M10 ; le défaut est `google/gemini-3.8-flash` depuis le 2026-11-11, 12/0/0).
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
  ignoré en silence ailleurs reste ouvert après la première campagne (M8 vert sur les trois
  modèles joués). Le routage avancé
  (`provider {…}`, `models[]`, `plugins[]`) n'est pas exposé. Le préfixe de clé `sk-or-v1-`
  est confirmé sur une vraie clé (2026-10-07) ; la factory n'en infère rien encore. La
  campagne a aussi confirmé `usage.cost` sur les deux chemins, `message.reasoning` avec
  `reasoning_details[]` sur un modèle qui raisonne, et `openrouter/auto` qui nomme le modèle
  ayant répondu.
- **Mammouth AI** : l'abonnement multi-modèles français dont les crédits API inclus pilotent
  Orkeon, intégré documentation d'abord et campagné le 2026-10-07 (10/0/2 sur
  `gemini-3.7-flash` : tous les modes exercés verts ; le défaut est `gemini-3.8-flash` depuis le 2026-11-11, 9/1/2 avec M10 rouge). À trois
  indices concordants (2026-09-18) l'API est un proxy LiteLLM ; rien dans le provider n'en
  dépend. Les identifiants sont les chaînes nues des vendeurs (`gpt-5.6-sol`,
  `claude-sonnet-5`, `gemini-3.8-flash`) : le provider se cible par hôte (`api.mammouth.ai`)
  ou par la clé de provider explicite `mammouth` (`LlmProviderFactory.Create("mammouth", …)`,
  `orkeon llm probe -p mammouth`) et n'est jamais inféré d'un nom de modèle — la même chaîne
  sans base URL continue d'aller chez le vendeur. Les settings d'un run ne portent aucune clé
  de provider : un run atteint Mammouth par son `BaseUrl`. Seuls `messages`, `model`, `temperature`,
  `max_tokens`, `top_p` et `stream` sont documentés. Mesuré le 2026-10-07 : les outils
  passent, `response_format` est accepté mais un schéma strict ne contraint pas (le cas
  MiniMax, il reste donc non déclaré), `reasoning_effort` est accepté et la trace d'un modèle
  qui raisonne revient dans `reasoning_content` (pas encore déclaré — avertissement
  structuré, jamais un drop silencieux) ; la vision est déclarée depuis la
  liste `text, image` du vendeur. Les tarifs sont les bornes hautes du vendeur
  (`gemini-3.8-flash`, comme `gemini-3.7-flash` avant lui, à 1,5 / 7,5 $/M, le double du direct).
- **Clés identity-linked Anthropic** : refusent toute requête sans en-tête
  `anthropic-workspace-id` (2026-08-30). Renseigner `LlmConfig.WorkspaceId`
  (CLI : `--workspace-id`) ; les clés classiques n'en ont pas besoin.
- **Polly** et **Sanitization clé API** : fournis par `HttpLlmProviderBase` → actifs partout.

## Clés d'API : la variable par fournisseur

**Un run prend la clé que résout la configuration, sinon la variable que nomme le fichier de
réglages** : `Llm:ApiKey` dans le fichier, surchargée par `ORKEON_Llm__ApiKey` dans
l'environnement (`ORKEON_Llm__Profiles__<id>__ApiKey` pour un profil) ; sinon la variable que
nomme l'`ApiKeyEnvVar` de la section — lue dans l'environnement du processus, puis, sous Windows,
dans la portée Utilisateur
([configuration](./configuration.md#la-clé-dapi-apikey-apikeyenvvar)). Les noms de fournisseurs
ci-dessous sont les noms conventionnels : un fichier de réglages stocke le *nom* de la variable et
jamais la clé, comme `ApiKeyEnvVar` de la section `Llm` et de l'entrée `Llm:Profiles:<id>` d'un
profil. Exporter `DEEPSEEK_API_KEY` suffit donc à un `orkeon run` en terminal sur un fichier qui
la nomme : `"ApiKeyEnvVar": "DEEPSEEK_API_KEY"`. Un fichier qui ne nomme aucune variable ne lit
toujours que `Llm:ApiKey` et `ORKEON_Llm__ApiKey`.

| Fournisseur | Variable (le nom conventionnel pour `ApiKeyEnvVar`) | Clé délivrée sur | Timeout conseillé | Solde exposé par l'API ([détails](#ce-que-chaque-api-dit-du-solde-du-compte)) |
|---|---|---|---|---|
| OpenAI | `OPENAI_API_KEY` | `platform.openai.com/api-keys` | défaut moteur (30 s) | ✗ (dépense seulement, pour une clé admin) |
| Anthropic | `ANTHROPIC_API_KEY` | `console.anthropic.com` | défaut moteur | ✗ (dépense seulement, pour une clé Admin API) |
| DeepSeek | `DEEPSEEK_API_KEY` | `platform.deepseek.com` | **600 s** | ✓ `GET /user/balance` |
| Mistral AI | `MISTRAL_API_KEY` | `console.mistral.ai` | défaut moteur | ✗ (consommation seulement, pour une clé Admin API) |
| Google Gemini | `GEMINI_API_KEY` | `aistudio.google.com/apikey` | défaut moteur | ✗ |
| Grok (x.AI) | `XAI_API_KEY` | `console.x.ai` | défaut moteur | ◐ pour une clé de gestion seulement |
| MiniMax | `MINIMAX_API_KEY` | `platform.minimax.io` | **600 s** | ✗ |
| Together AI | `TOGETHER_API_KEY` | `api.together.ai` | défaut moteur | ✗ |
| Qwen | `DASHSCOPE_API_KEY` | `dashscope.console.aliyun.com` | défaut moteur | ◐ pour une AccessKey Alibaba Cloud seulement |
| Kimi (Moonshot) | `MOONSHOT_API_KEY` | `platform.moonshot.ai` | **600 s** | ✓ `GET /v1/users/me/balance` (USD sur `.ai`, CNY sur `.cn`) |
| HuggingFace | `HF_TOKEN` | `huggingface.co/settings/tokens` | défaut moteur | ✗ |
| Z.AI (GLM) | `ZAI_API_KEY` | `z.ai/manage-apikey` | **600 s** | ✗ |
| OpenRouter | `OPENROUTER_API_KEY` | `openrouter.ai/keys` | défaut moteur | ◐ par clé (`GET /api/v1/key`) ; les crédits du compte pour une clé de gestion seulement |
| Mammouth AI | `MAMMOUTH_API_KEY` | `mammouth.ai` — le fournisseur documente « depuis les réglages API » ; la page exacte se confirme avec la première clé | défaut moteur | ✗ (dépense d'une clé, réponse non documentée) |
| Ollama · Docker Model Runner | aucune | — | défaut moteur | — (local, sans compte) |
| Compatible OpenAI (`custom`) | aucune en propre : les réglages la nomment par `ApiKeyEnvVar`, sinon `ORKEON_Llm__ApiKey` est lue, la clé du défaut de chaque run | — | défaut moteur | ✗ (hôte inconnu) |
| Azure OpenAI | pas de variable dédiée : son endpoint par ressource en fait une entrée « compatible OpenAI » | — | — | ✗ (dépense dans Cost Management, pour une identité Microsoft Entra) |

Les 600 s conseillées ne sont pas décoratives. Les quatre fournisseurs dont le modèle **par
défaut** raisonne avant de répondre débordent les 30 s du moteur, et le run du 2026-09-20 a
été perdu sur deux timeouts Kimi remontés comme réponse vide. Un fichier de réglages
demande de monter `Llm:TimeoutSeconds` explicitement.

Deux noms qui ne sont **pas** celui-là, et qu'on confond avec lui :

- `ORKEON_LLM_API_KEY` — le défaut de `orkeon llm probe -k` et `orkeon llm models -k`.
  Outillage de campagne seulement ; un run ne le lit jamais.
- `ORKEON_<NOM>` — la chaîne de secrets des **outils**, pas celle du LLM
  (`EnvironmentSecretProvider`, puis `Secrets:<NOM>` dans le fichier) : `ORKEON_TAVILY_API_KEY`
  pour `web_search`, `BRAVE_API_KEY` lu tel quel pour `brave_search`. Voir la
  [référence de configuration](./configuration.md).

`orkeon init --api-key-env <nom>` écrit `Llm:ApiKeyEnvVar: <nom>` dans
le fichier généré — rien pour `ORKEON_Llm__ApiKey`, que le runtime lit nativement —, si bien que
chaque run lit la variable qu'il nomme.

## Ce que chaque API dit du solde du compte

Ce qu'il reste sur le compte derrière une clé, tel que l'API de chaque fournisseur le dit. Trois
fournisseurs seulement servent un solde à la clé d'inférence que porte un run ; pour tous les
autres, c'est la console du fournisseur qu'il faut consulter — la colonne « Clé délivrée sur »
ci-dessus, précédée de `https://`. Une requête de solde part vers l'hôte de la clé : une clé de la
plateforme `.cn` de Kimi est refusée par l'hôte `.ai`, et inversement. Chaque ligne a été lue
dans la documentation de référence du fournisseur le 2026-09-24.

| Fournisseur | Solde | Endpoint | Ce qu'il renvoie | Clé acceptée | Source |
|---|---|---|---|---|---|
| DeepSeek | ✓ disponible | `GET /user/balance` | `balance_infos[]`, une entrée par devise (`CNY`, `USD`) : `total_balance`, `granted_balance`, `topped_up_balance` en chaînes décimales ; `is_available` | la clé d'API (Bearer) | api-docs.deepseek.com/api/get-user-balance |
| Kimi (Moonshot) | ✓ disponible | `GET /v1/users/me/balance` | `data.available_balance`, `voucher_balance`, `cash_balance` (négatif en cas d'impayé) — dollars US sur `api.moonshot.ai`, yuans sur `api.moonshot.cn` ; la réponse ne nomme aucune devise | la clé d'API (Bearer) de cette plateforme | platform.kimi.ai/docs/api/balance · platform.kimi.com/docs/api/balance |
| OpenRouter | ◐ par clé | `GET /api/v1/key` | `data.limit_remaining` : les dollars US que la clé peut encore dépenser sous sa limite, `null` quand elle n'en a pas ; `limit`, `usage`. Les crédits du compte (`total_credits`, `total_usage`) sont sur `GET /api/v1/credits` | la clé d'API pour `/key` ; une **clé de gestion** pour `/credits`, qui répond 403 à toute autre | openrouter.ai/docs/api/api-reference/api-keys/get-current-api-key · openrouter.ai/docs/api/api-reference/credits/get-remaining-credits |
| Grok (x.AI) | ◐ clé admin requise | `GET /v1/billing/teams/{team_id}/prepaid/balance` sur `management-api.x.ai` | le solde prépayé, en cents US | une **clé de gestion** seulement | docs.x.ai/developers/rest-api-reference/management/billing |
| Qwen | ◐ clé admin requise | `QueryAccountBalance`, OpenAPI de facturation d'Alibaba Cloud | `AvailableAmount`, `AvailableCashAmount`, `CreditAmount`, `Currency` | une **AccessKey Alibaba Cloud** autorisée à `bss:DescribeAcccount` — jamais la clé DashScope | alibabacloud.com/help/en/user-center/developer-reference/api-bssopenapi-2017-12-14-queryaccountbalance |
| OpenAI | ✗ non exposé | aucun — `GET /v1/organization/costs` rapporte la dépense | la dépense par jour (`amount.value`, `currency`), pas un solde | une clé admin | developers.openai.com/cookbook/examples/completions_usage_api |
| Anthropic | ✗ non exposé | aucun — `GET /v1/organizations/cost_report` rapporte la dépense | la dépense en cents US, pas un solde | des identifiants Admin API — « workspace API keys don't work » | platform.claude.com/docs/en/manage-claude/usage-cost-api |
| Mistral AI | ✗ non exposé | aucun — `GET /v1/admin/usage` rapporte la consommation | la consommation par catégorie sur un mois ; le solde de crédits n'est que sur la page Billing de la console | une clé Admin API | docs.mistral.ai/admin/admin-api/usage-metrics · docs.mistral.ai/admin/billing-usage/billing |
| Azure OpenAI | ✗ non exposé | aucun — Cost Management rapporte la dépense | la dépense par compteur | une identité Microsoft Entra avec le rôle Cost Management Reader, jamais la clé de la ressource | learn.microsoft.com/en-us/azure/foundry/concepts/manage-costs |
| Google Gemini | ✗ non exposé | aucun | le solde prépayé se gère « directly within the Google AI Studio Billing tab » | — | ai.google.dev/gemini-api/docs/billing |
| Together AI | ✗ non exposé | aucun | la carte « Credit balance » des réglages de facturation | — | docs.together.ai/docs/billing-credits · docs.together.ai/docs/billing-usage-limits |
| HuggingFace | ✗ non exposé | aucun | la page de facturation et les réglages Inference Providers | — | huggingface.co/docs/inference-providers/pricing |
| Z.AI (GLM) | ✗ non exposé | aucun | la référence de l'API ne liste aucun endpoint de facturation | — | docs.z.ai/llms.txt |
| MiniMax | ✗ non exposé | aucun en paiement à l'usage ; `GET /v1/token_plan/remains` sur `www.minimax.io` pour un Token Plan | Token Plan seulement, réponse non documentée | une clé d'abonnement Token Plan | platform.minimax.io/docs/token-plan/faq |
| Mammouth AI | ✗ non exposé | aucun — `GET /key/info`, documenté avec un hôte fictif | « how much credits has been spent on a key » : la dépense, réponse non documentée | la clé d'API | info.mammouth.ai/docs/api-quick-start |
| Ollama · Docker Model Runner | — sans objet | aucun | un runtime local n'a pas de compte | — | — |

Deux endpoints sont documentés sans la forme de leur réponse : le `remains` du Token Plan de
MiniMax et le `key/info` de Mammouth. Un endpoint compatible OpenAI (`custom`) n'expose aucun
solde connu, et un endpoint local n'a pas de compte.

## Défauts et modèles plus récents — revues des catalogues du 2026-09-19, du 2026-10-07 et du 2026-11-11

Les pages modèles et tarifs de chaque vendeur ont été lues le 2026-09-19, avec les catalogues
publics des deux agrégateurs (OpenRouter, 447 modèles ; Mammouth, 100). La règle est celle
qu'énonce `LlmProviderDefaultModels` : un défaut ne change que si le vendeur retire le nom ;
sinon un modèle plus récent est un **candidat** tant qu'une campagne n'a pas archivé un M1.
Les campagnes du 2026-10-07 et du 2026-11-11 ont rejoué les candidats : la date d'un verdict
dit quelle campagne a mesuré quoi, et les modèles qu'elles ont ajoutés aux candidats figurent
dans la colonne « Plus récent ».

| Provider | Défaut (code) | Servi le 2026-09-19 | Plus récent sur l'API du vendeur | Verdict |
|---|---|---|---|---|
| OpenAI | `gpt-5.6-sol` | oui — 4 / 20 $/M jusqu'au 2026-11-21, cible de remplacement des deux vagues de dépréciation 2026 | `gpt-6-astra` (10 / 50, effort `low`…`max` sans `none`, facturation ×2 au-delà de 272K tokens d'entrée) ; `gpt-6.1-terra` (listé après le 2026-10-07) | reste — **campagné le 2026-09-21** : `gpt-6-astra` 10/2 avec `temperature: 1` épinglée ; il n'a pas de `reasoning_effort: none`, les function tools sur `/v1/chat/completions` sont donc refusés (`use /v1/responses`) — pas un défaut tant que le dialecte ne parle pas Responses ; `gpt-6-astra-pro` est une étiquette d'agrégateur, pas un id OpenAI ; **campagné le 2026-11-11** (après le correctif qui refait passer `reasoning_effort: none` jusqu'à l'API) : `gpt-5.6-sol` 11/1, `gpt-6-sol` 11/1, `gpt-6-luna` 12/0/0, `gpt-6.1-terra` 12/0/0 — il accepte `none`, contrairement à `gpt-6.1-sol` ; `gpt-6-astra` 10/2 et `gpt-6.1-sol` 9/3 restent rouges sur M2 et M5 (pas de `none`, les outils exigent `/v1/responses`) |
| Anthropic | `claude-sonnet-5` | oui — 2 / 10 $/M rendu définitif, actif au moins jusqu'au 2027-06-30 | `claude-fable-5-1` (2026-09-01, 10 / 50, thinking toujours actif, `tool_choice` forcé refusé) ; `claude-fable-5-2` (2026-11-09, contexte 1M, sortie 128K) | reste — **campagné le 2026-09-21** : `claude-fable-5-1` 11/1 deux fois (le rouge est M6, le protocole texte de repli ; outils natifs, schéma, vision et cache verts) — viable, à cinq fois le prix ; `claude-opus-5-fast` est un drapeau `speed`, pas un id ; `temperature` / `top_p` / `top_k` renvoient 400 sur tout modèle à partir d'Opus 4.7 ; **campagné le 2026-11-11** : le défaut 12/12, `claude-fable-5-2` 12/12 — y compris le M6 que `claude-fable-5-1` manque encore (11/1) |
| Azure OpenAI | déploiement | — | — | rien à défauter |
| Ollama | `llama3.2` | oui — 3B, texte seul, sans thinking, vieux d'un an | `qwen3.5:4b`, `gemma4:e4b`, `granite4.2:3b` (outils + thinking, même gabarit) | reste : un nouveau défaut impose un pull sur chaque machine |
| Together AI | `zai-org/GLM-5.3-Flash` (était `meta-llama/Llama-3.3-70B-Instruct-Turbo`) | oui — 0,15 / 0,50 $/M (catalogue lu le 2026-11-11) ; l'ancien défaut n'est plus servi en serverless | `Qwen/Qwen3.5-9B`, `deepseek-ai/DeepSeek-V4.1-Flash` ; Llama 4 a quitté le serverless | **changé le 2026-11-11** — chaque appel à l'ancien défaut répondait `Unable to access non-serverless model` (2/9/1), le défaut passe donc à `zai-org/GLM-5.3-Flash`, cible de remplacement de Together depuis le 2026-09-19 : 11/0/1 au dernier essai du jour (10/1/1 au premier, M10 : aucun token en cache rapporté ; 11/0/1 le 2026-09-21), il lit l'image, le défaut voit donc et il n'y a plus de compagnon vision séparé ; `Qwen/Qwen3.5-9B` 10/1/1 (M10), candidat |
| DeepSeek | `deepseek-flash` (était `deepseek-v4-flash`) | oui — V4.1 Flash, 0,30 / 1,20 $/M en pointe, vision native | c'est le plus récent | **changé le 2026-09-19** : l'ancien nom est un modèle retiré routé « temporairement » ici ; **rejoué le 2026-09-21** : 12/12 deux fois — M2 et M9, rouges pendant cinq campagnes sur `deepseek-v4-flash`, sont verts sur `deepseek-flash` ; **rejoué le 2026-11-11** : `deepseek-flash` 12/12 |
| Kimi | `kimi-k3.1-pro` (était `kimi-k2.6`) | oui — `kimi-k2.6` 0,95 / 4,00 $/M, sans date de retrait | `kimi-k3` (juillet 2026, 1M, 3 / 15, échantillonnage figé, pas de champ `thinking`, raisonne toujours) ; `kimi-k3.1-pro` et les variantes `-highspeed` (listés le 2026-11-11) | **changé le 2026-11-11** — **campagné le 2026-09-21** : `kimi-k3` 12/12 puis 11/1 (M2 sur la forme aplatie), trace de raisonnement, cache 0,97, JSON nu là où k2.6 clôture désormais son `json_object` — prêt, à trois fois le prix ; `kimi-k2.5` et `moonshot-v1-*` retirés le 2026-08-31 ; docs désormais sur `platform.kimi.ai` ; **2026-11-11** : `kimi-k2.6` 10/1/1 (M8 : son `json_object` toujours clôturé), `kimi-k3` 12/12, `kimi-k3.1-pro` 12/12 — le nouveau modèle de tête ; **changé le 2026-11-11** : le défaut passe à `kimi-k3.1-pro` (12/12 en direct et sur OpenRouter). La famille K3 fige `temperature`, `top_p` et son raisonnement : `KimiLlmProvider` laisse `temperature`, `top_p` et `thinking` hors d'une requête K3 et signale chacun, si bien qu'un hôte qui les règle reçoit un avertissement, pas un 400 |
| Qwen | `qwen3.7-plus` | oui — toujours le palier Plus, l'un des trois modèles recommandés | `qwen3.8-max` (= `qwen3.8-max-0902`), `qwen3.8-flash` ; pas de `qwen3.8-plus` | reste — **campagné le 2026-09-21** : `qwen3.8-max` 12/12, `qwen3.8-flash` 11/1 (M2), tous deux prêts, et `qwen3.7-plus` est toujours 12/12 ; la génération 3.8 ajoute `preserve_thinking` ; **2026-11-11** : `qwen3.7-plus`, `qwen3.8-flash` et `qwen3.8-max` 12/12 chacun |
| Mistral AI | `mistral-medium-2604` | oui — 1,50 / 7,50 $/M, non déprécié | rien pour le chat depuis avril (OCR 4.1 seulement) | reste ; les ids `devstral-*` / magistral sont dans la table des dépréciés ; **2026-11-11** : le défaut et `mistral-large-4` 12/12, `mistral-small-2603` 11/1 (M9) |
| HuggingFace | `meta-llama/Llama-3.1-8B-Instruct` | oui — mais outils sur un seul de ses quatre fournisseurs routés, et `:fastest` peut en choisir un autre | `Qwen/Qwen3.5-9B` (outils sur trois fournisseurs, dès 0,10 / 0,15), `zai-org/GLM-5.3-Flash`, `deepseek-ai/DeepSeek-V4.1-Flash` | candidat Qwen3.5-9B — **campagné le 2026-09-21** : 10/0/2, il voit et ses outils tiennent ; son premier passage a rendu un M1 vide après 4113 tokens de réflexion sur le repli 4096 du routeur — épingler `Llm:MaxTokens` (ou couper le thinking) avant d'en faire un défaut. Le routage explique toujours les rouges mouvants du défaut (M2 et M5 rouges ensemble au même passage) ; **2026-11-11** : le défaut 7/3/2 (M2, M5, M9), `Qwen/Qwen3.5-9B` 9/1/2 (M2) |
| Z.AI | `glm-5.3-flashx` (était `glm-5.2`) | oui — `glm-5.2` toujours sous « Latest Models », 1,40 / 4,40 $/M | `glm-5.3` (2026-08-18), `glm-5.3-flash` (0,15 / 0,50, image + vidéo), `glm-5.3-flashx` | **changé le 2026-11-11** — **campagné le 2026-09-21** : `glm-5.3-flash` 12/12 deux fois (il voit, là où 5.2 et 5.3 répondent `1210` ; cache lu) — le candidat le plus net du parc, à un neuvième du prix ; `glm-5.3` 10/2 (M2, M9) n'apporte rien sur 5.2. La déclaration `Toggle` demande toujours une garde par modèle avant une montée ; **2026-11-11** : `glm-5.3-flashx` de nouveau à 12/12, `glm-5.3` 11/1 (M9), `glm-5.3-flash` 11/1 (M2), `glm-5.2` 10/2 ; **changé le 2026-11-11** : le défaut passe à `glm-5.3-flashx` (12/12 le 2026-10-07 et au premier essai du 2026-11-11, 11/1 avec M10 rouge au dernier ; il voit et son cache est lu) ; derniers essais du jour : `glm-5.3-flash` 12/0/0, `glm-5.3` 10/2 (M2, M9), `glm-5.2` 11/1 (M9). La famille 5.3 ne sait pas couper la réflexion : `ZaiLlmProvider` ne lui envoie pas `thinking: disabled` et signale l'option à la place |
| Google Gemini | `gemini-3.8-flash` (était `gemini-3.7-flash`) | oui — `gemini-3.7-flash` « génération précédente », sans date d'arrêt, 0,75 / 3,75 $/M jusqu'au 2026-12-31 puis 1,50 / 7,50 | `gemini-3.8-flash` (GA 2026-09-02, même prix, thinking `minimal` refusé) ; `gemini-4-argon`, `gemini-4-carbon` | premier candidat à une montée — **campagné le 2026-09-21** : `gemini-3.8-flash` 11/0/1 deux fois, identique à 3.7 mode pour mode sur le transport direct ; OpenRouter et Mammouth non joués (pas de clé) ; **2026-11-11** : le défaut 11/0/1, `gemini-3.8-flash` 12/0/0, `gemini-4-argon` et `gemini-4-carbon` 11/0/1 chacun ; `gemini-omni-1.2-flash` est listé mais réservé à l'API Interactions, hors de portée de ce provider et non campagné ; **changé le 2026-11-11 (campagne complète rejouée)** : `gemini-3.8-flash` 12/0/0 en direct et chez OpenRouter (9/1/2 chez Mammouth, le même M10 qu'en 3.7), le défaut 11/0/1 — le défaut passe à `gemini-3.8-flash`, OpenRouter et Mammouth le suivent |
| Grok (x.AI) | `grok-4.6` | oui — recommandé, 2 / 6 $/M sous 200K tokens de prompt, 4 / 12 au-delà | aucun | reste (`grok-4.7` est listé depuis) ; **campagné le 2026-11-11** : `grok-4.6` et `grok-4.7` 12/12 |
| MiniMax | `MiniMax-M3` (était `MiniMax-M2`) | oui — `MiniMax-M2` listé « legacy », sans date de retrait | `MiniMax-M3` (2026-06-01, 1M, entrée image + vidéo, mêmes 0,30 / 1,20) ; `MiniMax-M3.1-Flash` | **changé le 2026-11-11** — **campagné le 2026-09-21** : `MiniMax-M3` 9/1/2 puis 8/1/3 — il lit l'image (alors compagnon vision du kit), M2 rouge comme M2, le bloc `<think>` inline découpé comme sur M2, 128 tokens en cache une fois ; la question du format de raisonnement est tranchée ; **2026-11-11** : `MiniMax-M3.1-Flash` 10/0/2 (M7 et M8 non déclarés par le provider), M9 vert — il lit l'image là où `MiniMax-M3` lit `72` ; `MiniMax-M3` 9/1/2 (M9), `MiniMax-M2` 8/2/2 (M2, M9) ; **changé le 2026-11-11 (campagne complète rejouée)** : `MiniMax-M3` 10/0/2 (il lit l'image), `MiniMax-M2` 6/4/2 (M2, M5, M9, M10) — le défaut passe à `MiniMax-M3` et le compagnon vision est retiré |
| OpenRouter | `google/gemini-3.8-flash` (était `google/gemini-3.7-flash`) | oui | `google/gemini-3.8-flash` (2026-09-02, même prix) | suit le défaut direct — **campagné le 2026-10-07** : 11/1 sur les deux (M10 : champs de cache rendus à 0) ; **2026-11-11** : le défaut 12/0/0, `google/gemini-3.8-flash` 11/1/0 (M10), `moonshotai/kimi-k3.1-pro` 12/0/0 ; **changé le 2026-11-11 (campagne complète rejouée)** : `google/gemini-3.8-flash` 12/0/0, `google/gemini-3.7-flash` 11/1 (M10) — suit le défaut direct en 3.8 |
| Mammouth AI | `gemini-3.8-flash` (était `gemini-3.7-flash`) | oui | `gemini-3.8-flash` | suit le défaut direct — **campagné le 2026-10-07** : 10/0/2 sur les deux, tous les modes exercés verts ; **2026-11-11** : 9/1/2 sur les deux (M10 : 0 token en cache), `kimi-k3.1-pro` 10/0/2 ; **changé le 2026-11-11 (campagne complète rejouée)** : 9/1/2 sur les deux à nouveau (M10) — suit le défaut direct en 3.8 |

`ModelPricingRegistry` suit les mêmes pages : la famille GPT-5.6 et `gpt-6-astra`, et, pour
Anthropic (lu le 2026-10-30), Fable 5.1 / Fable 5 à 10 / 50, Opus 5.5 à 4 / 20, Opus 5 et Opus 4.5 à 4.8 à 5 / 25, Sonnet 5 à 2 / 10,
Haiku 5 à 0,10 / 0,50 et Haiku 4.5 à 1 / 5. Tarifs de cache : écriture 5 minutes à 1,25x le prix du prompt, écriture 1 heure à 2x
(chiffrée par le gestionnaire de coût), lecture à 0,1x (0,25 pour Fable 5.1, 0,20 pour Opus 5.5). Le palier Haiku 5.5 au-delà de
100K tokens de prompt (0,50 / 2,50) n'est pas modélisé.

## Modèles servis et leurs options par défaut — lecture des catalogues du 2026-11-11

> Le dernier résultat de chaque modèle campagné, régénéré après chaque campagne, est sur
> [Résultats des campagnes LLM](llm-campaign-results.md) ; les scores ci-dessous sont ceux
> qu'a relevés cette lecture des catalogues.

La campagne du 2026-11-11 a lu l'endpoint de catalogue de chaque fournisseur avec la clé de
campagne (`orkeon llm models -p <fournisseur>` rend les mêmes identifiants), puis joué le
protocole sur chaque défaut, sur les candidats des revues, et sur les modèles de chat courants
que la liste montrait et qu'aucune campagne n'avait encore rencontrés. Deux fournisseurs ont été
campagnés plus tard le même jour : **Kimi** avec une seconde clé (la première était refusée, 401),
et **Z.AI** sur une clé valide (le premier compte n'avait pas de solde : 429,
`Insufficient balance or no resource package`). Un score marqué `(2026-10-07)` ci-dessous n'a
pas été rejoué. Il
en sort trois sortes de faits, tenues séparées à dessein :

- **ce que l'endpoint publie** d'un modèle — contexte, plafond de sortie, échantillonnage
  par défaut, niveaux d'effort. La donnée du vendeur : lue, pas mesurée ;
- **ce que le modèle impose** — une valeur qu'il refuse de voir changer. Aucun endpoint ne
  la publie. On l'apprend en appelant le modèle (une passe sur M1, M5 et M7 aux réglages par
  défaut de la sonde) et elle est consignée dans `requiredParams` ;
- **ce que la campagne a mesuré** sur les douze modes automatisés.

La liste complète — 1 312 identifiants pour quatorze fournisseurs, chacun avec les options
que son endpoint publie — est
[`llmproviders-test/lib/served-models.json`](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/lib/served-models.json),
un instantané daté auquel la prochaine campagne se comparera. Azure OpenAI et Ollama n'ont
pas de ligne : l'un sert les déploiements d'un compte, l'autre ce qu'une machine a tiré.

| Fournisseur | Requête du catalogue | Identifiants | Ce qu'elle publie par modèle |
|---|---|---|---|
| OpenAI | `GET api.openai.com/v1/models` | 96 | les identifiants seuls |
| Anthropic | `GET api.anthropic.com/v1/models` | 15 | contexte, plafond de sortie, niveaux d'effort, types de thinking, modalités |
| DeepSeek | `GET api.deepseek.com/models` | 2 | contexte, plafond de sortie, modalités, niveaux d'effort et leur défaut |
| Z.AI | `GET api.z.ai/api/paas/v4/models` | 12 | les identifiants seuls |
| Kimi | `GET api.moonshot.ai/v1/models` | 6 | contexte, entrée image/vidéo, raisonnement |
| Google Gemini | `GET generativelanguage.googleapis.com/v1beta/models` | 62 | contexte, plafond de sortie, température / topP / topK par défaut, température maximale, thinking |
| Mistral AI | `GET api.mistral.ai/v1/models` | 64 | contexte, température par défaut, capacités, alias, dépréciation |
| Qwen | `GET dashscope-intl.aliyuncs.com/compatible-mode/v1/models` | 101 | les identifiants seuls |
| Grok (x.AI) | `GET api.x.ai/v1/language-models` | 8 | modalités, prix en $ par million de tokens, seuil de contexte long, alias |
| MiniMax | `GET api.minimax.io/v1/models` | 9 | les identifiants seuls |
| Together AI | `GET api.together.xyz/v1/models` | 270 | type, contexte, prix en $ par million de tokens |
| HuggingFace | `GET router.huggingface.co/v1/models` | 127 | modalités et, par fournisseur routé : contexte, outils, sortie structurée |
| OpenRouter | `GET openrouter.ai/api/v1/models` | 433 | contexte, plafond de sortie du premier endpoint, paramètres par défaut, raisonnement (obligatoire, effort par défaut, efforts acceptés) |
| Mammouth AI | `GET api.mammouth.ai/v1/models` | 107 | contexte, plafond de sortie |

Les modèles de chat de la génération courante, fournisseur par fournisseur. Un tiret dans les
deux colonnes de taille signifie que l'endpoint ne publie pas le chiffre — pour ces modèles,
les plafonds documentés sont ceux de la section suivante.

| Fournisseur | Modèle | Contexte | Sortie max | Publié par l'endpoint | Imposé par le modèle (mesuré) | Campagne ✅/❌/➖ |
|---|---|---|---|---|---|---|
| OpenAI | `gpt-5.6-sol` *(défaut)* | — | — | — | `temperature` = `1` · `reasoning_effort` = `none` avec des outils | 11/1/0 — ❌ M9 |
|  | `gpt-5.6-terra` | — | — | — | `temperature` = `1` · `reasoning_effort` = `none` avec des outils | 11/1/0 — ❌ M9 (2026-10-07) |
|  | `gpt-5.6-luna` | — | — | — | `temperature` = `1` · `reasoning_effort` = `none` avec des outils | 11/1/0 — ❌ M6 (2026-10-07) |
|  | `gpt-6-sol` | — | — | — | `temperature` = `1` · `reasoning_effort` = `none` avec des outils | 11/1/0 — ❌ M9 |
|  | `gpt-6-luna` | — | — | — | `temperature` = `1` · `reasoning_effort` = `none` avec des outils | 12/0/0 |
|  | `gpt-6.1-terra` | — | — | — | `temperature` = `1` · `reasoning_effort` = `none` avec des outils | 12/0/0 |
|  | `gpt-6.1-sol` | — | — | — | `temperature` = `1` | 9/3/0 — ❌ M2, M5, M9 |
|  | `gpt-6-astra` | — | — | — | `temperature` = `1` | 10/2/0 — ❌ M2, M5 |
| Anthropic | `claude-sonnet-5` *(défaut)* | 1 000 000 | 128 000 | efforts `low` `medium` `high` `xhigh` `max` · thinking `adaptive` `disabled` | `temperature` = `1` | 12/0/0 |
|  | `claude-sonnet-5-5` | 1 000 000 | 128 000 | efforts `low` `medium` `high` `xhigh` `max` · thinking `adaptive` | `temperature` = `1` | 12/0/0 |
|  | `claude-haiku-5-5` | 1 000 000 | 128 000 | efforts `low` `medium` `high` `xhigh` `max` · thinking `adaptive` `disabled` | `temperature` = `1` | 12/0/0 |
|  | `claude-opus-5` | 1 000 000 | 128 000 | efforts `low` `medium` `high` `xhigh` `max` · thinking `adaptive` `disabled` | `temperature` = `1` | 12/0/0 (2026-10-07) |
|  | `claude-opus-5-5` | 1 000 000 | 128 000 | efforts `low` `medium` `high` `xhigh` `max` · thinking `adaptive` | `temperature` = `1` | 12/0/0 |
|  | `claude-fable-5-1` | 1 000 000 | 128 000 | efforts `low` `medium` `high` `xhigh` `max` · thinking `adaptive` | `temperature` = `1` | 11/1/0 — ❌ M6 |
|  | `claude-fable-5-2` | 1 000 000 | 128 000 | efforts `low` `medium` `high` `xhigh` `max` · thinking `adaptive` | `temperature` = `1` | 12/0/0 |
| DeepSeek | `deepseek-flash` *(défaut)* | 1 048 576 | 393 216 | effort par défaut `high` (sur `low` `high` `max`) · text + image | — | 12/0/0 |
|  | `deepseek-v4-pro` | 1 048 576 | 393 216 | effort par défaut `high` (sur `low` `high` `max`) · text | — | 11/1/0 — ❌ M9 (2026-10-07) |
| Z.AI | `glm-5.2` | — | — | — | — | 11/1/0 — ❌ M9 (10/2/0 plus tôt le même jour) |
|  | `glm-5.3` | — | — | — | — | 10/2/0 — ❌ M2, M9 (11/1/0 plus tôt le même jour) |
|  | `glm-5.3-flash` | — | — | — | — | 12/0/0 (11/1/0, M2 rouge, plus tôt le même jour) |
|  | `glm-5.3-flashx` *(défaut)* | — | — | — | — | 11/1/0 — ❌ M10 (12/0/0 plus tôt le même jour) |
| Kimi | `kimi-k2.6` | 262 144 | — | image + vidéo + raisonnement | `temperature` = `1` | 10/1/1 — ❌ M8 |
|  | `kimi-k3` | 1 048 576 | — | image + vidéo + raisonnement | `temperature` = `1` | 11/1/0 (M2) |
|  | `kimi-k3.1-pro` *(défaut)* | 1 048 576 | — | image + vidéo + raisonnement | `temperature` = `1` | 12/0/0 |
|  | `kimi-k3.1-pro-highspeed` | 1 048 576 | — | image + vidéo + raisonnement | `temperature` = `1` | 12/0/0 |
|  | `kimi-k2.7-code` | 262 144 | — | image + vidéo + raisonnement | `temperature` = `1` | 12/0/0 |
|  | `kimi-k2.7-code-highspeed` | 262 144 | — | image + vidéo + raisonnement | `temperature` = `1` | 12/0/0 |
| Google Gemini | `gemini-3.7-flash` | 1 048 576 | 65 536 | `temperature` 1 (max 2) · `topP` 0.95 · `topK` 64 | — | 11/0/1 |
|  | `gemini-3.8-flash` *(défaut)* | 1 048 576 | 65 536 | `temperature` 1 (max 2) · `topP` 0.95 · `topK` 64 | — | 12/0/0 |
|  | `gemini-4-argon` | 1 048 576 | 32 768 | `temperature` 1 (max 2) · `topP` 0.95 · `topK` 64 | — | 11/0/1 |
|  | `gemini-4-carbon` | 1 048 576 | 32 768 | `temperature` 1 (max 2) · `topP` 0.95 · `topK` 64 | — | 11/0/1 |
|  | `gemini-3.5-flash-lite` | 1 048 576 | 65 536 | `temperature` 1 (max 2) · `topP` 0.95 · `topK` 64 | — | 12/0/0 (2026-10-07) |
|  | `gemini-3.1-pro-preview` | 1 048 576 | 65 536 | `temperature` 1 (max 2) · `topP` 0.95 · `topK` 64 | — | 10/1/1 — ❌ M5 (2026-10-07) |
| Mistral AI | `mistral-medium-2604` *(défaut)* | 262 144 | — | `temperature` 1 · outils + raisonnement + vision | `reasoning_effort` ∈ {`high`, `none`} | 12/0/0 |
|  | `mistral-large-4` | 1 048 576 | — | `temperature` 1 · outils + raisonnement + vision | `reasoning_effort` ∈ {`high`, `none`} | 12/0/0 |
|  | `mistral-small-2603` | 262 144 | — | `temperature` 0.3 · outils + raisonnement + vision | `reasoning_effort` ∈ {`high`, `none`} | 11/1/0 — ❌ M9 |
| Qwen | `qwen3.7-plus` *(défaut)* | — | — | — | — | 12/0/0 |
|  | `qwen3.7-flash` | — | — | — | — | 11/1/0 — ❌ M2 (2026-10-07) |
|  | `qwen3.7-max` | — | — | — | — | 11/1/0 — ❌ M9 (2026-10-07) |
|  | `qwen3.8-flash` | — | — | — | — | 12/0/0 |
|  | `qwen3.8-max` | — | — | — | — | 12/0/0 |
|  | `deepseek-v4.1-flash-1017` | — | — | — | — | 12/0/0 |
| Grok (x.AI) | `grok-4.6` *(défaut)* | — | — | text + image · 2 / 0.5 / 6 $/M entrée / cache / sortie | — | 12/0/0 |
|  | `grok-4.7` | — | — | text + image · 2 / 0.5 / 6 $/M entrée / cache / sortie | — | 12/0/0 |
| MiniMax | `MiniMax-M2` | — | — | — | — | 6/4/2 — ❌ M2, M5, M9, M10 |
|  | `MiniMax-M2.7` | — | — | — | — | 8/2/2 — ❌ M2, M9 (2026-10-07) |
|  | `MiniMax-M3` *(défaut)* | — | — | — | — | 10/0/2 |
|  | `MiniMax-M3.1-Flash` | — | — | — | — | 10/0/2 |
| Together AI | `zai-org/GLM-5.3-Flash` *(défaut)* | 1 048 575 | — | 0.15 / 0.5 $/M | — | 11/0/1 (10/1/1, M10 rouge, plus tôt le même jour) |
|  | `Qwen/Qwen3.5-9B` | 262 144 | — | 0.17 / 0.25 $/M | — | 10/1/1 — ❌ M10 |
|  | `meta-llama/Llama-3.3-70B-Instruct-Turbo` *(ancien défaut)* | 131 072 | — | 1.04 / 1.04 $/M | — | 2/9/1 — plus serverless |
| HuggingFace | `meta-llama/Llama-3.1-8B-Instruct` *(défaut)* | jusqu'à 131 072 | — | 3 fournisseur(s) routé(s), 1 avec outils | — | 7/3/2 — ❌ M2, M5, M9 |
|  | `Qwen/Qwen3.5-9B` | jusqu'à 262 144 | — | 3 fournisseur(s) routé(s), 2 avec outils | — | 9/1/2 — ❌ M2 |
| OpenRouter | `google/gemini-3.7-flash` | 1 048 576 | 65 536 | effort par défaut `medium` · raisonnement obligatoire | — | 11/1/0 — ❌ M10 |
|  | `google/gemini-3.8-flash` *(défaut)* | 1 048 576 | 65 536 | effort par défaut `medium` · raisonnement obligatoire | — | 12/0/0 |
|  | `moonshotai/kimi-k3.1-pro` | 1 048 576 | 943 718 | effort par défaut `max` | — | 12/0/0 |
|  | `deepseek/deepseek-v4.1-flash` | 1 048 576 | 943 718 | effort par défaut `high` | — | 4/0/0 (M5, M7, M8, M10) (2026-10-07) |
| Mammouth AI | `gemini-3.7-flash` | 1 048 576 | 65 536 | — | — | 9/1/2 — ❌ M10 |
|  | `gemini-3.8-flash` *(défaut)* | 1 048 576 | 65 536 | — | — | 9/1/2 — ❌ M10 |
|  | `kimi-k3.1-pro` | 1 048 576 | 943 718 | — | — | 10/0/2 |
|  | `deepseek-v4.1-flash` | 1 048 576 | 1 048 576 | — | — | 1/0/2 (M5, M7, M8) (2026-10-07) |

Ce que le tableau ne dit pas tout seul :

- **OpenAI** — la génération 6 est listée (`gpt-6-sol`, `gpt-6-luna`, `gpt-6.1-sol`,
  `gpt-6.1-terra`, à côté de `gpt-6-astra`). `gpt-6-sol`, `gpt-6-luna`, `gpt-6.1-terra` et
  les deux tiers 5.6 non rejoués se comportent comme le défaut : température 1 seulement, et
  les outils sur `/v1/chat/completions` exigent `reasoning_effort: none`. `gpt-6.1-terra`
  accepte `none`, contrairement à `gpt-6.1-sol`, et passe 12/12 dès son premier essai.
  `gpt-6.1-sol` se comporte comme `gpt-6-astra` : il n'a pas de `none`, les outils y restent
  donc refusés (M2 sur le tableau `messages`, M5) tant que le dialecte ne parle pas
  `/v1/responses`. Le premier passage du 2026-11-11 (06:10) était rouge sur M2 et M5 pour
  tous les modèles OpenAI : Orkeon abaissait le `none` épinglé à `low` depuis #242, et la
  ligne `gpt-5` du tableau de capacités attrapait les ids `gpt-5.6-*`. Corrigé et rejoué à
  06:25 : `gpt-5.6-sol` 11/1, `gpt-6-sol` 11/1, `gpt-6-luna` et `gpt-6.1-terra` 12/12 ;
  `gpt-6-astra` et `gpt-6.1-sol` restent rouges, comme prévu. M9 est le mode qui bouge à la
  température imposée : `29`, `26` et `18` ont été lus pour 73 le 2026-10-07, `23`, `43` et
  `23` le 2026-11-11 (`gpt-5.6-sol`, `gpt-6-sol`, `gpt-6.1-sol`).
- **Anthropic** — `claude-sonnet-5-5`, `claude-opus-5-5` et `claude-haiku-5-5` sont listés,
  le dernier le jour même de la campagne. Les quatre modèles campagnés pour la première fois
  refusent une température épinglée dans les mots de `claude-sonnet-5`. `claude-opus-5-5`
  répond à l'invite d'outil de la sonde par un HTTP 200, un `content` vide et
  `stop_reason: "refusal"` (catégorie `cyber`) : le provider ne lit pas `stop_reason` et rend
  une réponse vide — ses rouges M5 et M6 sont ce refus, pas l'appel d'outil. Le rouge M9 de
  `claude-haiku-5-5` est celui de la sonde : le modèle répond `Seventy-three red`, le contrôle
  cherche les chiffres. Le 2026-11-11, `claude-haiku-5-5` est à 12/12 et `claude-opus-5-5` à
  12/12 avec la même invite : le classifieur ne rencontre plus la formulation de la sonde.
  `claude-fable-5-2` (listé le 2026-11-09) refuse une température explicite comme le reste de
  la famille 5 et passe 12/12 dès son premier essai, y compris le M6 que `claude-fable-5-1`
  manque encore (11/1).
- **Mistral AI** — le compte n'est plus limité en débit, le défaut a donc de nouveau un
  résultat (11/1, sa ligne du 2026-08-30). `mistral-large-4` raisonne par défaut et diffuse
  `delta.content` sous la forme d'un tableau de morceaux typés : le flux texte levait une
  exception dessus (M3) et le flux de chat perdait la trace et les premiers mots de la
  réponse. Les deux flux lisent les deux formes depuis le 2026-10-07 ; rejoué sur ce
  correctif, le modèle était à 11/1, M3 vert, et il est à 12/12 le 2026-11-11 (son contexte
  publié est passé de 524 288 à 1 048 576). Le défaut est lui aussi à 12/12. `/v1/models`
  liste désormais aussi des modèles d'autres vendeurs servis par Mistral —
  `deepseek-v4-1-flash-1017` et `zai-glm-5-3-flash`, tous deux 11/1 dès leur premier essai : le
  message système est perdu sur la forme tableau `messages` de M2 alors que la forme aplatie
  le respecte.
- **Kimi** — joué une heure après les autres : le compte était d'abord suspendu pour solde
  insuffisant (HTTP 429, `exceeded_current_quota_error`, que la politique de retry traite
  comme une limite de débit). `kimi-k2.6` clôture toujours son `json_object` en markdown (M8,
  comme le 2026-09-21) ; `kimi-k2.7-code` est le seul modèle Kimi dont tous les modes sont
  verts. Lui aussi impose la température 1, ce que seuls ses flux ont montré : sur un appel
  bufferisé le provider retente une fois avec la valeur imposée, les deux chemins de flux non.
  **2026-11-11**, avec une seconde clé (la première était refusée, 401 `Incorrect API key
  provided`) : la liste passe à six identifiants — `kimi-k3.1-pro` et les variantes
  `-highspeed` de celui-ci et de `kimi-k2.7-code`. Les trois imposent eux aussi la température 1
  (6/6 à 0 : M2, M3, M4, M5, M9 et M10 refusés) et sont à 12/12 une fois épinglés ; `kimi-k3`
  est à 12/12 aussi, `kimi-k2.7-code` toujours à 12/12. `kimi-k2.6` reste à 10/1/1 — M8
  toujours clôturé, M10 ne rapporte aucune ventilation des tokens en cache. `kimi-k3.1-pro`
  est aussi à 12/12 via OpenRouter, 10/0/2 via Mammouth.
- **Z.AI** — `glm-5.3-flashx` est le deuxième modèle de la famille à passer tous les modes (au premier essai du jour).
  La liste omet `glm-4.6v-flash`, qui est servi (le compagnon vision, M9 vert le même jour) :
  un catalogue n'est pas toute l'offre. `glm-5.3-flashx` est de nouveau à 12/12 au premier essai, 11/1 au dernier (M10 rouge) ;
  `glm-5.3` passe M2 au vert au premier essai (11/1, reste M9 `1210`) puis le repasse au rouge
  au dernier (10/2) ; `glm-5.3-flash` garde M2 rouge au premier essai, son message système
  ignoré sous les deux formes — le modèle, pas Orkeon — et est à 12/12 au dernier ; `glm-5.2` est
  à 10/2 puis 11/1 (M9 `1210` ; M10 `0 cached` au premier essai). La première clé du jour n'avait pas de solde (429, code 1113), que le provider a
  retenté comme une limite transitoire pendant 21 minutes (issue #328). `glm-5.3-flashx-legal`
  est nouveau dans la liste, non campagné.
- **Qwen** — une clé internationale n'est servie que par `dashscope-intl.aliyuncs.com` ;
  l'endpoint compilé (`dashscope.aliyuncs.com`) lui répond 401. Régler `Llm:BaseUrl`. La liste
  internationale est tombée de 172 à 101 ids le 2026-11-11 (la lignée qwen3 open-weight et les
  familles qwen-vl / omni / tts l'ont quittée) ; `qwen3.7-plus`, `qwen3.8-flash` et
  `qwen3.8-max` sont à 12/12, et `deepseek-v4.1-flash-1017`, servi là, est à 12/12 dès son
  premier essai.
- **Gemini** — `gemini-3.5-flash-lite` est le seul modèle Gemini dont le détail du cache est
  revenu (4077 tokens en cache) ; le rouge M5 de `gemini-3.1-pro-preview` ne s'est pas
  reproduit en trois rejeux (non rejoué le 2026-11-11). `gemini-3.8-flash` est à 12/12 le
  2026-11-11 et le défaut à 11/0/1. `gemini-4-argon` et `gemini-4-carbon` (contexte 1 048 576,
  sortie 32 768, thinking) sont à 11/0/1 chacun dès leur premier essai. `gemini-omni-1.2-flash`
  est listé par `/v1beta/models` mais répond `This model only supports Interactions API` sur la
  surface compatible OpenAI : hors de portée de ce provider, non campagné.
- **MiniMax** — `MiniMax-M3.1-Flash` est nouveau dans la liste (9 ids) : 10/0/2 dès son premier
  essai, M7 et M8 non déclarés par le provider, M9 vert — il lit l'image là où `MiniMax-M3` se
  trompe de nombre (`72` ; 9/1/2 au premier essai, 10/0/2 au dernier : il lit l'image
  aussi). `MiniMax-M2` est à 8/2/2 (M2, M9), puis 6/4/2 au dernier essai (M2, M5, M9, M10).
- **Together AI** — `meta-llama/Llama-3.3-70B-Instruct-Turbo`, l'ancien défaut compilé, n'est plus
  servi en serverless : chaque appel répond `Unable to access non-serverless model` (2/9/1). Le
  défaut passe à `zai-org/GLM-5.3-Flash` (11/0/1 au dernier essai du jour, 10/1/1 au
  premier avec M10 rouge : l'endpoint ne rapporte aucun token en cache) ; `Qwen/Qwen3.5-9B` est à 10/1/1 (M10). Le défaut voit, il n'y a donc plus
  de compagnon vision séparé.
- **HuggingFace** — le défaut est à 7/3/2 (M2, M5, M9) et `Qwen/Qwen3.5-9B` à 9/1/2 (M2) ; le
  routage décide toujours du nombre de fournisseurs d'un modèle qui portent les outils.
- **OpenRouter et Mammouth AI** — premières campagnes, toutes deux sur le défaut Gemini du
  parc : un modèle, trois transports. Direct 11/0/1, OpenRouter 11/1/0, Mammouth 10/0/2 — la
  place de marché rend les champs de cache à 0 là où Google n'en rend aucun (M10), et les
  deux ➖ de Mammouth sont les capacités que le provider ne déclare pas. Le catalogue
  d'OpenRouter est le plus riche du parc : par modèle, les paramètres par défaut et le
  caractère obligatoire du raisonnement. Rejoué le 2026-11-11 : direct 11/0/1 (et
  `gemini-3.8-flash` 12/12), OpenRouter 12/0/0 sur le défaut et 11/1/0 sur `gemini-3.8-flash`
  (M10), Mammouth 9/1/2 sur les deux — son M10 lit désormais 0 token en cache là où il était
  vert le 2026-10-07.
- **Servis aussi, non campagnés** — entre autres `claude-fable-5`, `claude-opus-4-8`,
  `gemini-3.6-flash`, `gemini-3.5-flash`, `mistral-large-2512`, la famille `ministral`,
  `grok-4.5`, `grok-4.3`, `MiniMax-M2.1`, `MiniMax-M2.5`, `glm-5.1`, `glm-5-turbo`,
  `qwen3.6-plus`, `qwen3.5-plus`. L'instantané les liste tous.

## Plafonds de sortie — le maximum documenté par modèle

Une requête porte un plafond de sortie (`max_tokens`, `max_completion_tokens` chez OpenAI,
`num_predict` chez Ollama). Non épinglé, le plafond est **le maximum documenté du modèle**, tiré
du catalogue `LlmModelOutputLimits` d'`Orkeon.Constants.Llm` : un 4096 fixe laisserait un modèle
de raisonnement épuiser le budget à penser et répondre vide. Une valeur
épinglée (`Llm:MaxTokens`, un profil, le `max_tokens` d'une crew) gagne toujours ; un
modèle inconnu du catalogue garde le repli 4096. N'y entrent que des chiffres documentés, chacun
lu le 2026-09-19 :

| Fournisseur | Modèle | Plafond envoyé | Source | Note |
|---|---|---|---|---|
| OpenAI (et les déploiements Azure des mêmes ids) | `gpt-5.6-sol`, `gpt-6-astra` | 128 000 | developers.openai.com/api/docs/models | le raisonnement compte dans le plafond |
| Anthropic | `claude-sonnet-5`, `claude-opus-5`, `claude-fable-5-1` (les variantes datées suivent la famille) | 128 000 | platform.claude.com/docs/en/about-claude/models/overview | la réflexion compte dans `max_tokens` ; le champ est obligatoire, un Claude inconnu reçoit 4096 |
| Gemini | `gemini-3.7-flash`, `gemini-3.8-flash` (aussi sous `google/…` chez OpenRouter et nu chez Mammouth) | 65 536 | ai.google.dev/gemini-api/docs/models | jetons de réflexion inclus ; 65 537 fait un 400 |
| DeepSeek | `deepseek-flash` (et les noms retirés `deepseek-v4-flash*` qu'il route) | 393 216 | api-docs.deepseek.com/api/create-chat-completion | « 1 à 384K » ; 384 000 chez Mammouth |
| Kimi | `kimi-k3` | 131 072 | platform.kimi.ai/docs/guide/kimi-k3-quickstart | le défaut `max_completion_tokens` du fournisseur ; borne réelle 1M − prompt |
| Kimi | `kimi-k3.1-pro` | 131 072 | platform.kimi.ai/docs/guide/kimi-k3-quickstart | **un épinglage, pas un plafond documenté** : le défaut de K3 reporté sur le modèle par défaut du 2026-11-11 ; `kimi-k3` seul ne le couvrirait pas (`.` n'est pas un séparateur), et le repli à 4096 affamait K3 |
| Kimi | `kimi-k2.6` | 131 072 | platform.kimi.ai/docs/guide/troubleshooting | **un épinglage, pas un plafond documenté** : la borne est 256K − prompt ; un rejet retire le champ au rejeu |
| Qwen | `qwen3.7-plus`, `qwen3.8-flash`, `qwen3.8-max` | 131 072 | alibabacloud.com/help/en/model-studio | la chaîne de pensée a son propre `thinking_budget` ; 65 500 chez Mammouth |
| Z.AI | `glm-5.2`, `glm-5.3`, `glm-5.3-flash`, `glm-5.3-flashx` | 131 072 | docs.z.ai/guides/overview/concept-param | maximum du schéma ; défaut 65 536 |
| Z.AI | `glm-4.6v-flash` | 32 768 | docs.z.ai/guides/vlm/glm-4.6v | — |
| MiniMax | `MiniMax-M2` | 131 072 | platform.minimax.io/docs/guides/models-intro | « 128k (CoT compris) » ; **M3 est absent**, défaut depuis le 2026-11-11 — le fournisseur ne publie aucun chiffre de sortie, il tourne donc sur le repli de 4096 (512k n'apparaît que comme réglage de benchmark ; 512 000 chez Mammouth) |
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
une valeur épinglée appartient à l'utilisateur, et son rejet remonte tel quel.

## Valeurs de paramètres obligatoires, par modèle

Certains modèles refusent une requête tant qu'un paramètre ne porte pas une valeur précise.
C'est autre chose que les écarts de capacité par modèle ci-dessus (un modèle sans
thinking ou sans vision) : ici la capacité existe, mais le modèle dicte la valeur, et le
vendeur répond 400 à tout le reste. **La contrainte est par modèle, jamais par fournisseur** —
le même vendeur sert des modèles aux exigences opposées, un épinglage au niveau fournisseur
est donc à une campagne de casser le modèle voisin. Chaque ligne ci-dessous est mesurée en
réel ; rien n'est inféré.

| Fournisseur | Modèle | Paramètre | Valeur obligatoire | Les mots du vendeur | Mesuré |
|---|---|---|---|---|---|
| Kimi | `kimi-k2.6` | `temperature` | `1` | `invalid temperature: only 1 is allowed for this model` | 2026-08-03 |
| Kimi | `kimi-k2.7-code` | `temperature` | `1` — un appel bufferisé est retenté par le provider avec cette valeur, un flux est refusé | `invalid temperature: only 1 is allowed for this model` | 2026-10-07 |
| Kimi | `kimi-k3.1-pro`, `kimi-k3.1-pro-highspeed`, `kimi-k2.7-code-highspeed` | `temperature` | `1` — tous les modes de flux et M2, M5, M9, M10 refusés à 0 ; sur la famille K3, le provider laisse désormais le champ de côté (avec un avertissement) sur tous les chemins | `invalid temperature: only 1 is allowed for this model` | 2026-11-11 |
| OpenAI | `gpt-5.6-sol` | `temperature` | `1` | `'temperature' does not support 0 with this model. Only the default (1) value is supported.` | 2026-08-30 |
| OpenAI | `gpt-5.6-sol` | `reasoning_effort` | `"none"` quand la requête porte des function tools sur `/v1/chat/completions` | `Function tools with reasoning_effort are not supported for gpt-5.6-sol in /v1/chat/completions. To use function tools, use /v1/responses or set reasoning_effort to 'none'.` | 2026-08-30 |
| OpenAI | `gpt-6-astra` | `temperature` | `1` | `'temperature' does not support 0 with this model. Only the default (1) value is supported.` | 2026-09-21 |
| OpenAI | `gpt-6-astra` | `reasoning_effort` | **aucun `"none"` n'existe** (`low`, `medium`, `high`, `xhigh`) — le contournement Sol est impossible, les function tools restent refusés sur `/v1/chat/completions` tant que le dialecte ne parle pas `/v1/responses` | `'reasoning_effort' does not support 'none' with this model. Supported values are: 'low', 'medium', 'high', and 'xhigh'.` — et sans lui, `Function tools with reasoning_effort are not supported for gpt-6-astra in /v1/chat/completions. To use function tools, use /v1/responses` | 2026-09-21 |
| Anthropic | `claude-sonnet-5` | `temperature` | `1`, ou omettre le champ | `` `temperature` is deprecated for this model.`` (1 et l'omission passent ; 0 et 0.7 non) | 2026-08-30 |
| OpenAI | `gpt-5.6-terra`, `gpt-5.6-luna`, `gpt-6-sol`, `gpt-6-luna` | `temperature` et `reasoning_effort` | comme `gpt-5.6-sol` : `1`, et `"none"` avec des outils | les deux phrases de `gpt-5.6-sol`, au nom du modèle près | 2026-10-07 |
| OpenAI | `gpt-6.1-sol` | `temperature` et `reasoning_effort` | comme `gpt-6-astra` : `1`, et **pas de `"none"`** | `'reasoning_effort' does not support 'none' with this model. Supported values are: 'low', 'medium', 'high', and 'xhigh'.` | 2026-10-07 |
| Anthropic | `claude-opus-5`, `claude-opus-5-5`, `claude-sonnet-5-5`, `claude-haiku-5-5` | `temperature` | `1`, ou omettre le champ | `` `temperature` is deprecated for this model.`` | 2026-10-07 |
| Anthropic | `claude-fable-5-2` | `temperature` | `1`, ou omettre le champ | refusée comme le reste de la famille 5 — la phrase n'a pas été consignée pour ce modèle | 2026-11-11 |
| OpenAI | `gpt-6.1-terra` | `temperature` et `reasoning_effort` | comme `gpt-5.6-sol` : `1`, et `"none"` avec des outils — contrairement à `gpt-6.1-sol`, ce modèle a un `none` | les deux phrases de `gpt-5.6-sol`, au nom du modèle près ; 12/12 avec ces deux épinglages | 2026-11-11 |
| Mistral | `mistral-large-4` | `reasoning_effort` | `high` ou `none` seulement | `reasoning_effort low is not supported for this model, supported values: [<ReasoningEffort.high: 'high'>, <ReasoningEffort.none: 'none'>]` | 2026-10-07 |
| Mistral | `mistral-small-2603` | `reasoning_effort` | `high` ou `none` seulement | `reasoning_effort='low' is not supported for this model. Must be one of (<ReasoningEffort.none: 'none'>, <ReasoningEffort.high: 'high'>)` | 2026-10-07 |
| Mistral | `mistral-medium-2604` | `reasoning_effort` | `high` ou `none` seulement | `reasoning_effort low is not supported for this model, supported values: [<ReasoningEffort.high: 'high'>, <ReasoningEffort.none: 'none'>]` | 2026-08-30 |
| Mistral | `mistral-medium-2604` | `top_p` | `1` explicite quand `temperature` vaut 0 et que le raisonnement est actif (l'omission n'y vaut PAS 1) | `top_p must be 1 when using greedy sampling.` | 2026-08-30 |

Chez OpenAI, les tiers GPT-5.6 et GPT-6 ne prennent des function tools sur
`/v1/chat/completions` qu'avec `reasoning_effort: none` (`gpt-6-astra` et `gpt-6.1-sol` n'ont
pas de `none` : les outils exigent `/v1/responses`). L'épinglage doit arriver à l'API sous la
forme `none` : le 2026-11-11, un premier passage voyait Orkeon l'abaisser à `low`, ce qui a
mis M2 et M5 au rouge sur tous les modèles OpenAI jusqu'au correctif.

Une température que rien ne fixe ne part pas : un hôte qui n'en épingle aucune fait
tourner ces modèles sur leur propre défaut, qu'ils acceptent — `orkeon init` n'en
écrit pas. Un hôte qui en épingle une doit épingler la valeur imposée.

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
le fournisseur l'accepte. Les preuves d'exécution réelle sont archivées séparément par le kit
de campagne ([`llmproviders-test/`](https://github.com/Orkeon/orkeon/tree/main/llmproviders-test),
dont le README porte le journal des campagnes ; le dernier résultat par modèle est sur
[Résultats des campagnes LLM](llm-campaign-results.md)) ; lancez une campagne avec
`orkeon llm probe --provider <nom> --archive <dossier>`, ou le `run-campaign.sh` du kit
([valider un fournisseur](../architecture/llm-providers.md#valider-un-fournisseur-contre-son-api-réelle)).
