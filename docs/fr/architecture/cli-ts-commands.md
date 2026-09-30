> 🇬🇧 [English version](../../architecture/cli-ts-commands.md)

# Commandes CLI TypeScript

> Référence utilisateur du sous-système `Orkeon.Cli.Commands.Scripting`. Pour la
> spécification d'architecture et la justification de conception, voir
> l'archive de conception des mainteneurs (feature `cli-ts-commands`, SPEC).

## De quoi s'agit-il

Un moyen d'ajouter des **commandes REPL interactives** à un runner CLI Orkeon en
déposant des fichiers `*.cmd.ts` dans un dossier — pas de recompilation .NET, pas
de redémarrage de l'hôte au-delà du runner lui-même. Chaque script déclare une ou
plusieurs commandes via le helper global `defineCommand({...})` ; le chargeur les
détecte au démarrage, les valide, et les expose aux côtés des commandes intégrées
`help`/`exit`/`clear`.

Les commandes peuvent aussi **dispatcher du travail vers des agents vivants** — en
synchrone (bloquer et retourner la réponse) ou en asynchrone (émettre un ticket,
réagir quand l'agent répond). Voir
[Dispatcher des commandes vers des agents](#dispatcher-des-commandes-vers-des-agents).

## Démarrage rapide

```bash
mkdir -p ./commands
cat > ./commands/hello.cmd.ts <<'EOF'
defineCommand({
  name: "hello",
  description: "Say hello to the world (or to someone in particular).",
  args: {
    who: { type: "string", default: "world" },
  },
  async handler(args, ctx) {
    return ctx.continue(`Hello, ${args.who}!`);
  },
});
EOF

dotnet run --project src/apps/Orkeon.ConsoleApp -- \
    --runner=scripted-commands \
    --commands-dir ./commands
```

À l'invite :

```
scripted> /help            # 'hello' apparaît
scripted> /hello           # → Hello, world!
scripted> /hello --who=Ada # → Hello, Ada!
scripted> /help-cmd hello  # signature avec les arguments typés
scripted> /exit
```

Dans le REPL scripté, seule une ligne qui commence par `/` est une commande. `@chemin`
référence un fichier ou un dossier (Tab complète les noms de commandes et les chemins), et tout
le reste est du texte libre — confié à la commande de repli (ci-dessous), ou refusé s'il n'y en
a pas. Le message qu'une commande rend via `ctx.continue(...)` est ce que le REPL affiche ;
`ctx.log` part vers le volet des logs, que la TUI masque au démarrage.

## Options CLI

| Option                            | Effet                                                                               |
|-----------------------------------|-------------------------------------------------------------------------------------|
| `--runner=scripted-commands`      | Démarre directement dans le REPL scripted-commands au lieu du menu principal (dont l'entrée `scripted` ouvre le même REPL). |
| `--commands-dir <chemin>`         | Ajoute un répertoire à scanner (répétable ; monté en lecture seule comme `/cli-commands`, puis `/cli-commands-1`, … dans le VFS). |
| `--no-script-commands`            | Désactive entièrement la découverte : aucun `*.cmd.ts` n'est chargé, seuls `help-cmd` et les commandes par défaut restent. |
| `--strict-commands`               | Équivalent à `FailFastOnInvalidScript=true + ContinueOnConflict=false` (CI).        |
| `--settings <chemin>`             | Chemin d'appsettings explicite (répétable — les fichiers suivants surchargent les précédents). |
| `--mount <phys:virt:droits>`      | Ajoute un montage VFS (répétable) — p. ex. pour rendre un dossier de commandes accessible. |
| `--crews-dir <chemin>`            | Ajoute un répertoire de résolution de crews (répétable ; monté en lecture seule comme `/crews`, `/crews-1`, …) — ce qui rend `script-host` / `runCrewAsync("nom")` résoluble. |

Chaque option à valeur accepte `--option valeur` et `--option=valeur`. `orkeon-repl` prend aussi
`--ui` et `--repl-wrap` ([référence du CLI](../reference/cli.md#orkeon-repl--la-console-interactive-séparée)).

## appsettings.json

Les mêmes options sous `Orkeon:Cli:ScriptCommands` :

```json
{
  "Orkeon": {
    "Cli": {
      "ScriptCommands": {
        "Enabled": true,
        "Directories": ["/cli-commands"],
        "FailFastOnInvalidScript": false,
        "EsbuildTranspile": true,
        "MaxScripts": 50,
        "ContinueOnConflict": true,
        "FallbackCommandName": "assistant",
        "Limits": {
          "MemoryLimitBytes": 67108864,
          "RecursionLimit": 100,
          "ExecutionTimeout": "00:05:00"
        }
      }
    }
  }
}
```

Les répertoires sont des **chemins virtuels** résolus via `IFileSystemService` —
configurez une entrée `Orkeon:FileSystem:Mounts` si le répertoire n'est pas déjà
monté. `FallbackCommandName` (défaut `assistant`) route toute ligne REPL **non**
préfixée de `/` vers cette commande scriptée — l'interrupteur qui fait du REPL un
agent conversationnel. Aucun exemple livré ne définit de commande `assistant` : sans elle,
le texte libre reçoit la réponse d'une commande inconnue.

## Référence `defineCommand`

```typescript
defineCommand({
  name: "deploy",                 // ^[a-z][a-z0-9-]*$, never help / ? / h
  aliases: ["d", "ship"],         // optional, must not duplicate `name`
  description: "Deploy a crew.",  // single line, ≤ 200 chars

  args: {
    target: { type: "string", required: true, choices: ["dev", "prod"] },
    crew:   { type: "string", required: true },
    dryRun: { type: "boolean", default: false },
  },

  async handler(args, ctx) {
    ctx.log.info(`Deploying ${args.crew} to ${args.target}`);
    return ctx.continue();
  },
});
```

`help`, `?` et `h` sont réservés. Un script peut délibérément prendre `exit`, `quit`, `q`,
`clear` ou `cls` : les commandes scriptées sont résolues avant les commandes par défaut, et le
loader journalise le masquage.

Quand `args` est omis, le handler reçoit `{ raw: string[] }` à la place — la
signature `(args, ctx) => ...` reste stable entre commandes typées et non typées.

### Types d'arguments supportés

| `type`      | Notes                                                                        |
|-------------|------------------------------------------------------------------------------|
| `"string"`  | `choices: readonly string[]` optionnel, `default` optionnel.                  |
| `"number"`  | `min`, `max`, `default` optionnels.                                          |
| `"boolean"` | Un `--flag` nu ⇒ `true`. `default` optionnel.                                |
| `"string[]"`| Glouton : en positionnel, consomme la fin de la ligne ; en forme drapeau, consomme jusqu'au prochain `--key`. |

Les erreurs de validation remontent sous la forme d'un `Error: ...` imprimé par le
runner ; le handler n'est **pas** invoqué.

## Référence `ctx` (le contexte d'exécution)

```typescript
interface CommandRuntimeContext {
  readonly command: { readonly name: string; readonly rawInput: string };

  readonly log: {
    debug(msg: string, data?: object): void;
    info(msg: string, data?: object): void;
    warn(msg: string, data?: object): void;
    error(msg: string, data?: object): void;
  };

  write(text: string): void;
  writeLine(text: string): void;
  clear(): void;

  prompt(spec: PromptSpec): Promise<string | boolean>;
  progress(spec: { total?: number; label: string }): ProgressHandle;
  table<T extends object>(rows: readonly T[], columns?: readonly (keyof T)[]): void;

  readonly signal: CommandSignal;       // CancellationToken — see "Cancellation"
  readonly services: ServiceLocator;    // whitelisted

  continue(message?: string): CommandActionResult;
  exit(farewell?: string): CommandActionResult;
}
```

`ctx.command.rawInput` contient pour l'instant le nom de la commande, pas la ligne tapée.
`ctx.progress` rend un handle doté de `advance(label?)`, `set(value, label?)` et
`done(label?)`. Une globale `console.log/info/debug/warn/error` renvoie vers `ctx.log`.

Le `.d.ts` complet est livré comme ressource embarquée dans
`Orkeon.Cli.Commands.Scripting.dll` ; rien ne l'écrit encore sur disque. Pour
l'autocomplétion IDE, copiez `src/cli/Orkeon.Cli.Commands.Scripting/Typings/orkeon-cli.d.ts`
en `.orkeon/orkeon-cli.d.ts` à côté de vos scripts — le chemin qu'attend la ligne
`/// <reference path=…>` des exemples. Un fichier de commandes est aussi un moteur complet du
DSL crew : `agentBuilder`, `crewBuilder`, `llm`, `tools.*` et `rag` y sont tous, et le `await`
de premier niveau fonctionne.

### Prompts

```typescript
const target = await ctx.prompt({ type: "select", message: "Target?", choices: ["dev","prod"] });
const ok     = await ctx.prompt({ type: "confirm", message: "Proceed?", default: false });
const name   = await ctx.prompt({ type: "text", message: "Name?" });
const pwd    = await ctx.prompt({ type: "password", message: "Password:" });
```

### Annulation

`ctx.signal` est le `CancellationToken` .NET brut exposé par Jint. Les propriétés
sont en **PascalCase** à cause de la réflexion :

```typescript
while (!ctx.signal.IsCancellationRequested) {
  // long-running work
}
ctx.signal.ThrowIfCancellationRequested();
```

L'alias TypeScript `CommandSignal` déclaré dans le `.d.ts` est une commodité de
documentation ; les membres au runtime sont en PascalCase.

### Services

```typescript
const fs = ctx.services.get<IFileSystemService>("fs");
const tools = ctx.services.get<IBaseTool[]>("tools");
if (ctx.services.has("llm")) { /* … */ }
```

La whitelist de l'hôte détermine ce qui est accessible. Clés par défaut : `fs` et
`tools`, plus en option `llm`, `logger`, `commands` (la façade de dispatch — voir
plus bas) et `script-host` (lancement de crews depuis une commande — voir
[Piloter des crews depuis le REPL](./coding-agent-ts.md)). `configuration` n'est
délibérément **pas** exposée — la racine de configuration porte des clés d'API — et
demander une clé hors de la whitelist lève une erreur. Les hôtes ajoutent les leurs en
passant une `Action<ScriptServiceWhitelist>` à `AddScriptCommands` (`Add(name, factory)`,
`AddOptional(name, factory)`).

## Dispatcher des commandes vers des agents

Une commande peut s'adresser à un **agent vivant par son nom** et laisser *sa*
réponse décider quand la commande est terminée. C'est la façade `commands`,
accessible via `ctx.services.get("commands")`. Sous le capot, elle s'appuie sur un
`IAgentChannel` qui lui est propre — un canal en mémoire que crée `AddScriptCommands` : la façade résout le nom d'agent → `AgentId`, corrèle
la requête/réponse côté hôte, et trace chaque dispatch dans un registre
interrogeable.

> **Règle clé :** la commande se termine quand l'**agent répond**, pas quand le
> handler retourne. Le routage est point-à-point — une commande cible exactement un
> agent, qui est son unique finisseur (pas de fan-out, pas de join).

### Côté agent — `onCommand`

Un agent déclare qu'il répond aux commandes dispatchées avec `onCommand` (dans le
DSL crew — voir [scripting.md](../architecture/scripting.md)). La valeur qu'il
retourne est la réponse qui termine la commande :

```typescript
const echo = agentBuilder()
  .name("echo").role("Echo").goal("Echo a payload back")
  .onCommand("run", (env) => env.payload.toUpperCase())     // answers intent "run"
  .onCommand((env) => ({ success: true, payload: "ack" }))  // catch-all (any intent)
  .build();
```

Le handler reçoit une **enveloppe** `{ intent, payload, from, correlationId }` et
retourne soit une chaîne de payload, soit `{ success?, payload?, error? }`.
L'agent doit être **activé** sur le bus de dispatch (`AgentCommandRegistrar`) pour
qu'une commande `.cmd.ts` puisse l'atteindre par son nom. Le JS du handler
s'exécute sous le verrou moteur de l'agent ; il est donc sûr même quand un
dispatch asynchrone d'arrière-plan l'invoque.

### Dispatch synchrone — `request`

Une `defineCommand` normale qui attend l'agent. `request` bloque jusqu'à ce que
l'agent réponde et retourne la `CommandResponse` (de sorte qu'un `await` dessus se
résout en la valeur — une commande sync est faite pour bloquer) :

```typescript
defineCommand({
  name: "ask",
  description: "Ask the 'echo' agent and wait.",
  args: { text: { type: "string", required: true } },
  handler(args, ctx) {
    const res = ctx.services.get("commands").request("echo", "run", args.text);
    return res.success ? ctx.continue("→ " + res.payload)
                       : ctx.continue("agent error: " + res.error);
  },
});
```

`request(agent, intent, payload)` retourne `{ agent, intent, success, payload, error? }`.

### Dispatch asynchrone — `defineAsyncCommand`

Utilisez `defineAsyncCommand` quand le travail doit se détacher : `dispatch` tire
et rend l'invite immédiatement ; le `completed` optionnel est rejoué plus tard
quand l'agent répond.

```typescript
defineAsyncCommand({
  name: "ask-bg",
  description: "Ask the 'echo' agent in the background.",
  maxConcurrent: 3,                                  // admission quota (see below)
  args: { text: { type: "string", required: true } },
  dispatch(args, ctx) {                              // does NOT block
    const ticket = ctx.services.get("commands").post("echo", "run", args.text);
    ctx.log.info("launched (ticket " + ticket + ")");
    return { ticket };
  },
  completed(result, ctx) {                           // run when the agent answers
    ctx.writeLine("✓ " + result.agent + ": " + result.payload);
  },
});
```

- `post(agent, intent, payload)` retourne immédiatement un **ticket** (string) et
  exécute la requête sur une tâche d'arrière-plan.
- `completed(result, ctx)` n'est **pas** appelé depuis le thread d'arrière-plan
  (Jint est mono-thread). Dès que le travail aboutit, l'hôte imprime une ligne
  concise (`✓ [t1] ask-bg → echo: done`) et exécute `completed` sur le moteur du
  script, sous son verrou — inutile de taper une autre commande.
- Pour une valeur à lire à la demande, sondez avec `result --ticket=…` (ci-dessous).

### Quota d'admission — `maxConcurrent`

Déclaré sur `defineAsyncCommand`, il borne le nombre d'**instances en vol de cette
commande**. Omis ⇒ non borné (∞). L'acquisition est non bloquante : quand le quota
est plein, une nouvelle invocation est **rejetée immédiatement** (le `dispatch` ne
s'exécute jamais) avec un message `Rejected: quota of N instance(s) of '<cmd>' reached.`
Le slot est libéré quand l'agent répond. `maxConcurrent` n'a d'effet réel que pour
les commandes async — une commande sync tient déjà le moteur et est sérialisée.

### Introspection des commandes en vol

Le registre de dispatch est exposé de deux manières. Depuis un script :

```typescript
const facade = ctx.services.get("commands");
facade.list({ state: "running" });   // CommandInstanceView[] ; aussi { name }, { agent }, ou "running"
facade.get("t3");                    // one view, or undefined
facade.cancel("t3");                 // request cancellation; returns boolean
```

Et sous forme de **commandes intégrées** au REPL (rapides, restent réactives
pendant que le travail async tourne en arrière-plan) :

| Commande                   | Effet                                                         |
|----------------------------|---------------------------------------------------------------|
| `ps [--state=…]`           | Liste les instances. État : `running` (défaut), `done`, `failed`, `cancelled`, `rejected`, `all`. |
| `inspect --ticket=<t>`     | Détail complet d'une instance (état, agent, durée écoulée, résultat, progression). Un ticket nu fonctionne aussi : `/inspect t3`. |
| `result --ticket=<t>`      | Imprime le payload résultat (sondage). Signale « still running » si pas terminé. |
| `cancel --ticket=<t>`      | Demande l'annulation d'un ticket en vol.                      |

Une `CommandInstanceView` porte : `ticket`, `name`, `kind` (`sync`/`async`),
`targetAgent`, `intent`, `correlationId`, `state`, `startedAt`, `completedAt?`,
`elapsedMs`, `tokens`, `result?`, `error?`, `progress?`. Les tickets s'écrivent `t1`,
`t2`, … ; les 200 entrées terminales les plus récentes sont conservées pour que
`result`/`inspect` puissent lire un ticket récent, puis évincées. Les quatre commandes
intégrées ne sont enregistrées que si au moins un `*.cmd.ts` a été trouvé.

### Câblage (côté hôte)

`AddScriptCommands` enregistre le substrat de dispatch comme singleton (son propre
canal in-memory — le bus de dispatch CLI — plus l'annuaire de noms et le registre
d'instances), ce qui fait se résoudre la clé optionnelle `commands`. Les agents sont
connectés avec
`AgentCommandRegistrar.Register(agent, engine, engineLock, service.Channel, service.Directory, logger?)`,
qui enregistre le handler de canal et le mapping nom→id. C'est l'hôte qui s'en charge :
`orkeon-repl` ne charge que des fichiers `*.cmd.ts` et n'active aucun agent, donc tant qu'un
hôte n'en enregistre pas, `request` et `post` lèvent
`commands: unknown agent 'echo'. Registered: (none).`

### Exemple de bout en bout

[`examples/cli-ts-commands/`](https://github.com/Orkeon/orkeon/blob/main/examples/cli-ts-commands/README.md)
fournit `dispatch.cmd.ts` (les commandes `ask` / `ask-bg`) et `echo-agent.ork.ts` (l'agent
`onCommand`). Avec l'agent enregistré par l'hôte :

```
scripted> /ask hello            # → HELLO        (sync, bloque)
scripted> /ask-bg hello         # rend la main ; puis  ✓ [t1] ask-bg → echo: done
scripted> /ps --state=all       # t1 ask-bg echo done …
scripted> /result --ticket=t1   # [t1] HELLO
```

## Construire un hôte REPL en C#

`orkeon-repl` est un hôte construit à partir de quatre projets ; un hôte à vous assemble les
mêmes pièces.

| Projet | Ce qu'il apporte à un hôte |
|---|---|
| `Orkeon.Cli.Abstractions` | Les contrats : `IInteractiveCommand` (`Name`, `Aliases`, `Description`, `ExecuteAsync(CommandContext, ct)` qui rend `CommandResult.Continue(...)` / `Exit(...)`), `IInteractiveCommandRegistry` (`Commands`, `Fallback` facultatif), `IConsoleAdapter` avec `SystemConsoleAdapter` et le `LineEditingConsoleAdapter` à édition de ligne, et `InteractiveRunnerBase` — la boucle elle-même. |
| `Orkeon.Cli` | `AddOrkeonCli()` : les trois commandes par défaut — `help` (`?`, `h`), `exit` (`quit`, `q`), `clear` (`cls`) — et le `DefaultCommandRegistry` qui les porte, en singletons. |
| `Orkeon.Cli.Commands.Scripting` | `AddScriptCommands(configuration?, configure?, configureWhitelist?)` : le loader des `*.cmd.ts` et son `ScriptCommandRegistry`, le substrat de dispatch (`commands`), `script-host` et `ScriptHost`. `AddLlmConsoleStreaming(configuration)` affiche dans la console la sortie streamée d'`act()` quand `Orkeon:Cli:ConsoleStreaming:Enabled` vaut `true`. |
| `Orkeon.Cli.TerminalGui` | `AddOrkeonCliTerminalGui(options)` : la console Terminal.Gui v2 à deux volets (ci-dessous). |

Un runner dérive d'`InteractiveRunnerBase`, dont le constructeur prend le registre par défaut,
le registre propre à l'hôte, l'adaptateur de console, un logger et le fournisseur de services ;
il fournit une `Banner` et un `Prompt`, et peut surcharger `SlashCommandsOnly` (seules les
lignes préfixées de `/` sont des commandes), `ResolveFallback()` (la commande qui reçoit tout le
reste), `OnStartAsync`, `OnExitAsync` et `OnUnknownCommandAsync`. Une ligne se résout d'abord
dans le registre de l'hôte, puis dans les commandes par défaut — d'où la possibilité, pour un
script, de masquer `exit` ou `clear`. Le registre qu'enregistre `AddScriptCommands` ne charge
rien quand il est résolu : appelez son `EnsureLoadedAsync` depuis `OnStartAsync`, comme le fait
le `ScriptedCommandsRunner` d'`orkeon-repl`.

```csharp
services.AddOrkeonCli();                                   // help / exit / clear + DefaultCommandRegistry
services.AddScriptCommands(configuration);                 // *.cmd.ts, commands, script-host
services.AddSingleton<IConsoleAdapter, SystemConsoleAdapter>();
services.AddSingleton<MyRunner>();                         // : InteractiveRunnerBase
services.AddOrkeonCliTerminalGui(new TerminalGuiOptions()); // en dernier : remplace l'adaptateur et les loggers

// puis
await using var tui = provider.GetRequiredService<TerminalGuiHost>();
await tui.RunAsync(provider.GetRequiredService<MyRunner>(), CancellationToken.None);
```

### La console à deux volets — `Orkeon.Cli.TerminalGui`

`AddOrkeonCliTerminalGui` remplace la console par un écran Terminal.Gui v2 : le REPL l'occupe, et
un tiroir de logs (`Ctrl+G`) reçoit les lignes de journal qui sinon s'entremêleraient avec
l'invite. Il enregistre `TerminalGuiHost`, remplace `IConsoleAdapter` par l'adaptateur posé sur
le volet REPL (la complétion Tab est branchée quand l'hôte enregistre un `IReplInputAssist`), et
remplace **tous** les `ILoggerProvider` par `TerminalGuiLoggerProvider` — un logger console
écrirait dans l'écran que Terminal.Gui possède. Le fournisseur est aussi publié pour tout le
processus (`AmbientLoggerProvider`), si bien qu'un hôte construit à l'intérieur d'une commande
journalise dans le même volet. Appelez-le après les autres enregistrements, et appelez
`ClearStdoutLoggersForTerminalGui()` dans la configuration des logs ; un second appel est ignoré.
`TerminalGuiHost.RunAsync(runner, ct)` exécute le REPL sur une tâche d'arrière-plan pendant que
Terminal.Gui possède le thread principal, et rend la main quand le REPL se termine ou que
l'utilisateur quitte.

`TerminalGuiOptions` (un record à propriétés `init` — passez une instance construite) :
`InitialSplitRatio` (0.5), `DefaultMinimumLogLevel` (`Information`), `LogsBufferCapacity`
(5000), `LogsPaneTitle` (`Logs`), `LogsVisibleAtStartup` (`false`), `ReplWordWrap` (`true`),
`BannerEnabled` (`true`), `Banner`, `Glyphs` (`Auto`), `SpinnerVerbs`. Les touches : `Ctrl+G`
tiroir de logs, `Ctrl+R` volet REPL, `Ctrl+L` / `Ctrl+K` vident les logs / le REPL, `Ctrl+F`
recherche dans les logs, `Ctrl+↑` / `Ctrl+↓` redimensionnent, `F2` / `Maj+F2` plus / moins de
détail dans les logs, `F3` retour à la ligne, `F4` volet des agents, `Ctrl+C` annule la commande
en cours (deux fois en deux secondes pour forcer la sortie), `Ctrl+Q` quitte. `orkeon-repl`
choisit cette console avec `--ui` ([référence du CLI](../reference/cli.md#orkeon-repl--la-console-interactive-séparée)).

## Matrice de support TypeScript (esbuild → Jint)

| Fonctionnalité                                  | Support |
|-------------------------------------------------|---------|
| `interface`, `type`, `enum` (non-const)         | ✅      |
| `import`/`export` (chemins relatifs)            | ✅      |
| `async`/`await`, `Promise`, `Promise.all`       | ✅      |
| Déstructuration, spread, valeurs par défaut, rest | ✅    |
| Classes, getters/setters, héritage              | ✅      |
| Littéraux de gabarit (template literals)        | ✅      |
| `Map`/`Set`/`WeakMap`/`WeakSet`                 | ✅      |
| `JSON.parse`/`JSON.stringify`                   | ✅      |
| Regex (sans lookbehind)                         | ✅      |
| Modules npm (`fs`, `path`, `node:*`)            | ❌ Utilisez `ctx.services.get("fs")`. |
| `fetch`, `setTimeout`, `setInterval`            | ❌/⚠️    |
| Décorateurs (`@experimental`)                   | ❌ esbuild s'arrête au stage-3. |
| Chemins/alias `tsconfig`                        | ⚠️ imports relatifs uniquement. |

## Contraintes (bonnes à connaître)

- **Découverte au démarrage uniquement.** Modifiez un script, redémarrez le
  runner. Pas de hot reload (délibéré — spec §14).
- **Un moteur par fichier de script, gardé chaud.** Toutes les commandes qu'un
  fichier déclare partagent l'état Jint de ce fichier d'une invocation à l'autre ;
  les commandes de fichiers différents sont isolées.
- **Jint n'est pas thread-safe.** Le runner sérialise les invocations ; un
  `SemaphoreSlim` protège chaque moteur défensivement.
- **Limites de sandbox (profil CLI)** : 64 Mo de mémoire, 100 de récursion max,
  5 min d'exécution. Surcharge via `Orkeon:Cli:ScriptCommands:Limits`.
- **Politique de conflit** : le premier script gagne, par ordre ordinal des
  chemins ; le second est journalisé en Warning. Basculez
  `ContinueOnConflict=false` pour échouer vite.
- **VFS uniquement.** Les scripts lisent le disque via `ctx.services.get("fs")` —
  pas de `System.IO` direct.

## Dépannage

| Symptôme                                 | Cause probable                                                               | Action                                                          |
|------------------------------------------|------------------------------------------------------------------------------|-----------------------------------------------------------------|
| `hello` n'apparaît pas dans `/help`      | Script hors des `Directories` configurés, ou erreur d'évaluation journalisée en Error | Vérifiez les logs de `Orkeon.Cli.Commands.Scripting.Loading.ScriptCommandLoader`. |
| `Esbuild binary not found. Tried (in order): …` | Aucun esbuild sur le chemin de recherche                               | Définissez `ORKEON_ESBUILD_PATH`, lancez `npm ci` dans `tools/scripting-esbuild/`, ou mettez `esbuild` dans le `PATH` ([ordre de recherche](./scripting.md#configuration-et-chaîne-doutils)). |
| `defineCommand is not defined`           | Script évalué avant les bindings (bug)                                        | Ouvrez une issue avec le chemin du script.                       |
| Le prompt ne s'affiche pas dans le panneau REPL | Adaptateur autre que Terminal.Gui ou heuristique de préfixe manquée    | Assurez-vous que le script écrit les prompts avec un suffixe `> `. |
| `Error: Argument '--target' value 'staging' is not in choices [dev, prod].` | Faute de frappe ou `choices` obsolètes  | Utilisez `/help-cmd <name>` pour voir la signature à jour.       |
| Une ligne n'exécute rien / `Unknown command` | Tapée sans le `/` initial, et pas de commande de repli                  | Préfixez les commandes par `/`, ou définissez la commande `FallbackCommandName`. |
