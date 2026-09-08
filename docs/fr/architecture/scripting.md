> 🇬🇧 [English version](../../architecture/scripting.md)

# DSL de scripting — architecture

Le DSL de scripting d'Orkeon est un langage à syntaxe TypeScript embarqué dans le runtime,
implémenté par le projet `Orkeon.Scripting` (et exposé via la CLI `orkeon`).
C'est la manière recommandée d'écrire des crews quand on ne veut pas écrire de C#.

## Pourquoi un DSL

Les auteurs familiers de l'outillage frontend bénéficient d'une expérience d'écriture
en fichier unique (`.ork.ts`) sans étape de compilation de leur côté : le runtime
supprime les types TypeScript via esbuild et exécute le JavaScript résultant dans Jint
avec des limites de sandbox. Le DSL expose toute la surface d'Orkeon — agents, crews,
tâches, outils personnalisés, machines à états, graphes, événements, locks, hooks de
cycle de vie — via des builders fluents et des déclarations littérales.

## Sa place dans la Clean Architecture

`Orkeon.Scripting` est un nouveau projet qui dépend de `Orkeon.Domain`,
`Orkeon.Application` et `Orkeon.Infrastructure`. Il ne modifie pas ces
couches : c'est un adaptateur opt-in qui traduit les appels côté JS en
invocations de domaine existantes.

```
src/
└── scripting/
    ├── Orkeon.Scripting/         ← cette couche (runtime Jint + bindings)
    │   ├── ScriptHost.cs
    │   ├── JsEngineFactory.cs
    │   ├── Builders/             ← JsAgentBuilder, JsCrewBuilder, JsTaskBuilder, JsToolBuilder
    │   ├── Bindings/             ← enregistrements globaux (agentBuilder, crewBuilder, …)
    │   ├── Runtime/              ← JsCrew, JsExecutionContext, JsAgentContext, JsLlmFacade, …
    │   ├── Orchestration/        ← JsStateMachine, JsStateGraph
    │   ├── ErrorPolicy/          ← JsErrorAction, ErrorCodeMapper
    │   ├── Telemetry/            ← ScriptingActivitySource
    │   └── Toolchain/            ← EsbuildTranspiler, PassThroughTranspiler
    └── Orkeon.Scripting.Cli/     ← `orkeon run <crew.ork.ts | crew.yaml>`
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
la voie (`.yaml`/`.yml` **ou un dossier portant une crew multi-fichiers** → runner de crew YAML, `.ork.ts`/`.js` → DSL de scripting) :

```bash
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run examples/09-experimental/llm-response-format/crew.yaml
```

Pour les crews YAML, l'outil délègue au runner one-shot partagé (le `RunnerExecution`
d'`Orkeon.Hosting` — le chemin de code exact d'`orkeon run`), donc les drapeaux propres
au YAML s'appliquent : `-V/--var KEY=VALUE`, `--initial-context`, plus les drapeaux
partagés `--settings/--mount/--allow-external-mounts/--verbose/--llm-log[-path]` et les
drapeaux diagnostics/protocole (`--validate`, `--list-tools`, `--events jsonl`,
`--stream`, `--client`). Les drapeaux propres aux scripts (`--inputs`, `--inputs-file`,
`--memory-limit-mb`) sont ignorés sur la voie YAML. Le runner YAML affiche la sortie du crew sous une bannière
`=== Crew Output ===` au lieu d'un `result` JSON.

## Récapitulatif de l'API

Deux pages s'appuient dessus : [Écrire une crew en TypeScript](../guides/write-a-crew-in-typescript.md)
pour le récit, et la [Référence du DSL de scripting](../reference/scripting-dsl.md) pour le
tableau méthode par méthode — dont la colonne que les déclarations ne peuvent pas porter :
**laquelle des deux formes de script honore réellement chaque méthode**.

Les déclarations sont la référence. Elles sont livrées avec le DSL, ce sont elles que lit
votre éditeur, et elles vivent sous `src/scripting/Orkeon.Scripting/Typings/` —
concaténées au build dans le `orkeon.d.ts` que la CLI émet.

| Concept | Où regarder |
|---------|---------------|
| `agentBuilder()` / `crewBuilder()` / `taskBuilder()` / `toolBuilder()` | `agent.d.ts`, `crew.d.ts`, `task.d.ts`, `tool.d.ts` |
| `ExecutionContext` et `AgentContext` (`ctx.llm`, `ctx.memory`, A2A, locks, spawn) | `context.d.ts` |
| `ctx.llm.act` — la boucle LLM ⇄ appels d'outils, et ses `ActOptions` | `context.d.ts` |
| Événements (`ctx.events.queue` / `ctx.events.topic`) | `events.d.ts` |
| Formes littérales `stateMachine` / `stateGraph` | `fsm.d.ts`, `graph.d.ts` |
| `onError`, `ErrorAction`, codes d'erreur | `agent.d.ts`, `errors.d.ts` |
| Hooks de cycle de vie (`onAgentStart`, `onCrewComplete`, …) | `agent.d.ts`, `crew.d.ts` |
| `onCommand` — répondre par nom aux commandes CLI dispatchées | [cli-ts-commands.md](../architecture/cli-ts-commands.md#côté-agent--oncommand) |
| Namespace intégré `tools.X(...)` | `tools.d.ts` |
| Providers LLM (`llm.openai`, `llm.default`, etc.) | `llm.d.ts` |
| RAG (`rag.ingest`, `rag.query`) | `rag.d.ts` |

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

## Limites V1

- `concurrency(N)` plafonné à 1 (mutex). Le sémaphore à N détenteurs est prévu en V1.5.
- Les locks n'ont pas de timeout. `LockTimeoutError` est prévu en V1.5.
- Le streaming via `ctx.llm.stream` est **par token** dès que le provider est un
  `IStreamingLlmProvider` (les 14 providers livrés le sont) ; le chunk unique en
  texte plein n'est que le repli d'un provider custom non-streaming.
- `ctx.llm.embed` renvoie un vecteur stub ; l'intégration avec de vrais embedders est
  un chantier ultérieur.
- La composabilité hiérarchique FSM/Graph (sub-states, sub-graphs) est prévue en V1.5.
- Les événements sont uniquement en mémoire (pas de persistance Redis/NATS).

## Référence

Cette page dit ce qu'est le DSL et où il se situe ; les typings disent ce qu'il expose. La
surface des crews est `orkeon.d.ts` — construit depuis les `Typings/*.d.ts` ci-dessus, et
émis à côté de la sortie de build de la CLI. `orkeon-cli.d.ts` est un autre fichier pour une
autre surface : les commandes `*.cmd.ts` documentées dans
[cli-ts-commands.md](../architecture/cli-ts-commands.md).
