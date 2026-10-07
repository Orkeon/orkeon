> 🇬🇧 [English version](../../guides/llm-response-format.md)

# Format de réponse LLM

> Forcer la sortie JSON à la frontière du provider plutôt que de rafistoler les prompts. Un
> seul value object, `LlmResponseFormat`, est traduit par chaque provider selon ce que son API
> déclare pouvoir honorer.

## Pourquoi

La plupart des API LLM savent contraindre la forme d'une réponse au niveau de la couche API.
Demandez `response_format: {"type": "json_object"}` à DeepSeek et l'API **garantit** du JSON
valide — pas d'accolades `{`/`}` cassées, pas de prose en préambule ; demandez un JSON Schema à
OpenAI, Gemini ou Anthropic et le vendeur valide la réponse contre ce schéma côté serveur.
C'est une seconde barrière qui complète la grammaire GBNF (serveurs compatibles llama.cpp, derrière
`Llm:Grammar`) et le `StructuredOutputResolver` en aval.

Pour Orkeon, cela compte surtout pour :

- les livrables `structured_output` qui doivent parser proprement
- les agents managers hiérarchiques qui retournent des décisions JSON
- les workflows scriptés en TypeScript qui attendent des objets typés

## Les trois formats

| `Type` | Construit avec | Sens |
|---|---|---|
| `text` | `LlmResponseFormat.Text()` | Le défaut du provider. Ne produit *rien sur le fil* — écrire `text` ou ne rien écrire est équivalent. |
| `json_object` | `LlmResponseFormat.JsonObject()` | Du JSON bien formé, sans schéma. |
| `json_schema` | `LlmResponseFormat.JsonSchema(name, schema, strict = true)` | Du JSON validé contre un JSON Schema (`LlmJsonSchema` : le schéma voyage en chaîne JSON ; `strict` demande au vendeur de rejeter tout écart, ignoré là où aucun mode strict n'existe). |

`Type` est une chaîne ouverte : toute autre valeur est transmise telle quelle (le mapper YAML
logue un avertissement, event id `101`), si bien qu'une nouvelle valeur vendeur fonctionne sans
nouvelle version du framework.

## Cascade — trois couches, cinq surfaces

La fusion est faite **une seule fois** dans `LlmConfigResolver.Resolve(baseConfig, taskOverride, callOverride)`.
Priorité du plus fort au plus faible : override d'appel → override de task → config de base.
Chaque champ est résolu indépendamment (`ResponseFormat`, `Temperature`, `MaxTokens`, `TopP`,
`Thinking`, `Cache`), et un champ `null` sur un override n'efface jamais une valeur héritée.

| Surface | Atterrit dans | Où elle s'écrit |
|---|---|---|
| 1. Défaut crew | config de base | `llm:` du YAML de crew — fusionné champ par champ dans le `llm:` de chaque agent |
| 2. Agent | config de base | `llm:` du YAML d'agent, `agentBuilder().withResponseFormat(...)` / `.withResponseSchema(...)`, la `LlmConfig` de l'agent en C# |
| 3. Task | override de task (`LlmConfigOverride`) | `llm_override:` du YAML de task, `taskBuilder().withResponseFormat(...)` / `.withResponseSchema(...)`, `CrewTaskBuilder.WithResponseFormat(...)` / `.WithLlmOverride(...)` |
| 4. Au niveau script | config de base | `llmConfig.with({ responseFormat })` sur la config passée à `agentBuilder().llm(...)` |
| 5. À l'appel | override d'appel | `ctx.llm.complete/chat/stream(..., { responseFormat })` dans un script, `LlmProviderExtensions.GenerateAsync/ChatAsync(..., LlmConfigOverride, baseConfig)` en C# |

## Surfaces YAML

### Défaut crew

```yaml
llm:
  model: deepseek-flash
  response_format: json_object   # every agent of this crew now replies in JSON
```

### Override agent (gagne sur la crew)

```yaml
agents:
  extractor:
    role: "Invoice extractor"
    llm:
      model: deepseek-flash
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

Le bloc `llm_override:` accepte `response_format`, `response_schema`, `temperature`,
`max_tokens`, `top_p` et `thinking` — les champs du `llm:` de niveau agent, moins `model` et
`cache:`.

### JSON Schema

`response_schema:` accompagne `response_format: json_schema`, au niveau agent, crew ou task.
`schema` est le JSON Schema **sous forme de chaîne JSON** (un bloc scalaire le garde lisible) ;
`name` vaut `response` par défaut, `strict` vaut `true` :

```yaml
llm:
  model: gpt-5.6-sol
  response_format: json_schema
  response_schema:
    name: invoice
    strict: true
    schema: |
      {"type": "object",
       "properties": {"number": {"type": "string"}, "total": {"type": "number"}},
       "required": ["number", "total"], "additionalProperties": false}
```

Un `response_schema:` seul implique `json_schema`. `json_schema` **sans** schéma se replie sur
`json_object` avec un avertissement (event id `102`).

## Surfaces TypeScript / `.ork.ts`

```typescript
// Builder — agent (the host's configured provider, here DeepSeek)
const extractor = agentBuilder()
    .name("extractor")
    .role("Invoice extractor")
    .llm(llm.default_.with({ model: "deepseek-flash" }))
    .withResponseFormat("json_object")
    .build();

// Builder — task (overrides agent on this task only)
const task = taskBuilder()
    .description("Return invoice fields as JSON")
    .expectedOutput("JSON")
    .agent(extractor)
    .withResponseSchema("invoice", {
        type: "object",
        properties: { number: { type: "string" }, total: { type: "number" } },
        required: ["number", "total"],
    })
    .build();

// Call-time override (most surgical — wins over everything else)
const res = await ctx.llm.complete(
    "Return the answer as a json object",
    { responseFormat: "json_object" }
);
```

`.llm(...)` prend un `LlmConfig` — `llm.default_`, `llm.model("…")`, `llm.profile("…")` ou
`.with({...})` sur l'un d'eux — et refuse une chaîne ou un objet littéral. Le fournisseur est
celui de l'hôte, ou celui du profil de l'hôte que nomme `llm.profile(...)` ; `.llm(...)` règle le
modèle. Un format de réponse est vérifié contre le fournisseur sur lequel l'agent tourne vraiment. `withResponseSchema(name, schema, strict?)` prend un objet
littéral ou une chaîne JSON et implique `json_schema`. `ctx.llm.extract(prompt, schema)` demande
`json_object` par défaut, sauf si l'appel passe `{ responseFormat: "text" }`.

## Surface fluent C#

```csharp
var task = new CrewTaskBuilder()
    .Description("Extract as JSON")
    .ExpectedOutput("JSON")
    .WithResponseFormat(LlmResponseFormat.JsonSchema("invoice", invoiceSchemaJson))
    .Build();

// Call-time override via extension method
await provider.GenerateAsync(
    prompt,
    LlmConfigOverride.ForResponseFormat(LlmResponseFormat.JsonObject()),
    agent.LlmConfig,
    ct);
```

`WithResponseFormat` accepte aussi une chaîne (`"json_object"`) ; `WithLlmOverride(LlmConfigOverride)`
pose le patch de task complet.

## Sur le fil

- **Famille compatible OpenAI** (tous les providers sauf Anthropic et Ollama) —
  `OpenAICompatibleProviderBase` écrit `response_format` une seule fois, pour chaque provider
  qui déclare la capacité : `{"type": "json_object"}`, ou
  `{"type": "json_schema", "json_schema": {"name", "strict", "schema"}}`.
- **Anthropic** — `output_config.format`, schéma uniquement : Anthropic n'a pas d'équivalent
  de `json_object`, une demande de JSON sans schéma est donc signalée (avertissement structuré)
  plutôt qu'envoyée.
- **Ollama** — `format` : l'objet schéma lui-même, ou `"json"` pour `json_object`.

Ce qu'un provider ne peut pas honorer n'est jamais abandonné en silence :

- un provider qui déclare `None` n'envoie rien et logue `Option 'response_format' was declared
  but … does not support it` (event id `110`) ;
- un schéma envoyé à un provider `JsonObject` est **rétrogradé** en `json_object` avec le même
  avertissement (`response_format.schema`) — décrivez la forme dans le prompt.

## Le garde-fou du mot-clé "json"

Le mode JSON de DeepSeek exige que le prompt (message system **ou** user) contienne le mot
`"json"` quelque part — faute de quoi l'API peut émettre un **flux d'espaces blancs sans fin**
jusqu'à épuisement de `max_tokens`. Chaque provider qui déclare `RequiresJsonKeywordInPrompt`
(DeepSeek aujourd'hui) logue un `Warning` structuré (event id `100`, `LogMissingJsonKeyword`)
quand il détecte la situation :

```
DeepSeek was asked for a JSON response format but no system/user message contains the
word 'json'. This API may then emit an unbounded whitespace stream until max_tokens.
Add 'json' to the prompt to be safe.
```

Nous ne mutons **pas** le prompt à votre place — l'appelant garde le contrôle. Ajoutez `"Reply as a json object."` au message system et l'avertissement disparaît.

## Matrice de support des providers

Le support est **piloté par capacité** (`LlmProviderCapabilities.ResponseFormat` — voir le
[comparatif des providers](../reference/llm-providers-comparison.md)). La déclaration est par
provider alors que la réalité est par modèle : un modèle qui refuse le champ répond avec
l'erreur du vendeur, qui remonte en `LlmResponse.Error` et fait échouer la tâche avec cette
raison.

| Provider | Capacité déclarée | Notes |
|---|---|---|
| OpenAI, Azure OpenAI, Grok, Gemini, Mistral, Together AI | `JsonSchema` | Validation de schéma côté serveur. |
| **Anthropic** | `JsonSchema` | Dialecte propre (`output_config`) — schéma uniquement, pas de `json_object` nu. |
| **Ollama** | `JsonSchema` | Dialecte propre (`format`). |
| **DeepSeek**, Kimi, Qwen, HuggingFace, Z.AI | `JsonObject` | JSON bien formé garanti ; un schéma est rétrogradé avec un avertissement. DeepSeek exige en plus le mot-clé `json` (ci-dessus). |
| **MiniMax** | `None` | Accepté mais non contraignant — mesuré le 2026-08-30 (schéma ignoré, `json_object` clôturé en markdown) ; un format déclaré produit l'avertissement structuré de capacité. |
| **OpenRouter** | `JsonSchema` | Documenté par endpoint ; M8 vert sur les trois modèles campagnés le 2026-10-07. Le provider n'envoie pas `provider.require_parameters`, un schéma peut donc encore être ignoré par un endpoint qui ne le supporte pas — un M8 vert ne l'exclut pas. |
| **Mammouth AI** | `None` | Accepté mais non contraignant — mesuré le 2026-10-07 (`json_object` rend du JSON nu, un schéma strict n'a pas contraint `gemini-3.7-flash`) ; un format déclaré produit l'avertissement structuré de capacité. |

## Livrables `structured_output`

Une tâche dont le livrable est `source: structured_output` porte son schéma (`schema_inline` ou
`schema_path`) sous deux formes, et l'adaptateur du client de chat envoie celle que le fournisseur
honore — jamais les deux, ce que `llama-server` refuse :

- une **grammaire GBNF**, sur un point d'accès que les réglages déclarent capable de la prendre
  (`"Llm": { "Grammar": true }` — Docker Model Runner, `llama-server`) ;
- sinon un **format de réponse `json_schema`** (nom `structured_output`, `strict: false`, car le
  mode strict d'OpenAI refuse un schéma qui laisse un objet ouvert ou une propriété optionnelle),
  sur un fournisseur qui déclare `JsonSchema` dans la matrice ci-dessus ;
- sinon rien ne contraint : la grammaire atteint le fournisseur, qui l'abandonne avec un
  avertissement structuré nommant `Llm:Grammar`.

Un format de réponse que la crew pose elle-même (`llm:` / `llm_override:`) l'emporte toujours sur
le schéma du livrable. Dans tous les cas, `StructuredOutputResolver` vérifie encore la réponse
au parsing JSON avant d'écrire le fichier.

## Comment ça circule dans l'orchestrateur

La cascade est fusionnée exactement une fois par tour (`LlmConfigResolver.Resolve`), sur 3 sites d'appel — dans les boucles d'agent et le coordinateur de validation (`LegacyTextAgentLoop`, `NativeToolCallingAgentLoop`, `OutputValidationCoordinator`), que pilote `ExecutionOrchestrator` :

1. Boucle legacy `[TOOL_CALL]` basée texte — `_llmProvider.ChatAsync(prompt, effectiveConfig, …)`
2. Boucle de tool-calling natif — `_fullProvider.ChatAsync(messages, effectiveConfig, …)`. `BuildNativeLlmConfig` s'amorce depuis `agent.LlmConfig`, si bien que le nom du modèle et la config Thinking survivent à l'entrée du chemin natif ; un agent sans config reçoit une configuration qui ne nomme aucun modèle (`LlmConfig.OnProfile()`), et le fournisseur exécute l'appel sur le sien.
3. Retry de correction de validation — `_llmProvider.ChatAsync(correctionPrompt, effectiveConfig, …)`

Les 6 stratégies de process (Sequential, Hierarchical, Autonomous, Graph, Parallel, Consensual) exécutent les tâches via `IAgentExecutionService.ExecuteTaskAsync`, qui délègue à `ExecutionOrchestrator.ExecuteTaskCoreAsync` — elles bénéficient de la cascade gratuitement.

`LlmBasedManager` (le manager LLM en mode Hierarchical) n'applique pas d'override de task : ses appels tournent sur le LLM que la crew donne à son manager — le fournisseur posé par `WithManagerLlm`, sinon le profil et le modèle de l'agent manager, sinon le profil par défaut — avec la configuration propre de ce fournisseur pour tout le reste (ni format de réponse, ni override de task).

## Référence

- Value objects : `Orkeon.Domain.SharedKernel.ValueObjects.LlmResponseFormat`, `LlmJsonSchema`
- Record de patch : `Orkeon.Domain.SharedKernel.ValueObjects.LlmConfigOverride`
- Fusion : `Orkeon.Domain.SharedKernel.ValueObjects.LlmConfigResolver.Resolve`
- Traduction wire : `Orkeon.Infrastructure.LLMs.Base.OpenAICompatibleProviderBase.ApplyProviderSpecificOptions` (virtuelle — Qwen, OpenRouter et Together AI la surchargent et appellent la base pour `response_format`) ; `AnthropicLlmProvider` et `OllamaLlmProvider` pour leurs dialectes propres
- Mapping YAML : `Orkeon.Infrastructure.Configuration.Yaml.YamlCrewMapper.MapResponseFormat`
- Extensions call-time : `Orkeon.Infrastructure.LLMs.Extensions.LlmProviderExtensions`
- Plan / spec : l'archive des mainteneurs (plan LLM-RESPONSE-FORMAT)
- Doc API DeepSeek : https://api-docs.deepseek.com/api/create-chat-completion
