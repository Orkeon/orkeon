> 🇬🇧 [English version](../../reference/hosting.md)

# Orkeon.Hosting — bootstrap des runners et des hôtes

`Orkeon.Hosting` est la couche de bootstrap partagée qui transforme les bibliothèques Orkeon en un hôte
exécutable. Elle possède l'ordre de câblage dont un runtime fonctionnel a besoin — fournisseur LLM,
services cœur, suites d'outils standard, système de fichiers virtuel et registre d'outils adossé à la
DI — plus les flux d'exécution de bout en bout (kickoff one-shot, boucle interactive, listing des
outils) utilisés par chaque runner et CLI Orkeon.

Elle est consommée par le harnais des runners et par les hôtes externes qui la référencent comme
paquet. Cette page documente sa surface de bootstrap publique.

## Paquet

| | |
|---|---|
| PackageId | `Orkeon.Hosting` |
| Dépend de | les bibliothèques cœur `Orkeon.*` (Domain, Application, Infrastructure, Analysis, Scripting) et huit des neuf suites `Orkeon.Tools.*` (`Orkeon.Tools.Rag` est volontairement absente — le RAG reste opt-in), plus `CommandLineParser` et `Microsoft.Extensions.Hosting` |
| Packagé par | `.github/workflows/publish.yml` (`dotnet pack Orkeon.sln`) — toute la solution est packagée sur un tag `v*`, `Orkeon.Hosting` est donc inclus automatiquement |

Les références de projets `Orkeon.*` deviennent des dépendances de paquet dans le nuspec ; la référence
à l'analyseur de conformité VFS est `PrivateAssets=all` et correctement exclue du paquet.

## `RunnerHost.Build`

`RunnerHost` est un builder d'hôte statique. `Build` retourne un `IHost` entièrement configuré :

```csharp
IHost host = RunnerHost.Build(
    settingsPath: "appsettings.json",   // chemin d'appsettings résolu (nullable)
    cliMounts: ["/data:/data:ro"],       // arguments --mount de la CLI (« physique:virtuel:droits »)
    allowExternalMounts: false,          // autoriser des bases de montage hors de la racine du workspace
    llmLogPath: null,                    // si renseigné, capture les échanges HTTP LLM en .jsonl
    configureLogging: null,              // personnalisation optionnelle de ILoggingBuilder
    configureServices: null,             // hook optionnel pour enregistrer les services du runner
    configureBuilder: null);             // hook IHostBuilder optionnel — orkeon-host s'en sert pour UseSystemd()/UseWindowsService()
```

Il compose `Host.CreateDefaultBuilder()` avec :

- **Configuration d'application** — résolution des appsettings plus les arguments de montage CLI
  repliés dans la configuration.
- **Services** — `ConfigureRunnerServices` (ci-dessous).

`RunnerHost` porte une exception bootstrap `[SuppressVfsCompliance]` : il résout des chemins de settings
fournis par l'utilisateur et provisionne les montages VFS *avant* que le conteneur DI (et donc
`IFileSystemService`) n'existe.

### Comportement de `ConfigureRunnerServices`

L'ordre d'enregistrement est délibéré :

1. **Logging** — le logging du runner (Console + Information par défaut) et, quand `llmLogPath` est
   renseigné, le `DelegatingHandler` de capture des échanges LLM.
2. **Le fournisseur LLM d'abord** — `RegisterLlmProvider` lit la section de config `Llm` et enregistre
   le fournisseur (et son `IChatClient`) **avant** `AddOrkeonApplication` / `AddOrkeonInfrastructure`.
   Cet ordre compte : l'infrastructure Orkeon enregistre ses fallbacks LLM/`IChatClient` en `TryAdd`,
   un fournisseur apporté par l'hôte doit donc être enregistré en premier pour gagner.
3. **Services cœur** — `AddOrkeonApplication()` puis `AddOrkeonInfrastructure()`.
4. **Outils stricts** — `CrewFactoryOptions.StrictTools` vaut `true` par défaut ici (un crew qui
   référence un outil inconnu échoue bruyamment avec `unknown tool(s): …; available: …`) ; opt-out via
   `"Orkeon:CrewFactory:StrictTools": false`. (Le défaut de la bibliothèque reste tolérant.)
5. **Permission gate** — `AddOrkeonPermissionGate(configuration)` (opt-in par config
   `Orkeon:Security:PermissionGate:Enabled` ; no-op sinon).
6. **Suites d'outils cœur** — système de fichiers, data, web, code, abstractions, outils de
   session ; puis l'EventHub en mémoire plus ses outils agents et l'ACL EventHub
   (`AddOrkeonEventHubAcl`, défaut permissif : une crew sans bloc `links:` se comporte comme avant).
7. **Montages VFS** — `AddOrkeonFileSystem` quand `Orkeon:FileSystem:Mounts` existe **et contient au
   moins une entrée** (un tableau vide n'enregistre rien).
8. **Suites d'outils tardives** — RaggableTree (outils de graphe sémantique, opt-out via
   `"RaggableTree:Enabled": false` ; pré-enregistre les embeddings locaux quand ils sont le provider
   choisi), les outils WebSearch et `cache_search`, et l'outil de recherche Brave quand
   `BRAVE_API_KEY` est présent.
9. **Registre d'outils** — `ServiceProviderToolRegistry` est enregistré comme `IToolRegistry` singleton.
10. **Services du runner** — le hook `configureServices` de l'appelant s'exécute en dernier.

## `ServiceProviderToolRegistry`

L'implémentation d'`IToolRegistry` qui résout les noms d'outils YAML/TS en instances `IBaseTool`
**depuis la DI**. Son constructeur prend `IEnumerable<IBaseTool>` — chaque outil enregistré par les
suites — et les indexe par nom (insensible à la casse). `CrewFactory` le consomme pour construire les
agents avec leurs outils déclarés, raison pour laquelle chaque suite enregistre sous `IBaseTool` : un
outil non enregistré ne peut pas être résolu (et, avec `StrictTools`, fait échouer le chargement du
crew au lieu d'être silencieusement ignoré).

## `RunnerExecution` — flux d'exécution

`RunnerExecution` est la glu d'exécution partagée : arrêt gracieux (SIGTERM/SIGINT), câblage de
l'`AutoSummaryWriter` quand un montage `/output:rw` est déclaré, presets de verbosité, et les flux
d'exécution. Tous les points d'entrée construisent l'hôte en interne (même bootstrap), résolvent
settings/montages et retournent un code de sortie processus.

| Point d'entrée | Rôle |
|---|---|
| `RunOneShotAsync(opts, loggerCategory, configureServices?, externalCt?)` | Exécute un kickoff de crew de bout en bout. Codes de sortie : **0** succès, **1** erreur de config, **2** échec du crew, **130** annulé. |
| `RunInteractiveLoopAsync(opts, loggerCategory, stopWords, kickoffPerInputAsync, onSessionStart, …)` | Boucle REPL ; chaque entrée déclenche un kickoff via le délégué fourni par l'appelant ; un mot d'arrêt termine la boucle (exit 0). |
| `RunListToolsAsync(opts, loggerCategory, configureServices?)` | Construit l'hôte sans crew et imprime sur stdout les noms d'outils runtime triés et dédupliqués (logs sur stderr) — le contrat d'outils runtime consommé par l'outillage de packaging/lint. |
| `RunValidateAsync(opts, loggerCategory, configureServices?)` | Le dry-run derrière `--validate` : construit l'hôte et charge la crew (résolution stricte des outils) sans sonder le LLM ni lancer de kickoff. |
| `LoadCrewAsync(host, opts)` | Charge et mappe la définition de crew depuis la cible résolue — la brique que les flux ci-dessus partagent. |

## Consommer depuis un hôte longue durée

Un service longue durée *peut* simplement envelopper `RunnerHost.Build` — c'est exactement ce que
fait le daemon `orkeon-host` (`Orkeon.Host/Program.cs`), en passant `configureBuilder` pour
`UseSystemd()`/`UseWindowsService()`. Un hôte qui possède déjà son `IHostBuilder` (une app ASP.NET,
par exemple) **réplique l'ordre d'enregistrement de `ConfigureRunnerServices`** dans son propre
`Program.cs` — il n'existe pas de raccourci packagé pour cela ; la console REPL inline la même
séquence à la main :

```csharp
// 1. Enregistrer le fournisseur LLM EN PREMIER (avant AddOrkeonInfrastructure, dont le fallback
//    TryAdd gagnerait sinon).
// 2. Services cœur :
services.AddOrkeonApplication();
services.AddOrkeonInfrastructure(configuration);
// 3. Suites d'outils (déterminent les outils utilisables par les crews) :
services.AddOrkeonFileSystemTools();
services.AddOrkeonDataTools();
services.AddOrkeonWebTools();
// … les autres suites AddOrkeon*Tools() …
// 4. Montages VFS depuis la configuration (l'hôte web provisionne au moins un montage) :
services.AddOrkeonFileSystem(configuration);
// 5. Le registre d'outils EN DERNIER, pour qu'il capture chaque IBaseTool enregistré :
services.AddSingleton<IToolRegistry, ServiceProviderToolRegistry>();
```

Parce qu'un hôte web exécute typiquement chaque crew dans son propre scope DI (les dépôts de crews
d'Orkeon sont scoped), `ServiceProviderToolRegistry` — un singleton sur l'ensemble des `IBaseTool`
enregistrés — est partagé entre les exécutions, tandis que `CrewFactory` et l'orchestrateur se
résolvent par scope.
