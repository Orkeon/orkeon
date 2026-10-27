> 🇬🇧 [English version](../../architecture/scripting.md)

# DSL de scripting — architecture

Le DSL de scripting d'Orkeon est un langage à syntaxe TypeScript embarqué dans le runtime,
implémenté par le projet `Orkeon.Scripting` (et exposé via la CLI `orkeon`).
C'est la manière recommandée d'écrire des crews quand on ne veut pas écrire de C#.

## Pourquoi un DSL

Les auteurs familiers de l'outillage frontend bénéficient d'une expérience d'écriture
en fichier unique (`.ork.ts`) sans étape de compilation de leur côté : le runtime
confie le fichier à esbuild — qui supprime les types et, quand le fichier est sur disque,
assemble ses imports relatifs — et exécute le JavaScript résultant dans Jint avec des
limites de sandbox. Le DSL expose toute la surface d'Orkeon — agents, crews,
tâches, outils personnalisés, machines à états, graphes, événements, locks, hooks de
cycle de vie — via des builders fluents et des déclarations littérales.

## Sa place dans la Clean Architecture

`Orkeon.Scripting` dépend de `Orkeon.Domain`, `Orkeon.Application`,
`Orkeon.Infrastructure` et `Orkeon.Rag.Abstractions` (l'espace de noms `rag.*`).
Il ne modifie pas ces couches : c'est un adaptateur opt-in qui traduit les appels
côté JS en invocations de domaine existantes. Il n'enregistre rien lui-même en DI —
il n'y a pas d'extension `AddOrkeon*` : la CLI, le runner partagé et le REPL
(`AddScriptCommands`, voir [Commandes CLI en TypeScript](./cli-ts-commands.md))
construisent eux-mêmes le `ScriptHost`.

```
src/
└── scripting/
    ├── Orkeon.Scripting/         ← cette couche (runtime Jint + bindings)
    │   ├── ScriptHost.cs
    │   ├── JsEngineFactory.cs
    │   ├── ScriptingHostPorts.cs ← porte de permissions + puits de deltas fournis par l'hôte
    │   ├── Adapters/             ← JsCrewConfigurationAdapter (le passage déclaratif → CrewConfiguration)
    │   ├── Builders/             ← JsAgentBuilder, JsCrewBuilder, JsTaskBuilder, JsToolBuilder
    │   ├── Bindings/             ← enregistrements globaux (agentBuilder, crewBuilder, llm, tools, rag, …)
    │   ├── Runtime/              ← JsCrew, JsExecutionContext, JsAgentContext, JsLlmFacade, …
    │   ├── Orchestration/        ← JsStateMachine, JsStateGraph
    │   ├── ErrorPolicy/          ← JsErrorAction, ErrorCodeMapper
    │   ├── Exceptions/           ← les exceptions typées du runtime (StateMutationOutsideWithException, …)
    │   ├── Internal/             ← JsHostError, fabriques de trampolines JS
    │   ├── Configuration/        ← ScriptingLimitsOptions, ScriptingToolchainOptions
    │   ├── Versioning/           ← la directive `/// <reference orkeon-script="1.0" />` ou `// @orkeon-script 1.0`
    │   ├── Telemetry/            ← ScriptingActivitySource
    │   ├── Testing/              ← doublures LLM / outil (non exposées aux scripts)
    │   ├── Toolchain/            ← EsbuildTranspiler, PassThroughTranspiler
    │   └── Typings/              ← les déclarations *.d.ts
    └── Orkeon.Scripting.Cli/     ← l'outil `orkeon` : `run`, `forge`, `init`, `doctor`, `llm`, `rag`, `usecases`, `email`, `settings`, `typings`, `mcp`
```

## Démarrage rapide

Les tutoriels exécutables vivent dans [`examples/scripting/`](https://github.com/Orkeon/orkeon/tree/main/examples/scripting)
(du hello world aux littéraux FSM/graphe et au RAG). De bout en bout :

```bash
dotnet build src/scripting/Orkeon.Scripting.Cli/Orkeon.Scripting.Cli.csproj
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run examples/scripting/01-hello-world.ork.ts
```

La CLI émet le résultat du script en JSON sur stdout ; les codes de sortie suivent la
convention habituelle (`0` ok, `1` erreur de script, `2` erreur runtime, `130` annulé).

Le même verbe `run` accepte aussi un **crew YAML** — la cible sélectionne
la voie (`.yaml`/`.yml` **ou un dossier portant une crew multi-fichiers** → le runner de
crew partagé ; tout autre fichier → DSL de scripting). Un script qui affecte
`globalThis.crew` est une définition de crew *déclarative* et part lui aussi vers le runner
partagé, comme le YAML ; les autres tournent sur l'hôte de scripting
([les deux formes](../reference/scripting-dsl.md#les-deux-formes)) :

```bash
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run examples/09-experimental/llm-response-format/crew.yaml
```

Pour les crews YAML et les scripts déclaratifs, l'outil délègue au runner one-shot
partagé (le `RunnerExecution` d'`Orkeon.Hosting`), donc les drapeaux propres au YAML
s'appliquent : `-V/--var KEY=VALUE`, `--initial-context`, plus les drapeaux partagés
`--settings/--mount/--mount-id/--allow-external-mounts/--verbose/--llm-log[-path]` et les
drapeaux diagnostics/protocole (`--validate`, `--list-tools`, `--events jsonl`,
`--stream`, `--client`). Les drapeaux des scripts procéduraux (`--inputs`, `--inputs-file`,
`--memory-limit-mb`) sont ignorés sur cette voie — un script déclaratif le signale sur
stderr. Le runner partagé affiche la sortie du crew sous une bannière
`=== Crew Output ===` au lieu d'un `result` JSON. Toutes les options :
[la référence du CLI](../reference/cli.md#orkeon-run).

## Récapitulatif de l'API

Deux pages s'appuient dessus : [Écrire une crew en TypeScript](../guides/write-a-crew-in-typescript.md)
pour le récit, et la [Référence du DSL de scripting](../reference/scripting-dsl.md) pour le
tableau méthode par méthode — dont la colonne que les déclarations ne peuvent pas porter :
**laquelle des deux formes de script honore réellement chaque méthode**.

Les déclarations sont la référence. Elles sont livrées avec le DSL, ce sont elles que lit
votre éditeur, et elles vivent sous `src/scripting/Orkeon.Scripting/Typings/` —
concaténées au build en un seul `orkeon.d.ts`, que l'outil `orkeon` embarque et écrit à côté
de votre projet avec `orkeon typings` ([Référence](#référence)).

| Concept | Où regarder |
|---------|---------------|
| `agentBuilder()` / `crewBuilder()` / `taskBuilder()` / `toolBuilder()` | `agent.d.ts`, `crew.d.ts`, `task.d.ts`, `tool.d.ts` |
| `ExecutionContext` et `AgentContext` (`ctx.llm`, `ctx.memory`, A2A, locks, spawn) | `context.d.ts` |
| `ctx.llm.act` — la boucle LLM ⇄ appels d'outils, et ses `ActOptions` | `context.d.ts` |
| Événements (`ctx.events.queue` / `ctx.events.topic`) | `events.d.ts` |
| Formes littérales `stateMachine` / `stateGraph` | `fsm.d.ts`, `graph.d.ts` |
| `onError`, `ErrorAction`, codes d'erreur | `agent.d.ts`, `errors.d.ts` |
| Les globales échangées entre le runner et le script (`crew`, `inputs`, `result`) | `globals.d.ts` |
| La directive de version `/// <reference orkeon-script="1.0" />` ou `// @orkeon-script 1.0` | `orkeon-script.d.ts` |
| Hooks de cycle de vie (`onAgentStart`, `onCrewComplete`, …) | `agent.d.ts`, `crew.d.ts` |
| `onCommand` — répondre par nom aux commandes CLI dispatchées | [cli-ts-commands.md](../architecture/cli-ts-commands.md#côté-agent--oncommand) |
| Namespace intégré `tools.X(...)` | `tools.d.ts`, `tools.generated.d.ts` (écrit par `BuiltInToolTypings` depuis les outils qu'un run enregistre, épinglé par `BuiltInToolTypingsTests`) |
| Les réglages de modèle de l'agent (`llm.default_`, `llm.model(...)`) | `llm.d.ts` |
| RAG (`rag.ingest`, `rag.query`, `rag.retrieve`) | `rag.d.ts` |

## Outils autonomes dans act

Un outil construit avec `toolBuilder()` et attaché à un agent par `withAutonomousTool` est proposé
au modèle dans la boucle `act` du REPL. Le garde-fou de permissions le traite d'après ce que le
script a déclaré, et rien d'autre (épinglé par `ReplAutonomousToolActTests`) :

- **Proposé et exécuté.** Un outil déclaré avec `access("read")` est proposé au modèle ; son
  `execute` peut appeler `tools.fileRead(...)` et la valeur retournée parvient au modèle encodée
  en JSON dans le résultat de l'outil.
- **L'`access` déclaré est la seule entrée du garde-fou.** `read` est autorisé. Un outil `edit` est
  refusé en mode `plan` et soumis à confirmation en mode `default` : la demande passe par le canal
  d'approbation, et sous `orkeon run`, qui n'en a pas, un Ask devient un Deny
  (`ModePermissionGate.cs`). Un outil sans `access` déclaré compte comme une écriture, par défaut
  restrictif.
- **Une approbation par appel initié par le modèle.** Les appels `tools.*` faits dans `execute` ne
  sont pas redemandés : approuver l'outil du script couvre ce que fait son corps.

## Options de act

`ctx.llm.act(prompt, options)` accepte quatre options en plus des réglages du modèle. Elles sont fixées
au début de l'appel (une `ActSession` par appel) :

- **`tools: ["file_read"]`** n'offre au modèle que ce sous-ensemble des outils de l'agent. Un nom que
  l'agent ne possède pas lève `UnknownToolError` avant que le modèle soit interrogé. Un appel que le
  modèle fait à un outil que l'appel n'offre pas reçoit aussitôt `ERROR: tool '<name>' is not available to
  this agent.` : ni la porte de permission ni `onToolCall` ne le voient, et aucune approbation n'est demandée.
- **`effort: "low" | "medium" | "high"`** règle la réflexion du fournisseur pour l'appel
  (`LlmConfig.Thinking`, activée avec cet effort). Le même `effort` est accepté par les autres appels
  `ctx.llm`, par `ctx.llm.with({...})` et par `agent().effort(level)` ; toute autre valeur est refusée
  avec `effort must be low|medium|high`.
- **`onToolCall(call)`** s'exécute avant chaque appel d'outil, après la porte de permission. Il renvoie
  `"allow"` ; `"ask"` (l'appel passe par le canal d'approbation comme une édition en mode `default`, et
  une réponse négative vaut refus) ; `{ deny: raison }` (l'appel ne s'exécute pas, le modèle lit
  `DENIED: <raison>`) ; ou `{ input }` (l'appel s'exécute avec l'entrée réécrite, qui repasse par la
  porte). Un rappel qui lève une exception refuse avec le message de l'exception ; toute autre valeur
  renvoyée (rien compris) refuse aussi, et `"ask"` sans porte de permission refuse.
- **`onToolResult(call, result)`** s'exécute après chaque appel d'outil avec l'appel et son résultat (le
  JSON analysé, ou le texte s'il n'est pas du JSON). Un `{ context: "..." }` renvoyé remplace la ligne
  montrée au modèle pour ce résultat ; ne rien renvoyer la conserve.

Les deux rappels s'exécutent sur le thread du moteur, par la même pompe que les outils de script (voir
le modèle de threading ci-dessous) ; la porte de permission passe toujours en premier.

## Le budget de tokens d'act

`budget({ tokens })` est une somme courante : chaque appel de chat de la boucle ajoute son prompt et sa
complétion (le total du fournisseur). Chaque appel renvoie tout le contexte, si bien que le contexte est
compté de nouveau à chaque appel : un tour de n appels dont le contexte croît régulièrement le paie
environ n fois, et le chiffre ne borne ni la taille de la conversation ni ce qu'un fournisseur facture
avec son cache. Chaque appel journalise la somme courante à côté de la limite (`act: agent '<nom>' call
used <n> tokens, running sum <somme>/<limite> tokens over <n> calls`, Information).

Quand la somme atteint la limite, l'act n'échoue pas. Un appel dont les tokens la dépassent garde sa
réponse : une réponse finale est rendue telle quelle, et les appels d'outils s'exécutent. La garde
avant l'appel suivant termine alors l'act par un dernier appel de chat, sans outils (`ToolMode.None`),
après un tour utilisateur qui dit au modèle que son budget est épuisé et lui demande de répondre
maintenant avec ce qu'il a. Cette réponse est le résultat de l'act, avec `budgetExhausted: true` ; ses
tokens sont comptés mais ne lancent aucune autre conclusion. Un avertissement journalise que le budget
est épuisé. Quand ce dernier appel échoue, ou ne répond rien, l'act rejette avec
`BudgetExhaustedError`, dont le message nomme ce qu'il a compté : `Token budget exhausted: <somme>
tokens sent and received over <n> calls (limit <limite>)`. Les autres dimensions (appels d'outils,
temps mural, profondeur de délégation, enfants) rejettent toujours à la garde, et `maxIterations`
termine toujours par `exhausted: true`.

## Agents enfants : `spawn_agent` et `ctx.spawnBackground`

- **`spawn_agent`** est un outil appelable par le modèle, toujours proposé dans `act`. Il exécute un
  agent nommé de la crew comme enfant (`{ "agent", "input" }`) et renvoie sa sortie. L'enfant tourne
  sous un budget enfant (profondeur de délégation + 1) ; un refus (profondeur ou budget de spawn
  épuisé, agent inconnu) est un résultat d'outil en échec que le modèle lit, jamais une exception.
  L'exécution est du travail du moteur : elle passe par la pompe racine, comme les outils de script
  (voir le modèle de threading ci-dessous).
- **`ctx.spawnBackground(agent, input)`** démarre un agent de la crew en arrière-plan et renvoie son
  ticket (`{ id, agent }`) aussitôt. Sa complétion arrive dans la boîte de réception du contexte de
  script, et le tour suivant d'`act` la lit comme un message utilisateur : `agent <name> completed: <text>`
  ou `agent <name> failed: <reason>`.
- **`waitFor: "children"`** (option d'`act`) : quand le modèle répond alors que des enfants en
  arrière-plan tournent encore, la boucle les attend et réinjecte leurs messages. Les enfants encore
  actifs à la fin d'`act` sont annulés.
- **`crew.concurrency(n)`** borne le nombre d'enfants simultanés (4 par défaut, `n` >= 1). Le créneau
  est acquis avant le sémaphore par agent.

### Instances d'agents : `crew.instances`, `crew.send`, `crew.stop`

Chaque ticket d'arrière-plan et chaque appel à `spawn_agent` est une **instance d'agent** de sa crew,
d'identifiant `a<ticket>` (`a1`, `a2`, ...). Les identifiants ne sont visibles que du script : la
réponse de l'outil `spawn_agent` ne change pas.

- **`crew.instances()`** liste des lignes `{ id, agent, state }` ; `state` vaut `running`,
  `completed`, `failed` ou `stopped`. `ctx.instanceId` est l'identifiant de l'instance dont le corps
  d'un enfant est l'exécution.
- **`crew.send(id, message)`** lance un `act` nu de plus sur le même agent (ses outils et son message
  système, un budget enfant neuf) amorcé par la transcription conservée de l'instance, ajoute le
  résultat à cette transcription et se résout avec le texte de la réponse. Il prend un permis de la
  porte de la crew, compte comme un spawn contre `MaxSpawnedAgents` et prend le sémaphore de l'agent.
  Un `send` sur un identifiant inconnu ou évincé, sur une instance en cours, ou pendant qu'un autre
  `send` du même identifiant s'exécute, est refusé.
- **`crew.stop(id)`** annule aussitôt une instance en cours et renvoie `true` (`false` pour un
  identifiant terminé ou inconnu). Le créneau de la porte est libéré quand l'`act` s'achève, après la
  fin des appels d'outils en vol.
- **Bornes** (un seul propriétaire, APP-05) : `crew.maxInstances(n)` (32 par défaut) garde au plus `n`
  instances, en évinçant la plus ancienne terminée, jamais une instance en cours ; chaque
  transcription est plafonnée en nombre de messages et en nombre total de caractères, les plus anciens
  messages étant retirés d'abord. Un `act` échoué ou annulé ne stocke aucune transcription (la
  précédente reste) ; un `act` terminé par `{ interrupted: true }` stocke sa transcription sans le
  dernier tour d'assistant dont les appels d'outils n'ont pas de résultat.

### Mémoire durable d'agent : `ctx.memory.agent`

`ctx.memory.agent` (`store`, `get`, `search`, `delete`) survit à l'exécution quand l'hôte fournit un
`IScriptAgentMemoryStore` : la CLI orkeon et le runner hébergé le font, avec un fichier JSON par
projet et par agent sous `/workspace/.orkeon/agent-memory/<projet>/`. Une valeur stockée par l'agent
`a` est renvoyée à l'exécution suivante pour `a`, jamais pour `b` ; la clé est
`agent:<projet>:<nom de l'agent>`. Une écriture remplace tout le fichier sous un verrou par fichier et
restaure une sauvegarde si elle échoue ; une clé contenant un saut de ligne est refusée. Les hôtes sans
ce port (`run`, l'assistant et le banc d'essai de Forge) gardent `ctx.memory.agent` en mémoire, le
temps de l'exécution.

## Coexistence avec YAML

La voie de configuration YAML reste supportée et inchangée. Les scripts et les crews
YAML peuvent partager la même application hôte : le DSL est l'une des surfaces
d'écriture parmi d'autres, pas un remplacement. L'outil `orkeon` publié exécute
désormais **les deux** surfaces directement (`orkeon run crew.yaml` et
`orkeon run crew.ork.ts`), si bien qu'un consommateur externe qui ne dépend que des
packages publiés n'a plus à compiler un runner sur mesure pour exécuter des crews YAML.

## Appels bloquants depuis les scripts

Jint exécute un script sur un seul thread : un appel hôte qui attend son
résultat bloque tout le script — et le REPL qui l'héberge — jusqu'à son retour.
Deux ponts CLI conservent délibérément ce contrat synchrone (audités comme
ANT-007/ANT-010, « bloquants assumés ») :

- `ctx.services.get("script-host").runCrew(name, input?)` — exécute un crew et
  attend sa sortie. **Crews courts uniquement.** L'attente est bornée par un
  timeout configurable (`ScriptHostFacadeOptions.RunCrewTimeout`, section de
  config `Orkeon:Cli:ScriptHost`, défaut **10 minutes**) : à expiration, le
  script reçoit une `TimeoutException` claire, le run abandonné est annulé de
  manière coopérative, et le thread du REPL est toujours libéré (une boucle JS
  pure qui ignore l'annulation finit par être stoppée par l'`ExecutionTimeout`
  propre au bac à sable Jint).
- `ctx.services.get("commands").request(agent, intent, payload)` — bloque
  jusqu'à la réponse de l'agent ; la même consigne s'applique
  (voir [cli-ts-commands.md](../architecture/cli-ts-commands.md#dispatch-synchrone--request)).

La voie nominale pour le travail long est le cycle à ticket : `runCrewAsync(name, input?)`
(adossé à `CommandDispatchService.postWork`, même mécanisme que `commands.post`)
retourne un ticket immédiatement et livre le résumé du crew au callback
`completed(result)` d'un `defineAsyncCommand`.

## Modèle de threading

Un moteur Jint n'est pas lié à un thread, mais il n'a qu'un **seul draineur** : un thread à
la fois exécute les jobs de sa boucle d'événements, et un drain lancé depuis l'intérieur
d'un job ne peut pas pomper — il attendrait des jobs que seul le thread qu'il bloque pourrait
exécuter. La famille de défauts que SCR-25 a supprimée (un run de crew qui pendait après
n'importe quel `await` de tête, un graphe d'états dont les nœuds suspendent qui expirait
depuis un corps, des appels `ctx.state.with` concurrents qui faisaient planter le moteur, un
handler de topic ou un hook FSM qui suspend, un callback `onDelta` sur un thread du pool, un
`runStream` non itérable) venait toute de code CLR qui rentrait dans le moteur du mauvais
côté : après un `await`, ou en drainant depuis l'intérieur d'un job. Le runtime suit
désormais une règle, que les reproducteurs de
`tests/scripting/Orkeon.Scripting.Tests/Runtime/EngineThreadingContractTests.cs` fixent :

- **Toute boucle qui rappelle du code de script vit en JavaScript.** Le run de crew
  (`crew.run`, `crew.runAgent`, `crew.runStream`), le graphe d'états (`run`, `runStream`), la
  machine à états (`send`), `ctx.state.with`, la livraison des topics (`publish`, le `lock`
  de l'événement) et le côté script de `ctx.llm.act` sont des fonctions async — des
  générateurs async pour les flux, pour que `for await` fonctionne dessus — construites une
  fois depuis une fabrique JavaScript. Le CLR ne fournit que des aides synchrones (prendre
  l'instantané des agents, ouvrir une tentative, enregistrer un résultat) et des `Task` que
  la boucle attend (l'acquisition d'un sémaphore, un délai de retry, l'annulation du run).
  Chaque ligne de ces boucles s'exécute comme une réaction de promesse sur le thread qui
  draine, quel qu'il soit.
- **Un rappel CLR ne rentre jamais dans le moteur après un `await`.** Un délégué exposé au
  script peut rendre une valeur synchrone, une `Task` dont le résultat est une valeur CLR
  (Jint la règle sur la boucle), ou une promesse JS obtenue de manière synchrone. Il n'appelle
  jamais `Invoke`, `Evaluate`, `FromObject` ni `SetValue` depuis une continuation, et ne
  draine jamais depuis l'intérieur d'un job.
- **Une aide CLR signale un échec par un throw JavaScript.** `JsHostError` enveloppe
  l'exception dans une `Error` qui la porte sur `clr` (son nom de type sur `clrType`) : le
  `catch` et le `finally` du script s'exécutent, la boucle libère ce qu'elle tient, et le
  côté CLR retrouve l'exception typée depuis la valeur rejetée.
- **Le CLR ne pilote le moteur qu'à trois pompes racines**, chacune moteur au repos, sous le
  verrou par moteur, sur un seul thread pour toute l'évaluation — le préfixe synchrone et
  chaque job qui suit : `ScriptHost` pour le script lui-même, `JsCrew.RunAsync` pour le
  handoff `globalThis.crew` et pour les hôtes C#, `JsTool.CallAsync` pour un outil script
  appelé par l'orchestrateur. Depuis C#, `JsCrew.run`, `runAgent` et `runStream` sont les
  fonctions JS elles-mêmes (`JsValue`) ; `RunAsync` est l'entrée CLR, et elle refuse de
  démarrer depuis un rappel CLR que le script a invoqué — depuis un script, appelez
  `crew.run()`.
- **Les pompes du script et de la crew tournent sur un thread dédié, pas sur un worker du
  pool.** Une pompe bloque son thread jusqu'à ce que la promesse se règle : sur le pool, elle
  retenait un worker pendant tout le run, et un pool plafonné s'affamait. `ScriptHost.EvaluateAsync`
  et `JsCrew.RunAsync` démarrent la leur par `RootPump.Start`
  (`TaskCreationOptions.LongRunning | DenyChildAttach`) : un thread par script et par run de
  crew piloté depuis C#, sans borne sur leur nombre, libéré à la fin du run. `JsTool.CallAsync`
  fait exception et reste sur un worker du pool par `Task.Run` : un appel d'outil est court, et
  un thread dédié par appel coûtait 145 à 164 µs par démarrage pour environ 10 µs l'appel
  (médiane +1491 % contre un seuil de 1 %). La garde permanente `EngineThreadingGuardTests`
  nomme cette exemption (commentaire `GAP-57 CH-03` ; sa règle `task-run-around-pump` saute
  cette lambda) et refuse un `Task.Run` autour de toute autre pompe.
- **L'annulation est la seule borne d'une attente ; `ExecutionTimeout` borne l'exécution.**
  Aucun timeout de promesse ne subsiste dans le runtime : une pompe racine draine jusqu'à ce
  que sa promesse se règle ou que son jeton se déclenche. L'`ExecutionTimeout` du bac à sable
  (temps mural, `Orkeon:Scripting:Limits`) borne l'évaluation elle-même et couvre désormais un
  run entier piloté par le CLR, comme il couvrait déjà un script entier (`MemoryLimitBytes`
  aussi). Son horloge s'arrête pendant que le contrôle des permissions décide d'un appel d'outil
  de `ctx.llm.act`, car un canal d'approbation peut attendre un opérateur ; celle du budget de
  temps mural de l'agent aussi. Un `RunAsync` annulé laisse à la boucle un court délai de grâce pour se dérouler —
  ses blocs `finally`, `onCrewError` — avant d'abandonner le run et de libérer ce que le CLR
  possède. Un run qu'un corps ouvre sans `signal` propre — `crew.runAgent`, le `run` d'une
  sous-crew — est l'enfant du run qui l'a ouvert : annulé avec lui, déroulé dans son
  déroulement. Le parent est la tentative ouverte la plus récente encore ouverte sur le
  moteur, exact en imbrication séquentielle et au mieux sous des runs entrelacés sur une même
  boucle d'événements ; `{ signal: ctx.signal }` est la forme explicite.
- **Les tests attendent un moteur sous une garde nommée.** Le runtime n'a aucun timeout de
  promesse : un test qui attend un moteur hériterait sinon des 10 secondes implicites de Jint. Il
  attend sous `HangGuards.EnginePromise` (2 minutes, `Orkeon.Tests.Shared.Timing`) : posée sur
  chaque lambda d'options `new Engine` d'un test, et, pour les moteurs que construisent la
  fabrique et `ScriptHost`, une fois par projet de tests par un `[ModuleInitializer]` sur
  `JsEngineFactory.PromiseTimeout` (interne). Le défaut du produit ne change pas (celui de
  Jint). Le balayage `TestEngineHangGuardTests` échoue sur un moteur de test sans garde.

## Configuration et chaîne d'outils

Les limites de la sandbox sont lues dans `Orkeon:Scripting:Limits` (`ScriptingLimitsOptions`)
par la voie script d'`orkeon run`, le runner partagé et la forge :

| Clé | Défaut | Rôle |
|---|---|---|
| `MemoryLimitBytes` | `104857600` (100 Mo) | Plafond mémoire de Jint. `orkeon run --memory-limit-mb` le surcharge pour un script procédural ; `0` ou moins le désactive. |
| `RecursionLimit` | `64` | Profondeur d'appel JavaScript maximale. |
| `ExecutionTimeout` | `00:00:30` | Borne d'horloge d'une évaluation — un script entier, ou tout un run de crew piloté par le CLR ; les décisions du contrôle des permissions ne comptent pas. |

Les commandes `*.cmd.ts` du REPL ont leur propre profil, `Orkeon:Cli:ScriptCommands:Limits`
([Commandes CLI en TypeScript](./cli-ts-commands.md#appsettingsjson)).

La chaîne esbuild est lue dans `Orkeon:Scripting:Toolchain` (`ScriptingToolchainOptions`), par
chaque hôte qui transpile : `orkeon run`, le runner partagé, `orkeon doctor`, `orkeon forge` et
les commandes `*.cmd.ts` du REPL :

| Clé | Défaut | Rôle |
|---|---|---|
| `EsbuildPath` | — | Chemin absolu du binaire esbuild ; premier de l'ordre de recherche ci-dessous. |
| `EsbuildTimeout` | `00:00:30` | Borne d'horloge d'une transpilation ou d'un bundle esbuild. |

esbuild est cherché, dans l'ordre : `Orkeon:Scripting:Toolchain:EsbuildPath`, la variable
d'environnement `ORKEON_ESBUILD_PATH`, un `esbuild-bin/esbuild[.exe]` à côté du binaire en cours
(ce que livrent les archives de release), le `tools/scripting-esbuild/node_modules/` du dépôt
(installé par le build d'`Orkeon.Scripting` avec `npm ci`, sauté avec
`-p:SkipScriptingNpmInstall=true` ; cherché jusqu'à huit dossiers parents depuis le répertoire de
travail et depuis le binaire), puis le `PATH`. Quand rien n'est trouvé, l'erreur liste chaque
endroit essayé ; `orkeon doctor` le signale dans la vérification `esbuild`.

**L'outil dotnet `orkeon` ne livre pas esbuild.** Son paquet ne porte aucun `esbuild-bin/` :
après `dotnet tool install -g Orkeon.Scripting.Cli`, un script `.ork.ts` ne tourne qu'une fois
esbuild présent sur la machine — `npm install -g esbuild` (il arrive sur le `PATH`), ou un
binaire désigné par `ORKEON_ESBUILD_PATH` ou `EsbuildPath`. Rien n'est téléchargé au premier
lancement. Les crews YAML n'en ont jamais besoin.

Un script peut commencer par `/// <reference orkeon-script="1.0" />`, ou par
`// @orkeon-script 1.0`, la forme que `tsc` accepte : il rejette la forme référence (TS1084) puis
saute sa passe sémantique, si bien qu'un projet qui vérifie les types de ses scripts emploie le
commentaire de ligne, que génère `orkeon forge`. La directive est facultative ; présente, une
version que le runtime ne prend pas en charge (seule `1.0` aujourd'hui) fait échouer le run avec
`ScriptVersionMismatchError` avant toute exécution. Un script qui porte les deux formes est lu par
sa forme référence.

## Limites V1

- `concurrency(N)` plafonné à 1 (mutex). Le sémaphore à N détenteurs est prévu en V1.5.
- Les locks n'ont pas de timeout. `LockTimeoutError` est prévu en V1.5.
- Le streaming via `ctx.llm.stream` est **par token** dès que le provider est un
  `IStreamingLlmProvider` (les 16 providers livrés le sont) ; le chunk unique en
  texte plein n'est que le repli d'un provider custom non-streaming.
- `ctx.llm.embed` renvoie un vecteur stub ; l'intégration avec de vrais embedders est
  un chantier ultérieur.
- La composabilité hiérarchique FSM/Graph (sub-states, sub-graphs) est prévue en V1.5.
- Les événements sont uniquement en mémoire (pas de persistance Redis/NATS).

## Référence

Cette page dit ce qu'est le DSL et où il se situe ; les typings disent ce qu'il expose. La
surface des crews est `orkeon.d.ts` — les `Typings/*.d.ts` ci-dessus concaténés
(`errors.d.ts` d'abord, les autres par ordre alphabétique) par le build
d'`Orkeon.Scripting`, qui l'embarque dans l'assembly et l'écrit aussi dans
`src/scripting/Orkeon.Scripting/bin/<configuration>/net10.0/dist/orkeon.d.ts`.
`orkeon-cli.d.ts` est un autre fichier pour une autre surface : les commandes `*.cmd.ts`
documentées dans [cli-ts-commands.md](../architecture/cli-ts-commands.md).

Les deux sont livrés dans l'outil `orkeon`. `orkeon typings` les écrit dans `./.orkeon/` (ou
`--out <dossier>`), en écrasant ce qui s'y trouve — relancez-le après une mise à jour de
l'outil, pour que l'éditeur lise la surface du runtime qui exécutera les scripts
([référence du CLI](../reference/cli.md#orkeon-typings)).
