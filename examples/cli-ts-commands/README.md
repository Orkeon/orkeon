# CLI TypeScript Commands — démo

Minimal demonstration of `Orkeon.Cli.Scripting`: a `*.cmd.ts` script discovered at
runtime, evaluated through Jint, and exposed in the interactive REPL.

## Lancer la démo

Depuis la racine du repo :

```bash
dotnet run --project src/apps/Orkeon.ConsoleApp -- \
    --runner=scripted-commands \
    --commands-dir examples/cli-ts-commands
```

(Sur Windows, même commande en cmd ou PowerShell — le runner détecte la TTY et
choisit automatiquement entre Terminal.Gui et plain console.)

Le `--commands-dir` ajoute un mount VFS à la volée et configure
`Orkeon:Cli:ScriptCommands:Directories` pour pointer dessus. Sans le flag, le
runner démarre sans commande scriptée (juste `help`, `exit`, `clear`).

## Transcript REPL

```
==================================
   Orkeon Scripted Commands REPL
==================================

Type 'help' for the command list, 'help-cmd <name>' for a single command's signature.

scripted> help

Available commands:

  [Default]
    help, ?, h  Show available commands
    exit, quit  Exit the application
    clear, cls  Clear the console

  [(no group)]
    hello       Greet someone (or the world).
    help-cmd    Show detailed help for a scripted command (usage: help-cmd <name>).

scripted> hello
Hello, world!

scripted> hello --who=Cyril
Hello, Cyril!

scripted> help-cmd hello
hello — Greet someone (or the world).
Arguments:
  --who  string  default: "world"

scripted> exit
```

## Comment ça marche

1. Au démarrage, `ScriptCommandLoader` énumère `*.cmd.ts` sous
   `examples/cli-ts-commands/` via `IFileSystemService.EnumerateFilesAsync`.
2. Chaque script est transpilé par `EsbuildTranspiler` (ou
   `PassThroughTranspiler` en fallback) puis évalué dans un nouveau
   `Jint.Engine` avec `globalThis.defineCommand` câblé sur un
   `CommandDescriptorCollector`.
3. Le collector est figé après `engine.Evaluate(...)`; tout appel ultérieur de
   `defineCommand(...)` (par exemple depuis un handler) jette une exception.
4. Les descripteurs sont validés (`^[a-z][a-z0-9-]*$`, unicité, pas de
   collision avec `help`/`exit`/`clear`) puis transformés en `ScriptCommand`
   exposé via `ScriptCommandRegistry`.
5. À chaque invocation, le runner appelle `ScriptCommand.ExecuteAsync(ctx, ct)`,
   parse les args contre le schema TS, et appelle le handler JS avec
   `(args, ctx)` — `ctx` est le `CommandRuntimeContext` (log, prompt, progress,
   …).

## Dispatch vers les agents

Ce dossier contient aussi `dispatch.cmd.ts` (commandes `ask` / `ask-bg`) et
`echo-agent.ork.ts` (un agent qui répond via `onCommand`). Une commande adresse
un agent **par son nom** au travers de `ctx.services.get("commands")` :

- `ask <text>` — dispatch **synchrone** : bloque et affiche la réponse de l'agent.
- `ask-bg <text>` — dispatch **asynchrone** : rend la main avec un ticket ;
  `completed(...)` est rejoué à la réponse de l'agent.
- `ps` / `inspect --ticket=…` / `result --ticket=…` / `cancel --ticket=…` —
  introspection et contrôle des commandes en vol.

```
scripted> ask hello            # → HELLO
scripted> ask-bg hello         # launched (ticket t1)
scripted> ps                   # t1 ask-bg echo running …
scripted> result --ticket=t1   # [t1] HELLO
```

Détails : `docs/architecture/cli-ts-commands.md` §« Dispatching commands to
agents » et `project/features/cli-ts-commands/COMMAND-DISPATCH-DESIGN.md`.

## Pour aller plus loin

- Doc utilisateur complète : `docs/architecture/cli-ts-commands.md`.
- Spec d'architecture : `project/features/cli-ts-commands/SPEC.md`.
- Design du dispatch : `project/features/cli-ts-commands/COMMAND-DISPATCH-DESIGN.md`.
- Récap d'implémentation : `project/tasks/done/CLI-TS-IMPLEMENTATION-DONE.md`.
