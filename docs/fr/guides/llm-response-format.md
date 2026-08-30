> 🇬🇧 [English version](../../guides/llm-response-format.md)

# Format de réponse LLM

> Forcer la sortie JSON à la frontière du provider plutôt que de rafistoler les prompts. Premier provider câblé : **DeepSeek**.

## Pourquoi

Les API compatibles OpenAI exposent un champ `response_format` que le modèle honore au niveau de la couche API. Positionnez `response_format: {"type": "json_object"}` et DeepSeek **garantit** que la réponse est du JSON valide — pas d'accolades `{`/`}` cassées, pas de prose en préambule. C'est une seconde barrière qui complète la grammaire GBNF existante (llama.cpp / Ollama) et le `StructuredOutputResolver` en aval.

Pour Orkeon, cela compte surtout pour :

- les livrables `structured_output` qui doivent parser proprement
- les agents managers hiérarchiques qui retournent des décisions JSON
- les workflows scriptés en TypeScript qui attendent des objets typés

## Cascade — 5 niveaux d'override

```
1. LlmConfig.ResponseFormat       (global default / crew YAML)
2. agent.LlmConfig.ResponseFormat (agent YAML)
3. task.LlmOverride.ResponseFormat (task YAML — NEW)
4. script-time override            (TS via Jint — NEW)
5. call-time override              (method parameter — NEW)
```

La fusion est faite **une seule fois** dans `LlmConfigResolver.Resolve(baseConfig, taskOverride, callOverride)`. Priorité du plus fort au plus faible : callOverride → taskOverride → baseConfig. Un champ `null` sur un override n'efface jamais une valeur héritée.

## Surfaces YAML

### Défaut crew

```yaml
llm:
  model: deepseek-v4-flash
  response_format: json_object   # every agent of this crew now replies in JSON
```

### Override agent (gagne sur la crew)

```yaml
agents:
  extractor:
    role: "Invoice extractor"
    llm:
      model: deepseek-v4-flash
      response_format: json_object   # only this agent forces JSON
```

### Override task (gagne sur l'agent — le chirurgical)

```yaml
tasks:
  extract_invoice:
    description: "Extract fields. Reply as a json object."
    expected_output: "JSON object with all invoice fields"
    llm_override:
      response_format: json_object
      temperature: 0.0
```

Le bloc `llm_override:` accepte les mêmes champs que le `llm:` au niveau agent, moins le modèle (qu'il n'a pas de sens d'échanger par task dans l'orchestrateur actuel).

## Surfaces TypeScript / `.ork.ts`

```typescript
// Builder — agent
const extractor = agentBuilder()
    .name("extractor")
    .role("Invoice extractor")
    .llm({ provider: "deepseek", model: "deepseek-v4-flash" })
    .withResponseFormat("json_object")
    .build();

// Builder — task (overrides agent on this task only)
const task = taskBuilder()
    .description("Return invoice fields as JSON")
    .expectedOutput("JSON")
    .agent(extractor)
    .withResponseFormat("json_object")
    .build();

// Call-time override (most surgical — wins over everything else)
const res = await llm.complete(
    "Return the answer as a json object",
    { responseFormat: "json_object" }
);
```

## Surface fluent C#

```csharp
var task = new CrewTaskBuilder()
    .Description("Extract as JSON")
    .ExpectedOutput("JSON")
    .WithResponseFormat(LlmResponseFormat.JsonObject())
    .Build();

// Call-time override via extension method
await provider.GenerateAsync(
    prompt,
    LlmConfigOverride.ForResponseFormat(LlmResponseFormat.JsonObject()),
    agent.LlmConfig,
    ct);
```

## Le garde-fou du mot-clé "json"

Le `response_format: json_object` de DeepSeek exige que le prompt (message system **ou** user) contienne le mot `"json"` quelque part — faute de quoi l'API peut émettre un **flux infini d'espaces blancs** jusqu'à épuisement de `max_tokens`. La base partagée (`OpenAICompatibleProviderBase`, donc chaque provider compatible OpenAI qui déclare `RequiresJsonKeywordInPrompt`) logue un `Warning` structuré (event id `100`, `LogMissingJsonKeyword`) si elle détecte la situation — en substance :

```
DeepSeek response_format=json_object is set but no system/user message
contains the word 'json'. The API may emit an infinite whitespace stream
until max_tokens. Add 'json' to the prompt to be safe.
```

Nous ne mutons **pas** le prompt à votre place — l'appelant garde le contrôle. Ajoutez `"Reply as a json object."` au message system et l'avertissement disparaît.

## Matrice de support des providers

Le support est **piloté par capacité** (`LlmProviderCapabilities.ResponseFormat`,
traduit une fois par `OpenAICompatibleProviderBase` — voir le
[comparatif des providers](../reference/llm-providers-comparison.md)) :

| Provider | Capacité déclarée | Notes |
|---|---|---|
| OpenAI, Azure OpenAI, Grok, Gemini, Mistral, TogetherAI | `JsonSchema` | Validation de schéma côté serveur. |
| **Anthropic** | `JsonSchema` | Dialecte propre (`output_config`) — schema-only, pas de `json_object` nu. |
| **Ollama** | `JsonSchema` | Dialecte propre (`format`). |
| **DeepSeek** (`deepseek-v4-flash`, `deepseek-v4-pro`), Kimi, Qwen, HuggingFace, Z.AI | `JsonObject` | JSON bien formé garanti ; un schéma est rétrogradé avec un avertissement. |
| `deepseek-reasoner` (R1) | ⚠️ | Peut refuser `response_format` avec un HTTP 400. Tester avant production. L'erreur remonte comme une `APIError` typée via le pipeline existant — pas de crash. |
| **Gemini** | `None` | Un format de réponse déclaré produit l'avertissement structuré de capacité. |

## Comment ça circule dans l'orchestrateur

La cascade est fusionnée exactement une fois par tour (`LlmConfigResolver.Resolve`), sur 3 sites d'appel — dans les boucles d'agent et le coordinateur de validation (`LegacyTextAgentLoop`, `NativeToolCallingAgentLoop`, `OutputValidationCoordinator`), que pilote `ExecutionOrchestrator` :

1. Boucle legacy `[TOOL_CALL]` basée texte — `_llmProvider.ChatAsync(prompt, effectiveConfig, …)`
2. Boucle de tool-calling natif — `_fullProvider.ChatAsync(messages, effectiveConfig, …)`. Effet de bord de ce chantier : `BuildNativeLlmConfig` s'amorce désormais depuis `agent.LlmConfig` au lieu de `LlmConfig.Default()` — le nom du modèle et la config Thinking ne sont plus perdus à l'entrée du chemin natif.
3. Retry de correction de validation — `_llmProvider.ChatAsync(correctionPrompt, effectiveConfig, …)`

Les 6 stratégies de process (Sequential, Hierarchical, Autonomous, Graph, Parallel, Consensual) délèguent à `ExecutionOrchestrator.ExecuteTask` — elles bénéficient de la cascade gratuitement.

`LlmBasedManager` (le manager LLM en mode Hierarchical) n'est **pas** patché car il n'a pas de `task` dans son scope (selon le plan §2.9 : les sites sans task passent `taskOverride: null`).

## Référence

- Value object : `Orkeon.Domain.SharedKernel.ValueObjects.LlmResponseFormat`
- Record de patch : `Orkeon.Domain.SharedKernel.ValueObjects.LlmConfigOverride`
- Fusion : `Orkeon.Domain.SharedKernel.ValueObjects.LlmConfigResolver.Resolve`
- Traduction wire : `Orkeon.Infrastructure.LLMs.Base.OpenAICompatibleProviderBase.ApplyProviderSpecificOptions` (virtuelle — Qwen porte la seule surcharge ; DeepSeek opte déclarativement via `ResponseFormat = JsonObject`)
- Mapping YAML : `Orkeon.Infrastructure.Configuration.Yaml.YamlCrewMapper.MapResponseFormat`
- Extensions call-time : `Orkeon.Infrastructure.LLMs.Extensions.LlmProviderExtensions`
- Plan / spec : l'archive des mainteneurs (plan LLM-RESPONSE-FORMAT)
- Doc API DeepSeek : https://api-docs.deepseek.com/api/create-chat-completion
