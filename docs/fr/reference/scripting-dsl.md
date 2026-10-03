> 🇬🇧 [English version](../../reference/scripting-dsl.md)

# Référence du DSL de scripting (`.ork.ts`)

Chaque builder, chaque méthode, et la colonne qui n'existe nulle part ailleurs : **laquelle
des deux formes de script l'honore réellement**.

Les signatures vivent dans `src/scripting/Orkeon.Scripting/Typings/*.d.ts`, concaténées au
build dans le `orkeon.d.ts` que lit votre éditeur. Cette page ne les recopie pas. Elle répond
à la question que les déclarations ne peuvent pas trancher : *le moteur qui va exécuter mon
fichier lit-il seulement ceci ?*

Pour la version narrative — quoi écrire, dans quel ordre, et pourquoi — voir
[Écrire une crew en TypeScript](../guides/write-a-crew-in-typescript.md).

## Les deux formes

Un fichier `.ork.ts` est confié à l'un de **deux moteurs différents**, choisi par la façon
dont le fichier se termine. `orkeon run` renifle la source (`RunCommand.DeclaresCrewHandoffAsync`)
à la recherche d'une affectation à `globalThis.crew` — `(globalThis as any).crew = …` compte
aussi — et route selon ce qu'il trouve.

| | **Procédurale** | **Déclarative** |
|---|---|---|
| le fichier finit par | `await crew.run()` | `globalThis.crew = crew` |
| moteur | `ScriptHost.RunFromFileAsync` — le propre `await crew.run()` du script | runner partagé → `JsCrewConfigurationAdapter` → `ICrewOrchestrationService` |
| exécute les `.body()` d'agent | oui, un par agent, dans l'ordre de déclaration | **non — ignoré** |
| honore `withTask` / `process` / `manager` | **non — ignoré** | oui — `manager` là où le process en a un (`hierarchical`, `consensual`), refusé ailleurs |
| `orkeon run --validate` | échoue (*did not assign globalThis.crew*) — **après avoir exécuté le script** : le charger l'évalue, `await crew.run()` compris | fonctionne — le script est évalué, la crew n'est pas exécutée |

Ce sont des opposés, pas des variantes. La boucle procédurale (en JavaScript depuis SCR-25,
`JsCrew.Run.cs`, exécutée sous la propre pompe du script) parcourt les agents et ne regarde jamais les
tâches ; `Process` n'atteint qu'un tag de télémétrie. L'adaptateur, symétriquement, n'invoque
jamais un `body` : les seuls endroits où il en nomme un sont les avertissements qu'il émet
pour les déclarations qu'il ignore.

**La règle :** écrivez `.body()` et vous êtes en procédural ; écrivez `withTask` et vous êtes
en déclaratif. Jamais les deux dans un fichier. Une crew avec des tâches qui se termine par
`await crew.run()` exécute ses agents et ignore chaque tâche écrite — en journalisant un
avertissement qui le dit, seule raison pour laquelle l'erreur est désormais peu coûteuse à
trouver.

C'est `orkeon run` qui route un script `globalThis.crew` vers le moteur déclaratif. Un hôte
qui exécute le fichier via `ScriptHost` — le service `script-host` du REPL, un hôte C# qui
appelle `RunFromFileAsync` — n'a pas d'orchestrateur : il exécute la crew exportée en
**procédural**, `.body()` compris, tâches ignorées.

## Ce que lit chaque forme

Mesuré sur `JsCrewConfigurationAdapter.cs`, `JsCrew.cs` et `JsExecutionContext.cs`, pas
déduit.

### `agentBuilder()`

| Méthode | Procédurale | Déclarative |
|---|---|---|
| `name` | ✅ | ✅ |
| `role` `goal` `backstory` | conservés, pas envoyés : `ctx.llm` envoie votre seul prompt ; `role` ne sert qu'à `crew.findByRole` | ✅ **le prompt de l'agent** — `build()` exige `role` |
| `llm` | le fournisseur seulement : un agent configuré avec `llm.profile("…")` fait parler `ctx.llm` au fournisseur de ce profil, sur le modèle du profil ; sinon `ctx.llm` utilise le fournisseur de l'hôte sur son modèle configuré (par appel : `{ llm: { model } }`) | ✅ un `LlmConfig` — `llm.default_`, `llm.model("…")`, `llm.profile("…")`, `.with({...})` — règle le fournisseur de l'agent (l'un des profils de l'hôte), son modèle, sa température et son plafond de jetons. Une chaîne ou un objet simple est refusé par `.llm(...)` |
| `tools([...])` intégrés par nom | ✅ ce que `ctx.llm.act` peut appeler — un nom que l'hôte n'offre pas fait rejeter `act` par une `UnknownToolError`, avant tout appel au modèle | ✅ strict : un nom inconnu fait échouer le run |
| `withAutonomousTool(s)` instances | appelables depuis un `body` (`tool.execute(input)`) et proposées à `ctx.llm.act`, qui exécute le `execute` de l'outil dans le script | ✅ enregistrées et résolues par nom |
| `maxIterations` `verbose` `allowDelegation` | ❌ (`act` a son propre `maxIterations`) | ✅ |
| `withResponseFormat(type)` / `withResponseSchema(name, schema, strict?)` | ❌ (par appel : `{ responseFormat }`) | ✅ |
| `body` | ✅ **tout l'intérêt** | ❌ jamais invoqué |
| `withState` → `ctx.state` | ✅ | ❌ |
| `onError` | ✅ | ❌ |
| `onAgentStart` / `onAgentStop` | se déclenchent quand l'agent **rejoint** une crew (`crewBuilder().build()`, `crew.add`, `ctx.spawn`) et la **quitte** (`crew.remove`) — pas au début et à la fin du run | de même : `onAgentStart` se déclenche à `build()`, pendant l'évaluation, bien que le run avertisse que les deux sont ignorés |
| `concurrency` | seulement `1` — voir plus bas | ❌ |
| `onCommand` | ni l'un ni l'autre : c'est la couture de dispatch CLI, pas un hook d'exécution | |

### `crewBuilder()`

| Méthode | Procédurale | Déclarative |
|---|---|---|
| `name` | ✅ | ✅ |
| `goal` `verbose` | ❌ conservés, jamais lus | ✅ |
| `withAgent(s)` — un agent construit ; tout le reste est refusé | ✅ | ✅ |
| `withTask(s)` — une tâche construite ; tout le reste est refusé | ❌ ignoré | ✅ **tout l'intérêt** |
| `process` | ❌ tag de télémétrie seulement | ✅ (`"graph"` choisit la stratégie de reprise-et-routage du domaine, pas une topologie dessinée par le script — voir plus bas) |
| `manager` | ❌ | ✅ — `process("hierarchical")` : le manager assigne et revoit sur le `.llm(...)` de cet agent (`llm.profile("claude")` le place sur ce profil) ; `process("consensual")` : l'arbitre du repli `ManagerDecision` ; les quatre autres process le refusent quand le run adapte la crew, de même qu'un agent d'une autre crew |
| `memory` | ❌ | ✅ |
| `planning` — le `planning: true` du YAML : un plan pas à pas par tâche, lu dans le prompt de la tâche (coupé par défaut ; sur le profil par défaut de l'hôte) | ❌ (avertit) | ✅ |
| `budget` | ✅ | ❌ ignoré |
| `onCrewStart` / `onCrewComplete` / `onCrewError` | ✅ | ❌ |

Tout ce qui porte un ❌ du côté déclaratif est désormais **annoncé** : l'exécution journalise
un avertissement par déclaration abandonnée, en nommant la méthode et l'agent sur lequel elle
était écrite. C'est toujours abandonné — les deux formes sont deux moteurs — mais une crew ne
peut plus exécuter un `.body()` jamais invoqué sans rien en dire.

**Ce que rend `await crew.run()`** — un `CrewResult`, exactement ce que déclare `crew.d.ts` :
`output`, le texte de la dernière sortie d'agent qui n'était pas `null` (`""` quand aucun agent
n'a rien rendu), et `tasks`, une entrée `{ name, output, durationMs }` par exécution d'agent,
dans l'ordre où ils ont tourné, `output` étant ce que le corps a rendu. Rien de plus : la table
`artifacts` que déclaraient les typings n'était jamais remplie, et `crew.run<T>()` typait un
`output` que le runtime sert toujours en chaîne — les deux ont disparu (GAP-27), et
`TypingsRuntimeParityTests` compare les deux formes de résultat type par type.

#### `process("graph")` n'est pas `stateGraph`

Deux dispositifs différents qui partagent un mot. `process("graph")` exécute la crew sur la
stratégie graphe **du domaine** : une boucle fixe `agent_execute → route_decision` avec
disjoncteur, réglée par `GraphConfig`. `stateGraph({ nodes, edges })` est la topologie que
**vous** dessinez, et elle s'exécute quand vous appelez `.run()` dessus — depuis un `.body()`
d'agent, donc en procédural.

`crewBuilder().graph(g)` brouillait les deux : la méthode acceptait un `stateGraph`, le
rangeait dans un champ qu'aucun moteur n'a jamais lu, et `process("graph")` refusait de se
construire sans elle. Le mode était verrouillé derrière une méthode qui jetait son argument.
La méthode a disparu ; `process("graph")` se construit maintenant seul.

### `taskBuilder()`

Déclaratif uniquement — le moteur procédural ne lit jamais les tâches. Transmis à la crew :
`description`, `agent` (un agent construit), `expectedOutput`, `withContext(s)` (c'est ce qui
construit le DAG), `tools` (ajoutés aux outils de l'agent pour cette tâche seulement, sans jamais
les remplacer), `humanInput`, `asyncExecution`, `deliverable`,
`withResponseFormat(type)`, `withResponseSchema(name, schema, strict?)` et `withProfile(nom)` (le
`llm_override: { profile }` du YAML : cette tâche seule tourne sur un des profils de l'hôte, sur le
modèle propre de ce profil, et `"default"` la ramène au défaut de l'hôte ; un nom que l'hôte
n'offre pas fait échouer le chargement en listant les profils connus). Acceptés mais sans
effet : `name` (une configuration de tâche n'a pas de nom ; il ne sert qu'à nommer la tâche dans
une erreur de chargement) et `expect` (consigné dans le contexte de la tâche en l'absence de
`deliverable`, jamais validé). `withTaskTool` a disparu : rien ne le lisait, et `tools` le couvre.

`asyncExecution()` suit la règle du YAML : sous `.process("sequential")` la tâche tourne pendant
les tâches qui la suivent, et une tâche qui la cite dans `withContext` l'attend ; sous
`.process("parallel")` elle n'a pas d'effet propre ; sous les quatre autres modes la crew est
refusée quand le run l'adapte, en nommant la tâche (voir
[Tâches asynchrones](../orchestration/process-types.md#tâches-asynchrones-asyncexecution)).

L'`agent` d'une tâche et les tâches de son `withContext(s)` sont ceux de la crew (`withAgent(s)`,
`withTask(s)`) : un agent ou une tâche que la crew ne tient pas est refusé quand le run adapte la
crew, le message nommant la tâche, l'appel et ce que la crew a — la tâche tournait sur un autre
agent, ou sans attendre ce qu'elle citait.

### `toolBuilder()`

Les deux formes. `name`, `description`, `withSchema`, `execute`, `access`, `build`.

Le schéma n'est **pas** lu par le système de types : `toolBuilder()` sans argument de type
donne à `execute` une entrée `unknown`, et tout accès de champ dessus est une erreur.
Déclarez la forme que le schéma promet :

```ts
const wordCount = toolBuilder<{ text: string }, { words: number }>()
    .name("word_count")
    .withSchema({ type: "object", properties: { text: { type: "string" } }, required: ["text"] })
    .execute((input) => ({ words: input.text.trim().split(/\s+/).length }))
    .build();
```

## Les globales

Trois noms que le runner échange avec le script, déclarés dans `globals.d.ts`.

| Globale | Sens | Signification |
|---|---|---|
| `crew` | script → runner | Le handoff déclaratif. L'affecter *est* ce qui sélectionne le moteur déclaratif. |
| `inputs` | runner → script | Ce que `--inputs` / `--inputs-file` a analysé — ou l'`input` de `script-host.runCrew(name, input)` dans le REPL. Forme procédurale uniquement. |
| `result` | script → runner | Ce qu'un run procédural rapporte quand la valeur de complétion du script est `undefined` — ce qui est toujours le cas dès que le fichier a un `await` de premier niveau, puisqu'il s'exécute alors enveloppé dans une fonction async. Affectez-la sous la forme `globalThis.result = …` : un `const result` de premier niveau reste local à cette enveloppe. Sérialisé en JSON : affectez une projection simple — un `CrewResult` porte des objets hôtes et ne survit pas au voyage. |

## `ctx` — le contexte d'agent

Forme procédurale uniquement ; rien sur le chemin déclaratif n'en construit un.

`ctx.state` porte l'unique mutateur légal, `with()`. Il **remplace** l'état par ce que le
callback retourne, sous le mutex d'état de l'agent — portez donc les champs que vous ne
changez pas :

```ts
await ctx.state.with(prev => ({ ...prev, count: prev.count + 1 }));
```

Affecter directement (`ctx.state.count = 1`) lève `StateMutationOutsideWithException`. L'état
est un `Proxy` JS dont le trap `set` existe pour rendre cela bruyant plutôt que perdu.

`ctx.signal` est un **`CancellationToken` .NET projeté par interop**, pas un `AbortSignal` du
DOM — il n'y a pas de DOM dans Jint. Il porte `IsCancellationRequested` et `CanBeCanceled`,
casse CLR ; `aborted`, `addEventListener` et `throwIfAborted` valent tous `undefined`.
Transmettez-le (`crew.run({ signal: ctx.signal })`) plutôt que de l'interroger.

`ctx.llm` — `complete(prompt)`, `chat(messages)`, `stream(prompt)` (itérable async ;
`usage` et `reasoningChunks` se lisent après la boucle), `extract(prompt, schema)`,
`decide(prompt, choices)`, `embed(text)`, `act(prompt, opts)`, et `interrupt()` /
`isInterrupted`. Chaque appel part vers le fournisseur configuré de l'hôte ; sans fournisseur,
un écho `<undefined-llm:…>` répond, ce qui permet aux exemples de tourner sans clé. Un appel
prend deux options, qui corrigent toutes deux la configuration du fournisseur de l'hôte pour
cet appel seulement : `responseFormat` (`"json_object"`, `"json_schema"`, `"text"`) et
`llm: { model }` (un modèle par appel). Il n'y a ni fournisseur, ni température, ni plafond de
jetons par appel, et pas de `signal` — l'appel observe déjà celui du contexte.

`act` est la boucle LLM ⇄ appels d'outils : elle propose au modèle les outils intégrés que
l'agent a choisis avec `.tools([...])` et ses instances `withAutonomousTool` — dont le
`execute` s'exécute dans le script, sur le fil du moteur — jusqu'à ce que le modèle cesse de
demander ou que `maxIterations` (défaut 10, `0` = illimité) soit atteint. Elle se résout en
`{ output, iterations }` (plus `interrupted` ou `exhausted` quand la boucle s'est arrêtée
avant). Un nom de `.tools([...])` auquel aucun outil de l'hôte ne répond (comparaison
insensible à la casse) n'est pas sauté : `act` rejette par une `UnknownToolError`
(`agentName`, `toolNames`, `availableTools`) avant tout appel au modèle — le pendant procédural
de l'échec de chargement que la forme déclarative obtient pour le même nom ; un handler
`onError` la lit comme `validation`. `ActOptions.system` sème un vrai message `role:"system"` qui persiste à chaque
itération ; sans lui, `act()` envoie un seul message utilisateur. `permissionMode`
(`default`, `acceptEdits`, `bypassPermissions`, `plan`) est vérifié auprès de la porte de
permissions de l'hôte à chaque appel d'outil, quand l'hôte en a enregistré une — déclarez
`.access("read")` sur un outil de script pour qu'il passe `plan` ; `onDelta` reçoit le texte
streamé de chaque tour de l'assistant.

`ctx.delegate(agentOuNom, input)` est `crew.runAgent` : la cible s'exécute avec son propre
contexte, sous son sémaphore et sa politique `onError`. `ctx.send(agentOuNom, message)`,
`receive({ timeout })` et `broadcast(message)` échangent des messages ; `send` et `broadcast`
sont synchrones. `ctx.crew` est une vue en lecture seule de la crew (`name`, `findByName`,
`findById`, `findByRole`, `has`, `lock(name, fn)`). `ctx.log.info(message, ...args)` joint les
arguments supplémentaires par une espace, les objets en JSON. Et aussi `ctx.memory` (`crew`, et
`agent` dans un `body` d'agent), `ctx.events`, `ctx.lock(name, fn)`, `ctx.spawn`. Voir
`context.d.ts`.

Un échec côté hôte parvient au script comme une instance de la classe que déclare
`errors.d.ts` —
`try { await q.pop({ timeout: 100 }) } catch (e) { if (e instanceof ReceiveTimeoutError) … }`
— avec les champs de cette classe (`agentName`, `timeoutMs`…), plus `clrType` (le nom du type
.NET) et `clr` (l'exception). Un échec sans classe déclarée est un simple `Error` qui porte ces
deux mêmes propriétés. Un gestionnaire `onError` reçoit `{ code, message, exception, attempt,
agent: { id, name } }` et le contexte ; `code` vaut `rate_limit`, `network`, `timeout`,
`receive_timeout`, `state_mutation`, `agent_not_in_crew`, `validation` ou `unknown`.

## Les espaces de noms

| Globale | Ce qu'elle contient |
|---|---|
| `llm` | Les réglages de modèle que prend `agentBuilder().llm(...)` (forme déclarative). `llm.default_` — une valeur, pas une fonction — est le fournisseur de l'hôte sur son modèle configuré (un `model` vide quand l'hôte n'en configure aucun : l'agent tourne alors sur le modèle par défaut de ce fournisseur, jamais sur celui d'OpenAI), ou l'écho `<undefined-llm>` quand l'hôte n'en a pas ; `llm.model(name, overrides?)` est le même fournisseur sur un autre modèle ; `llm.profile(name, overrides?)` est l'un des profils nommés de l'hôte (`Llm:Profiles:<nom>`) sur son propre modèle — un agent configuré avec lui fait ses tours et ses appels `ctx.llm` sur ce fournisseur, et un nom que l'hôte n'offre pas lève une erreur qui liste les profils connus (`"default"` est `llm.default_`). `with({ model, temperature, maxTokens, responseFormat })` rend une copie, garde le profil et refuse toute autre clé. Il n'y a pas de fabrique par vendeur : un script choisit parmi les fournisseurs que l'hôte a configurés, par nom de profil. |
| `tools` | Les outils intégrés de l'hôte, appelés depuis un `body` : `tools.fileRead({ path })`, le nom snake_case passé en camelCase. `tools.d.ts` déclare `fileRead`, `fileWrite`, `directoryRead`, `webScrape`, `httpApi`, `searchTool`, `databaseQuery`, `delegateWork`, `askQuestion` et les treize outils `email*` ; tout autre outil enregistré s'atteint de la même façon. |
| `rag` | `rag.ingest({ collection, sources, chunkingStrategy?, reindex? })`, `rag.query(question, { collection, profile?, topN? })` (une réponse ancrée avec citations) et `rag.retrieve(...)` (les mêmes passages, sans génération). Demande un hôte qui a enregistré le sous-système RAG (`AddOrkeonRag`). |
| `ErrorAction` | Les fabriques qu'un gestionnaire `onError` retourne : `fail()`, `skip()`, `fallback(value)`, `retry({ delay?, max? })`. Tout le reste vaut `fail()`. |
| `stateMachine`, `stateGraph`, `START`, `END` | Ci-dessous. |

## `stateMachine` et `stateGraph`

Les deux sont pilotés par littéral et les deux étaient **mal déclarés jusqu'au 2026-09-07** ;
si vous travaillez avec un `orkeon.d.ts` plus ancien, c'est la section à lire.

`stateMachine` prend `{ name, initial, states }`. Les transitions vivent **dans chaque état**,
indexées par nom d'événement, et le champ de destination est `target` :

```ts
const fsm = stateMachine({
    name: "order", initial: "pending",
    states: {
        pending:  { transitions: { approve: { target: "approved" } }, onEntry: (c) => {} },
        approved: { transitions: { ship: { target: "shipped" } } },
        shipped:  {},
    },
});
await fsm.send("approve");   // résout vers le NOUVEL état
```

`send(event, payload?)` rend l'état où la machine a abouti — c'est-à-dire l'état *courant*,
inchangé, quand l'événement lui est inconnu ou qu'un garde a refusé. Un événement inconnu
n'est pas une erreur. Une transition peut porter un `guard` (rendre `false` l'interdit) ; un
état peut porter `onEntry` et `onExit`, qui reçoivent `{ state, payload }`. La machine expose
`name` et `current`. Il n'y a pas de méthode `run()` ni d'option `circuitBreaker`.

`stateGraph` prend `{ name, nodes, edges, graphConfig? }`. `edges` est un **objet indexé par
nœud source**, pas une liste de paires, et `START`/`END` sont de simples chaînes
(`"__START__"`) :

```ts
const g = stateGraph<{ done: boolean }>({
    name: "research",
    nodes: { gather: (s) => ({ ...s, done: true }) },
    edges: { [START]: "gather", gather: END },
    graphConfig: { circuitBreakerPreset: "Strict" },
});
```

Une arête est un nom de nœud, `END`, ou une fonction de l'état qui en rend un (une arête
conditionnelle). `graphConfig` borne chaque `run(state)` : `circuitBreakerPreset` (`Strict`,
`Default`, `Permissive`) ou les bornes individuelles `maxTransitions`, `maxStateVisits`,
`maxRetryCycles` et `maxTotalDurationSeconds` — dépasser un compte lève une erreur, manquer de
temps annule le run. `runStream(state)` produit chaque saut (`{ fromNode, toNode, state }`)
pour un `for await`.

`TState` ne peut pas être inféré depuis les corps de nœuds — un nœud prend et rend l'état,
l'inférence est donc circulaire — et retombe sur `Record<string, unknown>`. Annotez l'appel
(`stateGraph<OrderState>({...})`) pour qu'un nœud rendant la mauvaise forme soit signalé.

## Limites de conception

Les déclarations et le runtime C# sont écrits dans deux langages. Deux garde-fous les relient :
[`scripts/check-scripting-typings.sh`](https://github.com/Orkeon/orkeon/blob/main/scripts/check-scripting-typings.sh)
vérifie le typage de chaque `.ork.ts` de `examples/` contre les typings, et un test de parité
(`TypingsRuntimeParityTests`) compare, membre par membre, chaque interface déclarée au type
du runtime qui la porte. Il ne reste aucun écart entre les deux ; ce qui suit, ce sont des
limites de conception, chacune dite plutôt que silencieuse.

| Limite | Comportement |
|---|---|
| `budget()` en forme déclarative | Ignoré, et dit : l'exécution journalise un avertissement qui nomme la méthode. Le budget ne borne que la forme procédurale (clés `toolCalls`, `tokens`, `delegationDepth`, `spawnedAgents`, `wallTime`). |
| `.body()` en forme déclarative | Ignoré, et dit : l'exécution journalise un avertissement qui nomme l'agent. La confusion la plus coûteuse du DSL, et la raison du tableau des formes ci-dessus. |
| `globalThis.inputs` en forme déclarative | Jamais planté. Passer `--inputs`, `--inputs-file` ou `--memory-limit-mb` à un script déclaratif affiche un avertissement sur stderr au lieu de perdre l'option en silence. |
| `ctx.llm.embed` | Rend un vecteur bidon. Les vrais embedders sont une suite. |
| `concurrency(n)` avec `n > 1` | Rejeté au build avec un message clair — V1 est un mutex, le sémaphore à N détenteurs est V1.5. Bruyant, pas silencieux. |
| Les locks n'ont pas de timeout | `LockTimeoutError` est V1.5. |
| Les événements | En mémoire seulement ; pas de persistance Redis/NATS. |
| Composition FSM/Graphe | Sous-états et sous-graphes en V1.5. |

### Inclusion conditionnelle

Il n'y a pas de méthode de builder pour cela. `when(predicate)` était déclarée sur les trois
builders et n'était honorée par aucun moteur — `JsCrewBuilder.when` et `JsTaskBuilder.when`
jetaient l'argument, et le prédicat rangé par `JsAgentBuilder` n'était jamais lu, si bien que
`.when(() => false)` incluait l'agent quand même. Elle a été retirée plutôt qu'implémentée :
une méthode qui fait silencieusement l'inverse de ce qu'elle annonce vaut moins que pas de
méthode du tout.

Gardez l'appel :

```ts
const b = crewBuilder().name("nightly");
if (shouldAudit) b.withAgent(auditor);
```

## Réglage de l'éditeur

Lancez `orkeon typings` dans votre projet : il écrit `orkeon.d.ts` (ce DSL) et
`orkeon-cli.d.ts` (les commandes `*.cmd.ts` du REPL) dans `./.orkeon/` — `--out <dossier>` en
choisit un autre — et les écrase, relancez-le donc après une mise à jour de l'outil. Chaque
script commence alors par `/// <reference path="./.orkeon/orkeon.d.ts" />` (chemin relatif au
script). Les fichiers viennent de l'outil lui-même : ils décrivent le runtime qui exécutera vos
scripts. Dans un clone, les sources sont `src/scripting/Orkeon.Scripting/Typings/*.d.ts`,
concaténées par le build dans `bin/<configuration>/net10.0/dist/orkeon.d.ts` sous ce projet.
Copiez ensuite
[`tools/scripting-typecheck/tsconfig.base.json`](https://github.com/Orkeon/orkeon/blob/main/tools/scripting-typecheck/tsconfig.base.json),
qui est la configuration qu'utilise le garde-fou du dépôt lui-même.

Trois de ses options portent tout le poids :

- **`moduleDetection: "force"`** — sans elle, un `await crew.run()` au niveau supérieur est
  rejeté (TS1375) et un `const crew` au niveau supérieur entre en collision avec la globale
  `crew` (TS2451). Deux erreurs qui n'ont rien à voir avec votre code.
- **`target`/`lib` `ES2022`** — ce que Jint supporte. Demander plus promet des API absentes ;
  le DOM en particulier n'existe pas, d'où le fait que `ctx.signal` n'est pas un `AbortSignal`.
- **`types: []`** — il n'y a pas de Node dans un `.ork.ts`. `process`, `require` et `Buffer`
  n'existent pas à l'exécution et ne devraient pas exister à l'écriture non plus.

## Où aller ensuite

- [Écrire une crew en TypeScript](../guides/write-a-crew-in-typescript.md) — le guide.
- [DSL de scripting — architecture](../architecture/scripting.md) — ce qu'est le runtime et où il se situe.
- [Commandes CLI en TypeScript](../architecture/cli-ts-commands.md) — le plan de contrôle `.cmd.ts`.
- [Piloter des crews depuis le REPL](../architecture/coding-agent-ts.md) — le pont `.cmd.ts` → `crew.ork.ts`.
