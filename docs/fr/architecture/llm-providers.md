> 🇬🇧 [English version](../../architecture/llm-providers.md)

# Fournisseurs LLM

`HttpLlmProviderBase` (`Orkeon.Infrastructure.LLMs.Base`) fournit la base abstraite pour tous les fournisseurs LLM. Elle intègre la gestion HTTP (`IHttpClientFactory`), les politiques de résilience Polly (retry, circuit breaker, timeout), et la sérialisation JSON.

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
dur dériverait) et la substitution est journalisée en avertissement structuré. Les 14
providers sont des `IStreamingLlmProvider`, et `RateLimitedLlmProvider`
décore n'importe lequel d'entre eux. La matrice par provider vit dans
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
