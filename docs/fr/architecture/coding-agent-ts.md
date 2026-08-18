> 🇬🇧 [English version](../../architecture/coding-agent-ts.md)

# Agent de codage Orkeon (TypeScript)

L'agent de codage est un assistant de codage agentique (à la Claude Code) construit **sur la
pile scriptée d'Orkeon** — le registre de slash-commands `*.cmd.ts` (`Orkeon.Cli.Commands.Scripting`),
le runtime de crew `crew.ork.ts` (`Orkeon.Scripting`), et les outils C# `ToolBase`. Il fait
l'objet de `experiments/07-orkeon-coding-agent-ts/` (le submodule compagnon `experiments`) (spec + plan + résultats).

## Architecture : plan de contrôle vs moteur

```
REPL (Orkeon.ConsoleApp --runner=scripted-commands)
  │  parses "/cmd args" + free text
  ▼
*.cmd.ts  (Orkeon.Cli.Commands.Scripting)              ══ CONTROL PLANE ══
  defineCommand (sync) / defineAsyncCommand (async)        never does the work itself
  │                         │                          │
  │ tools.*  (direct ops)   │ services.get("script-host")│ services.get("commands")
  ▼                         ▼  .runCrew / .runCrewAsync   ▼  .request / .post
ops thin              ══ ENGINE: crew.ork.ts ══       living agent .onCommand(env)
/cost /diff …         ScriptHost.RunFromFileAsync       (lightweight reply, no ctx)
                      .body() / ctx.llm.act / budget
                                │ tools.<camelCase>(params)
                                ▼
                      C# tools (IBaseTool / ToolBase)
                                │
                      ISessionBufferService · ICategoryMemoryStore · ICostBudgetManager
```

La règle d'exécution (`COMMAND-DISPATCH-DESIGN.md §2`) : le registre de commandes est un
**plan de contrôle**, pas un moteur de concurrence. Tout le TS s'exécute dans Jint
(mono-thread, pas de boucle d'événements). Une commande — sync ou async — ne fait jamais le
travail long elle-même ; elle *parle* à un moteur côté hôte via la whitelist de services.
`script-host` **est** ce moteur pour les crews.

## Le service `script-host` (pont cmd → crew)

`ctx.services.get("script-host")` expose `ScriptHostFacade` :

| Méthode | Sémantique |
|---|---|
| `runCrew(name, input?)` | Charge `crews/<name>/crew.ork.ts`, l'exécute via `ScriptHost.RunFromFileAsync` (honore `.body()` + `ctx.llm`), **attend**, retourne `CrewRunOutput`. Crews courtes. |
| `runCrewAsync(name, input?)` | Poste l'exécution sur un thread du pool, retourne un **ticket** immédiatement. La complétion s'écoule vers le `completed(result)` d'une `defineAsyncCommand` via le même cycle de ticket que `commands.post`. Workflows longs. |
| `listCrews()` | Noms des crews découvertes. |

`input` est transmis au moteur de crew comme `globalThis.inputs` (un objet JS parsé depuis
du JSON avant évaluation — le hook de pré-exécution du `ScriptHost`). Les crews lisent
`globalThis.inputs`.

## La boucle interactive (`main-loop`)

La boucle est un agent dont **le `.body()` est la boucle** : il appelle
`ctx.llm.act(prompt, opts)`, qui exécute le cycle LLM ⇄ tool-calling sur le catalogue d'outils de
l'agent. Elle est lancée comme une crew (`runCrewAsync("main-loop", { prompt, permissionMode })`)
— *pas* via `onCommand`, qui n'a pas de `ctx`. La continuité de conversation entre les
exécutions vient du singleton `ISessionBufferService`.

`ActOptions.system` sème un **vrai message `role:"system"`** en tête du prompt utilisateur
(persistant à chaque itération de la boucle d'outils). Sans lui, `act()` envoyait un unique
message utilisateur — c'est ainsi que les agents scriptés tournaient avant cette option :
l'identité et la politique d'outils voyageaient avec l'autorité d'un message utilisateur, et
la gestion système native des providers (le `system` top-level d'Anthropic, `cache_control`)
ne se déclenchait jamais. Un message système de niveau conversation l'emporte sur
`LlmConfig.SystemMessage` chez tous les providers.

Barrière de permissions (v1) : les outils de lecture sont toujours autorisés ; `file_write`
et `shell_command` sont enveloppés (via `withAutonomousTool`) de sorte que **le mode `plan`
les refuse** (lecture seule). La confirmation interactive à chaque écriture nécessite le
canal de prompt du REPL (différée en v2). Budget : `process("autonomous")` + `.budget({...})`
(`AgentExecutionBudget`, 5 dimensions).

## Les primitives de session (Phase 2 / 6)

- **`ISessionBufferService`** (singleton) — le buffer de conversation : messages,
  métadonnées, troncature head+tail, estimation de tokens. Pivot de la boucle et des
  commandes de session.
- **`ICategoryMemoryStore`** — CRUD de mémoire typée sur quatre catégories
  (user/project/feedback/reference).
- **`ICostBudgetManager`** — télémétrie cumulative coût/tokens/appels (existant ; désormais
  câblé en DI).

Six create-tools les exposent aux scripts : `session_store`, `session_snip`, `token_budget`,
`memory_store`, `session_cost`, `session_stats`. Enregistrez-les avec
`AddOrkeonSessionTools()`.

## Les outils dans les commandes (`tools.*` dans `.cmd.ts`)

`JsEngineFactory.Create()` enregistre déjà le namespace `tools` ; la factory CLI est
désormais construite avec les outils intégrés (et le provider LLM), donc
`tools.<camelCase>(params)` fonctionne dans les handlers `.cmd.ts` comme dans les crews.
C'est ainsi que `/cost`, `/diff`, `/memory`, … appellent directement les outils.

## Exécution

```bash
# REPL (loads the 17 commands)
DEEPSEEK_API_KEY=sk-... bash experiments/07-orkeon-coding-agent-ts/run-repl.sh

# A single crew standalone (honours .body() + ctx.llm)
bash experiments/07-orkeon-coding-agent-ts/run-crew.sh crews/git-commit/crew.ork.ts
```

Le REPL a besoin d'une clé LLM pour les crews/la boucle. Les 17 commandes et le lancement de
crew sont exercés par des tests automatisés (`Exp07CommandSurfaceTests`,
`ScriptHostFacadeTests`) sans clé. Voir
`experiments/07-orkeon-coding-agent-ts/RESULTS.md` pour la matrice d'acceptation.
