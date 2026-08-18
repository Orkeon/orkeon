> 🇬🇧 [English version](../../architecture/cli-ts-commands.md)

# Commandes CLI TypeScript

> Référence utilisateur du sous-système `Orkeon.Cli.Commands.Scripting`. Pour la
> spécification d'architecture et la justification de conception, voir
> the maintainers' design archive (feature `cli-ts-commands`, SPEC).

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
    ctx.log.info(`Hello, ${args.who}!`);
    return ctx.continue();
  },
});
EOF

dotnet run --project src/apps/Orkeon.ConsoleApp -- \
    --runner=scripted-commands \
    --commands-dir ./commands
```

À l'invite :

```
scripted> help              # 'hello' shows up
scripted> hello             # → Hello, world!
scripted> hello --who=Cyril # → Hello, Cyril!
scripted> help-cmd hello    # signature with typed args
scripted> exit
```

## Options CLI

| Option                            | Effet                                                                               |
|-----------------------------------|-------------------------------------------------------------------------------------|
| `--runner=scripted-commands`      | Démarre directement dans le REPL scripted-commands au lieu du menu principal.       |
| `--commands-dir <path>`           | Ajoute un répertoire à scanner (répétable ; monté comme `/cli-commands[-N]` dans le VFS). |
| `--no-script-commands`            | Désactive entièrement la découverte. Le registre est vide.                          |
| `--strict-commands`               | Équivalent à `FailFastOnInvalidScript=true + ContinueOnConflict=false` (CI).        |

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
monté.

## Référence `defineCommand`

```typescript
defineCommand({
  name: "deploy",                 // ^[a-z][a-z0-9-]*$, must not collide with help/exit/clear
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

Le `.d.ts` complet est livré comme ressource embarquée dans
`Orkeon.Cli.Commands.Scripting.dll` (la Phase 5 le publiera automatiquement sur disque ;
pour l'instant, copiez `src/cli/Orkeon.Cli.Commands.Scripting/Typings/orkeon-cli.d.ts` à
côté de vos scripts pour l'autocomplétion IDE).

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
const cfg = ctx.services.get<IConfiguration>("configuration");
const tools = ctx.services.get<IBaseTool[]>("tools");
```

La whitelist de l'hôte détermine ce qui est accessible. Clés par défaut : `fs`,
`configuration`, `tools`, plus en option `llm`, `logger` et `commands` (la façade
de dispatch — voir plus bas). Les hôtes ajoutent les leurs en passant une
`Action<ScriptServiceWhitelist>` à `AddScriptCommands`.

## Dispatcher des commandes vers des agents

Une commande peut s'adresser à un **agent vivant par son nom** et laisser *sa*
réponse décider quand la commande est terminée. C'est la façade `commands`,
accessible via `ctx.services.get("commands")`. Sous le capot, elle s'appuie sur
l'`IAgentChannel` existant : la façade résout le nom d'agent → `AgentId`, corrèle
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
  completed(result, ctx) {                           // replayed at the next pump
    ctx.writeLine("✓ " + result.agent + ": " + result.payload);
  },
});
```

- `post(agent, intent, payload)` retourne immédiatement un **ticket** (string) et
  exécute la requête sur une tâche d'arrière-plan.
- `completed(result, ctx)` n'est **pas** appelé depuis le thread d'arrière-plan
  (Jint est mono-thread). Il est rejoué sur le thread moteur au prochain « pump » —
  typiquement la prochaine invocation de commande sur le même script. Une ligne
  hôte concise est aussi imprimée immédiatement à la complétion, pour que vous
  voyiez quelque chose sans attendre.
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
facade.list({ state: "running" });   // CommandInstanceView[]
facade.get("t3");                    // one view, or undefined
facade.cancel("t3");                 // request cancellation; returns boolean
```

Et sous forme de **commandes intégrées** au REPL (rapides, restent réactives
pendant que le travail async tourne en arrière-plan) :

| Commande                   | Effet                                                         |
|----------------------------|---------------------------------------------------------------|
| `ps [--state=…]`           | Liste les instances. État : `running` (défaut), `done`, `failed`, `cancelled`, `rejected`, `all`. |
| `inspect --ticket=<t>`     | Détail complet d'une instance (état, agent, durée écoulée, résultat, progression). |
| `result --ticket=<t>`      | Imprime le payload résultat (sondage). Signale « still running » si pas terminé. |
| `cancel --ticket=<t>`      | Demande l'annulation d'un ticket en vol.                      |

Une `CommandInstanceView` porte : `ticket`, `name`, `kind` (`sync`/`async`),
`targetAgent`, `intent`, `correlationId`, `state`, `startedAt`, `completedAt?`,
`elapsedMs`, `result?`, `error?`, `progress?`. Les entrées terminales sont
conservées un moment (borné) pour que `result`/`inspect` puissent lire un ticket
récent, puis évincées.

### Câblage (côté hôte)

`AddScriptCommands` enregistre le substrat de dispatch comme singleton (son propre
canal in-memory — le bus de dispatch CLI — plus l'annuaire de noms et le registre
d'instances) et ajoute `commands` à la whitelist par défaut. Les agents sont
connectés avec
`AgentCommandRegistrar.Register(agent, engine, engineLock, service.Channel, service.Directory)`,
qui enregistre le handler de canal et le mapping nom→id. Voir
the maintainers' design archive (feature `cli-ts-commands`, COMMAND-DISPATCH-DESIGN §11) pour la
cartographie complète des fichiers.

### Exemple de bout en bout

`examples/cli-ts-commands/` fournit `dispatch.cmd.ts` (les commandes `ask` /
`ask-bg`) et `echo-agent.ork.ts` (l'agent `onCommand`).

```
scripted> ask hello            # → HELLO        (sync, blocks)
scripted> ask-bg hello         # launched (ticket t1)   (async, returns now)
scripted> ps                   # t1 askbg echo running …
scripted> result --ticket=t1   # [t1] HELLO
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
- **Un moteur par script, gardé chaud.** Les invocations d'une même commande
  partagent l'état Jint ; les invocations entre commandes différentes sont isolées.
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
| `hello` n'apparaît pas dans `help`       | Script hors des `Directories` configurés, ou erreur d'évaluation journalisée en Error | Vérifiez les logs de `Orkeon.Cli.Commands.Scripting.ScriptCommandLoader`. |
| `esbuild not found` au démarrage         | Outil non installé                                                            | `npm i -g esbuild`, ou copie dans `tools/scripting-esbuild/`.   |
| `defineCommand is not defined`           | Script évalué avant les bindings (bug)                                        | Ouvrez une issue avec le chemin du script.                       |
| Le prompt ne s'affiche pas dans le panneau REPL | Adaptateur autre que Terminal.Gui ou heuristique de préfixe manquée    | Assurez-vous que le script écrit les prompts avec un suffixe `> `. |
| `Error: --target value 'staging' is not in choices [dev, prod]` | Faute de frappe ou `choices` obsolètes                 | Utilisez `help-cmd <name>` pour voir la signature à jour.        |
