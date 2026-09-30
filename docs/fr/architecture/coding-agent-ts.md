> 🇬🇧 [English version](../../architecture/coding-agent-ts.md)

# Piloter des crews depuis le REPL — le pont `.cmd.ts` → `crew.ork.ts`

Une commande scriptée est un **plan de contrôle** : elle analyse ce que vous avez tapé et
décide de ce qui doit arriver. Une crew est un **moteur** : elle fait le travail. La couture
entre les deux est le service `script-host`, et cette page documente cette couture — le motif
derrière un assistant de codage agentique bâti sur la pile scriptée d'Orkeon, où des commandes
`*.cmd.ts` (`Orkeon.Cli.Commands.Scripting`) pilotent des crews `crew.ork.ts`
(`Orkeon.Scripting`) au-dessus des outils C# `ToolBase`.

Tout ce qui suit est livré et exécutable depuis un clone. La section
[`## Exécution`](#exécution) en fin de page le lance.

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
/hello /crews         ScriptHost.RunFromFileAsync       (lightweight reply, no ctx)
                      .body() / ctx.llm.act / budget
                                │ tools.<camelCase>(params)
                                ▼
                      C# tools (IBaseTool / ToolBase)
                                │
                      ISessionBufferService · ICategoryMemoryStore · ICostBudgetManager
```

La règle que ce schéma encode : le registre de commandes est un plan de contrôle, pas un
moteur de concurrence. Tout le TypeScript tourne dans Jint — mono-thread, avec une boucle
d'événements mais sans timers — donc une commande, synchrone ou asynchrone, ne fait jamais le travail long
elle-même. Elle *parle* à un moteur côté hôte au travers de la whitelist de services, et
`script-host` **est** ce moteur pour les crews. Le versant dispatch de la même règle est dans
[Commandes CLI en TypeScript](cli-ts-commands.md).

## Le service `script-host` (pont cmd → crew)

`ctx.services.get("script-host")` expose `ScriptHostFacade` :

| Méthode | Sémantique |
|---|---|
| `runCrew(name, input?)` | Charge `<crews-dir>/<nom>/crew.ork.ts`, l'exécute via `ScriptHost.RunFromFileAsync` (honore `.body()` + `ctx.llm`), **attend**, rend un `CrewRunOutput` (`{ ok, summary?, artifacts?, error? }`). Crews courtes : l'attente est bornée par `RunCrewTimeout` (ci-dessous), au-delà de quoi le script reçoit une `TimeoutException`. |
| `runCrewAsync(name, input?)` | Poste l'exécution sur un thread du pool et rend un **ticket** immédiatement. La complétion est drainée vers le `completed(result)` d'un `defineAsyncCommand`, par le même cycle de tickets que `commands.post`. Workflows longs. |
| `listCrews()` | Les noms de crews découverts. |

Une crew inconnue n'est pas une exception : `runCrew` rend `ok: false` avec
`crew '<nom>' not found under: …`. `runCrewAsync` a besoin du substrat de dispatch
qu'enregistre `AddScriptCommands`, et lève une erreur sans lui.

`ScriptHost` exécute le fichier en **procédural** : une crew qui se termine par
`globalThis.crew = crew` n'est pas confiée ici à un orchestrateur, elle est exécutée comme
`await crew.run()` l'exécuterait — `.body()` exécutés, tâches ignorées
([les deux formes](../reference/scripting-dsl.md#les-deux-formes)). Écrivez en forme
procédurale les crews que ce pont lance.

Une crew se résout par son nom dans les répertoires donnés à l'hôte : `--crews-dir` est
répétable, chaque répertoire est monté en lecture seule comme `/crews`, `/crews-1`, …
(`CliCrewMountBootstrapper`), et la crew nommée `review` est le fichier
`<dir>/review/crew.ork.ts` ; le premier répertoire qui l'a l'emporte. `input` arrive dans la
crew comme `globalThis.inputs`, un objet JS analysé depuis du JSON avant l'évaluation, par le
hook de pré-exécution de `ScriptHost`, et le `globalThis.result` de la crew revient comme
`summary` (une chaîne telle quelle, tout le reste sérialisé en JSON).

La même résolution se configure sous `Orkeon:Cli:ScriptHost` (`ScriptHostFacadeOptions`) :

| Clé | Défaut | Rôle |
|---|---|---|
| `CrewDirectories` | vide | Répertoires virtuels parcourus dans l'ordre ; `--crews-dir` s'y ajoute. |
| `CrewFileName` | `crew.ork.ts` | Le fichier d'entrée cherché sous `<dir>/<nom>/`. |
| `RunCrewTimeout` | `00:10:00` | Borne d'un `runCrew` synchrone ; zéro ou négatif la désactive. |

## Une boucle d'agent dans une crew — `ctx.llm.act`

Un agent interactif est un agent dont le **`.body()` est la boucle** : il appelle
`ctx.llm.act(prompt, opts)`, qui déroule le cycle LLM ⇄ appels d'outils sur le catalogue
d'outils de l'agent, jusqu'à ce que le modèle cesse de demander des outils ou que
`maxIterations` soit atteint (`src/scripting/Orkeon.Scripting/Typings/context.d.ts`, `act<T>`
et `ActOptions`). Les outils proposés au modèle sont les outils intégrés que l'agent a choisis
avec `.tools([...])`. Il se lance comme une crew — `runCrewAsync(name, { prompt, permissionMode })`,
la crew transmettant `inputs.permissionMode` à `act` — et *non* par `onCommand`, qui n'a pas
de `ctx` et ne peut donc pas atteindre le LLM. La continuité de la
conversation entre exécutions vient du singleton `ISessionBufferService`.

`ActOptions.system` sème un **vrai message `role:"system"`** devant le prompt utilisateur, et
il persiste à chaque itération de la boucle d'outils. Sans lui, `act()` envoie un seul message
utilisateur — ce qui est la façon dont les agents scriptés ont tourné jusqu'à l'existence de
cette option : l'identité et la politique d'outils voyageaient avec l'autorité d'un
utilisateur, et la gestion native du system des fournisseurs (Anthropic `system` au premier
niveau, `cache_control`) ne se déclenchait jamais. Un message system au niveau conversation
l'emporte sur `LlmConfig.SystemMessage` chez tous les fournisseurs.

## Permissions et budget

Le garde de permissions est un service DI de premier rang, `IPermissionGate`
(`Orkeon.Application.Interfaces.Security`) implémenté par `ModePermissionGate`
(`Orkeon.Infrastructure.Security`), consulté à chaque appel d'outil dans `ctx.llm.act`. Quatre
modes (`bypassPermissions`, `plan`, `acceptEdits`, `default`), classification
lecture/écriture depuis la déclaration `IBaseTool.Access` de l'outil lui-même (avec une table
d'outils de lecture curée et les préfixes `codebase_`/`symbol_`/`index_` en repli), fermeture
par défaut sur un outil inconnu et sur un mode inconnu. Il n'y a pas encore d'approbation
interactive : un appel que le mode n'autorise pas est refusé, et le modèle lit `DENIED: …`
comme réponse de l'outil ; `Interactive` (défaut `false`) ne change que ce message, le flux
d'approbation lui-même étant pour plus tard. Le tout est derrière
`Orkeon:Security:PermissionGate:Enabled` (défaut `false`) / `:Interactive`, câblé par le REPL
et `RunnerHost` (`AddOrkeonPermissionGate`), sans effet quand c'est désactivé. Dans la TUI,
Maj+Tab fait tourner le mode par défaut de la session. Voir
[les sous-systèmes opt-in](../reference/opt-in-subsystems.md).

Le budget est l'autre borne : `process("autonomous")` plus `.budget({...})`
(`AgentExecutionBudget`, cinq dimensions).

## Les primitives de session

Trois ports portent ce qu'une conversation doit conserver entre deux exécutions :

- **`ISessionBufferService`** (singleton) — le tampon de conversation : messages, métadonnées,
  troncature tête+queue, estimation de tokens. Pivot de la boucle et des commandes de session.
- **`ICategoryMemoryStore`** — CRUD mémoire typé sur quatre catégories
  (user/project/feedback/reference).
- **`ICostBudgetManager`** — télémétrie cumulée coût/tokens/appels.

`AddOrkeonSessionTools()` les expose aux scripts sous forme de six outils — `session_store`,
`session_snip`, `token_budget`, `memory_store`, `session_cost`, `session_stats` — catalogués
avec les autres dans [l'inventaire des outils](../tools/inventory.md).

## Les outils dans les commandes (`tools.*` dans `.cmd.ts`)

`JsEngineFactory.Create()` enregistre l'espace de noms `tools`, et la factory de la CLI est
construite avec les outils intégrés et le fournisseur LLM : `tools.<camelCase>(params)`
fonctionne donc dans un handler `.cmd.ts` exactement comme dans une crew. C'est ainsi qu'une
commande fait elle-même un petit travail plutôt que de payer une crew — lire un fichier,
mettre en forme un rapport — pendant que tout ce qui est long passe par `script-host`.

## Exécution

Deux répertoires, deux rôles. `--commands-dir` charge le plan de contrôle, `--crews-dir`
fournit les moteurs. Les deux pointent vers du matériel livré dans ce dépôt, et aucun ne
demande de clé d'API :

```bash
# Le plan de contrôle seul — les commandes de démonstration du dépôt
dotnet run --project src/apps/Orkeon.ConsoleApp -- \
    --runner=scripted-commands \
    --commands-dir examples/cli-ts-commands

# Le pont — des commandes qui lancent une crew par son nom via script-host
dotnet run --project src/apps/Orkeon.ConsoleApp -- \
    --runner=scripted-commands \
    --commands-dir examples/cli-ts-commands \
    --crews-dir    examples/cli-ts-commands/crews
```

```
scripted> /crews
review

scripted> /review src/Program.cs
src/Program.cs: source file — worth a read

scripted> /review-bg examples/README.md
  ✓ [t1] review-bg → crew:review: done
✓ review: examples/README.md: not a source file — skipped
```

La crew de démonstration n'appelle aucun modèle, d'où l'absence de clé ; pointez le `.body()`
d'un agent sur `ctx.llm.act` et le même pont en porte une vraie. Sans le REPL, une crew
s'exécute directement depuis la CLI :

```bash
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run examples/scripting/01-hello-world.ork.ts
```

Le pont lui-même est couvert par `ScriptHostFacadeTests`, dans
`tests/cli/Orkeon.Cli.Commands.Scripting.Tests/`, qui exerce la résolution de crew et le cycle
de tickets sans clé.

## Où aller ensuite

- [DSL de scripting — architecture](scripting.md) : ce qu'est le runtime `.ork.ts` et où il se situe.
- [Commandes CLI en TypeScript](cli-ts-commands.md) : le plan de contrôle en entier —
  `defineCommand`, schémas d'arguments, dispatch vers les agents.
- [`examples/cli-ts-commands/`](https://github.com/Orkeon/orkeon/blob/main/examples/cli-ts-commands/README.md) :
  la source de la session ci-dessus.
