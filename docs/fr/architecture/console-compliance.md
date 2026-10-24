> 🇬🇧 [English version](../../architecture/console-compliance.md)

# Conformité console

Le code du framework ne touche jamais `System.Console`. La console du processus est un état global :
une écriture dessus ne peut pas être capturée par un test qui tourne à côté d'un autre, un échange
`Console.SetOut` entre en course avec tous les autres tests du processus, et sous `orkeon mcp serve`
une ligne parasite sur stdout corrompt le canal JSON-RPC. Les sorties passent par un `ILogger`
injecté ; les lectures et les écritures interactives par un `TextWriter`/`TextReader` injecté ou une
abstraction de console que le point d'entrée de l'exécutable construit et transmet.

## L'analyseur : `ORKCON001`

`ConsoleUsageAnalyzer` est livré dans `src/analyzers/Orkeon.Compliance.Vfs/`, à côté des [règles VFS](./vfs-compliance.md),
et s'applique à tous les projets de `src/`. Il signale toute référence à un membre de
`System.Console` (appel, propriété, événement, groupe de méthodes), jugée sur le **symbole résolu** :
`using static System.Console; WriteLine(...)` est signalé, une propriété nommée `Console` de type
`IConsoleAdapter` ne l'est pas. Un texte dans une chaîne littérale (le programme que
`ProcessIsolationSandbox` écrit pour un processus enfant) n'est pas une référence et n'est pas signalé.

`.editorconfig` la place en `error` sous `[src/**.cs]`. Les tests (`/tests/`) et les exemples
(`/examples/`) en sont exemptés.

## Exemption : `[SuppressConsoleCompliance]`

L'attribut est public dans `Orkeon.Domain` (espace de noms `Orkeon.Compliance.Vfs`) ; un projet qui
ne référence pas `Orkeon.Domain` en déclare une copie interne, reconnue par son nom. Sa raison commence
par l'une de deux catégories, vérifiées par `SuppressionReasonCategoryTests` :

| Catégorie | Sens | Où |
|---|---|---|
| `CONSOLE-ADAPTER` | Une implémentation d'une abstraction de console sur la console du processus | `SystemConsoleAdapter`, `LineEditingConsoleAdapter`, `SystemLineEditorConsole` (`Orkeon.Cli.Abstractions`), `SystemRunnerConsole` (`Orkeon.Hosting`), `SystemTerminalProbe` (`Orkeon.Cli.TerminalGui`) |
| `CONSOLE-SINK` | L'unique point d'un exécutable qui transmet les vrais flux | `orkeon` (`Program.UseUtf8Console`), `orkeon-repl` (`Program.Main`), `orkeon-host` (`HostConsole`, `StartupFailureReporting.cs`), `orkeon-studio-config` et `orkeon-studio-run` (`Program.Main`), Orkeon Studio (`App()`) |

Rien d'autre ne le porte : un nouvel exécutable reçoit un puits, une nouvelle abstraction de console
un adaptateur.

## D'où viennent les flux

| Couche | Abstraction | Implémentation système |
|---|---|---|
| Commandes du REPL (`Orkeon.Cli.Abstractions`) | `IConsoleAdapter` | `SystemConsoleAdapter`, `LineEditingConsoleAdapter` |
| Runners et CLI `orkeon` (`Orkeon.Hosting`) | `IRunnerConsole` : `Output`, `ErrorOutput`, `Input`, `IsInputRedirected`, `OnInterrupt(handler)` | `SystemRunnerConsole.Instance`, porté par `RunnerOptionsBase.RunnerConsole` |
| Interface terminal (`Orkeon.Cli.TerminalGui`) | `ITerminalProbe` : sondes de redirection, `OutputIsUtf8`, `TreatControlCAsInput`, `Output`, `Diagnostics` | `SystemTerminalProbe.Instance`, porté par `TerminalGuiOptions.Terminal` |
| Adaptateurs de bibliothèque | paramètres `TextReader`/`TextWriter` | `ConsoleHumanInputProvider(logger, input, output)`, `McpServer.RunStdioAsync(reader, writer, ct)` |

`IRunnerConsole.OnInterrupt` remplace les gestionnaires `Console.CancelKeyPress` que chaque verbe
enregistrait ; l'enregistrement est libéré à la fin du verbe.

## Avertissements opérateur : la catégorie `Orkeon.Operator`

Les avertissements de démarrage de l'hôte des runners (jetons indisponibles, LLM non configuré,
référence de clé d'API non résolue, serveur MCP indisponible) étaient écrits à la fois dans le logger
et sur `Console.Error`, parce qu'un hôte peut garder son logger sous `Error`. Ils passent désormais
par la seule catégorie de logger `Orkeon.Operator`, servie par un fournisseur dédié dont le niveau
minimal est forcé à `Warning` quel que soit `-v`, et qui écrit une ligne sur
`IRunnerConsole.ErrorOutput`. `RunnerHost` l'ajoute pour le CLI `orkeon` et `orkeon-host`, après la
configuration de journalisation de l'appelant (donc après un `ClearProviders`) ; `orkeon-repl`
l'ajoute dans `ReplStartup`.
