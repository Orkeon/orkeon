> 🇬🇧 [English version](../../reference/hosting.md)

# Orkeon.Hosting — bootstrap des runners et des hôtes

`Orkeon.Hosting` est la couche de bootstrap partagée qui transforme les bibliothèques Orkeon en un hôte
exécutable. Elle possède l'ordre de câblage dont un runtime fonctionnel a besoin — fournisseur LLM,
services cœur, suites d'outils standard, système de fichiers virtuel et registre d'outils adossé à la
DI — plus les flux d'exécution de bout en bout (kickoff one-shot, boucle interactive, listing des
outils) utilisés par chaque runner et CLI Orkeon.

Dans ce dépôt, elle est consommée par `Orkeon.Scripting.Cli` (l'outil `orkeon`) et par
`Orkeon.Host` (le démon `orkeon-host`). Elle n'est **pas** distribuée comme paquet NuGet — voir
[Distribution](#distribution) ci-dessous. Cette page documente sa surface de bootstrap publique, la
[télémétrie](#télémétrie) qu'exporte un hôte bâti dessus, et l'intégration
.NET Aspire qui lance ses exécutables.

## Distribution

**`Orkeon.Hosting` n'est pas un paquet NuGet.** Son csproj pose `IsPackable=false`, et la
[matrice de publication](publication-matrix.md#paquets-abandonnés) la classe dans les *paquets
abandonnés* : elle n'est poussée ni sur NuGet.org ni sur GitHub Packages, donc
`dotnet add package Orkeon.Hosting` ne peut pas se résoudre (`NU1101`).

Elle n'est pas non plus embarquée dans le paquet parapluie `Orkeon`.
`src/packaging/Orkeon/Orkeon.csproj` embarque douze assemblies — `Orkeon.Domain`,
`Orkeon.Application`, `Orkeon.Infrastructure`, `Orkeon.Constants.{Llm,FileSystem,Configuration,Protocol}`,
`Orkeon.Tools.Abstractions`, `Orkeon.Analysis{,.Abstractions}`, `Orkeon.Rag{,.Abstractions}` — et
`Orkeon.Hosting` n'en fait pas partie.

| | |
|---|---|
| Assembly | `Orkeon.Hosting.dll` (`src/hosting/Orkeon.Hosting`) |
| Packageable | non — `IsPackable=false`, sur aucun flux |
| Dépend de | les bibliothèques cœur `Orkeon.*` (Domain, Application, Infrastructure, Analysis, Scripting), les satellites `Orkeon.Constants.{Cli,Configuration,FileSystem}`, et tous les projets `Orkeon.Tools.*` sauf un — Abstractions, Analysis, Code, Data, Email, Embeddings.Local, EventHub, FileSystem, Web ; `Orkeon.Tools.Rag` est volontairement absent, le RAG reste opt-in — plus `CommandLineParser` et `Microsoft.Extensions.Hosting` |
| Livrée par | les **canaux CLI et installeurs** uniquement : l'outil dotnet `orkeon` (`Orkeon.Scripting.Cli`) et les archives d'installation / `.deb` / MSI produits par `release.yml`, où `Orkeon.Hosting.dll` est posée à côté de `orkeon` et `orkeon-host` comme assembly d'implémentation privée — jamais comme une référence qu'un consommateur ajoute |

**Construire un hôte externe contre elle** passe donc par les sources : cloner le dépôt et ajouter
une `ProjectReference` vers `src/hosting/Orkeon.Hosting/Orkeon.Hosting.csproj`. La surface de
paquets supportée pour les consommateurs est le parapluie `Orkeon` (plus `Orkeon.Tools` et les
opt-ins) ; `Orkeon.Hosting` est une couche de bootstrap interne, documentée ici pour les
appelants in-tree.

## `RunnerHost.Build`

`RunnerHost` est un builder d'hôte statique. `Build` retourne un `IHost` entièrement configuré :

```csharp
IHost host = RunnerHost.Build(
    settingsPath: "appsettings.json",   // chemin d'appsettings résolu (nullable)
    mounts: new RunnerMountPlan          // toute la surface VFS, en un seul objet
    {
        CliMounts = ["/data:/data:ro"],   // arguments --mount de la CLI (« physique:virtuel:droits »)
        InternalMounts = [],              // montages enregistrés en MountVisibility.Internal — résolubles
                                          // par le VFS, jamais listés à un agent (ADR-008)
        AllowExternalMounts = false,      // autoriser des bases de montage hors de la racine du workspace
        SelectedMountIds = [],            // valeurs --mount-id, parsées : les entrées des settings gardées
                                          // quand plusieurs déclarent une même racine virtuelle (VFS-90)
        CrewMountReferences = [],         // le bloc mounts: de la crew — sélectionne et valide, ne restreint jamais
        LlmLogVirtualPath = null,         // si renseigné, un répertoire VIRTUEL que l'appelant a monté :
                                          // les échanges HTTP LLM y sont capturés en .jsonl
    },
    configureLogging: null,              // personnalisation optionnelle de ILoggingBuilder
    configureServices: null,             // hook optionnel pour enregistrer les services du runner
    configureBuilder: null);             // hook IHostBuilder optionnel — orkeon-host s'en sert pour UseSystemd()/UseWindowsService()
```

Un chemin virtuel est toujours un nom commençant par `/` — jamais un chemin disque
([ADR-008](../adr/ADR-008-virtual-paths-are-the-only-currency.md)).
`RunnerVirtualRoots` — dans le paquet sans dépendance `Orkeon.Constants.FileSystem`
([ADR-009](../adr/ADR-009-shared-constants-satellites.md)), pour que le moteur et l'outillage
lisent une seule déclaration — nomme les racines que les runners livrés se réservent : `/crew`
(le dossier de définition du crew), `/script` (le dossier d'un point d'entrée scripté),
`/llm-logs`, `/sandbox` (où les bacs à sable de code déposent ce qu'ils exécutent) et
`/credentials` (les jetons OAuth des comptes e-mail, refusée à un montage utilisateur par toutes
les commandes).
`RunnerVirtualRoots.All` est l'ensemble contre lequel un appelant refuse un `--mount` utilisateur ;
demander l'ensemble plutôt que comparer les racines une à une est délibéré, car l'oubli de
`/sandbox` a survécu à une comparaison deux à deux tant qu'elle restait verte. Un appelant qui
active la journalisation des échanges monte son répertoire de logs en interne et passe ici
`RunnerVirtualRoots.LlmLogs` — c'est ce que fait la CLI.

`LoadCrewAsync` prend de même sa cible en chemin **virtuel** : elle demande au VFS si la cible est
un répertoire au lieu de sonder le disque, donc un chemin physique qu'on lui passe est refusé.

Il compose `Host.CreateDefaultBuilder()` avec :

- **Configuration d'application** — les sources du builder par défaut retirées, celles de chaque
  hôte Orkeon (`RunnerSettings.ComposeSources`, que composent aussi
  `RunnerSettings.ReadConfiguration` — `orkeon doctor`, la sonde d'`orkeon init` — et le REPL) : les
  variables d'environnement sans préfixe, le fichier `settingsPath` s'il existe, puis
  l'environnement préfixé `ORKEON_` (`ORKEON_Llm__Model` remplace `Llm:Model`). Ni
  l'`appsettings.json` et l'`appsettings.{Environment}.json` de la racine de contenu — le
  répertoire courant —, ni les secrets utilisateur (GAP-36). Puis une couche en mémoire qui
  porte les décisions de montage : les valeurs `--mount` placées par racine virtuelle, les
  montages internes, le montage `/credentials` des jetons OAuth e-mail, et les entrées
  `PathSecurity:AdditionalAllowedDirectories` qui laissent le validateur de chemins atteindre les
  dossiers de ces montages (un `--mount` hors du dossier de travail seulement avec
  `AllowExternalMounts`).
- **Services** — `ConfigureRunnerServices` (ci-dessous).
- **`configureBuilder`** — invoqué en dernier, sur l'`IHostBuilder` lui-même.

Une fois construit, l'hôte juge d'abord ses réglages (GAP-40), que le run s'en serve ou non : chaque
section qu'une inscription a déclarée — ses options créées, donc converties par le lieur et soumises à
leurs règles, les noms qu'elles portent compris —, les noms de section sous `Orkeon:` et ses groupes,
les clés de chaque section déclarée, et `Orkeon:Rag:LlmProfile` contre les profils qu'il offre
([quand un réglage est refusé](./configuration.md#quand-un-réglage-est-refusé)). Seules les options et
les fabriques nommées sont créées : aucun magasin, fournisseur, modèle ni connexion. Le premier refus
est une `RunnerSettingsException` qui nomme sa clé, l'hôte libéré : rien n'est journalisé, averti ni
démarré.

Ensuite l'hôte journalise les décisions de montage qu'il a prises, avertit (dans le
journal et sur stderr) quand un compte e-mail OAuth n'a pas de magasin de jetons ou quand il n'y a
pas de section `Llm` — le runtime se replie alors sur le fournisseur écho —, dit d'où vient la clé
du défaut et de chaque profil offert aux crews — les profils que cache sa liste blanche
(`LlmProfileAccessOptions`, que lie `orkeon-host`) sur une ligne à part, jamais signalés — et résout les
fournisseurs de traces et de métriques OpenTelemetry, parce que les runners ne *démarrent* jamais
l'hôte et que ces fournisseurs n'existeraient sinon jamais (voir [Télémétrie](#télémétrie)).

`RunnerHost` porte une exception bootstrap `[SuppressVfsCompliance]` : il résout des chemins de settings
fournis par l'utilisateur et provisionne les montages VFS *avant* que le conteneur DI (et donc
`IFileSystemService`) n'existe.

### Comportement de `ConfigureRunnerServices`

L'ordre d'enregistrement est délibéré :

1. **Logging** — le logging du runner (console sur une ligne au niveau **Warning** par défaut ; `--verbose 1`/`2` ou un callback `configureLogging` le relève) et, quand
   `RunnerMountPlan.LlmLogVirtualPath` est renseigné, le `DelegatingHandler` de capture des échanges LLM.
2. **Le fournisseur LLM d'abord** — `RegisterLlmProvider` lit la section de config `Llm` et enregistre
   le fournisseur (et son `IChatClient`, sur la configuration de cette section) **avant**
   `AddOrkeonApplication` / `AddOrkeonInfrastructure` — ou le fournisseur écho quand la section manque.
   L'infrastructure n'enregistre aucun modèle à elle : un hôte qui n'en enregistre aucun échoue à sa
   première résolution LLM, en nommant le service manquant (GAP-29).
3. **Services cœur** — `AddOrkeonApplication()` puis `AddOrkeonInfrastructure()` (la surcharge
   sans paramètre), puis `AddOrkeonTelemetry(configuration)` pour la section `Telemetry`.
4. **Outils stricts** — `CrewFactoryOptions.StrictTools` vaut `true` par défaut ici (un crew qui
   référence un outil inconnu échoue bruyamment avec `unknown tool(s): …; available: …`) ; opt-out via
   `"Orkeon:CrewFactory:StrictTools": false`. (Le défaut de la bibliothèque reste tolérant.)
5. **Permission gate** — `AddOrkeonPermissionGate(configuration)` (opt-in par config
   `Orkeon:Security:PermissionGate:Enabled` ; no-op sinon).
6. **Suites d'outils cœur** — système de fichiers, data, web, code, abstractions, outils de
   session ; puis l'EventHub en mémoire plus ses outils agents et l'ACL EventHub
   (`AddOrkeonEventHubAcl`, défaut permissif : une crew sans bloc `links:` se comporte comme avant) ;
   puis les outils e-mail (`AddOrkeonEmailTools(configuration)`, inertes tant qu'aucun compte n'est
   déclaré) et, quand un compte e-mail OAuth est déclaré, leur magasin de jetons sur la racine
   interne `/credentials` — montée par l'étape de configuration, atteinte par
   `PrivilegedFileSystemAccess`.
7. **Montages VFS** — `AddOrkeonFileSystem` quand `Orkeon:FileSystem:Mounts` **ou**
   `Orkeon:FileSystem:InternalMounts` existe **et contient au moins une entrée** (deux tableaux
   vides n'enregistrent rien). L'une ou l'autre liste suffit à rendre le VFS réel : `--list-tools`
   n'a que la seconde. Plusieurs entrées de `Mounts` peuvent déclarer une même racine si chacune
   porte un identifiant (VFS-90) : `MountSelection.Resolve` décide, pendant la composition de la
   configuration, laquelle ce run garde — un `--mount` sur la racine, sinon `SelectedMountIds`,
   sinon `CrewMountReferences` — et écrit les autres à `null` à leur propre index ; une sélection
   que rien ne résout lève le texte même que les gardes des runners impriment, si bien qu'un
   host construit sans elles refuse de la même façon.
8. **Suites d'outils tardives** — RaggableTree (outils de graphe sémantique, opt-out via
   `"RaggableTree:Enabled": false` ; pré-enregistre les embeddings locaux quand ils sont le provider
   choisi), les outils WebSearch et `cache_search`, et l'outil de recherche Brave quand
   `BRAVE_API_KEY` est présent (clé de configuration ou variable d'environnement).
9. **MCP** — `AddOrkeonMcp(configuration)` quand la section `MCP` déclare au moins un serveur
   sous `MCP:Servers` et que `MCP:Enabled` n'est pas `false`. Enregistrer n'est pas connecter :
   les serveurs sont connectés par les flux ci-dessous, avant le chargement de la crew
   (voir [MCP](../architecture/mcp.md#activation)). Une section qui porte encore la clé
   supprimée `MCP:EnableServer` fait échouer la construction de l'hôte, serveurs déclarés ou non
   (GAP-24).
10. **Registre d'outils** — rien de propre : `AddOrkeonInfrastructure()` a déjà enregistré le `ToolRegistry`
    par défaut, qui lit chaque `IBaseTool` enregistré par les étapes ci-dessus à sa première résolution.
11. **Services du runner** — le hook `configureServices` de l'appelant s'exécute en dernier.

`semantic_search` n'est pas dans cette liste : il est enregistré par `AddSemanticSearchTool()`,
qu'`orkeon run` appelle par son hook `configureServices` et qu'`orkeon-host` n'appelle pas.

## Le registre d'outils (`ToolRegistry`)

L'`IToolRegistry` par défaut (`Orkeon.Infrastructure.Tools`, enregistré par `AddOrkeonInfrastructure()`,
livré dans le paquet `Orkeon`) résout les noms d'outils YAML/TS en instances `IBaseTool` **depuis la
DI**. Son constructeur prend `IEnumerable<IBaseTool>` — chaque outil enregistré par les suites — et les
indexe par nom (insensible à la casse) ; deux outils enregistrés sous un même nom font lever le
constructeur, qui nomme les deux types. `CrewFactory` le consomme pour construire les agents avec leurs
outils déclarés, raison pour laquelle chaque suite enregistre sous `IBaseTool` : un outil non enregistré
ne peut pas être résolu (et, avec `StrictTools`, fait échouer le chargement du crew au lieu d'être
silencieusement ignoré). `RegisterToolAsync` ajoute un outil à l'exécution — c'est ce que fait le
client MCP — et **refuse** (rend `false`) un nom qu'un autre outil tient déjà. Lectures et
enregistrements à l'exécution peuvent s'entrelacer sans risque : `orkeon-host` mène plusieurs crews
pendant que ses serveurs MCP se connectent. Le registre n'indexe que des noms — aucune recherche par
étiquette ni par capacité (GAP-11).

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
| `RunValidateAsync(opts, loggerCategory, configureServices?, externalCt?)` | Le dry-run derrière `--validate` : construit l'hôte et charge la crew (résolution stricte des outils) sans sonder le LLM ni lancer de kickoff. |
| `LoadCrewAsync(host, factory, configPath, logger, ct, targetIsDirectory?)` | Charge et mappe la définition de crew depuis son chemin **virtuel** (fichier YAML, dossier de crew, script `.ork.ts`/`.ork.js`) — la brique que les flux ci-dessus partagent, et ce qu'`orkeon-host` appelle à chaque exécution. Elle ne connecte pas les serveurs MCP. |

`RunOneShotAsync`, `RunValidateAsync` et `RunListToolsAsync` connectent les serveurs MCP configurés
avant de charger ou de lister quoi que ce soit, pour que les trois voient la même surface d'outils.
Les gardes que la CLI exécute avant de construire un hôte sont publiques elles aussi —
`EnsureReservedRootsAreFree` (ajoute toujours `/credentials`), `EnsureVirtualRootsAreUnique`,
`EnsureMountSelectionIsResolvable`, `EnsureMountSourcesExist` — tout comme
`RegisterGracefulShutdown`, `DetectOutputMountPath`, `ConfigureVerboseLogging` (`1` : Information
pour les modules Orkeon ; `2` : Debug) et `IsScriptedCrewDefinition` (`.ork.ts` / `.ork.js`).

## Consommer depuis un hôte longue durée

Un service longue durée *peut* simplement envelopper `RunnerHost.Build` — c'est exactement ce que
fait le daemon `orkeon-host` (`Orkeon.Host/Program.cs`), en passant `configureBuilder` pour
`UseSystemd()`/`UseWindowsService()`. Le daemon reste hors de la sélection des montages (VFS-90,
D-11) : ses crews montent sous des racines par crew (`/crews*`), aucune racine n'y est donc jamais
déclarée deux fois, un `--mount` opérateur portant un préfixe d'identifiant se parse comme un
autre, et aucun bloc `mounts:` de crew n'est lu. Un hôte qui possède déjà son `IHostBuilder` (une app ASP.NET,
par exemple) **réplique l'ordre d'enregistrement de `ConfigureRunnerServices`** dans son propre
`Program.cs` — il n'existe pas de raccourci packagé pour cela ; la console REPL inline la même
séquence à la main :

```csharp
// 1. Enregistrer le fournisseur LLM (AddOrkeonLlmProvider) : AddOrkeonInfrastructure n'enregistre
//    aucun modèle à lui, et un conteneur qui n'en a pas échoue à sa première résolution LLM.
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
// 5. Rien pour le registre d'outils : AddOrkeonInfrastructure a enregistré le ToolRegistry
//    par défaut, qui lit chaque IBaseTool enregistré ci-dessus à sa première résolution.
```

Parce qu'un hôte web exécute typiquement chaque crew dans son propre scope DI (les dépôts de crews
d'Orkeon sont scoped), le `ToolRegistry` — un singleton sur l'ensemble des `IBaseTool`
enregistrés — est partagé entre les exécutions, tandis que `CrewFactory` et l'orchestrateur se
résolvent par scope.

## Télémétrie

`AddOrkeonTelemetry(configuration)` lit la section `Telemetry` :

| Clé | Défaut | Effet |
|---|---|---|
| `Enabled` | `true` | `false` n'enregistre que le singleton `OrkeonMetrics` — aucun fournisseur, aucun exportateur. |
| `OtlpEndpoint` | — | Un point de terminaison OTLP explicite ; il l'emporte sur l'environnement. Une adresse `http://` ou `https://` : toute autre valeur est refusée, en nommant la clé. |
| `MaxMemoryMB` | `2048` | Seuil du contrôle de santé `system_resources`. |

**Clés supprimées (GAP-35).** `ExportToConsole` attachait les exportateurs console d'OpenTelemetry,
qui écrivent sur stdout — là où `--events jsonl`, le manifeste de `--list-tools` et
`orkeon mcp serve` parlent à un programme —, et `PrometheusEndpoint` était liée et lue par rien. Les
deux ont disparu, avec le paquet `OpenTelemetry.Exporter.Console`, et une section qui en écrit encore
une — quelle que soit sa valeur, `Enabled` à `false` compris — est refusée par une
`InvalidOperationException` qui nomme la clé et ce qui la remplace : un collecteur OTLP
(`Telemetry:OtlpEndpoint`, `OTEL_EXPORTER_OTLP_ENDPOINT`, le tableau de bord .NET Aspire). Dans les
runners, c'est un réglage refusé : code 1, ou 78 pour `orkeon-host`. Un hôte C# qui veut la console
ajoute l'exportateur à son propre `AddOpenTelemetry()`.

**Où vont les données.** Un `Telemetry:OtlpEndpoint` explicite sert de point de terminaison aux
exportateurs. Sans lui, un `OTEL_EXPORTER_OTLP_ENDPOINT` non vide attache les exportateurs OTLP sans
aucun réglage propre à Orkeon — l'exportateur lit alors lui-même le point de terminaison, le
protocole et les en-têtes dans les variables standard `OTEL_EXPORTER_OTLP_*`, ce qui permet à un
processus lancé par .NET Aspire de rapporter sans rien configurer. Il les lit dans la configuration
de l'hôte, par sa couche de variables d'environnement sans préfixe : la raison pour laquelle chaque
hôte Orkeon garde cette couche, sous son fichier de réglages
([d'où viennent les réglages](./configuration.md#doù-viennent-les-réglages)). Sans l'un ni l'autre,
rien n'est exporté.

**Ce qui est exporté.** Les traces des sources d'activité `Orkeon.Crew`, `Orkeon.Agent`,
`Orkeon.Task`, `Orkeon.Llm`, `Orkeon.Tool`, `Orkeon.Memory` et `Orkeon.EventHub` plus
l'instrumentation HttpClient ; les métriques du meter `Orkeon` plus l'instrumentation runtime et
HttpClient ; et, dès que l'export OTLP est actif, les journaux structurés (message formaté et scopes
compris) vers le même point de terminaison. La ressource nomme le service `Orkeon`. Les noms de spans
et de métriques suivent les conventions GenAI d'OpenTelemetry — voir
[Sous-systèmes opt-in](opt-in-subsystems.md).

La section enregistre aussi trois contrôles de santé — `llm_provider`, `memory_provider` (qui lit une
clé absente : chaque provider le sert, Pinecone et ChromaDB compris),
`system_resources` — qu'aucun runner livré n'expose : `orkeon` ne sert aucun HTTP, et la seule surface HTTP d'`orkeon-host` est son serveur A2A opt-in (`Orkeon:Host:A2A`), qui ne sert aucun point de santé.

## .NET Aspire — `Orkeon.Hosting.Aspire`

`Orkeon.Hosting.Aspire` (paquet `Orkeon.Hosting.Aspire`, voir la
[matrice de publication](publication-matrix.md)) décrit des processus Orkeon comme ressources d'un
AppHost Aspire, pour que le tableau de bord Aspire montre leurs spans, métriques et journaux
([ADR-011](../adr/ADR-011-aspire-dashboard-observability.md)). Il n'exécute lui-même aucune crew : il
lance les exécutables livrés, trouvés sur le `PATH` ou nommés par `command`.

**Installer.** Dans un projet AppHost Aspire (`Aspire.AppHost.Sdk`) :

```bash
dotnet add package Orkeon.Hosting.Aspire --prerelease
dotnet tool install -g Orkeon.Scripting.Cli --prerelease   # l'exécutable `orkeon` que lance AddOrkeonCrewRun
```

`orkeon-host` vient des archives d'installation complètes ou du MSI per-machine du service
([service host](../architecture/service-host.md)) ; passez son chemin en `command` s'il n'est pas sur
le `PATH`. Lancez ensuite l'AppHost avec `dotnet run` et ouvrez le tableau de bord.

**Ce qui circule.** Vers chaque processus : ses arguments et les variables `ORKEON_` fixées par
`WithOrkeonSetting`/`WithOrkeonModel`, plus les variables `OTEL_EXPORTER_OTLP_*` et
`OTEL_SERVICE_NAME` qu'Aspire injecte. Depuis lui : les traces, métriques et journaux structurés
décrits sous [Télémétrie](#télémétrie), et la sortie console. Rien d'autre — ni endpoint, ni sonde
de santé, ni état : le tableau de bord observe, il ne pilote pas un run.

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// le daemon orkeon-host, ses crews enregistrées dans son fichier de settings
builder.AddOrkeonHost("orkeon-host", settingsPath: "host.appsettings.json")
       .WithOrkeonSetting("Orkeon:Host:RunTimeout", "00:10:00");

// un `orkeon run`, fichiers sous ./out, sur un modèle local
builder.AddOrkeonCrewRun("quickstart", crewPath: "../../quickstart/crew.yaml")
       .WithOrkeonModel(new Uri("http://localhost:11434"), "qwen2.5:1.5b");

builder.Build().Run();
```

| Membre | Ce qu'il fait |
|---|---|
| `AddOrkeonHost(name, settingsPath?, workingDirectory?, command = "orkeon-host")` | Une `OrkeonHostResource` (exécutable) lancée en `orkeon-host [--settings <settingsPath>] --allow-external-mounts`, dans `workingDirectory` (défaut : le dossier de l'AppHost), avec l'exportateur OTLP câblé. Aucun endpoint : le daemon ne sert pas de HTTP. |
| `AddOrkeonCrewRun(name, crewPath, outputDirectory?, settingsPath?, command = "orkeon")` | Une `OrkeonCrewRunResource` lancée en `orkeon run <crewPath> --mount <sortie>:/output:rw --allow-external-mounts [--settings <settingsPath>]` dans le dossier de l'AppHost, avec l'exportateur OTLP câblé. Le dossier de sortie (défaut `<AppHost>/out`) est créé dès la déclaration de la ressource. |
| `WithOrkeonSetting(key, value)` | Fixe n'importe quelle clé de configuration par l'environnement `ORKEON_` que lisent les runners : `Llm:Model` devient `ORKEON_Llm__Model`. La clé est le chemin de configuration complet — les clés propres au daemon vivent sous `Orkeon:Host`, donc `Orkeon:Host:RunTimeout`, pas `Host:RunTimeout`. |
| `WithOrkeonModel(baseUrl, model, apiKey?)` | Raccourci pour `Llm:BaseUrl` (`/` final retiré), `Llm:Model` et, s'il est donné, `Llm:ApiKey`. |

Un AppHost exécutable se trouve dans
[`examples/aspire/AppHost`](https://github.com/orkeon/orkeon/blob/main/examples/aspire/AppHost/Program.cs).

## Microsoft Agent Framework

Pour exécuter une crew depuis une application Microsoft Agent Framework — ou un agent MAF dans une
crew — voir [Interop Agent Framework](agent-framework-interop.md) : son exemple construit son hôte
avec `RunnerHost.Build` et ajoute le pont par `configureServices`.
