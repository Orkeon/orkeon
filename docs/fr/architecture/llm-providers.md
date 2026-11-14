> 🇬🇧 [English version](../../architecture/llm-providers.md)

# Fournisseurs LLM

Les fournisseurs de cette page servent la catégorie `Generation`. Les fournisseurs d'embedding, de reranking et de décision (TypeSafe, Liquid, Kev) sont listés dans [Fournisseurs de modèles](./model-providers.md) ; un modèle de décision juge un appel au LLM à travers un profil de décision ([Modèles de décision](./decision-models.md)).

`HttpLlmProviderBase` (`Orkeon.Infrastructure.LLMs.Base`) fournit la base abstraite pour tous les fournisseurs LLM. Elle intègre la gestion HTTP (`IHttpClientFactory`), les politiques de retry Polly, et la sérialisation JSON.

**Budget de retry.** `Llm:MaxRetries` (10 par défaut) couvre les échecs qui reviennent en quelques secondes — erreurs de requête, 5xx, 429 (sauf un solde épuisé, plus bas). Un appel bufferisé qui atteint `Llm:TimeoutSeconds` est réessayé **une fois** (`ResilienceDefaults.LlmTimeoutRetries` : chaque tentative coûte le délai entier), puis le provider répond par un échec qui nomme le réglage et les deux issues (un délai plus long, la réflexion coupée) ; une annulation de l'appelant n'est jamais réessayée. Un appel échoué voyage en `LlmResponse.Error` et, à travers l'adaptateur de chat client, en exception — il n'est jamais confondu avec une réponse vide, si bien que la boucle agent fait échouer la tâche immédiatement avec la raison du provider au lieu de relancer sans outils.

**Fenêtre de limite de débit.** Un 429, ou un 529 qui porte un en-tête de réinitialisation, n'est pas réessayé sur l'échelle de recul : l'appel attend la réinitialisation de la fenêtre, sur le chemin tamponné comme sur la connexion de flux (`RateLimitResetHeaders`). Il lit `Retry-After` (secondes ou date HTTP), `retry-after-ms` (millisecondes, envoyé par OpenAI et TypeSafe), `anthropic-ratelimit-requests-reset` et `anthropic-ratelimit-tokens-reset` (RFC 3339) et `x-ratelimit-reset-requests` et `x-ratelimit-reset-tokens` (durées OpenAI comme `6m0s`). `retry-after-ms`, quand il est envoyé, est le plus précis des deux en-têtes `Retry-After` et le remplace ; la plus longue de cette valeur et des en-têtes de réinitialisation l'emporte alors. Une valeur finie au-delà de la fenêtre fait échouer l'appel avec `LlmRateLimitException`, jamais avec un dépassement arithmétique. L'attente ne dépasse jamais `Llm:MaxRateLimitWait` (5 minutes par défaut, distinct du plafond de 30 secondes de l'échelle 5xx) : une réinitialisation au-delà fait échouer l'appel aussitôt avec la raison (`LlmRateLimitException`) au lieu de garer une exécution sans surveillance. Chaque attente est annoncée à `ILlmRetryObserver` avec la raison `rate-limit window` et le délai. Un 529 sans en-tête garde l'échelle.

**Solde épuisé.** Un 429 dont le corps nomme un solde ou un quota dépensé — `insufficient_quota` d'OpenAI, `exceeded_current_quota_error` de Kimi, `Insufficient balance` de Z.AI — n'est pas une limite de débit : aucune attente ne le lève. Il n'est pas réessayé, ni sur l'échelle ni sur une fenêtre, sur le chemin tamponné comme sur la connexion de flux (`ResiliencePolicies`, `LlmFailureClassifier.IsBalanceExhausted`), et la ligne d'erreur ajoute `(account balance exhausted: ...)` après les mots du fournisseur. Il reste classé `rate_limited`, si bien que la chaîne de repli d'un profil passe au fournisseur suivant, et le modèle de décision le signale comme non transitoire. Quand le corps ne peut pas être lu, le 429 est traité comme une limite de débit. Hors du champ : un solde refusé avec un autre statut (le `400` « credit balance is too low » d'Anthropic, un `402` de DeepSeek, MiniMax ou OpenRouter) n'est pas réessayé non plus, mais est classé `refused` et ne bascule pas ; le fournisseur d'embeddings du graphe de code (`Orkeon.Analysis`) a sa propre boucle de réessai et réessaie encore `insufficient_quota` (issue #328).

**Un appel streamé est borné de la même façon.** Une requête streamée part en `ResponseHeadersRead`, et `HttpClient.Timeout` cesse de compter dès que les en-têtes sont arrivés : chaque chemin de streaming (dialecte OpenAI, Azure, Anthropic, Ollama — flux chat et flux texte) court donc sous un `StreamReadBudget` : `Llm:TimeoutSeconds` borne l'appel entier, en-têtes et corps, et `Llm:StreamIdleSeconds`, optionnel, borne le silence entre deux lignes du corps (non posé par défaut). Une borne atteinte termine le flux chat par un événement `Completed` dont la réponse porte `error` (la phrase qui nomme le réglage) et `error_type` (`TaskCanceledException` pour la borne de l'appel entier, `StreamIdleTimeout` pour celle du silence) par-dessus le contenu déjà arrivé, sans `RawResponseBody` ; le flux texte lève une `HttpRequestException` portant la même phrase. Un flux que le provider ferme sans son marqueur de fin (`finish_reason` ou `[DONE]`, `message_stop`, `done: true`) est lui aussi un appel échoué quand aucun texte de réponse ni appel d'outil n'est arrivé (`error_type` `StreamTruncated`, la phrase disant combien de raisonnement a été streamé) ; avec un début de réponse, il est servi tel quel, marqué `stream_truncated` dans ses métadonnées. L'adaptateur de client de chat fait échouer un tour streamé sur tout `error`, contenu partiel ou non ; la phase des en-têtes renvoie une fois un `HttpClient.Timeout` atteint (`LlmTimeoutRetries`), comme le chemin bufferisé. Chaque provider consigne la raison d'arrêt du fournisseur, brute, sous `finish_reason` dans les métadonnées de la réponse (`length` pour le dialecte OpenAI et le `done_reason` d'Ollama, `max_tokens` pour le `stop_reason` d'Anthropic), bufferisée ou streamée ; hors le flux de chaînes seules d'Azure. Un script la lit pour distinguer un budget de sortie épuisé d'une réponse vide.

**La pensée est rejouée telle quelle (Anthropic).** Le tour assistant d'un appel d'outil garde ses blocs `thinking` et `redacted_thinking` (texte, `signature`, `data`) tels que le fournisseur les a envoyés, en réponse groupée ou en flux, dans `LlmMessage.RawThinkingBlocks` ; la requête suivante les réémet en tête, avant les blocs `text` et `tool_use` du tour, comme l'API l'exige pour la pensée étendue avec des outils. Les autres fournisseurs les ignorent. Un appel d'outil sans `id`, sans `name` ou dont les `arguments` ne sont pas un objet est refusé par une erreur qui nomme son index, son nom et le champ.

Fournisseurs implémentés :

| Fournisseur | Classe | Namespace |
|-------------|--------|-----------|
| OpenAI | `OpenAILlmProvider` (via `OpenAICompatibleProviderBase`) | `Orkeon.Infrastructure.LLMs` |
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
OpenAI n'ont pas de classe propre : `OpenAILlmProvider` les pilote via leur URL de base. Endpoints,
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
     que partagent le run et la CLI ; ni le modèle ni la clé ne sont consultés) :

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
`Temperature` et `TopP` (nullables eux aussi), `Seed`, `SystemMessage`,
`ResponseFormat`, `Cache`, `Tools`, la grammaire et le modèle ; les paramètres personnalisés
fusionnent, ceux de l'appel l'emportant. Chaque champ que l'appel fixe l'emporte. Les réglages qui
ne peuvent pas être vides — les pénalités (0, le défaut de chaque vendeur) et le mode d'outil —
sont ceux de l'appelant, et `MaxRetries` et `Grammar` se lisent dans la configuration du provider
quand il est construit. Un appel ne peut pas effacer ce que fixe son provider ; il le remplace
(`Thinking = { Enabled = false }`, `LlmResponseFormat.Text()`). L'adaptateur de client de chat
enregistré sans configuration de base part de celle du provider (`ILlmProvider.BaseConfig`).

**Échantillonnage : ce qui est fixé part, ce qui ne l'est pas ne part pas.**
`Temperature` et `TopP` sont nuls quand rien ne les fixe — ni l'appel, ni l'agent, ni la tâche, ni
le profil — et rien n'atteint alors le fil : le modèle applique son propre défaut (souvent 1 ;
celui du Modelfile chez Ollama). Fixés, ils partent quelle que soit leur valeur. Un modèle par défaut peut refuser une valeur
d'échantillonnage qu'il ne prend pas (un 0,7 imposé sur `gpt-5.6-sol` ou `claude-sonnet-5`) : c'est
pourquoi rien n'est envoyé tant que rien ne la fixe. Une exception
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
(les niveaux d'effort que le champ connaît : `low`, `medium`, `high` par défaut, jusqu'à `xhigh` pour OpenAI et
Grok, `max` sans `xhigh` pour Z.ai, et les cinq pour Anthropic, OpenRouter, DeepSeek, Kimi, Qwen, Together AI,
Hugging Face, MiniMax et Mammouth AI ; la table par modèle, plus bas, les restreint modèle par modèle) : un niveau demandé qui n'est pas déclaré est
envoyé comme le plus haut niveau déclaré en dessous, avec un avertissement dans le journal du provider ; une valeur
hors des cinq, comme le `none` d'OpenAI, n'est pas un niveau et part telle quelle, le vendeur en juge), `Vision`,
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
avertissement au lieu de relire le prompt comme un plan. Les décorateurs qui enveloppent un
provider (`MeteredLlmProvider`, `RateLimitedLlmProvider`) transmettent les capacités.
`OpenAICompatibleProviderBase` traduit la déclaration en dialecte OpenAI une seule fois
(payloads vision, `response_format`, thinking, les diagnostics `CapabilityMismatchHint`) ;
Anthropic et Ollama écrivent leur propre dialecte, et Qwen surcharge le hook pour les champs
de réflexion de DashScope. Une option qu'un provider ne peut pas honorer produit un
avertissement structuré (event id `110`, `Option '…' was declared but … does not support it —
it was not sent`) — jamais un abandon silencieux ; cela vaut pour
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
température fixée et envoyée est remplacée, puisque celle que rien ne fixe ne part pas. Deux
coutures de dialecte supplémentaires servent les agrégateurs : `ReasoningFieldName`
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
streaming de l'adaptateur de client de chat (`GetStreamingResponseAsync`) se construit
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
Gemini (`extra_content.google.thought_signature`), que Gemini exige en retour, voyage ainsi ;
un appel qui ne portait rien d'autre est rejoué en `id`, `type` et `function`. Un flux dit ce que
dit la réponse bufferisée : Anthropic assemble
les blocs `tool_use` qu'il diffuse ; MiniMax sépare son bloc `<think>` de tête des deltas de
contenu (`LeadingReasoningTag`), si bien que le texte d'un flux est la réponse que garde sa
réponse finale ; et, sur le protocole natif, le dialecte OpenAI et Anthropic donnent à une
réponse diffusée le corps que porte leur réponse bufferisée, où le repli du protocole texte lit
un appel que le modèle a écrit en texte. La matrice par provider vit dans [le comparatif des
providers](../reference/llm-providers-comparison.md).

**Deltas d'appels d'outils.** Le flux de chat informe son appelant d'un appel d'outil pendant que le modèle le construit : `LlmStreamEventKind.ToolCallDelta` porte `ToolCallDelta(Index, Id, Name, ArgumentsDelta)`, et l'événement final porte toujours l'appel entier, inchangé. Le dialecte OpenAI (les quatorze providers qui dérivent de `OpenAICompatibleProviderBase`, Azure OpenAI compris) émet un delta par fragment de `delta.tool_calls` ; Anthropic un à l'ouverture d'un bloc `tool_use` (id et nom) et un par `input_json_delta` ; Ollama un delta complet par appel, puisqu'il envoie les appels entiers. Aucun des 16 providers n'est muet : un test vérifie que les trois familles les couvrent, si bien qu'un provider ajouté plus tard doit rejoindre l'une d'elles ou dire qu'il ne le peut pas (un avertissement structuré, jamais un abandon silencieux). L'adaptateur de chat client transmet les fragments en mises à jour sans contenu, si bien que le repli du tour est celui d'avant ; `ILlmDeltaSink.OnToolCallDelta` les reçoit (vide par défaut) et ils voyagent dans l'événement `llm.delta` (`deltaKind: "tool_call"`). Les deltas de raisonnement ne sont pas transmis aux sinks. Avec un intercepteur DLP du canal de sortie externe enregistré (`AddOrkeonDlp`), les arguments de chaque appel sont retenus par index jusqu'à la fin du flux, scannés entiers et émis en un seul delta : un secret coupé entre deux fragments est rédigé ; sans intercepteur ils sortent au fil de l'eau. Une chaîne de repli s'engage sur un provider à son premier événement, delta d'appel d'outil compris : les deltas partiels d'un provider tombé avant tout événement n'atteignent jamais l'appelant, et celui qui tombe après l'avoir atteint garde l'appel, son échec terminant le flux.

### Points d'extension du dialecte de la base compatible OpenAI

Ce qui diffère entre les vendeurs compatibles s'exprime par une poignée de membres protégés,
pas par des copies du constructeur de payload :

| Point d'extension | Par défaut | Surchargé par |
|---|---|---|
| `ApplyProviderSpecificOptions` | juge les options de réflexion et écrit `response_format` d'après les capacités déclarées | Together AI (ajoute `context_length_exceeded_behavior: truncate`) |
| `WriteReasoningFields` | écrit les contrôles de réflexion jugés (interrupteur, effort, budget) ; par défaut `thinking: {type}` et `reasoning_effort` | Qwen (`enable_thinking`, `thinking_budget`), OpenRouter (l'objet de requête `reasoning`), Together AI (`reasoning: {enabled}`), Hugging Face (`chat_template_kwargs.enable_thinking`), MiniMax (`thinking: disabled` seulement) |
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
conserve par modèle les jetons de prompt, de complétion, d'écriture et de lecture du cache, de raisonnement et le coût
(`CostReport.ModelUsageByModel`), et `session_stats` les renvoie dans `by_model_tokens[]` à côté de la table
de coûts `by_model`, inchangée. Un compte de cache ou de raisonnement qu'aucun appel n'a rapporté vaut null dans
`ModelUsage`, jamais 0 (un appel qui n'a rien rapporté n'ajoute rien aux autres : null + 5 = 5), et sa clé est
absente de l'entrée de `by_model_tokens[]` ; 0 signifie que le fournisseur n'a rien mesuré. Un modèle dont le `ModelPricing` déclare `CacheWritePricePerMillion` /
`CacheReadPricePerMillion` est tarifé `(prompt - écriture - lecture) x tarif prompt + écriture x tarif
d'écriture + lecture x tarif de lecture + complétion x tarif de complétion` ; sans ces tarifs, le coût est
inchangé. Chaque entrée Anthropic intégrée les déclare (#257) : une écriture à 1,25x le tarif prompt, une lecture
à 0,1x sauf chiffre propre du fournisseur (Opus 5.5 et Sonnet 5.5 lisent à 0,20 $/M, Fable 5.1 à 0,25 $/M). Le
tarif d'écriture est celui du TTL de 5 minutes, 1,25x ; la part d'une heure d'une écriture est comptée à 2x le
tarif prompt : Anthropic la rapporte dans `usage.cache_creation.ephemeral_1h_input_tokens`, que `AnthropicLlmProvider` lit
(tamponné et en flux) dans `LlmResponse.CacheWrite1hTokens`, et que `MeteredLlmProvider` transmet au
`CostUsageEvent` sous `CacheWrite1hTokens`, sous-ensemble de `CacheWriteTokens`. `claude-opus-4-5` (et ses ids datés) est
tarifé à la grille officielle 5 / 25 $/M, et non à celle du préfixe `claude-opus-4`. Haiku 5.5
est tarifé à son tarif pour un prompt jusqu'à 100K jetons ; un prompt plus long est facturé davantage, ce que le
registre ne modélise pas.

Le TTL du cache est `LlmCacheConfig.Ttl` (`5m` ou `1h`). Un appel peut le porter comme option
`LlmChatOptionsKeys.CacheTtl` (`orkeon:cache_ttl`), que `ChatOptionsComposer` remplit depuis le
`llm.cache.ttl` de l'agent ; l'adaptateur de client de chat le replie dans le `Cache.Ttl` de l'appel et
Anthropic l'émet dans `cache_control`. Un TTL d'appel sans configuration de cache qui place un point d'arrêt
est lui-même la demande : l'adaptateur marque le prompt système avec cette durée (`LlmCacheConfig.SystemPrompt()`),
puisqu'une durée sur rien ne mettrait rien en cache. Les fournisseurs compatibles OpenAI et Ollama ne peuvent pas l'honorer et
journalisent l'avertissement structuré d'option non honorée (événement 110, `cache_ttl`) au lieu de
l'abandonner en silence.

La demande est explicite, depuis le YAML (`llm.cache: { system, tools, ttl }`) ou depuis un script :
`agentBuilder().cache({ systemPrompt, tools, ttl })` et la clé `cache` de `LlmConfig.with({...})` produisent le
même `LlmCacheConfig` (`LlmCacheConfig.Request`). Le TTL vaut `5m` ou `1h`, toute autre valeur est refusée dans les
deux, et un `ttl` de script sans `systemPrompt` ni `tools` est refusé puisqu'il ne met rien en cache. Un objet qui
ne marque rien (`cache: {}`, `cache({})`) est un Off explicite (`LlmCacheConfig.Off`) : il désactive un cache
hérité, celui de l'hôte compris, là où un cache omis en hérite.

Un hôte fixe un cache par défaut pour chaque agent avec `Llm:Cache` (`SystemPrompt`, `Tools`, `Ttl`), et un profil
avec `Llm:Profiles:<name>:Cache`. Il est validé comme le bloc YAML (`LlmCacheConfig.Request`) : un `Ttl` autre que
`5m` ou `1h`, ou sans `SystemPrompt` ni `Tools`, refuse le chargement en nommant la clé. Il ne s'applique qu'à un
fournisseur dont le cache est explicite (`LlmProviderCapabilities.ExplicitPromptCaching`, Anthropic) : ailleurs le
`BaseConfig` du fournisseur l'écarte, si bien qu'il n'atteint pas l'appel et n'avertit pas. Le cache propre d'un
agent ou d'une tâche, un Off explicite compris, l'emporte toujours. Les appels procéduraux `ctx.llm` d'un agent
scripté tournent avec le cache et l'effort que son builder a fixés (`.cache(...)`, `.effort(...)`, ou le
`LlmConfig` donné à `.llm(...)`), les options propres d'un appel l'emportant.

## Bascule de profil

Un profil qui bascule nomme ses remplaçants : `Llm:Profiles:<name>:Fallback: [b, c]` (une liste de noms de profils, ou une seule valeur
séparée par des virgules ; `default` désigne le profil de l'hôte). Un appel qui échoue sur `<name>` d'une façon qu'un nouvel essai
ne guérit pas passe à `b`, puis à `c` ; un profil enregistré en code prend la même liste avec
`services.AddOrkeonLlmProfile(name, provider, baseConfig, ["b", "c"])`.

| Échec | Classe (`reason`) | Bascule |
|---|---|---|
| connexion refusée, réinitialisée, DNS, flux fermé avant toute réponse | `transport` | oui |
| un 5xx | `server_error` | oui |
| un 429, une fois `Llm:MaxRetries` épuisé, ou une fenêtre de limite plus longue que `MaxRateLimitWait` | `rate_limited` | oui |
| le délai HTTP, un 408, un flux silencieux au-delà de `StreamIdleSeconds` | `timeout` | oui |
| tous les profils d'une chaîne imbriquée ont échoué | `exhausted` | oui |
| tout autre 4xx (400, 401, 403, 422...), un contenu bloqué (les étages de décision), un échec dont on ne sait rien | — | **non** : c'est la réponse |
| l'annulation de l'appelant, levée ou rapportée dans la réponse par le fournisseur | — | non : l'appel est annulé, rien n'est basculé ni essayé |

`LlmFailureClassifier` est l'unique classement : `ResiliencePolicies` en tire sa liste de statuts à rejouer (5xx, 408, 429), si bien
que ce qu'un nouvel essai peut guérir et ce qu'une chaîne reprend forment le même ensemble.

Construction de la chaîne (`LlmProfileRegistry`) : `FallbackLlmProvider` est le maillon le plus externe, **au-dessus** de profils déjà assemblés.
Chaque profil garde ses décorateurs (`RateLimited` → `Traced` → `Metered` → `CallMode` → fournisseur) : la métrique et la trace nomment le
fournisseur qui a répondu, un appel en échec est tracé comme l'échec qu'il est, et chaque profil garde son mode d'appel. Le client de chat de la
chaîne lit les appels d'outils par détection automatique du format (forme Anthropic ou OpenAI), pas par un analyseur propre à chaque profil. Les `Capabilities`
de la chaîne (et le choix de sortie structurée que le client de chat en tire) sont celles du **premier profil seul** ; un profil suivant qui en a moins est
signalé par l'avertissement ci-dessous, sans entrer dans celles de la chaîne.

Chaque profil utilise ses **propres réglages**. Le client de chat part de la configuration du premier profil et pose dessus les options de l'appelant ; la chaîne
donne à un profil suivant l'appel dont chaque champ qui vaut encore celui du premier profil est remplacé par la valeur de ce profil (modèle, clé, point d'accès,
`MaxTokens`, `Temperature`, `Thinking`, `CustomParameters`, `SystemMessage`, `StopSequences`, `GrammarGbnf`, délais, tous). Un champ que l'appelant a fixé sur l'appel
(un `MaxTokens` explicite, une température dans les options de chat) diffère de celui du premier profil et reste pour tous les profils. Un appelant qui fixe un champ à
exactement la valeur du premier profil ne se distingue pas du profil : la valeur du profil suivant s'applique.

- **Streaming** : un flux ne bascule **qu'avant son premier delta** (une exception, ou une complétion en échec, sans rien émis). Dès qu'un jeton est
  arrivé chez l'appelant, l'appel appartient à son profil et un échec ultérieur est celui de l'appelant. Un profil qui ne diffuse pas répond tamponné.
- **Budget** : `Llm:Profiles:<name>:FallbackBudgetSeconds` (absent : aucun) est le temps maximal que la chaîne passe, depuis son premier appel, avant de cesser
  d'essayer le profil suivant. Il est vérifié entre deux profils et ne coupe jamais un appel en cours : sans lui, une chaîne coûte au pire le
  `TimeoutSeconds` de chaque profil multiplié par son `MaxRetries`.
- **Chaîne épuisée** : un appel tamponné répond par une réponse d'erreur dont l'`ErrorType` est `FallbackExhausted` et dont le texte nomme chaque profil et son échec ;
  un appel qui a échoué par exception lève une seule `HttpRequestException` portant le même texte, la dernière exception pour cause.
- **Validation** (à la construction du registre, pas dans `CrewFactory`, qui ne voit que les profils nommés par les crews) : un nom qu'aucun profil ne définit, un profil qui se nomme
  lui-même et un cycle (`a -> b -> a`) refusent le démarrage de l'hôte. Un secours peut être un profil que `Orkeon:Host:LlmProfiles` cache aux crews.
- **Secours inférieur** : un secours dont les `LlmProviderCapabilities` sont en dessous de celles du premier profil (vision, format de réponse, réflexion, grammaire GBNF) journalise un
  avertissement nommant ce qui lui manque à la construction de la chaîne ; une option qui en a besoin est alors traitée par le fournisseur qui la reçoit, comme pour tout fournisseur.
- **Événement** : chaque bascule écrit `llm.fallback` sur le flux `--events` ([le bus d'événements de run](run-event-bus.md)) et un avertissement dans le journal. Un hôte qui enregistre un
  `ILlmFallbackObserver` en est averti aussi. Seul `orkeon run` (avec `--events`) en enregistre un : `orkeon-host`, le REPL et un hôte bibliothèque basculent
  pareillement mais n'écrivent aucun `llm.fallback`, l'avertissement du journal étant la seule trace tant que l'hôte n'enregistre pas son propre observateur.
- **Pas de persistance du choix** : une chaîne repart de son premier profil à chaque appel. Avec un primaire en panne, chaque tour d'une boucle d'outils paie le
  `TimeoutSeconds` du primaire multiplié par son `MaxRetries` avant d'atteindre le secours ; `FallbackBudgetSeconds` ne raccourcit pas cette attente (il est vérifié entre
  deux profils) : réduisez le `TimeoutSeconds` et le `MaxRetries` propres d'un profil qui a un secours.
- **Une clé révoquée bloque la chaîne** : un 401 et un 403 ne basculent jamais (c'est une erreur de configuration, pas une panne), si bien qu'un primaire dont la clé a été
  révoquée répond son erreur à chaque appel et que ses secours ne sont jamais essayés.

## Ce qu'accepte un modèle, et l'audio en entrée

`LlmProviderCapabilities` est déclaré une fois par fournisseur : il ne distingue pas deux modèles d'un même
éditeur. `gpt-4o` ne prend aucun effort de raisonnement, `o3-mini` en prend trois, et les deux passent par le
même fournisseur. Le côté par modèle est une table, `IModelCapabilityRegistry` (`ORKEXP005`,
[expérimental](../reference/experimental-apis.md)), bâtie comme `ModelPricingRegistry` : un **préfixe** d'identifiant
de modèle donne des `ModelCapabilities` (`AudioInput`, `AudioOutput`, `Vision`, `Thinking`, `ReasoningEfforts`) ; le plus long
préfixe du modèle demandé l'emporte, sans tenir compte de la casse. Un préfixe ne correspond qu'à une frontière : la
fin de l'identifiant ou un séparateur (`-`, `.`, `:`, `@`, `/`) juste après lui, donc `glm-5.3` couvre
`glm-5.3-flash` et jamais `glm-5.30`. L'identifiant `vendeur/modèle` d'un agrégateur (OpenRouter, Together AI,
Hugging Face) est cherché entier d'abord, puis par la partie qui suit son dernier `/`. La table intégrée est dans le
code et couvre les familles OpenAI (chat, audio, raisonnement), Gemini, Claude, Grok, Mistral, et, chez les autres
éditeurs, les modèles sur lesquels la campagne des fournisseurs a mesuré une restriction : `glm-5.3`,
`kimi-k2.7-code`, `MiniMax-M3.1-Flash` et `qwen3.7-plus`. Un modèle sans entrée propre (`kimi-k3`, `kimi-k3.1-pro`,
les modèles `qwen3.8`, `deepseek-flash`) prend les niveaux que déclare son fournisseur. Les modèles qui ne
raisonnent pas sont inscrits avec `Thinking: None` : `gpt-3.5`, `gpt-4`, `gpt-4-turbo`, `gpt-4o`, `gpt-4.1`, les
modèles audio (`gpt-4o-audio`, `gpt-4o-mini-audio`, `gpt-audio`), `gpt-5-chat`, `gemini-1.5` et `gemini-2.0`.

Une entrée peut aussi nommer le fournisseur qui sert le modèle, `<fournisseur>/<modèle>` (le `ILlmProvider.Name`
du fournisseur), pour une passerelle qui traduit à sa façon les valeurs d'un modèle : l'appel cherche d'abord
`<fournisseur>/<modèle>`, l'identifiant entier, puis l'entrée propre du modèle comme ci-dessus. Les entrées
intégrées viennent de la campagne M18 (2026-11-13) : `mammouth/gemini-3.7`, `mammouth/gemini-3.8`,
`openrouter/google/gemini-3.7` et `openrouter/google/gemini-3.8` raisonnent toujours et prennent `minimal` à `max`
(l'API de Google elle-même prend `none` à `high`), et `together/zai-org/glm-5.3` est sans restriction, les deux
interrupteurs compris (l'API de Z.AI elle-même refuse l'arrêt et la plupart des efforts). Un modèle que la table
ignore s'ajoute, ou une entrée intégrée se remplace, depuis la configuration, sous l'une ou l'autre forme de clé
(`Llm:ModelCapabilities:<modèle>:...` ou `Llm:ModelCapabilities:<fournisseur>/<modèle>:...`) :

```yaml
Llm:
  ModelCapabilities:
    acme-voice:                 # un préfixe : acme-voice-2 correspond
      AudioInput: true
      Thinking: EffortOnly          # None, EffortOnly (raisonne toujours), Toggle ou Budget
      ReasoningEfforts: [low, high]   # omis : aucune restriction ; écrit vide : le modèle n'en prend aucun
    together/zai-org/glm-5.3:   # ce modèle tel que Together AI le sert, lu avant glm-5.3
      Thinking: Toggle
```

Une entrée se fusionne avec la valeur intégrée du même identifiant **champ par champ** : un champ omis garde la
valeur intégrée, donc une entrée qui ne fixe que `AudioInput` garde la restriction d'effort intégrée du modèle (pour
un modèle que la table ignore, un interrupteur omis est faux et les efforts restent sans restriction). Un
`ReasoningEfforts` écrit vide dit que le modèle n'en prend aucun. `Thinking` prend un seul nom : une valeur
séparée par des virgules (`EffortOnly,Toggle`) refuse le chargement. Un registre que l'hôte enregistre lui-même
(`IModelCapabilityRegistry`, avant `AddOrkeonLlmProfiles`) l'emporte sur celui de la configuration. Un identifiant de
modèle contenant `:` (le `llama3:8b` d'Ollama) ne peut pas être une clé : la configuration .NET n'a pas
d'échappement pour le séparateur de chemin `:`. Écrivez plutôt son préfixe (`llama3` correspond à `llama3:8b` et à
tous les autres tags).

- **Audio en entrée.** Un `AudioContentPart` parvient à un modèle qui l'accepte sous la forme d'une partie
  `input_audio` de l'API Chat Completions (octets bruts, `audio/wav` ou `audio/mpeg` ; l'API ne prend pas d'adresse
  audio). La famille compatible OpenAI le fait par `ContentConverter.ToOpenAIContentParts(content, model, registry)` :
  OpenAI et Gemini (par sa couche compatible) sont couverts ; `ToAIContents` juge de même pour le chemin du client de
  conversation. Un modèle que la table ne crédite pas d'audio, ou ne connaît pas, est refusé par une
  `NotSupportedException` qui **nomme le modèle** et la clé `Llm:ModelCapabilities:<id>:AudioInput` qui le déclare :
  l'audio n'est jamais abandonné en silence. Anthropic et Ollama restent des refus (leurs API ne prennent pas
  d'audio) ; un fournisseur qui ne déclare pas la vision garde son avertissement structuré « message attachments
  (audio) » et n'envoie que le texte. Azure OpenAI se nomme par déploiement : déclarez le déploiement par une
  surcharge. La **sortie** audio est hors périmètre : la table l'enregistre, rien ne l'envoie ni ne la lit.
- **Réflexion et effort, jugés contre le modèle** (identifiants d'événement `110`, `113`). Chaque fournisseur,
  Anthropic et Ollama compris, juge `LlmConfig.Thinking` de la même façon avant de l'écrire. Le support de réflexion
  qui s'applique est le plus bas de celui du fournisseur et de celui du modèle : sur un modèle qui raisonne toujours
  (`EffortOnly`), « activé » n'envoie aucun interrupteur et « désactivé » est abandonné avec un avertissement
  structuré ; sur un modèle `Thinking: None`, rien n'est envoyé, avec un avertissement. Un effort est **abaissé** au
  plus haut niveau en dessous que prennent à la fois le champ du fournisseur et le modèle, avec un avertissement qui
  nomme le modèle et ses efforts ; un modèle dont la liste est écrite vide ne reçoit aucun effort. Une valeur du
  vendeur hors des cinq niveaux (`none`, `minimal`) part quand la liste du modèle la contient ou que le modèle est
  inconnu ; les efforts se comparent sans tenir compte de la casse. Un budget de jetons est jugé contre le seul
  support déclaré du fournisseur : un fournisseur qui déclare `Budget` l'envoie même à un modèle qui raisonne
  toujours, puisque la passerelle le convertit (OpenRouter sur Gemini 3.7, campagne M18). Le `think` d'Ollama ne
  porte qu'une valeur : « désactivé » avec un effort envoie `think: false` et avertit que l'effort n'est pas
  envoyé. Un modèle que la table ignore reçoit les options telles que demandées, avec un avertissement : rien n'a été
  vérifié, **une fois par instance de fournisseur et par modèle** (`Llm:ModelCapabilities:<id>` le fait taire) ; un
  modèle dont l'entrée omet `ReasoningEfforts` n'est pas restreint.
- **L'interrupteur d'Anthropic dépend du modèle.** « Activé » envoie `thinking: {type: adaptive, display: summarized}` :
  la trace revient sous forme de résumé. « Désactivé » envoie la valeur propre au modèle : `between_tools` sur
  Sonnet 5.5, qui l'accepte jusqu'à l'effort `high`, `disabled` jusqu'à `xhigh` sur Sonnet 5, et `disabled` jusqu'à
  `high` sur tout autre modèle Claude ; au-delà de cet effort, l'interrupteur est omis avec un avertissement, car l'API refuse la paire. Une
  réponse non diffusée porte les blocs de réflexion dans la métadonnée `reasoning_content`, comme le flux.
- **Ce qui revient.** Les jetons de raisonnement (`completion_tokens_details.reasoning_tokens`) sont lus pour chaque
  dialecte compatible OpenAI, en réponse complète comme en flux, et l'adaptateur du client de conversation reporte la
  raison de fin du fournisseur sur `ChatResponse.FinishReason` (`stop`, `length`, `tool_calls`, `content_filter`, ou
  la valeur propre du vendeur) : le `MODEL_CONTEXT_WINDOW_EXCEEDED` de Gemini se lit `length`, ses `RECITATION` et
  `PROHIBITED_CONTENT` se lisent `content_filter`.
- **Les chaînes de bascule** (`WarnAboutLesserFallbacks`) lisent la même table : une bascule dont le modèle ne prend
  pas d'audio, a un support de réflexion effectif plus bas (un modèle qui ne raisonne pas, ou qu'on ne peut pas
  désactiver) ou moins d'efforts que celui du principal est signalée une fois, à côté de la vision, du format de
  réponse et de la grammaire. Chaque maillon est lu avec le nom de son fournisseur, si bien qu'une entrée
  `<fournisseur>/<modèle>` s'applique ; une valeur du vendeur (`none`, `minimal`) n'est pas signalée manquante sur un
  modèle de bascule que la table ne liste pas, puisqu'un appel l'enverrait sans vérification.

## Providers d'embeddings

`Orkeon:Embeddings:Provider` est résolu par `DefaultEmbeddingProviderResolver`, après un provider
local ou d'analyse de code et avant le bouchon qui échoue vite. Cinq noms sont acceptés ; un autre
refuse le démarrage.

| `Provider` | Classe | Point d'accès | Modèle par défaut (dimensions) |
|---|---|---|---|
| `openai` | `OpenAIEmbeddingProvider` | tout générateur d'embeddings M.E.AI qu'inscrit l'hôte | `text-embedding-3-small` (1536) |
| `ollama` | `OllamaEmbeddingProvider` | `/api/embed` de l'hôte Ollama | le `Model` configuré |
| `mistral` | `MistralEmbeddingProvider` | `https://api.mistral.ai/v1/embeddings`, clé en `Bearer` | `mistral-embed` (1024) |
| `azure-openai` | `AzureOpenAIEmbeddingProvider` | `{Endpoint}/openai/deployments/{Model}/embeddings?api-version=`, en-tête `api-key` | le déploiement que nomme `Model` |
| `gemini` | `GeminiEmbeddingProvider` | `https://generativelanguage.googleapis.com/v1beta/openai/embeddings`, clé en `Bearer` | `gemini-embedding-001` (3072) |

Les trois derniers partagent `OpenAICompatibleEmbeddingProviderBase` : une seule forme de requête
(`model` et `input`), des réponses remises dans l'ordre de `index`, des lots coupés à `BatchSize`, et un
vecteur dont la longueur n'est pas la `Dimension` déclarée est refusé. Quand `Model` et `Dimension`
gardent les défauts globaux (`text-embedding-3-small`, 1536), `mistral` et `gemini` prennent leur propre
modèle et leurs dimensions ; `azure-openai` prend toujours `Model` comme nom du déploiement. Une réponse
4xx ou 5xx lève une `HttpRequestException` qui nomme le provider, le statut et le début du corps, jamais la
clé. Les appels d'embeddings ne sont pas rejoués : un 429 ou un 5xx parvient à l'appelant sous cette exception dès la première réponse, et `Llm:MaxRetries` ne vaut que pour le chat. `mistral-embed` (1024) et `gemini-embedding-001` (3072) ont une taille fixe et ces providers n'envoient aucune taille de sortie : une `Dimension` autre que cette taille (le défaut global compte pour non réglé) est refusée à la construction du provider, avec la taille attendue, avant tout appel. La clé est `ApiKey` ou la variable que nomme `ApiKeyEnvVar` ; sans l'une ni l'autre, le premier appel
échoue avec un message qui nomme les deux. Les identifiants de modèle des providers d'embeddings ne sont
pas dans la table des capacités de [Ce qu'accepte un modèle, et l'audio en entrée](#ce-quaccepte-un-modèle-et-laudio-en-entrée) : elle porte sur l'entrée de chat et l'effort de raisonnement.

**Changer de modèle d'embedding invalide les vecteurs stockés.** Au démarrage, un garde-fou (lancé par `RunnerHost.Build` pour `orkeon run`, les autres commandes et le démon, par le démarrage du REPL, et par le service hébergé d'un hôte qui démarre ses services) compare le
provider, le modèle et les dimensions à l'empreinte consignée dans `Orkeon:Embeddings:FingerprintPath`
(par défaut `/workspace/.orkeon/embedding-fingerprint.json`, dans le système de fichiers virtuel). Le
premier démarrage la consigne ; une empreinte différente arrête l'hôte avec une erreur qui nomme les deux
modèles. Rien n'est migré : rétablir les réglages précédents, ou ré-embedder et vider les vecteurs
stockés, puis supprimer le fichier d'empreinte. Tout hôte dont le provider d'embeddings est configuré est gardé, avec ou sans section `Orkeon:Embeddings` : un hôte qui a produit des vecteurs avec le provider par défaut puis écrit une section (passage à Mistral ou Gemini) trouve l'empreinte du défaut et est refusé. Un hôte sans provider d'embeddings (aucun enregistré, pas de section) n'écrit rien. Un hôte sans montage `/workspace` (la lecture est refusée, un avertissement est journalisé), ou avec un `FingerprintPath` vide, tourne sans garde-fou.

Cohere, Voyage et pgvector ne sont pas livrés.

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
  nommant le service manquant.
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
  avec la raison du limiteur.
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
  `Buffer`. Il se place sous le compteur et le limiteur, appliqué par `LlmProviderFactory`.
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

Il pilote `orkeon llm probe` sur les modes M1–M10, M12, M13 et M15–M18 du
protocole de test des fournisseurs (matrice interne des mainteneurs) et
archive un rapport Markdown par campagne. M15–M17 envoient la configuration de raisonnement
d'Orkeon à travers le fournisseur et consignent, par étape, les champs de raisonnement réellement
partis sur le fil. M18 fait l'inverse : avec `--raw-reasoning`, un gestionnaire réécrit les champs de
raisonnement de la requête juste avant son envoi (le fournisseur construit toujours
authentification, URL, en-têtes et dialecte) ; il contourne donc le jugement de capacités d'Orkeon
et mesure ce que le fournisseur de modèle accepte ou refuse lui-même.
Chaque étape lit aussi la réponse du fournisseur sur le fil avant que le provider ne l'analyse (trace,
jetons de raisonnement, raison de fin) : un écart entre ce que le fournisseur a renvoyé et ce que le
provider a restitué apparaît dans le rapport. Une fois tous les modes passés, chaque étape de M15–M17 est
confrontée à M18 : une valeur envoyée par Orkeon et refusée par le fournisseur (400, 422) signifie qu'il
manque une restriction à la table des capacités des modèles, une valeur qu'Orkeon a abaissée ou omise et
que le fournisseur accepte signifie que la table est trop étroite ; dans les deux cas le mode échoue. Une
valeur qui coupe le raisonnement ne compte comme acceptée que si elle l'a bien arrêté, et un fournisseur
qui raisonne encore à `effort:none` ne prouve rien en acceptant un effort.
`orkeon llm models -p <fournisseur> --filter 'gpt-5.6-*'` liste ce qu'un fournisseur sert
réellement : aucune campagne ne dépend d'une liste de modèles maintenue à la main.

---

> **Voir aussi** : [Comparatif des providers](../reference/llm-providers-comparison.md) · [Format de réponse LLM](../guides/llm-response-format.md) · [Contenu multi-modal](../guides/multimodal.md) · [Modèles locaux](../guides/local-models.md) · [Système de mémoire](./memory-system.md) · [Sécurité](./security.md) · [Retour à l'index](../INDEX.md)
