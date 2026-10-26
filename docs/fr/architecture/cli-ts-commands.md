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
| `--mount <phys:virt:droits>`      | Ajoute un montage VFS (répétable) — p. ex. pour rendre un dossier de commandes accessible. Placé et gardé comme pour `orkeon run` (GAP-49) : sur une racine que les settings déclarent, il remplace les entrées de cette racine pour la session, sur une racine neuve il s'ajoute ; un montage que le REPL ne peut honorer — `/sandbox`, `/credentials`, une racine qu'il monte lui-même (`/cli-commands…`, `/crews…` tant qu'un `--commands-dir` ou un `--crews-dir` les y met), une racine deux fois en ligne de commande, un dossier absent — est refusé avant que la console s'ouvre, code `1`, d'où qu'il ait été déclaré. Sans `--commands-dir`, `--mount <dossier>:/cli-commands:ro` atteint toujours un dossier de commandes. |
| `--allow-external-mounts`         | Un `--mount` hors du répertoire de travail est lisible (ou `ORKEON_ALLOW_EXTERNAL_MOUNTS=1`), comme pour `orkeon run` ; sans lui, le REPL refuse de démarrer (code de sortie `1`) avant l'ouverture de la console. Toute entrée des settings en vigueur (une entrée choisie par id comprise) et les dossiers de `--commands-dir` et `--crews-dir` — des entrées du REPL, comme le script d'`orkeon run` — sont lisibles où qu'ils soient, sans lui. |
| `--crews-dir <chemin>`            | Ajoute un répertoire de résolution de crews (répétable ; monté en lecture seule comme `/crews`, `/crews-1`, …) — ce qui rend `script-host` / `runCrewAsync("nom")` résoluble. |
| `--llm-log` / `--llm-log-path <rép>` | Le journal des échanges LLM d'`orkeon run` : JSONL sous `./llm-logs`, ou `<rép>`, qui implique `--llm-log` (le dernier `--llm-log-path` l'emporte). Un dossier hors du répertoire de travail demande `--allow-external-mounts` ; sans lui, le REPL refuse de démarrer (code de sortie `1`). |

Chaque option à valeur accepte `--option valeur` et `--option=valeur`. `--commands-dir`,
`--settings`, `--mount` et `--crews-dir` s'additionnent quand on les répète ;
`--allow-external-mounts` prend aussi `=true` ou `=false`. `orkeon-repl` prend aussi `--ui` et
`--repl-wrap` ([référence du CLI](../reference/cli.md#orkeon-repl--la-console-interactive-séparée)).

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
        "MaxScripts": 200,
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

`ctx.command.rawInput` est la ligne telle que tapée (`/deploy prod --force`) ; dans le
`completed` d'une commande asynchrone, c'est la ligne qui a lancé ce ticket, quelle que soit la
commande qui pompe ensuite la complétion. `ctx.progress` rend un handle doté de `advance(label?)`, `set(value, label?)` et
`done(label?)`. Une globale `console.log/info/debug/warn/error` renvoie vers `ctx.log`.

Le `.d.ts` complet est livré dans l'outil `orkeon` : `orkeon typings` l'écrit en
`.orkeon/orkeon-cli.d.ts` (à côté d'`orkeon.d.ts`) dans le répertoire courant — le chemin
qu'attend la ligne `/// <reference path=…>` des exemples ; `--out <dossier>` l'écrit ailleurs
([référence du CLI](../reference/cli.md#orkeon-typings)). Le garde-fou de typage du dépôt,
`scripts/check-scripting-typings.sh`, vérifie chaque `*.cmd.ts` livré contre lui. Un fichier de commandes est aussi un moteur complet du
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
plus bas), `script-host` (lancement de crews depuis une commande — voir
[Piloter des crews depuis le REPL](./coding-agent-ts.md)) et, dans `orkeon-repl`, `permissions`
(les règles d'appels d'outils de la session, plus bas) ; pour ces trois-là, les typings rendent
`CommandsFacade`, `ScriptHostFacade` et `PermissionRulesFacade` sans argument de type. `configuration` n'est
délibérément **pas** exposée — la racine de configuration porte des clés d'API — les typings
ne la déclarent donc pas, et demander une clé hors de la whitelist lève une erreur. Les hôtes ajoutent les leurs en
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
- **Chaînage depuis `completed`.** `completed` peut appeler `runCrewAsync(name, input)` :
  il retourne aussitôt un ticket, et la fin du second crew est drainée vers sa propre
  commande (le même nom de commande), dont le `completed` s'exécute à son tour. La
  chaîne est bornée par `MaxChainDepth` (8 par défaut, `ScriptCommandsConfiguration.MaxChainDepth`) ;
  la profondeur compte combien de sauts `completed` séparent une instance de son
  lancement. Un `runCrewAsync` plus profond est refusé par une `ChainDepthExceededException`,
  dont le message donne la profondeur et la limite.

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
(5000), `LogsPaneTitle` (`Logs`), `LogsVisibleAtStartup` (`false`), `ReplWordWrap` (`true`), `ReplTranscriptCapacity` (10000 lignes),
`BannerEnabled` (`true`), `Banner`, `Glyphs` (`Auto`), `SpinnerVerbs`. Les touches : `Ctrl+G`
tiroir de logs, `Ctrl+R` volet REPL, `Ctrl+L` / `Ctrl+K` vident les logs / le REPL, `Ctrl+F`
recherche dans les logs, `Ctrl+↑` / `Ctrl+↓` redimensionnent, `F2` / `Maj+F2` plus / moins de
détail dans les logs, `F3` retour à la ligne, `↓` (depuis la dernière ligne
du prompt) ou `F4` liste des tâches, `Ctrl+C` copie la sélection,
`Ctrl+V` et le clic droit collent dans la saisie, `Ctrl+Q` quitte (il demande d'abord confirmation
tant qu'un tour s'exécute). `orkeon-repl`
choisit cette console avec `--ui` ([référence du CLI](../reference/cli.md#orkeon-repl--la-console-interactive-séparée)).

**Touches sur le prompt.** `Échap` efface le brouillon ; un second `Échap` aussitôt après (toute
autre touche entre les deux désarme la paire, et elle s'arme aussi sur un brouillon vide)
interrompt le tour soumis depuis le prompt, qu'il s'exécute comme commande synchrone ou comme ticket
détaché, y compris pendant une attente de nouvelle tentative (`Reconnecting to …`). La barre d'aide
affiche `esc esc to interrupt` tant que ce tour s'exécute. Seul le tour du prompt est interrompu :
un crew lancé par autre chose (un `runCrewAsync` détaché, la compaction, l'indexation) continue, et
reste atteignable par `/ps` et `/cancel <ticket>` ; aucune touche ne l'annule. `Ctrl+C` n'annule
jamais et ne quitte jamais : avec une sélection il la copie (le transcript, ou la saisie, où un
glissement de souris sélectionne aussi) ; sans sélection il imprime une ligne d'aide
(`Ctrl+C copies a selection — esc esc interrupts the turn.`). `Ctrl+V` colle le presse-papiers dans
la saisie quelle que soit la vue qui a le focus, en mode runner comme en mode REPL, et s'efface
tant qu'une boîte de dialogue modale (recherche, confirmation de sortie) a le focus. Dans la
liste des tâches (`↓` ou `F4`), `Échap` revient au prompt et n'annule rien, un tour en cours ou non ;
il arme la paire comme un `Échap` sur le prompt (`F4` et `Tab` non), si bien que `Échap Échap` depuis la liste
interrompt un tour en cours. `Ctrl+Q` quitte aussitôt quand rien ne tourne, et demande confirmation tant qu'un
tour s'exécute, comme pour une commande synchrone.

**Prompt, historique et liste des tâches.** Le prompt lit une touche dans l'un de trois états, jamais
deux fois : le champ, l'historique et la liste des tâches. Dans le champ, `↑` sur la première ligne
(une ligne visuelle : une longue ligne se replie, et `↑`/`↓` en parcourent d'abord les lignes) entre
dans l'historique, garde le texte tapé comme brouillon et affiche l'entrée la plus récente, curseur à
la fin ; sans historique, il déplace le curseur comme d'habitude. `↓` sur la dernière ligne donne le
focus à la liste des tâches quand elle contient une tâche autre que `main` (la barre d'aide affiche
alors `↓ tasks`), et ne fait rien sinon. `Début` / `Fin` atteignent le début / la fin de la ligne,
`Ctrl+Début` / `Ctrl+Fin` ceux du texte. Dans l'historique, `↑` affiche l'entrée précédente (rien sur
la plus ancienne), `↓` la suivante, et depuis la plus récente rend le brouillon et quitte
l'historique ; toute modification le quitte et garde le texte ; `Entrée` envoie l'entrée affichée et
abandonne le brouillon. Les entrées vides et la répétition de la plus récente ne sont pas
enregistrées. Dans la liste (la barre d'aide affiche `↑/↓ select · enter view`), `↓` descend sans
boucler, `↑` remonte et, sur la première ligne, revient au champ ; `Échap` revient au champ depuis
n'importe quelle ligne, `F4` aussi, et le champ retrouve son texte et son curseur ; `Début` / `Fin`
atteignent la première / la dernière ligne ; `Entrée` imprime le détail de la tâche sélectionnée
dans le transcript et n'envoie rien ; une touche imprimable revient au champ avec ce caractère tapé.
`Tab` revient au champ comme `Échap` ; `Ctrl+V` ou un clic droit y revient et colle au curseur
restauré. L'état suit le focus : un clic sur une ligne donne le focus à la liste sur cette ligne (un
double-clic imprime aussi son détail), et le focus qui atteint le champ par un autre chemin quitte
la liste et garde le texte courant ; le curseur `❯` n'est jamais dessiné tant que les touches vont
au prompt.
La ligne sélectionnée est le destinataire du prochain message, et le reste de retour dans le champ :
le texte indicatif affiche `Message @<tâche>…` et le filet au-dessus de l'invite se termine par `── <tâche> ─` ;
sélectionner `main` rend le destinataire par défaut. La sélection suit sa tâche quand la liste se
retrie, et le champ reprend le focus, avec `main` pour destinataire, quand la liste se vide. Envoyer
à une tâche demande le `TuiIntegration.SendToTask` de l'hôte (`(ticket, texte) → bool`) ; tant
qu'aucun hôte ne le fournit, `Entrée` avec une tâche pour destinataire imprime que l'envoi à une
tâche n'est pas encore pris en charge, et le texte reste dans le champ, ni envoyé ni enregistré. Ces
touches s'effacent tant qu'une boîte de dialogue modale a le focus.

**La zone du bas.** Sous le transcript viennent la ligne du spinner (glyphe et verbe dans l'accent,
la parenthèse atténuée), un filet `─` atténué sur toute la largeur, terminé par `── <tâche> ─` dans
l'accent tant que le destinataire est une tâche autre que `main`, l'invite (`❯ ` accent et gras, le
texte indicatif atténué et en italique), un filet nu, la barre d'état, une ligne vide et la liste des
tâches. La barre d'état est indentée de deux colonnes et joint ses parties par un ` · ` atténué : le
mode de permission (`⏵⏵ bypass permissions on (shift+tab to cycle)`, en jaune pour
`bypassPermissions`, `dontAsk` et `acceptEdits`, atténué sinon), les aides, et `↓ tasks` tant
qu'une tâche autre que `main` existe ; depuis la liste elle affiche `↑/↓ select · enter view` seul.
La liste montre `main` d'abord, puis les tâches dans l'ordre de création : le `❯` de l'accent en
colonne 0 tant que la liste a le focus, `●` pour le destinataire et `◯` pour les autres (vert en
cours, rouge en échec ou annulée, atténué sinon), le nom en gras, la phrase d'activité coupée par
`…`, et alignés à droite et atténués `<durée> · ↓ <jetons> tokens`, le total de la tâche, mis à jour
par le sondage à 1 Hz. Les largeurs sont des colonnes de terminal mesurées sur les graphèmes (un
emoji ou un caractère CJK en compte deux) ; sur un volet étroit la phrase cède d'abord, puis le nom,
puis les métriques, et un redimensionnement reconstruit les lignes à la nouvelle largeur. Elle
grandit jusqu'à 6 lignes, puis défile avec le curseur. Entre deux sondages, une nouvelle tâche
imprime `● <nom>(<intention>)` puis `⎿  Backgrounded agent (↓ tasks)` dans le transcript, et une
tâche terminée `● Agent "<nom>" finished · <durée>`, `✗ Agent "<nom>" failed · <durée>` ou
`⊘ Agent "<nom>" cancelled · <durée>` : l'état final vient du `TuiIntegration.FinishedAgentRows` de
l'hôte (les tâches terminées qu'il conserve), si bien qu'une tâche qui démarre et finit entre deux
sondages reçoit quand même ses deux lignes, dans l'ordre ; sans lui, une tâche qui quitte la liste
se lit comme terminée. Un destinataire qui se termine pendant que le prompt a le focus imprime
`@<tâche> finished · recipient is main`, et un sondage dont le fournisseur de lignes lève une
exception n'écrit rien et laisse les lignes, la liste et le destinataire tels quels. `Échap Échap` imprime `⎿  Interrupted · What should Orkeon do instead?`. Les couleurs viennent de rôles sémantiques
(Normal, Dim, Accent, Ok, Alert, Error) sur le fond du terminal, en 24 bits quand `COLORTERM` vaut
`truecolor` ou `24bit` et en 16 couleurs nommées sinon. `--glyphs ascii` change
`❯ ● ◯ ✔ ✗ ⊘ ⏵ ⎿ ※ ─ ↑ ↓ …` en `> * o v x - > | * - ^ v ...`, les `↑/↓` de la barre d'aide et la
phrase coupée compris.

**Le témoin du modèle.** Le témoin aligné à droite de la barre d'aide nomme le modèle sélectionné
(24 colonnes au plus, un nom plus long finit par `…`) et se colore selon ce que la console a observé
des appels de ce modèle, jamais par une sonde : vert tant que le modèle est utilisable, une réponse
finale 2xx comprise ; ambre pendant une attente de nouvelle tentative de ce modèle ; rouge après un
non-2xx final, un délai dépassé, ou quand aucun modèle n'est configuré (l'étiquette dit alors
`no model`). Seuls les appels du modèle affiché le colorent, et un changement de modèle le remet au
vert. Un appel arrêté par l'appelant (`Échap Échap`) n'est pas un échec et laisse la couleur
inchangée.

**État de session partagé avec les commandes.** La console lit et écrit deux clés de l'état de
session (`ISessionBufferService`, qu'une commande atteint par `get_state` et `set_state` de
`session_store`) : `default_permission_mode` (`SessionStateKeys.DefaultPermissionMode`,
`Orkeon.Constants.Cli`), le mode de permission qu'elle affiche et fait tourner sur `Maj+Tab`
(`default` → `acceptEdits` → `plan` → `bypassPermissions`), et `selected_model`
(`SessionStateKeys.SelectedModel`), le modèle que nomme le témoin. La commande `/mode` d'un produit
écrit la première clé, et la console affiche son choix : les clés sont un contrat et gardent leur
nom. La commande `/model` d'Orkeon Code n'écrit pas encore `selected_model` ; d'ici là, le témoin
retombe sur le réglage `Llm:Model`.

**Approbations des appels d'outils.** Avec le contrôle des permissions activé, `orkeon-repl`
enregistre un canal d'approbation (`AddConsoleToolApproval`) : un appel pour lequel le mode demande
est soumis à l'opérateur, dont la ligne suivante y répond. Venu d'un crew lancé par `runCrewAsync`,
la boucle de lecture lui remet cette ligne (`IReplLineInterceptor`) avant de résoudre une commande ;
venu d'un crew lancé par `runCrew`, qui tient la boucle, l'appel en attente lit lui-même la réponse,
et le temps mis à répondre ne compte pas dans `RunCrewTimeout`. Une commande passe le mode de la
session à `act` dans `permissionMode` ; elle ne gère pas la question elle-même. Voir
[la référence du CLI](../reference/cli.md#orkeon-repl--la-console-interactive-séparée).

**Règles de session.** Le contrôle lit les règles d'autorisation et de refus de la session avant le
mode (`IToolPermissionRules`, tenues par le REPL). Une commande les pose par
`ctx.services.get("permissions")` : `allow(tool, argument?, pattern?)`, `deny(...)`, `clear()`,
`list()`. Une règle couvre tous les appels d'un outil, ou ceux dont l'argument correspond en entier
à un motif, `*` valant n'importe quelle suite de caractères. Une règle de refus refuse l'appel dans
tous les modes ; une règle d'autorisation le laisse passer sans demander dans tous les modes sauf
`plan`, qui reste en lecture seule : `dontAsk` lance donc ce qu'une règle autorise et refuse le
reste. Les règles durent jusqu'à la sortie du REPL. `enabled` (en lecture seule) dit si l'hôte a
enregistré le contrôle (`Orkeon:Security:PermissionGate:Enabled = true`) : à false, les règles et les
modes ne contrôlent rien et chaque appel d'outil passe, si bien qu'une commande qui affiche un mode
peut le dire. Un crew lit le même fait par `ctx.llm.permissionGate.enabled`.

```typescript
const permissions = ctx.services.get("permissions");
permissions.allow("shell_command", "command", "npm test*");
permissions.deny("shell_command", "command", "git push*");
```

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
| `Esbuild binary not found. Tried (in order): …` | Aucun esbuild sur le chemin de recherche                               | Définissez `Orkeon:Scripting:Toolchain:EsbuildPath` ou `ORKEON_ESBUILD_PATH`, lancez `npm ci` dans `tools/scripting-esbuild/`, ou mettez `esbuild` dans le `PATH` — `npm install -g esbuild` le fait ; l'outil dotnet ne le livre pas ([ordre de recherche](./scripting.md#configuration-et-chaîne-doutils)). |
| `defineCommand is not defined`           | Script évalué avant les bindings (bug)                                        | Ouvrez une issue avec le chemin du script.                       |
| Le prompt ne s'affiche pas dans le panneau REPL | Adaptateur autre que Terminal.Gui ou heuristique de préfixe manquée    | Assurez-vous que le script écrit les prompts avec un suffixe `> `. |
| `Error: Argument '--target' value 'staging' is not in choices [dev, prod].` | Faute de frappe ou `choices` obsolètes  | Utilisez `/help-cmd <name>` pour voir la signature à jour.       |
| Une ligne n'exécute rien / `Unknown command` | Tapée sans le `/` initial, et pas de commande de repli                  | Préfixez les commandes par `/`, ou définissez la commande `FallbackCommandName`. |
