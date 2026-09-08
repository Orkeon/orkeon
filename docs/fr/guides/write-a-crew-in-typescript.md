> 🇬🇧 [English version](../../guides/write-a-crew-in-typescript.md)

# Écrire une crew en TypeScript

Les crews Orkeon sont d'ordinaire montrées en YAML. Elles peuvent aussi s'**écrire en
TypeScript**, dans un fichier nommé `quelquechose.ork.ts`, et cette surface est plus large que
celle du YAML : les agents peuvent porter du vrai code, les outils s'écrivent sur place, et un
corps peut piloter sa propre boucle LLM ⇄ outils.

Ce guide va d'un agent unique à une crew de trois agents avec un graphe de dépendances et un
fichier en sortie. Toutes les commandes ci-dessous tournent depuis un clone **sans clé d'API**
— le runtime retombe sur un fournisseur d'écho quand aucun n'est configuré.

Si vous voulez des signatures plutôt qu'un récit, allez à la
[Référence du DSL de scripting](../reference/scripting-dsl.md).

## Trente secondes

```bash
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- \
    run examples/scripting/01-hello-world.ork.ts
```

Ce fichier fait quatorze lignes :

```ts
const greeter = agentBuilder()
    .name("Greeter").role("Greeter").goal("Greet the user")
    .body((input, ctx) => "Hello from Orkeon Scripting!")
    .build();

const crew = crewBuilder().name("greeting-crew").withAgent(greeter).build();

await crew.run();
```

Trois choses y sont déjà vraies et méritent d'être nommées. `agentBuilder()`, `crewBuilder()`
et consorts sont des **globales** — il n'y a rien à importer. `role` et `goal` ne sont pas des
étiquettes, ce sont le prompt que reçoit un agent adossé à un LLM. Et la dernière ligne,
`await crew.run()`, n'est pas un détail de style : elle sélectionne le moteur.

## La chose à comprendre d'abord : il y a deux formes

Un fichier `.ork.ts` est confié à l'un de **deux moteurs différents**, et le fichier choisit
lequel par sa façon de finir. Le runner lit votre source à la recherche d'une affectation à
`globalThis.crew`.

| | **Procédurale** | **Déclarative** |
|---|---|---|
| le fichier finit par | `await crew.run()` | `globalThis.crew = crew` |
| exécute les `.body()` | oui, un par agent | **non** |
| honore `withTask`, `process`, `manager` | **non** | oui |
| `--validate` sans exécuter | échoue | fonctionne |

Ce sont des opposés, pas deux orthographes de la même chose. Le moteur procédural itère les
agents et ne regarde jamais les tâches. L'adaptateur déclaratif n'invoque jamais un `.body()`.

**La règle :** si vous écrivez `.body()`, vous êtes en procédural. Si vous écrivez `withTask`,
vous êtes en déclaratif. Jamais les deux dans un fichier.

L'échec que cela prévient est silencieux. Une crew avec trois tâches soigneusement écrites qui
se termine par `await crew.run()` va s'exécuter, afficher un résultat, et **ignorer chaque
tâche** — sans avertissement, sans erreur. C'est l'erreur la plus coûteuse de ce DSL, d'où sa
place en tête de page plutôt qu'en note de bas de page.

## Forme A — la crew déclarative

C'est la forme pour « plusieurs agents, plusieurs étapes, dont l'une dépend d'une autre ».
L'exemple travaillé est
[`examples/scripting/crew-review-desk/`](https://github.com/Orkeon/orkeon/blob/main/examples/scripting/crew-review-desk/README.md) :
trois agents relisent un diff et laissent un rapport markdown.

### Les agents

```ts
const scanner = agentBuilder()
    .name("scanner")
    .role("Change scanner")
    .goal("Establish what a change touches, factually and without judgement")
    .backstory(`Reads diffs for a living. Reports scope and refuses to speculate about
intent. Everything downstream depends on this being boring and correct.`)
    .tools(["file_read", "directory_read"])
    .withAutonomousTools(pickTools("diff_stats", "touched_files"))
    .maxIterations(6)
    .build();
```

`role`, `goal` et `backstory` **sont le prompt**. Vagues, ils produisent des agents vagues.

### Les outils, trois surfaces

Un agent atteint les outils de trois façons, et elles ne sont pas interchangeables :

1. **Intégrés par nom** — `.tools(["file_read", "directory_read"])`. Résolus depuis le
   catalogue de l'hôte, et la résolution est *stricte* : un nom non enregistré fait échouer le
   run plutôt que de laisser discrètement l'agent avec un outil de moins.
2. **Outils TypeScript en instances** — `.withAutonomousTools([...])`, construits avec
   `toolBuilder()`. Ils voyagent avec le script : aucun enregistrement côté hôte.
3. **Impérativement, depuis un corps** — `tools.fileRead({ path })`, en camelCase, forme
   procédurale uniquement.

Écrire un outil demande un schéma et un handler. Notez l'argument de type : TypeScript ne lit
pas votre schéma JSON, déclarez donc la forme qu'il promet ou `input` sera `unknown`.

```ts
const diffStats = toolBuilder<{ diff: string }, { added: number; removed: number }>()
    .name("diff_stats")
    .description("Counts added and removed lines in a unified diff")
    .withSchema({
        type: "object",
        properties: { diff: { type: "string", description: "Unified diff text" } },
        required: ["diff"],
    })
    .execute((input) => {
        const lines = input.diff.split("\n");
        const added = lines.filter((l) => l.startsWith("+") && !l.startsWith("+++")).length;
        const removed = lines.filter((l) => l.startsWith("-") && !l.startsWith("---")).length;
        return { added, removed };
    })
    .build();
```

### Les tâches, et le DAG

Les tâches portent le travail. `withContext` est ce qui construit le graphe de dépendances :
une tâche qui en déclare une autre comme contexte s'exécute *après* elle et reçoit sa sortie.

```ts
const review = taskBuilder()
    .name("review")
    .agent(reviewer)
    .description("Using the scope report, review the same diff. Judge each flag.")
    .expectedOutput("A list of findings, each with a verdict and a one-line justification")
    .withContext(scan)          // <- s'exécute après `scan`, et reçoit sa sortie
    .build();
```

`expectedOutput` n'est pas décoratif non plus — c'est le contrat montré au modèle.

### Les livrables

Une tâche peut écrire sa sortie dans un fichier :

```ts
.deliverable({ path: "/output/review.md", source: "final_message", format: "markdown" })
```

`/output` est un montage virtuel. Sans lui le run réussit quand même, et le fichier n'a
simplement nulle part où atterrir.

### Le handoff

```ts
const crew = crewBuilder()
    .name("review-desk")
    .process("sequential")
    .withAgents([scanner, reviewer, reporter])
    .withTasks([scan, review, report])
    .build();

globalThis.crew = crew;      // <- PAS `await crew.run()`
```

La définition peut alors être vérifiée sans rien exécuter — sans clé, sans modèle, en deux
secondes environ :

```bash
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- \
    run examples/scripting/crew-review-desk/main.ork.ts --validate
```

```
VALIDATION OK: …/crew-review-desk/main.ork.ts (agents=3, tasks=3, tools resolved=5)
```

Ce contrôle n'existe que sur cette forme, et c'est une bonne raison de la préférer : il
attrape un nom d'outil non enregistré ou une tâche pointant vers un agent non déclaré avant
d'avoir dépensé un token.

## Forme B — le script procédural

Ici, le `.body()` de l'agent **est** le programme. À utiliser quand la logique est la vôtre et
le LLM une sous-routine, ou quand il n'y a pas de LLM du tout.

```ts
const calculator = agentBuilder()
    .name("Calculator").role("Computer").goal("Sum a list of numbers")
    .body((input, ctx) => {
        const total = [1, 2, 3, 4, 5].reduce((a, n) => a + n, 0);
        ctx.log.info(`computed total = ${total}`);
        return total;
    })
    .build();

await crewBuilder().withAgent(calculator).build().run();
```

### `ctx.llm.act` — une boucle d'agent en neuf lignes

`act` déroule le cycle LLM ⇄ appels d'outils sur le catalogue propre à l'agent, jusqu'à ce que
le modèle cesse de demander des outils ou que `maxIterations` soit atteint :

```ts
.body(async (input, ctx) => {
    return await ctx.llm.act("Summarise the release note in /script/notes.md", {
        system: "You are terse. Two sentences, no preamble.",
        maxIterations: 5,
        onDelta: (d) => ctx.log.info(d),
    });
})
```

`system` sème un vrai message `role:"system"` qui persiste à chaque itération. Sans lui,
`act()` envoie un seul message utilisateur — identité et politique d'outils voyageant avec
l'autorité d'un utilisateur, et la gestion native du system des fournisseurs ne se déclenchant
jamais.

### L'état

Un agent peut porter un état, et il y a exactement une façon légale de le changer :

```ts
.withState(() => ({ count: 0 }))
.body(async (input, ctx) => {
    await ctx.state.with(prev => ({ ...prev, count: prev.count + 1 }));
    return ctx.state.count;
})
```

`with()` **remplace** l'état par ce que le callback retourne, sous un mutex — étalez donc les
champs que vous ne changez pas. Affecter directement (`ctx.state.count = 1`) lève une
exception : l'état est un proxy dont le trap `set` existe pour rendre cela bruyant plutôt que
perdu.

### Entrées, mémoire, erreurs

```ts
const topic = (globalThis.inputs?.topic as string) ?? "espresso";
```

```bash
… run examples/scripting/10-inputs-and-memory.ork.ts --inputs '{"topic":"orkeon"}'
```

`ctx.memory.crew` et `ctx.memory.agent` sont des stocks clé/valeur cloisonnés. `onError` rend
une action construite par une fabrique — **pas** une chaîne :

```ts
.onError((err) => ErrorAction.retry({ delay: 10, max: 3 }))
```

`ErrorAction.fail()`, `.skip()`, `.fallback(value)`, `.retry({...})`. Retourner quoi que ce
soit que le runtime ne reconnaît pas devient `fail()`.

## Réglage de l'éditeur

Les typings sont livrés sous `orkeon.d.ts` — à côté de la sortie de build de la CLI, et dans
le paquet `Orkeon` à `content/typings/orkeon.d.ts`. Pointez-y votre éditeur, et copiez
[`tools/scripting-typecheck/tsconfig.base.json`](https://github.com/Orkeon/orkeon/blob/main/tools/scripting-typecheck/tsconfig.base.json).

Trois de ses options portent tout le poids, et les omettre produit des erreurs qui n'ont rien
à voir avec votre code :

- **`moduleDetection: "force"`** — sinon un `await crew.run()` au niveau supérieur est rejeté
  (TS1375) et un `const crew` au niveau supérieur entre en collision avec la globale `crew`
  (TS2451).
- **`target`/`lib` `ES2022`** — ce que le moteur supporte.
- **`types: []`** — il n'y a pas de Node ici. `process`, `require` et `Buffer` n'existent pas.

## Ce qu'est réellement le runtime

`.ork.ts` est transpilé par **esbuild** et exécuté par **Jint**. Cela a des conséquences qu'il
vaut mieux connaître avant d'en être surpris :

- **C'est la syntaxe TypeScript, pas le compilateur TypeScript.** Les types sont retirés,
  jamais vérifiés. Rien à l'exécution ne vous dira qu'un type était faux — d'où l'importance
  du tsconfig ci-dessus.
- **Il n'y a ni Node ni DOM.** Pas de `fs`, pas de `fetch`, pas de `process`. L'accès aux
  fichiers passe par les outils ; `ctx.signal` est un jeton d'annulation .NET, pas un
  `AbortSignal`.
- **Un seul thread, pas de boucle d'événements.** Un appel hôte qui attend bloque tout le
  script.
- **`import` fonctionne entre vos propres fichiers**, résolu relativement au script.

## Dix erreurs et ce qu'elles veulent dire

| Message | Cause |
|---|---|
| `did not assign globalThis.crew` | `--validate`, ou un runner qui veut la forme déclarative, sur un fichier finissant par `await crew.run()`. |
| La crew tourne mais chaque tâche est ignorée | L'inverse : `withTask` dans un fichier finissant par `await crew.run()`. |
| `Crew configuration references unknown tool(s): x` | Un nom dans `.tools([...])` absent du catalogue de l'hôte. Le message liste tous les noms disponibles. |
| `FSM transition 'a.b' targets undeclared state 'c'` | Un `target` qui n'est pas une clé de `states`. |
| `stateGraph literal must declare an edge from START` | `edges` écrit comme un tableau. C'est un objet indexé par nœud source. |
| `stateGraph has no path from START to END` | Aucune route statique ; ajoutez-en une ou passez par une arête conditionnelle. |
| `StateMutationOutsideWithException` | `ctx.state.x = …` au lieu de `ctx.state.with(...)`. |
| `.concurrency must be positive` / `V1 supports .concurrency(1) only` | Le sémaphore à N détenteurs est en V1.5. |
| `RecursiveAgentInvocationException` | `ctx.spawn` d'un agent portant le nom de l'appelant. |
| Un retry qui n'arrive jamais | `onError` rendant une chaîne au lieu de `ErrorAction.retry({...})`. |

## Hors périmètre, volontairement

`budget()` et `globalThis.inputs` ne font rien sur le chemin déclaratif ; `.body()` non plus.
`.when(predicate)` ne fait rien **nulle part** — gardez l'appel `withAgent`/`withTask` avec un
`if` à la place. `ctx.llm.embed` rend un vecteur bidon. La liste complète, avec ce que chacun
fait réellement, est dans
[Écarts connus entre les typings et le runtime](../reference/scripting-dsl.md#écarts-connus-entre-les-typings-et-le-runtime).

## Où aller ensuite

- [Référence du DSL de scripting](../reference/scripting-dsl.md) — chaque méthode, et quelle forme l'honore.
- [DSL de scripting — architecture](../architecture/scripting.md) — ce qu'est le runtime et où il se situe.
- [Piloter des crews depuis le REPL](../architecture/coding-agent-ts.md) — lancer une crew depuis une commande `.cmd.ts`.
- [`examples/scripting/`](https://github.com/Orkeon/orkeon/blob/main/examples/scripting/README.md) — chaque extrait ci-dessus, exécutable.
