> 🇬🇧 [English version](../../architecture/llm-providers.md)

# Fournisseurs LLM

`HttpLlmProviderBase` (`Orkeon.Infrastructure.LLMs.Base`) fournit la base abstraite pour tous les fournisseurs LLM. Elle intègre la gestion HTTP (`IHttpClientFactory`), les politiques de résilience Polly (retry, circuit breaker, timeout), et la sérialisation JSON.

**Budget de retry.** `Llm:MaxRetries` (10 par défaut) couvre les échecs qui reviennent en quelques secondes — erreurs de requête, 5xx, 429. Un appel bufferisé qui atteint `Llm:TimeoutSeconds` est réessayé **une fois** (`ResilienceDefaults.LlmTimeoutRetries` : chaque tentative coûte le délai entier), puis le provider répond par un échec qui nomme le réglage et les deux issues (un délai plus long, la réflexion coupée) ; une annulation de l'appelant n'est jamais réessayée. Un appel échoué voyage en `LlmResponse.Error` et, à travers l'adaptateur de chat client, en exception — il n'est jamais confondu avec une réponse vide, si bien que la boucle agent fait échouer la tâche immédiatement avec la raison du provider au lieu de relancer sans outils (LLM-11).

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
  `google`, `grok` / `xai`, `minimax`, `zai` / `glm` / `zhipu`, `openrouter`, `mammouth`.
  Une clé inconnue lève `NotSupportedException`. C'est le chemin de
  `orkeon llm probe --provider`.
- `Create(config)` déduit le provider — c'est ce que fait un run, puisque la section `Llm` du
  fichier de settings ne porte aucune clé de provider. La première règle qui correspond
  l'emporte :
  1. **URL de base, hôtes connus** — `/engines/` ou `model-runner.docker.internal` (Docker
     Model Runner, dialecte OpenAI — testé en premier, avant la règle localhost), `azure` /
     `.cognitiveservices.`, `together.xyz`, les hôtes DashScope, `deepseek.com` (refusé quand
     le chemin est `/anthropic`), `moonshot.cn` / `moonshot.ai`, `mistral.ai`,
     `generativelanguage.googleapis.com`, `api.x.ai`, `api.minimax.io` / `api.minimaxi.com`,
     `huggingface.co` / `hf.co`, `api.z.ai` / `bigmodel.cn`, `openrouter.ai`, `mammouth.ai`.
  2. **URL de base, générique** — contient `openai` → OpenAI, `anthropic` → Anthropic,
     `localhost` ou `11434` → Ollama.
  3. **Préfixe du modèle** — `gpt`, `claude`, `llama` / `codellama` (Ollama), `mistral` /
     `ministral` (le `mistral` nu et `mistral:<tag>` vont à Ollama), `qwen`, `deepseek`,
     `moonshot`, `glm`, `gemini`, `grok`, `minimax`, `openrouter/`. Mammouth n'est jamais
     déduit d'un nom de modèle : ses identifiants sont ceux des vendeurs.
  4. **Préfixe de la clé API** — `hf_` → HuggingFace, `xai-` → Grok.
  5. Sinon **OpenAI**.

Chaque provider construit par la fabrique est enveloppé dans `MeteredLlmProvider` (voir
[Décorateurs et enregistrement](#décorateurs-et-enregistrement)) et rendu derrière un
`LlmProviderAdapter`.

## Capacités déclarées

Chaque provider déclare un value object `LlmProviderCapabilities` (Domain, exposé sur
`ILlmProvider` ; la base vaut `LlmProviderCapabilities.Unknown` par défaut) : `ResponseFormat`
(`None`/`JsonObject`/`JsonSchema`), `Thinking` (`None`/`EffortOnly`/`Toggle`/`Budget`),
`Vision`, `ExplicitPromptCaching`, `RequiresJsonKeywordInPrompt`, `ReplaysReasoningContent`.
`OpenAICompatibleProviderBase` traduit la déclaration en dialecte OpenAI une seule fois
(payloads vision, `response_format`, thinking, les diagnostics `CapabilityMismatchHint`) ;
Anthropic et Ollama écrivent leur propre dialecte, et Qwen surcharge le hook pour les champs
de réflexion de DashScope. Une option qu'un provider ne peut pas honorer produit un
avertissement structuré (event id `110`, `Option '…' was declared but … does not support it —
it was not sent`) — jamais un abandon silencieux. `CapabilityMismatchHint` (providers compatibles OpenAI et
Ollama) couvre le versant par modèle : quand un vendeur refuse une capacité que le provider déclare (un modèle texte
seul qui reçoit une image, un modèle sans réflexion ou sans outils), l'erreur dit quelle
hypothèse était fausse. Le cas miroir — une contrainte que seul le **serveur** peut énoncer — a son
propre point d'extension : `OpenAICompatibleProviderBase.TryAdaptRejectedPayload` donne au
provider une chance d'adapter une charge utile refusée en 4xx et de la ré-émettre une seule
fois (chemins generate et chat ; le streaming ne réessaie jamais). Kimi s'en sert pour le
`invalid temperature: only 1 is allowed for this model` de Moonshot — la valeur imposée est
lue dans le refus lui-même (quels modèles l'exigent est décidé côté serveur, une liste en
dur dériverait) et la substitution est journalisée en avertissement structuré. Deux
coutures de dialecte supplémentaires servent les agrégateurs (LLM-09) : `ReasoningFieldName`
nomme le champ vendeur où la trace de raisonnement est lue (`reasoning_content` par défaut,
`reasoning` chez OpenRouter — la clé de métadonnée Orkeon reste `reasoning_content`), et
`usage.cost` devient la métadonnée `cost` partout où un vendeur facture dans la réponse —
bufferisée ou en flux — avec `cost_currency` à côté quand le provider énonce la devise dans
laquelle son vendeur facture (`CostCurrency` : `USD` chez OpenRouter, dont les crédits sont
des dollars ; aucun autre provider n'en énonce). De là, le coût voyage tel que facturé :
l'adaptateur de client de chat le porte sur le `ChatResponse` (`AdditionalProperties`, aussi
sur son repli en flux), la boucle d'agent le rapporte sur l'événement d'usage
(`CostUsageEvent.Cost`, null quand le vendeur n'a rien facturé — le `0` d'un modèle gratuit
reste `0`), la façade de scripting fait de même pour `ctx.llm.*`, et le `cost.updated` du run
le relaie avec `costSource: "vendor"` ([le bus d'événements du run](run-event-bus.md)).
`CostBudgetManager` ne calcule depuis son registre que le coût d'un appel que personne n'a
chiffré, pour ses propres budgets ; cette estimation n'atteint jamais le fil. Un
chunk portant un `error` racine après le HTTP 200 termine un flux comme un refus pré-flux —
métadonnée `error` sur le flux chat, `HttpRequestException` sur le flux texte — jamais
comme une complétion propre. Les 16 providers sont des `IStreamingLlmProvider`. La matrice
par provider vit dans [le comparatif des providers](../reference/llm-providers-comparison.md).

### Points d'extension du dialecte de la base compatible OpenAI

Ce qui diffère entre les vendeurs compatibles s'exprime par une poignée de membres protégés,
pas par des copies du constructeur de payload :

| Point d'extension | Par défaut | Surchargé par |
|---|---|---|
| `ApplyProviderSpecificOptions` | écrit `thinking` / `reasoning_effort` et `response_format` d'après les capacités déclarées | Qwen (`enable_thinking`, `thinking_budget`), OpenRouter (l'objet de requête `reasoning`), Together AI (ajoute `context_length_exceeded_behavior: truncate`) |
| `MaxTokensFieldName` | `max_tokens` | OpenAI (`max_completion_tokens`) |
| `AlwaysEmitTopP` | `top_p` omis quand il vaut 1 | Mistral (toujours écrit) |
| `SplitReasoningFromContent` / `EnrichAssistantMessage` | rien n'est extrait ; `reasoning_content` rejoué quand `ReplaysReasoningContent` est déclaré | MiniMax (bloc `<think>` en ligne extrait, réinséré au rejeu) |
| `ReasoningFieldName` | `reasoning_content` | OpenRouter (`reasoning`) |
| `CostCurrency` | aucune | OpenRouter (`USD`) |
| `TryAdaptRejectedPayload` | ré-émet une fois sans le plafond de sortie quand celui du catalogue a été refusé | Kimi (la température imposée, puis la règle de la base) |
| `BuildEndpoint` | `{baseUrl}/chat/completions` | Azure OpenAI (URL datée par déploiement, ou la surface v1 avec `api_version: v1`) |

## Décorateurs et enregistrement

- **`MeteredLlmProvider`** — l'unique endroit où l'usage LLM est mesuré : il rapporte chaque
  appel du provider qu'il enveloppe à l'`ILlmUsageSink` de l'hôte, avec le coût du vendeur
  quand la réponse en porte un, et sinon une estimation signalée comme telle.
  `LlmProviderFactory` enveloppe chaque provider qu'elle construit ; un provider que la
  fabrique ne construit pas (le provider écho d'un hôte sans section `Llm`, un agent Microsoft
  Agent Framework, un double de test) s'enregistre avec
  `services.AddOrkeonLlmProvider(sp => …, baseConfig)`, qui l'expose en `ILlmProvider`,
  `IBasicLlmProvider` et `IChatClient` sur une seule instance mesurée.
- **`RateLimitedLlmProvider`** — fait passer chaque appel par l'`ILlmRateLimiter` (le bloc de
  settings `RateLimiting`). Il enveloppe le provider remis au moteur de scripting, dont les
  appels `ctx.llm.*` contournent le throttling propre de l'orchestrateur ; il n'est pas prévu
  comme décorateur global, sans quoi le chemin YAML serait limité deux fois.
- **Journalisation des échanges LLM** — `LlmLoggingDelegatingHandler`
  (`Orkeon.Infrastructure.Logging`) capture chaque échange HTTP (en-têtes et payload, assainis
  par `LogSanitizer` : en-têtes d'authentification masqués par nom, secrets des corps par
  motif) en JSON Lines, plus un résumé en log structuré.
  `services.AddLlmExchangeLogging(logDirectory, options)` l'injecte dans tous les clients
  `IHttpClientFactory` ; la surcharge `IHttpClientBuilder` cible un seul client nommé, et
  `AddLlmExchangeFileLogging(logDirectory)` garde la capture JSONL sans le résumé console.
  `LlmLoggingOptions` : `MaxBodyLengthChars` (0 = pas de troncature), `LogStreamingExchanges`
  (true ; un flux est capturé par sa seule requête), `FullEmbeddingLog` (true ; false réduit
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
