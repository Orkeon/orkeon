> 🇬🇧 [English version](../../architecture/llm-providers.md)

# Fournisseurs LLM

Les fournisseurs de cette page servent la catégorie `Generation`. Les fournisseurs d'embedding, de reranking et de décision (TypeSafe) sont listés dans [Fournisseurs de modèles](./model-providers.md) ; un modèle de décision juge un appel au LLM à travers un profil de décision ([Modèles de décision](./decision-models.md)).

`HttpLlmProviderBase` (`Orkeon.Infrastructure.LLMs.Base`) fournit la base abstraite pour tous les fournisseurs LLM. Elle intègre la gestion HTTP (`IHttpClientFactory`), les politiques de retry Polly, et la sérialisation JSON.

**Budget de retry.** `Llm:MaxRetries` (10 par défaut) couvre les échecs qui reviennent en quelques secondes — erreurs de requête, 5xx, 429. Un appel bufferisé qui atteint `Llm:TimeoutSeconds` est réessayé **une fois** (`ResilienceDefaults.LlmTimeoutRetries` : chaque tentative coûte le délai entier), puis le provider répond par un échec qui nomme le réglage et les deux issues (un délai plus long, la réflexion coupée) ; une annulation de l'appelant n'est jamais réessayée. Un appel échoué voyage en `LlmResponse.Error` et, à travers l'adaptateur de chat client, en exception — il n'est jamais confondu avec une réponse vide, si bien que la boucle agent fait échouer la tâche immédiatement avec la raison du provider au lieu de relancer sans outils (LLM-11).

**Fenêtre de limite de débit.** Un 429, ou un 529 qui porte un en-tête de réinitialisation, n'est pas réessayé sur l'échelle de recul : l'appel attend la réinitialisation de la fenêtre, sur le chemin tamponné comme sur la connexion de flux (`RateLimitResetHeaders`). Il lit `Retry-After` (secondes ou date HTTP), `retry-after-ms` (millisecondes, envoyé par OpenAI et TypeSafe), `anthropic-ratelimit-requests-reset` et `anthropic-ratelimit-tokens-reset` (RFC 3339) et `x-ratelimit-reset-requests` et `x-ratelimit-reset-tokens` (durées OpenAI comme `6m0s`), et la plus longue l'emporte. L'attente ne dépasse jamais `Llm:MaxRateLimitWait` (5 minutes par défaut, distinct du plafond de 30 secondes de l'échelle 5xx) : une réinitialisation au-delà fait échouer l'appel aussitôt avec la raison (`LlmRateLimitException`) au lieu de garer une exécution sans surveillance. Chaque attente est annoncée à `ILlmRetryObserver` avec la raison `rate-limit window` et le délai. Un 529 sans en-tête garde l'échelle.

**Un appel streamé est borné de la même façon (LLM-12).** Une requête streamée part en `ResponseHeadersRead`, et `HttpClient.Timeout` cesse de compter dès que les en-têtes sont arrivés : le corps était lu sur le seul token de l'appelant, si bien qu'un modèle qui réfléchit plusieurs minutes avant son premier token faisait pendre un run Studio au-delà de tout `Llm:TimeoutSeconds`. Chaque chemin de streaming (dialecte OpenAI, Azure, Anthropic, Ollama — flux chat et flux texte) court désormais sous un `StreamReadBudget` : `Llm:TimeoutSeconds` borne l'appel entier, en-têtes et corps, et `Llm:StreamIdleSeconds`, optionnel, borne le silence entre deux lignes du corps (non posé par défaut). Une borne atteinte termine le flux chat par un événement `Completed` dont la réponse porte `error` (la phrase qui nomme le réglage) et `error_type` (`TaskCanceledException` pour la borne de l'appel entier, `StreamIdleTimeout` pour celle du silence) par-dessus le contenu déjà arrivé, sans `RawResponseBody` ; le flux texte lève une `HttpRequestException` portant la même phrase. Un flux que le provider ferme sans son marqueur de fin (`finish_reason` ou `[DONE]`, `message_stop`, `done: true`) est lui aussi un appel échoué quand aucun texte de réponse ni appel d'outil n'est arrivé (`error_type` `StreamTruncated`, la phrase disant combien de raisonnement a été streamé) ; avec un début de réponse, il est servi tel quel, marqué `stream_truncated` dans ses métadonnées. L'adaptateur de client de chat fait échouer un tour streamé sur tout `error`, contenu partiel ou non ; la phase des en-têtes renvoie une fois un `HttpClient.Timeout` atteint (`LlmTimeoutRetries`), comme le chemin bufferisé.

Fournisseurs implémentés :

| Fournisseur | Classe | Namespace |
|-------------|--------|-----------|
| OpenAI | `OpenAIProvider` (via `OpenAICompatibleProviderBase`) | `Orkeon.Infrastructure.LLMs` |
| Anthropic | `AnthropicLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Azure OpenAI | `AzureOpenAILlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Ollama | `OllamaLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Together AI | `TogetherAiLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| DeepSeek | `DeepSeekLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| HuggingFace | `HuggingFaceLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Kimi | `KimiLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Qwen | `QwenLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Mistral AI | `MistralLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Z.AI (Zhipu GLM) | `ZaiLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Google Gemini | `GeminiLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Grok (x.AI) | `GrokLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| MiniMax | `MiniMaxLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| OpenRouter (agrégateur) | `OpenRouterLlmProvider` | `Orkeon.Infrastructure.LLMs` |
| Mammouth AI (agrégateur) | `MammouthLlmProvider` | `Orkeon.Infrastructure.LLMs` |

Quatorze d'entre eux étendent `OpenAICompatibleProviderBase` (elle-même un
`HttpLlmProviderBase`) ; Anthropic et Ollama étendent directement `HttpLlmProviderBase`, car
leurs API ne suivent pas la forme OpenAI. Docker Model Runner et tout autre serveur compatible
OpenAI n'ont pas de classe propre : `OpenAIProvider` les pilote via leur URL de base. Endpoints,
modèles par défaut et clés de provider sont des constantes partagées dans
`Orkeon.Constants.Llm` (`LlmProviderEndpoints`, `LlmProviderDefaultModels`, `LlmProviderKeys`,
`LlmModelOutputLimits`).

Des adaptateurs génériques (`ChatClientToLlmProviderAdapter`, `LlmProviderToChatClientAdapter`, `ChatClientToBasicLlmProviderAdapter`) sont disponibles dans `Orkeon.Infrastructure.LLMs.Adapters` pour intégrer d'autres fournisseurs compatibles avec l'interface `IChatClient`. Un agent Microsoft Agent Framework peut aussi servir de modèle (`AIAgentLlmProvider`, [ADR-010](../adr/ADR-010-agent-framework-interop.md)).

### Résolution du provider

`LlmProviderFactory` (`Orkeon.Infrastructure.LLMs`) a deux points d'entrée :

- `Create(providerType, config)` prend une clé de provider explicite (`LlmProviderKeys`) :
  `openai`, `anthropic`, `ollama`, `azure-openai` / `azure`, `together` / `togetherai`,
  `qwen`, `deepseek`, `kimi` / `moonshot`, `mistral`, `huggingface` / `hf`, `gemini` /
  `google`, `grok` / `xai`, `minimax`, `zai` / `glm` / `zhipu`, `openrouter`, `mammouth`, et
  `custom` / `docker-model-runner`, qui construisent le dialecte OpenAI sur l'URL de base configurée.
  Une clé inconnue lève `NotSupportedException`. C'est le chemin de
  `orkeon llm probe --provider`.
- `Create(config)` déduit le provider — c'est ce que fait un run, puisque la section `Llm` du
  fichier de settings ne porte aucune clé de provider. La première règle qui correspond
  l'emporte :
  1. **Avec une URL de base, l'adresse seule décide** (`LlmEndpointReading`, la lecture unique
     que partagent le run, la CLI et Studio ; ni le modèle ni la clé ne sont consultés) :

     | Adresse | Provider |
     |---|---|
     | un hôte de vendeur connu — `azure` / `.cognitiveservices.`, `together.xyz`, les hôtes DashScope, `deepseek.com`, `moonshot.cn` / `moonshot.ai`, `mistral.ai`, `generativelanguage.googleapis.com`, `api.x.ai`, `api.minimax.io` / `api.minimaxi.com`, `huggingface.co` / `hf.co`, `api.z.ai` / `bigmodel.cn`, `openrouter.ai`, `mammouth.ai`, `openai.com`, `anthropic.com` | ce vendeur |
     | `/engines/` dans le chemin, `model-runner.docker.internal` ou le port `12434` | Docker Model Runner (dialecte OpenAI) |
     | le port `11434` | Ollama, à sa racine native (un `/v1` ou `/api` final est retiré) |
     | `anthropic` dans l'hôte ou dans le chemin | Anthropic |
     | toute autre adresse | serveur compatible OpenAI (`custom`), au chemin écrit |
     | un hôte de vendeur sous un chemin `/anthropic`, ou une adresse qui n'est pas une URL `http(s)` absolue | refusée au démarrage, avec le chemin du réglage et la correction |

     Un serveur n'est Ollama que sur le port `11434`, où qu'il tourne. Une
     adresse `custom` sans chemin (`http://hote:8000`) est lue comme un serveur compatible OpenAI
     qui répond à sa racine, et signalée une fois par section (`LlmEndpointNotice.Silent`, un
     avertissement sur stderr et dans le journal) : ajoutez le chemin de ses routes, souvent `/v1`.
  2. **Préfixe du modèle** (seulement sans adresse) — `gpt`, `claude`, `llama` / `codellama` (Ollama), `mistral` /
     `ministral` (le `mistral` nu et `mistral:<tag>` vont à Ollama), `qwen`, `deepseek`,
     `moonshot`, `glm`, `gemini`, `grok`, `minimax`, `openrouter/`. Mammouth n'est jamais
     déduit d'un nom de modèle : ses identifiants sont ceux des vendeurs.
  3. **Préfixe de la clé API** — `hf_` → HuggingFace, `xai-` → Grok.
  4. Sinon **OpenAI**.

Une section qui ne nomme aucun modèle saute la règle 2. **Le modèle d'un appel** est ensuite
résolu par le provider, de la même façon pour les seize (`HttpLlmProviderBase.ResolveModel`) :
celui de l'appel quand sa configuration en nomme un, sinon celui avec lequel le provider est
configuré — celui de son profil —, sinon son `DefaultModel` (son entrée de
`LlmProviderDefaultModels` ; Azure n'en a pas en propre et retombe sur celui d'OpenAI comme nom
de déploiement : nommez le vôtre). Une configuration qui ne nomme aucun modèle
(`LlmConfig.OnProfile()`, un `Model` vide) n'atteint jamais le fil vide, et ne porte jamais le
modèle d'un autre vendeur.

**La configuration d'un appel** suit la même règle, champ par champ (`LlmConfig.InheritFrom`,
appliquée par `HttpLlmProviderBase.EffectiveConfig` dans les seize providers) : une configuration
passée avec un appel **complète** celle avec laquelle le provider a été construit, elle ne la
remplace pas. Chaque champ que l'appel laisse vide — nul, une chaîne vide ou blanche, aucune
séquence d'arrêt — est celui du provider : la clé, `BaseUrl`, `TimeoutSeconds` (nullable : vide
partout, 30 s), l'`ApiVersion` d'Azure, le `WorkspaceId` d'Anthropic, `Thinking`, `MaxTokens`,
`Temperature` et `TopP` (nullables eux aussi depuis GAP-36), `Seed`, `SystemMessage`,
`ResponseFormat`, `Cache`, `Tools`, la grammaire et le modèle ; les paramètres personnalisés
fusionnent, ceux de l'appel l'emportant. Chaque champ que l'appel fixe l'emporte. Les réglages qui
ne peuvent pas être vides — les pénalités (0, le défaut de chaque vendeur) et le mode d'outil —
sont ceux de l'appelant, et `MaxRetries` et `Grammar` se lisent dans la configuration du provider
quand il est construit. Un appel ne peut pas effacer ce que fixe son provider ; il le remplace
(`Thinking = { Enabled = false }`, `LlmResponseFormat.Text()`). L'adaptateur de client de chat
enregistré sans configuration de base part de celle du provider (`ILlmProvider.BaseConfig`).
Avant, les providers prenaient la configuration d'un appel en entier (`config ?? Config`) : le
planificateur, la mémoire cognitive, les résumés de fenêtre de contexte et de RaggableTree et les
boucles d'agent hors du client de chat perdaient la clé — « API key is required » —, le point
d'accès et le délai du provider qu'ils atteignaient (GAP-29).

**Échantillonnage : ce qui est fixé part, ce qui ne l'est pas ne part pas** (GAP-36).
`Temperature` et `TopP` sont nuls quand rien ne les fixe — ni l'appel, ni l'agent, ni la tâche, ni
le profil — et rien n'atteint alors le fil : le modèle applique son propre défaut (souvent 1 ;
celui du Modelfile chez Ollama). Fixés, ils partent quelle que soit leur valeur. Le moteur prenait
0,7 et 1,0 pour « non fixé » : un agent qui demandait exactement ces valeurs tournait sur celles de
son profil, et le 0,7 qu'il posait sur chaque appel que personne ne configurait est refusé par les
modèles par défaut d'OpenAI (`gpt-5.6-sol`) et d'Anthropic (`claude-sonnet-5`). Une exception
mesurée : le dialecte de Mistral écrit `top_p: 1` quand rien n'en fixe (`AlwaysEmitTopP`). Les
pénalités, la graine et les séquences d'arrêt de la configuration d'un agent passent elles aussi
par le client de chat ; sur le fil, Ollama écrit `top_p`, `seed` et `stop` dans ses `options`, et
un dialecte qui n'a pas de champ pour une pénalité ou une graine le dit par l'avertissement
structuré décrit plus bas, jamais en silence. Une température, un `top_p` ou une pénalité part telle
qu'écrite, à six chiffres significatifs au plus (le `float` de `ChatOptions` n'en tient pas plus) :
`0.7`, jamais `0.699999988079071`, sous toute culture courante.

Chaque provider construit par la fabrique est enveloppé dans `CallModeLlmProvider`, puis, par
`LlmProviderEntrance`, dans `MeteredLlmProvider` (toujours ; il rend compte à l'`ILlmUsageSink` de
l'hôte quand celui-ci en a enregistré un), `TracedLlmProvider` et, quand l'hôte a enregistré un
`ILlmRateLimiter`, `RateLimitedLlmProvider` (voir [Décorateurs et enregistrement](#décorateurs-et-enregistrement)), et
rendu derrière un `LlmProviderAdapter`.

## Capacités déclarées

Chaque provider déclare un value object `LlmProviderCapabilities` (Domain, exposé sur
`ILlmProvider` ; un provider surcharge `HttpLlmProviderBase.DeclaredCapabilities`, qui vaut
`LlmProviderCapabilities.Unknown` par défaut) : `ResponseFormat`
(`None`/`JsonObject`/`JsonSchema`), `Thinking` (`None`/`EffortOnly`/`Toggle`/`Budget`), `EffortLevels`
(les niveaux d'effort que le champ connaît, `low`, `medium`, `high` par défaut, les cinq avec `xhigh` et `max`
pour Anthropic et OpenRouter, `max` sans `xhigh` pour DeepSeek et Z.ai : un niveau demandé qui n'est pas déclaré est
envoyé comme le plus haut niveau déclaré en dessous, avec un avertissement dans le journal du provider), `Vision`,
`ExplicitPromptCaching`, `RequiresJsonKeywordInPrompt`, `ReplaysReasoningContent`, `RunsOwnTools` (déclarée par le
seul pont Microsoft Agent Framework : c'est un agent qui exécute ses propres outils).
Une capacité n'est pas au vendeur de la déclarer : `GbnfGrammar`, qu'aucune API de vendeur ne
documente, est allumée par la configuration avec laquelle le provider est construit
(`Llm:Grammar`, `LlmConfig.GrammarEnabled`) pour un serveur compatible llama.cpp derrière les
providers compatibles OpenAI ou Ollama — `ILlmProvider.Capabilities` est la déclaration plus cet
interrupteur.
Et une n'est à aucun vendeur : `ReplaysPrompt`, déclarée par le seul fournisseur écho
(`UndefinedLlmProvider`, le modèle d'un hôte sans section `Llm`), dit que ce qu'il rend est son
prompt, jamais la réponse d'un modèle — le planificateur de la crew saute alors son appel avec un
avertissement au lieu de relire le prompt comme un plan (GAP-31). Les décorateurs qui enveloppent un
provider (`MeteredLlmProvider`, `RateLimitedLlmProvider`) transmettent les capacités.
`OpenAICompatibleProviderBase` traduit la déclaration en dialecte OpenAI une seule fois
(payloads vision, `response_format`, thinking, les diagnostics `CapabilityMismatchHint`) ;
Anthropic et Ollama écrivent leur propre dialecte, et Qwen surcharge le hook pour les champs
de réflexion de DashScope. Une option qu'un provider ne peut pas honorer produit un
avertissement structuré (event id `110`, `Option '…' was declared but … does not support it —
it was not sent`) — jamais un abandon silencieux ; depuis GAP-36, cela vaut pour
`frequency_penalty` et `presence_penalty` dans chaque dialecte, et pour `seed` partout où le
dialecte ne l'écrit pas (tous sauf Ollama). `CapabilityMismatchHint` (providers compatibles OpenAI et
Ollama) couvre le versant par modèle : quand un vendeur refuse une capacité que le provider déclare (un modèle texte
seul qui reçoit une image, un modèle sans réflexion ou sans outils), l'erreur dit quelle
hypothèse était fausse. Le cas miroir — une contrainte que seul le **serveur** peut énoncer — a son
propre point d'extension : `OpenAICompatibleProviderBase.TryAdaptRejectedPayload` donne au
provider une chance d'adapter une charge utile refusée en 4xx et de la ré-émettre une seule
fois (chemins generate et chat ; le streaming ne réessaie jamais). Kimi s'en sert pour le
`invalid temperature: only 1 is allowed for this model` de Moonshot — la valeur imposée est
lue dans le refus lui-même (quels modèles l'exigent est décidé côté serveur, une liste en
dur dériverait) et la substitution est journalisée en avertissement structuré ; seule une
température fixée et envoyée est remplacée, puisque celle que rien ne fixe ne part pas (GAP-36). Deux
coutures de dialecte supplémentaires servent les agrégateurs (LLM-09) : `ReasoningFieldName`
nomme le champ vendeur où la trace de raisonnement est lue (`reasoning_content` par défaut,
`reasoning` chez OpenRouter — la clé de métadonnée Orkeon reste `reasoning_content`), et
`usage.cost` devient la métadonnée `cost` partout où un vendeur facture dans la réponse —
bufferisée ou en flux — avec `cost_currency` à côté quand le provider énonce la devise dans
laquelle son vendeur facture (`CostCurrency` : `USD` chez OpenRouter, dont les crédits sont
des dollars ; aucun autre provider n'en énonce). De là, le coût voyage tel que facturé :
l'adaptateur de client de chat le porte sur le `ChatResponse` (`AdditionalProperties`, aussi
sur son chemin de streaming), la boucle d'agent le rapporte sur l'événement d'usage
(`CostUsageEvent.Cost`, null quand le vendeur n'a rien facturé — le `0` d'un modèle gratuit
reste `0`), la façade de scripting fait de même pour `ctx.llm.*`, et le `cost.updated` du run
le relaie avec `costSource: "vendor"` ([le bus d'événements du run](run-event-bus.md)).
`CostBudgetManager` ne calcule depuis son registre que le coût d'un appel que personne n'a
chiffré, pour ses propres budgets ; cette estimation n'atteint jamais le fil. Un
chunk portant un `error` racine après le HTTP 200 termine un flux comme un refus pré-flux —
métadonnée `error` sur le flux chat, `HttpRequestException` sur le flux texte — jamais
comme une complétion propre. Les 16 providers sont des `IStreamingLlmProvider`. Le chemin de
streaming de l'adaptateur de client de chat (`GetStreamingResponseAsync`, GAP-32) se construit
comme son chemin bufferisé — mêmes messages, mêmes rôles, mêmes outils, mêmes options — et lit
le flux chat du provider (`ChatStreamingAsync`) : le texte au fil, puis une dernière mise à jour
qui porte les appels d'outils (natifs ou du protocole texte), l'usage du provider, la raison de
fin, le modèle, le coût du vendeur et le raisonnement à rejouer. Repliées, les mises à jour
donnent la réponse de l'appel bufferisé : un tour d'agent diffusé appelle ses outils et est
compté exactement, jamais estimé ; un provider qui ne diffuse pas y répond en bufferisé. Un appel d'outil est
rejoué au provider avec toutes les propriétés qu'il portait, bufferisé ou diffusé, chaque appel
avec les siennes : l'adaptateur garde l'élément `tool_calls[i]` du provider sur son
`FunctionCallContent` et le rejoue entier, et la fusion des fragments diffusés garde ce qu'un
fragment ajoute au-delà de `index`, `id`, `type` et `function` — la signature de pensée de
Gemini (`extra_content.google.thought_signature`), que Gemini exige en retour, voyage ainsi
(LLM-14) ; un appel qui ne portait rien d'autre est rejoué en `id`, `type` et `function`, comme
avant. Les
flux eux-mêmes ont dû dire ce que disent les réponses bufferisées : Anthropic assemble désormais
les blocs `tool_use` qu'il diffuse ; MiniMax sépare son bloc `<think>` de tête des deltas de
contenu (`LeadingReasoningTag`), si bien que le texte d'un flux est la réponse que garde sa
réponse finale ; et, sur le protocole natif, le dialecte OpenAI et Anthropic donnent à une
réponse diffusée le corps que porte leur réponse bufferisée, où le repli du protocole texte lit
un appel que le modèle a écrit en texte. La matrice par provider vit dans [le comparatif des
providers](../reference/llm-providers-comparison.md).

### Points d'extension du dialecte de la base compatible OpenAI

Ce qui diffère entre les vendeurs compatibles s'exprime par une poignée de membres protégés,
pas par des copies du constructeur de payload :

| Point d'extension | Par défaut | Surchargé par |
|---|---|---|
| `ApplyProviderSpecificOptions` | écrit `thinking` / `reasoning_effort` et `response_format` d'après les capacités déclarées | Qwen (`enable_thinking`, `thinking_budget`), OpenRouter (l'objet de requête `reasoning`), Together AI (ajoute `context_length_exceeded_behavior: truncate`) |
| `MaxTokensFieldName` | `max_tokens` | OpenAI (`max_completion_tokens`) |
| `AlwaysEmitTopP` | `top_p` écrit seulement s'il est fixé | Mistral (`top_p: 1` quand rien n'en fixe) |
| `SplitReasoningFromContent` / `EnrichAssistantMessage` | rien n'est extrait ; `reasoning_content` rejoué quand `ReplaysReasoningContent` est déclaré | MiniMax (bloc `<think>` en ligne extrait, réinséré au rejeu) |
| `ReasoningFieldName` | `reasoning_content` | OpenRouter (`reasoning`) |
| `CostCurrency` | aucune | OpenRouter (`USD`) |
| `TryAdaptRejectedPayload` | ré-émet une fois sans le plafond de sortie quand celui du catalogue a été refusé | Kimi (la température imposée, puis la règle de la base) |
| `BuildEndpoint` | `{baseUrl}/chat/completions` | Azure OpenAI (URL datée par déploiement, ou la surface v1 avec `api_version: v1`) |

## Jetons du cache de prompt et TTL

`LlmResponse` porte trois compteurs de cache : `CacheHitTokens` (lus dans le cache), `CacheWriteTokens`
(écrits dans le cache — le `cache_creation_input_tokens` d'Anthropic) et `CacheMissTokens` (entrée non servie
par le cache). `PromptTokens` inclut déjà les jetons écrits et lus : une écriture n'est jamais comptée deux
fois. `MeteredLlmProvider` les enregistre une fois par appel dans le `CostUsageEvent` ; `CostBudgetManager`
conserve par modèle les jetons de prompt, de complétion, d'écriture et de lecture du cache et le coût
(`CostReport.ModelUsageByModel`), et `session_stats` les renvoie dans `by_model_tokens[]` à côté de la table
de coûts `by_model`, inchangée. Un modèle dont le `ModelPricing` déclare `CacheWritePricePerMillion` /
`CacheReadPricePerMillion` est tarifé `(prompt - écriture - lecture) x tarif prompt + écriture x tarif
d'écriture + lecture x tarif de lecture + complétion x tarif de complétion` ; sans ces tarifs, le coût est
inchangé.

Le TTL du cache est `LlmCacheConfig.Ttl` (`5m` ou `1h`). Un appel peut le porter comme option
`LlmChatOptionsKeys.CacheTtl` (`orkeon:cache_ttl`), que `ChatOptionsComposer` remplit depuis le
`llm.cache.ttl` de l'agent ; l'adaptateur de client de chat le replie dans le `Cache.Ttl` de l'appel et
Anthropic l'émet dans `cache_control`. Les fournisseurs compatibles OpenAI ne peuvent pas l'honorer et
journalisent l'avertissement structuré d'option non honorée (événement 110, `cache_ttl`) au lieu de
l'abandonner en silence.

## Décorateurs et enregistrement

- **`MeteredLlmProvider`** — l'unique endroit où l'usage LLM est mesuré : il rapporte chaque
  appel du provider qu'il enveloppe à l'`ILlmUsageSink` de l'hôte, avec le coût du vendeur
  quand la réponse en porte un, et sinon une estimation signalée comme telle.
  `LlmProviderFactory` enveloppe chaque provider qu'elle construit ; un provider que la
  fabrique ne construit pas (le provider écho d'un hôte sans section `Llm`, un agent Microsoft
  Agent Framework, un double de test) s'enregistre avec
  `services.AddOrkeonLlmProvider(sp => …, baseConfig)`, qui l'expose en `ILlmProvider`,
  `IBasicLlmProvider` et `IChatClient` sur une seule instance mesurée.
  `AddOrkeonInfrastructure()` n'enregistre aucun modèle à lui : `orkeon run`, `orkeon-host` et
  `orkeon-repl` enregistrent le leur depuis la section `Llm` — ou le provider écho quand elle
  manque —, et un conteneur qui n'en enregistre aucun échoue à sa première résolution LLM, en
  nommant le service manquant. Son ancien repli, un provider OpenAI sans clé, était le client de
  chat du REPL (GAP-29).
- **`RateLimitedLlmProvider`** — le `RateLimiting` de l'hôte à l'entrée de chaque provider :
  chaque appel prend un bail de l'`ILlmRateLimiter` (global, par provider, concurrence) le temps
  de l'appel, un flux pour toute son énumération. Il est posé avec le compteur, autour de lui,
  aux points qui comptent un provider — la fabrique, `AddOrkeonLlmProvider` et
  `AddOrkeonLlmProfile`, le `ForProvider` du registre des profils, le résolveur du manager, le
  planificateur d'une crew C# (`LlmProviderEntrance`) —, si bien que chaque appel au modèle de
  l'hôte est limité une fois, les `ctx.llm.*` des scripts compris. Un provider déjà limité est
  laissé tel quel ; un appel fait à l'intérieur d'un autre appel limité ne prend pas de bail (sous
  `MaxConcurrentRequests: 1`, il s'attendrait lui-même) ; un provider qui fait ses outils lui-même
  (`RunsOwnTools`, le pont Microsoft Agent Framework) n'est pas limité lui-même — ce qu'il appelle du
  modèle d'Orkeon l'est. Un refus est retenté cinq fois sur son `RetryAfter`, puis l'appel échoue
  avec la raison du limiteur (GAP-38).
- **Mode d'appel** — `CallModeLlmProvider` décide comment chaque appel d'un provider construit
  depuis un réglage atteint le modèle, pour tout appelant (boucle d'agent, manager, planificateur,
  juges, RAG). La clé `CallMode` du réglage (`Llm:CallMode`, `Llm:Profiles:<nom>:CallMode`) vaut
  `Auto` (absente ou vide), `Buffer` ou `Stream`, casse ignorée ; une autre valeur fait échouer le
  chargement. `Buffer` répond d'un bloc ; `Stream` et `Auto` streament sur le fil quand le provider
  streame nativement et replient les événements dans la même `LlmResponse` (texte, appels d'outils,
  raison de fin, usage), si bien que le compteur et le limiteur voient un seul appel. En `Auto`, un
  refus net du streaming avant tout événement (statut 404, 405, 415 ou 501, ou 400/422 dont le
  corps nomme le stream, ou un provider qui ne déclare pas de streaming) se replie sur un appel en
  buffer pour le reste de l'exécution (`LlmCallModeScope`, ouvert par exécution d'équipe), avec un
  seul avertissement ; un 401, 403, 429, un délai expiré ou un flux coupé après son premier
  événement reste un échec. En `Stream`, un refus fait échouer l'appel en nommant `Auto` et
  `Buffer`. Il se place sous le compteur et le limiteur, appliqué par `LlmProviderFactory`
  (STUDIO-71). Voir aussi : Studio propose le mode dans ses réglages et montre le mode de l'appel en cours dans la barre d'état — [Le compteur de jetons en direct](studio.md#le-compteur-de-jetons-en-direct-studio-71).
- **Journalisation des échanges LLM** — `LlmLoggingDelegatingHandler`
  (`Orkeon.Infrastructure.Logging`) capture chaque échange HTTP (en-têtes et payload, assainis
  par `LogSanitizer` : en-têtes d'authentification masqués par nom, secrets des corps par
  motif) en JSON Lines, plus un résumé en log structuré.
  `services.AddLlmExchangeLogging(logDirectory, options)` l'injecte dans tous les clients
  `IHttpClientFactory` ; la surcharge `IHttpClientBuilder` cible un seul client nommé.
  `LlmLoggingOptions` : `MaxBodyLengthChars` (0 = pas de troncature), `LogStreamingExchanges`
  (true ; un flux passe à l'appelant au fil de son arrivée et est journalisé une fois terminé,
  sous sa réponse reconstruite, ou le texte brut reçu s'il a été coupé), `FullEmbeddingLog` (true ; false réduit
  les tableaux d'embeddings à un aperçu). Les runners l'activent avec `--llm-log` /
  `--llm-log-path` et lisent les options dans la section de settings `LlmLogging` ; les
  fichiers vont dans le montage interne `/llm-logs`, jamais visible des agents. C'est un
  outil de débogage : la capture contient les prompts complets.

## Valider un fournisseur contre son API réelle

Tous les tests unitaires de ce domaine parlent à un handler HTTP mocké : ils prouvent
qu'Orkeon envoie ce qu'on croit, pas que le fournisseur l'accepte. La seconde preuve se
construit avec le kit de campagne [`llmproviders-test/`](https://github.com/Orkeon/orkeon/tree/main/llmproviders-test) :

```bash
llmproviders-test/run-campaign.sh --provider ollama --model llama3.2   # premier run, coût nul
llmproviders-test/run-campaign.sh --all --config providers.local.json --dry-run
```

Il pilote `orkeon llm probe` sur les modes M1–M10, M12 et M13 du
protocole de test des fournisseurs (matrice interne des mainteneurs) et
archive un rapport Markdown par campagne.
`orkeon llm models -p <fournisseur> --filter 'gpt-5.6-*'` liste ce qu'un fournisseur sert
réellement : aucune campagne ne dépend d'une liste de modèles maintenue à la main.

---

> **Voir aussi** : [Comparatif des providers](../reference/llm-providers-comparison.md) · [Format de réponse LLM](../guides/llm-response-format.md) · [Contenu multi-modal](../guides/multimodal.md) · [Modèles locaux](../guides/local-models.md) · [Système de mémoire](./memory-system.md) · [Sécurité](./security.md) · [Retour à l'index](../INDEX.md)
