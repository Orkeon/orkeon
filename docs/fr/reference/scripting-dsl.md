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
dont le fichier se termine. `RunCommand.DeclaresCrewHandoffAsync` renifle la source à la
recherche d'une affectation à `globalThis.crew`, et route selon ce qu'elle trouve.

| | **Procédurale** | **Déclarative** |
|---|---|---|
| le fichier finit par | `await crew.run()` | `globalThis.crew = crew` |
| moteur | `ScriptHost.RunFromFileAsync` → `JsCrew.RunAsync` | runner partagé → `JsCrewConfigurationAdapter` → `ICrewOrchestrationService` |
| exécute les `.body()` d'agent | oui, un par agent, dans l'ordre de déclaration | **non — ignoré** |
| honore `withTask` / `process` / `manager` | **non — ignoré** | oui |
| `orkeon run --validate` | échoue (*did not assign globalThis.crew*) | fonctionne |

Ce sont des opposés, pas des variantes. `JsCrew.RunAsync` itère `_agents` et ne regarde jamais
les tâches ; `Process` n'atteint qu'un tag de télémétrie. L'adaptateur, symétriquement,
contient **zéro occurrence** de `body` et de `Budget`.

**La règle :** écrivez `.body()` et vous êtes en procédural ; écrivez `withTask` et vous êtes
en déclaratif. Jamais les deux dans un fichier. Une crew avec des tâches qui se termine par
`await crew.run()` exécute ses agents et ignore chaque tâche écrite — en journalisant un
avertissement qui le dit, seule raison pour laquelle l'erreur est désormais peu coûteuse à
trouver.

## Ce que lit chaque forme

Mesuré sur `JsCrewConfigurationAdapter.cs` et `JsCrew.cs`, pas déduit.

### `agentBuilder()`

| Méthode | Procédurale | Déclarative |
|---|---|---|
| `name` `role` `goal` `backstory` | ✅ | ✅ (`role` retombe sur `name`) |
| `llm` | ✅ | ✅ |
| `tools([...])` intégrés par nom | ✅ | ✅ |
| `withAutonomousTool(s)` instances | ✅ | ✅ |
| `maxIterations` `verbose` `allowDelegation` | ✅ | ✅ |
| `body` | ✅ **tout l'intérêt** | ❌ jamais invoqué |
| `withState` → `ctx.state` | ✅ | ❌ |
| `onError` | ✅ | ❌ |
| `onAgentStart` / `onAgentStop` | ✅ | ❌ |
| `concurrency` | seulement `1` — voir plus bas | ❌ |
| `onCommand` | ni l'un ni l'autre : c'est la couture de dispatch CLI, pas un hook d'exécution | |

### `crewBuilder()`

| Méthode | Procédurale | Déclarative |
|---|---|---|
| `name` `goal` `verbose` | ✅ | ✅ |
| `withAgent(s)` | ✅ | ✅ |
| `withTask(s)` | ❌ ignoré | ✅ **tout l'intérêt** |
| `process` | ❌ tag de télémétrie seulement | ✅ (`"graph"` choisit la stratégie de reprise-et-routage du domaine, pas une topologie dessinée par le script — voir plus bas) |
| `manager` | ❌ | ✅ |
| `memory` | ❌ | ✅ |
| `budget` | ✅ | ❌ ignoré |
| `onCrewStart` / `onCrewComplete` / `onCrewError` | ✅ | ❌ |

Tout ce qui porte un ❌ du côté déclaratif est désormais **annoncé** : l'exécution journalise
un avertissement par déclaration abandonnée, en nommant la méthode et l'agent sur lequel elle
était écrite. C'est toujours abandonné — les deux formes sont deux moteurs — mais une crew ne
peut plus exécuter un `.body()` jamais invoqué sans rien en dire.

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

Déclaratif uniquement — le moteur procédural ne lit jamais les tâches. `name`, `description`,
`agent`, `expectedOutput`, `withContext(s)` (c'est ce qui construit le DAG), `expect`,
`tools`, `withTaskTool`, `humanInput`, `asyncExecution`, `deliverable`.

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
| `inputs` | runner → script | Ce que `--inputs` / `--inputs-file` a analysé. Forme procédurale uniquement. |
| `result` | script → runner | Ce que le run rapporte. Par défaut la valeur de la dernière expression. Sérialisé en JSON : affectez une projection simple — un `CrewResult` porte des objets hôtes et ne survit pas au voyage. |

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

`ctx.llm` — `generate`, `chat`, `stream`, `embed`, `act`. `act` est la boucle LLM ⇄ appels
d'outils : elle déroule le catalogue d'outils de l'agent jusqu'à ce que le modèle cesse de
demander ou que `maxIterations` soit atteint. `ActOptions.system` sème un vrai message
`role:"system"` qui persiste à chaque itération ; sans lui, `act()` envoie un seul message
utilisateur.

`ctx.memory`, `ctx.events`, `ctx.lock`, `ctx.spawn`, `ctx.delegate`, `ctx.send`/`receive`/
`broadcast`, `ctx.log`. Voir `context.d.ts`.

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

`send()` rend l'état où la machine a abouti — c'est-à-dire l'état *courant*, inchangé, quand
l'événement lui est inconnu ou qu'un garde a refusé. Un événement inconnu n'est pas une
erreur. Il n'y a pas de méthode `run()` ni d'option `circuitBreaker`.

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

`TState` ne peut pas être inféré depuis les corps de nœuds — un nœud prend et rend l'état,
l'inférence est donc circulaire — et retombe sur `Record<string, unknown>`. Annotez l'appel
(`stateGraph<OrderState>({...})`) pour qu'un nœud rendant la mauvaise forme soit signalé.

## Écarts connus entre les typings et le runtime

Les déclarations et le runtime C# sont écrits dans deux langages et rien ne les reliait
jusqu'à ce que
[`scripts/check-scripting-typings.sh`](https://github.com/Orkeon/orkeon/blob/main/scripts/check-scripting-typings.sh) le fasse.
Voici ce qui reste une fois ce garde-fou au vert.

| Écart | Comportement |
|---|---|
| `budget()` en forme déclarative | Ignoré, et dit : l'exécution journalise un avertissement qui nomme la méthode. Utilisez `process("autonomous")` en procédural. |
| `.body()` en forme déclarative | Ignoré, et dit : l'exécution journalise un avertissement qui nomme l'agent. La confusion la plus coûteuse du DSL, et la raison du tableau des formes ci-dessus. |
| `globalThis.inputs` en forme déclarative | Jamais planté. Passer `--inputs`, `--inputs-file` ou `--memory-limit-mb` à un script déclaratif affiche désormais un avertissement sur stderr au lieu de perdre l'option en silence. |
| Un objet `{ provider, model }` simple passé à `llm()` | Jeté. `ExtractLlmConfig` rend `null` pour tout ce qui n'est pas un `JsLlmConfig` : construisez-en un avec `llm.openai({...})`, `llm.default()`, etc. |
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

Les typings sont livrés sous `orkeon.d.ts` à côté de la sortie de build de la CLI, et dans le
paquet `Orkeon` à `content/typings/orkeon.d.ts`. Pointez-y votre éditeur et copiez
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
