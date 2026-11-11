> 🇬🇧 [English version](../../architecture/plugins.md)

# Système de plugins

> **Statut** : v1. Assembly `Orkeon.Plugins`
> (`src/plugins/Orkeon.Plugins`) — **non livrée comme paquet NuGet** ; voir
> [Construire un projet de plugin](#construire-un-projet-de-plugin).
> La spécification d'origine (permissions, hot-reload, configuration par
> plugin) reste une feuille de route ; le manifeste `plugin.json` est livré — voir `src/plugins/Orkeon.Plugins/SPECIFICATION.md`.

Le système de plugins permet de déposer des assemblies tierces dans un répertoire et de
les laisser contribuer des services (outils `IBaseTool`, providers LLM, providers
mémoire, ou tout autre service) au conteneur d'injection de dépendances de l'hôte —
sans recompiler l'hôte.

## ⚠️ Frontière de confiance — à lire avant tout

**Charger un plugin, c'est exécuter du code arbitraire avec tous les privilèges du
processus hôte.** Il n'y a **pas de sandbox en v1**, et un `AssemblyLoadContext` est un
mécanisme d'*isolation* (versions de dépendances indépendantes, déchargeabilité),
**pas une frontière de sécurité** : un plugin peut lire les variables d'environnement,
ouvrir des connexions réseau, accéder au disque physique (en dehors du VFS), etc.

Règles d'exploitation minimales :

- Ne pointer `OrkeonPluginsOptions.Directory` que vers un répertoire dont le **contenu
  est de confiance** (revue de code, signature/empreinte vérifiée hors-bande, chaîne
  d'approvisionnement maîtrisée).
- Protéger le répertoire de plugins en **écriture** contre les acteurs non fiables
  (un dépôt de DLL = une exécution de code au prochain démarrage).
- Le `ConfigureServices` d'un plugin s'exécute **au démarrage**, avant la construction
  du conteneur : un plugin malveillant n'a même pas besoin qu'on résolve ses services.
- En cas de doute, ne pas charger : préférer `ContinueOnError = false` (défaut) pour
  qu'un assembly inattendu fasse échouer le démarrage plutôt que d'être ignoré.

## Contrat `IOrkeonPlugin`

```csharp
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Plugins;

public sealed class WeatherPlugin : IOrkeonPlugin
{
    public string Name => "acme.weather-tools";   // nom stable
    public string Version => "1.0.0";             // informatif, SemVer recommandé

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<Orkeon.Domain.Tools.IBaseTool, WeatherForecastTool>();
    }
}
```

- Constructeur public sans paramètre obligatoire (instanciation via `Activator`).
- Le manifeste nomme le plugin (`name`, recoupé avec `Name`, et `version`, avec `Version`). L'assembly
  d'entrée peut contenir d'autres types `IOrkeonPlugin` : ils sont construits (`IOrkeonPlugin.Name` est une propriété
  d'instance, le chargeur doit donc les construire pour comparer le nom), mais leur `ConfigureServices` n'est
  jamais appelé. Un constructeur qui lève fait échouer le chargement.
- Côté projet plugin : référencer les contrats **sans les copier** à côté du plugin
  (embarquer son propre `Orkeon.Plugins.dll` casse l'identité de types — le chargeur
  détecte ce cas et le signale explicitement) — voir ci-dessous.

### Construire un projet de plugin

`Orkeon.Plugins` n'est **pas distribuée comme paquet NuGet à part**. Son csproj pose
`IsPackable=false` et la [matrice de publication](../reference/publication-matrix.md#paquets-abandonnés)
classe l'identifiant dans les *paquets abandonnés* ; l'assembly est livrée dans le paquet parapluie
`Orkeon`, l'une de ses treize assemblies embarquées (`src/packaging/Orkeon/Orkeon.csproj`), donc un
projet de plugin référence `Orkeon` (le modèle `orkeon-plugin` le fait).
`dotnet add package Orkeon.Plugins` ne se résout sur aucun flux — NuGet.org comme GitHub Packages
(`NU1101`).

`IOrkeonPlugin` vient donc du paquet publié `Orkeon`, comme les autres contrats que touche un
plugin (`IBaseTool`, `IFileSystemService`, les interfaces de providers). On le référence pour la
compilation seulement, pour que la copie de l'hôte reste la seule identité de types :

```xml
<ItemGroup>
  <!-- Contrats seulement : ExcludeAssets="runtime" garde les assemblies du framework hors de la
       sortie du plugin, pour que la copie de l'hôte reste la seule identité de types. -->
  <PackageReference Include="Orkeon" ExcludeAssets="runtime" />
</ItemGroup>
<PropertyGroup>
  <!-- émet le .deps.json utilisé pour résoudre les dépendances privées -->
  <EnableDynamicLoading>true</EnableDynamicLoading>
</PropertyGroup>
```

Côté hôte, `orkeon-repl` est le seul binaire livré qui charge des plugins : quand ses réglages
portent une section `Plugins`, il appelle `AddOrkeonPlugins(fileSystem, configuration)` pendant
qu'il compose son conteneur, et les outils d'un plugin atteignent ses commandes et les crews
qu'elles lancent comme les outils intégrés (un outil de plugin nommé comme un outil intégré refuse
le démarrage : le registre d'outils nomme les deux). La découverte lit un système de fichiers à
elle, construit depuis les montages déclarés (`Orkeon:FileSystem:Mounts`, et `:InternalMounts`
pour un dossier que les agents ne doivent pas voir) : les contrôles du registre tiennent — le
montage, ses droits, aucun chemin qui en sorte — mais pas le validateur de chemins des agents,
qui refuse tout `.dll` quel que soit le montage. `orkeon run` et `orkeon-host` ne chargent aucun
plugin ; un autre hôte appelle lui-même `AddOrkeonPlugins(...)`.

## Découverte (VFS)

La découverte passe par `IFileSystemService` : `OrkeonPluginsOptions.Directory` est un
**chemin virtuel** (ex. `/plugins`) qui doit résoudre dans un mount configuré. Une seule
disposition est reconnue, au premier niveau uniquement : **un sous-dossier par plugin**, qui contient
`plugin.json` et l'assembly d'entrée qu'il nomme. Une assembly posée à plat dans le répertoire
est refusée, pas ignorée.

```
/plugins/
├── Stray.dll                     # à plat : refusé (PluginManifestException), jamais chargé
└── WeatherPlugin/
    ├── plugin.json               # le manifeste, lu avant le chargement de l'assembly
    ├── WeatherPlugin.dll         # l'assembly d'entrée que nomme le manifeste
    ├── WeatherPlugin.deps.json   # pilote la résolution des dépendances privées
    └── Newtonsoft.Json.dll       # dépendance privée, jamais traitée comme plugin
```

L'assembly d'entrée doit porter l'extension `.dll` et son nom de fichier satisfaire `SearchPattern`
(`*.dll` par défaut, un glob simple insensible à la casse). Un sous-dossier sans `plugin.json` est
refusé (`PluginManifestException`) quand il contient un `.dll` quelconque, et ignoré sinon ; un assembly
d'entrée qui ne satisfait pas `SearchPattern` ou que les droits du mount refusent est refusé aussi, pas ignoré. La résolution du chemin physique
exigée par les API `AssemblyLoadContext` utilise le mécanisme du VFS
(`IFileSystemService.ResolveAndValidate` avec `FileAccessRights.Read`, contrôle des mounts
compris) ; une assembly d'entrée refusée par la politique de mount est refusée, comme tout
manifeste inutilisable. Les messages
d'erreur du système de plugins ne référencent que des chemins virtuels.

- `Directory` doit commencer par `/` — toute autre valeur lève une `ArgumentException` à l'appel.
- Un répertoire inexistant ne charge **rien, en silence** : aucun plugin, aucun échec.
- Les candidats sont chargés dans l'ordre ordinal de leur chemin virtuel : l'ordre des
  enregistrements (et donc « le dernier enregistré gagne » entre deux plugins) est déterministe.
- L'énumération ne suit pas les liens symboliques.

## Le manifeste (`plugin.json`)

Chaque plugin en porte un, ceux du dépôt compris. Il est lu par le VFS (`IFileSystemService`, un chemin
virtuel) avant le chargement de l'assembly :

```json
{
  "name": "acme.weather-tools",
  "version": "1.0.0",
  "orkeonVersion": "[1.0.0,2.0.0)",
  "entryAssembly": "WeatherPlugin.dll",
  "tools": [ "weather_forecast" ]
}
```

| Champ | Sens |
|---|---|
| `name` | Doit égaler `IOrkeonPlugin.Name`. |
| `version` | Doit égaler `IOrkeonPlugin.Version`. |
| `orkeonVersion` | Plage de style NuGet où doit tomber l'Orkeon qui tourne : `1.0.0` (cette version ou une suivante), `[1.0.0]`, `[1.0.0-beta.1,2.0.0)`, `(,2.0.0]`, `*`. Les métadonnées de build (`+sha`) de la version qui tourne sont ignorées ; une préversion se classe sous sa version finale. |
| `entryAssembly` | Nom de fichier d'un `.dll` du dossier du plugin : aucun chemin. |
| `entryType` | Facultatif. Nom complet du type `IOrkeonPlugin` à activer. Sans lui, un assembly à un seul type de plugin active celui-là ; avec plusieurs, tous sont activés pour lire leur `Name`. |
| `tools` | Noms des descripteurs `IBaseTool` que le plugin enregistre (`IBaseTool.Name`) ; peut être vide. Les autres enregistrements (services, services hébergés, fournisseurs) ne sont pas couverts par `tools`. |

`PluginManifestException` (une `PluginLoadException` : `ContinueOnError` la couvre) refuse le plugin
quand le manifeste est absent ou mal formé, qu'un champ requis manque, que l'assembly d'entrée est
absent, que `orkeonVersion` exclut l'Orkeon qui tourne, que le manifeste ne nomme aucun plugin de
l'assembly d'entrée ou en diffère par la version, ou que le plugin enregistre un `IBaseTool` dont le
nom n'est pas dans `tools`. Un plugin refusé ne laisse aucun enregistrement : la collection de services est restaurée telle que
l'hôte l'avait (un `Replace` ou `RemoveAll` d'un enregistrement de l'hôte compris), et un
`ConfigureServices` qui lève est annulé de même et ressort en `PluginLoadException`. Un outil enregistré
par instance est contrôlé à l'enregistrement. Un outil enregistré par type, fabrique ou clé est contrôlé
par le fournisseur même de l'hôte quand il construit l'outil (une fois, avec les services que l'hôte
enregistre après `AddOrkeonPlugins`, tel `ILogger<T>`) : un nom non déclaré lève `PluginManifestException`
qui nomme le plugin, et une exception du code de l'outil ressort en `PluginLoadException` avec la cause
dedans. Ce contrôle a lieu quand l'hôte résout les outils. Avec `ContinueOnError = false` il lève là ; avec
`ContinueOnError = true` l'outil refusé est écarté : il se résout en `null`, que chaque consommateur Orkeon
des outils du conteneur (`ToolRegistry`, les hôtes de script) ignore, les autres outils du plugin restent, et
le refus est consigné une fois dans `IPluginRegistry.Failures`. Un hôte qui lit lui-même
`GetServices<IBaseTool>()` ignore `null` de même. Un outil déclaré mais non enregistré n'est pas une erreur.

Un plugin minimal vit dans les tests (`tests/plugins/Orkeon.Plugins.Tests/Fixtures/SamplePlugin/`) : son
`plugin.json` et `SamplePlugin`, qui enregistre l'unique outil que déclare le manifeste.

## Chargement et isolation

Chaque assembly plugin est chargé dans son propre `PluginLoadContext` :

- **collectible** (`isCollectible: true`) → déchargeable ;
- dépendances résolues par `AssemblyDependencyResolver` (`.deps.json` du plugin) →
  chaque plugin peut embarquer **ses propres versions** de dépendances, bibliothèques natives
  comprises (`LoadUnmanagedDll` passe par le même résolveur) ;
- les assemblies dont le nom simple commence par un préfixe de
  `SharedAssemblyPrefixes` (`Orkeon.`, `Orkeon.Rag.Abstractions`, `Microsoft.Extensions.` par défaut — l'entrée du milieu est explicite pour que les types de contrat RAG gardent leur identité inter-ALC même chez les hôtes qui resserrent les défauts) ne sont
  **jamais** résolues dans le contexte du plugin : elles s'unifient avec le contexte
  hôte, garantissant une identité unique pour `IOrkeonPlugin`, `IBaseTool`,
  `IServiceCollection`, etc.

Déchargement : `PluginRegistry.UnloadAll()` (ou `Dispose()`) initie le déchargement de
tous les contextes. Le déchargement d'un ALC est **coopératif** : il ne s'achève que
lorsque plus aucun objet issu du plugin n'est joignable — en pratique, après le
`Dispose()` du `ServiceProvider` qui consomme les services du plugin. Le registre
étant enregistré comme *instance* singleton, le conteneur ne le dispose pas :
décharger reste une décision explicite de l'hôte.

## Activation DI (opt-in)

Cohérent avec le pattern des [sous-systèmes opt-in](../reference/opt-in-subsystems.md) :
**rien n'est chargé par défaut**, ni par `AddOrkeonApplication()`, ni par
`AddOrkeonInfrastructure()`. L'hôte appelle explicitement, une seule fois :

```csharp
using Orkeon.Plugins;

// L'IFileSystemService est construit au bootstrap, avant le DI
// (même étape que la provision des mounts VFS).
services.AddOrkeonPlugins(fileSystem, options =>
{
    options.Directory = "/plugins";
    options.SearchPattern = "*.dll";
    options.ContinueOnError = false;   // fail fast (défaut)
});

// Ou liaison depuis la configuration (section "Plugins") :
services.AddOrkeonPlugins(fileSystem, configuration);
```

```json
{
  "Plugins": {
    "Directory": "/plugins",
    "SearchPattern": "Acme.*.dll",
    "ContinueOnError": false,
    "SharedAssemblyPrefixes": [ "Acme.Contracts." ]
  }
}
```

Les clés sont les propriétés d'`OrkeonPluginsOptions` ; toutes sont facultatives (défauts :
`/plugins`, `*.dll`, `false`). Les entrées de `SharedAssemblyPrefixes` s'**ajoutent** aux trois
défauts (le binder de configuration complète la liste existante) — resserrer les défauts passe
par la surcharge à délégué. Les options sont consommées à l'appel et ne sont pas enregistrées
comme `IOptions<OrkeonPluginsOptions>`. Le mount `/plugins` lui-même relève de l'hôte : il se
déclare comme n'importe quel autre (`Orkeon:FileSystem:Mounts`, la lecture seule suffit).

Particularités :

- la découverte et le chargement sont **immédiats** (au moment de l'appel) : les
  plugins doivent contribuer leurs enregistrements à l'`IServiceCollection` avant la
  construction du provider ;
- un plugin dont l'assembly d'entrée n'a aucun `IOrkeonPlugin` nommé par le manifeste est refusé et
  son contexte déchargé ;
- `ContinueOnError = true` consigne les échecs dans `IPluginRegistry.Failures` au lieu
  de lever `PluginLoadException` ; les enregistrements du plugin refusé sont annulés
  dans les deux cas et son contexte déchargé ;
- `IPluginRegistry` (singleton) expose `Assemblies`, `Plugins` (nom/version) et
  `Failures` pour l'introspection.

## Limites v1

| Hors périmètre v1 | Piste (SPECIFICATION.md) |
|---|---|
| Sandbox / modèle de permissions | `PluginSecurityManager`, `PluginSandbox` |
| Hot-reload / rechargement à chaud | `ReloadPluginAsync` |
| Configuration persistée par plugin | `IPluginConfigurationStore` |
| Symlinks dans le répertoire de plugins | non suivis |

---

> **Voir aussi** : [Sécurité, résilience et plugins](../architecture/security.md) ·
> [Sous-systèmes opt-in](../reference/opt-in-subsystems.md) ·
> [Retour à l'index](../INDEX.md)
