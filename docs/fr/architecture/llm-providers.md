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

Des adaptateurs génériques (`ChatClientToLlmProviderAdapter`, `LlmProviderToChatClientAdapter`, `ChatClientToBasicLlmProviderAdapter`) sont disponibles dans `Orkeon.Infrastructure.LLMs.Adapters` pour intégrer d'autres fournisseurs compatibles avec l'interface `IChatClient`.

`LlmProviderFactory` (`Orkeon.Infrastructure.LLMs`) résout automatiquement le fournisseur à partir de la `LlmConfig` (détection par URL, nom de modèle, ou clé API).

## Capacités déclarées

Chaque provider déclare un value object `LlmProviderCapabilities` (Domain, exposé sur
`ILlmProvider` ; la base vaut `LlmProviderCapabilities.Unknown` par défaut) : `ResponseFormat`
(`None`/`JsonObject`/`JsonSchema`), `Thinking` (`None`/`EffortOnly`/`Toggle`/`Budget`),
`Vision`, `ExplicitPromptCaching`, `RequiresJsonKeywordInPrompt`, `ReplaysReasoningContent`.
`OpenAICompatibleProviderBase` traduit la déclaration en dialecte OpenAI une seule fois
(payloads vision, `response_format`, thinking, les diagnostics `CapabilityMismatchHint`) ;
Anthropic, Ollama et Qwen surchargent le hook pour leur propre dialecte. Une option qu'un
provider ne peut pas honorer produit un avertissement structuré — jamais un abandon
silencieux. Le cas miroir — une contrainte que seul le **serveur** peut énoncer — a son
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
comme une complétion propre. Les 16 providers sont des `IStreamingLlmProvider`, et
`RateLimitedLlmProvider` décore n'importe lequel d'entre eux. La matrice par provider vit dans
[le comparatif des providers](../reference/llm-providers-comparison.md).

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
archive un rapport Markdown par campagne. `orkeon llm models -p <fournisseur> --filter
'gpt-5.6-*'` liste ce qu'un fournisseur sert réellement : aucune campagne ne dépend d'une
liste de modèles maintenue à la main.

---

> **Voir aussi** : [Système de mémoire](./memory-system.md) · [Sécurité](../architecture/security.md) · [Retour à l'index](../INDEX.md)
