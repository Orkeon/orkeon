> 🇬🇧 [English version](../../architecture/plugins.md)

# Système de plugins

> **Statut** : v1 (chantier R1.6). Assembly `Orkeon.Plugins`
> (`src/plugins/Orkeon.Plugins`) — **non livrée comme paquet NuGet** ; voir
> [Construire un projet de plugin](#construire-un-projet-de-plugin).
> La spécification d'origine (manifeste, permissions, hot-reload, configuration par
> plugin) reste une feuille de route — voir `src/plugins/Orkeon.Plugins/SPECIFICATION.md`.

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
- Un assembly peut contenir plusieurs implémentations ; toutes sont instanciées.
- Côté projet plugin : référencer les contrats **sans les copier** à côté du plugin
  (embarquer son propre `Orkeon.Plugins.dll` casse l'identité de types — le chargeur
  détecte ce cas et le signale explicitement) — voir ci-dessous.

### Construire un projet de plugin

`Orkeon.Plugins` n'est **pas distribuée comme paquet NuGet**. Son csproj pose
`IsPackable=false`, la [matrice de publication](../reference/publication-matrix.md#paquets-abandonnés)
la classe dans les *paquets abandonnés*, et elle ne fait pas partie des onze assemblies embarquées
dans le paquet parapluie `Orkeon` (`src/packaging/Orkeon/Orkeon.csproj`).
`dotnet add package Orkeon.Plugins` ne se résout sur aucun flux — NuGet.org comme GitHub Packages
(`NU1101`).

Un auteur de plugin construit donc **depuis les sources** : cloner
[`Orkeon/orkeon`](https://github.com/Orkeon/orkeon) et pointer une `ProjectReference` vers
`src/plugins/Orkeon.Plugins/Orkeon.Plugins.csproj`. Les autres contrats que touche un plugin
(`IBaseTool`, `IFileSystemService`, les interfaces de providers) viennent, eux, du paquet publié
`Orkeon` : seul le point d'entrée `IOrkeonPlugin` exige la référence aux sources.

```xml
<ItemGroup>
  <!-- Contrats seulement : ExcludeAssets="runtime" garde Orkeon.Plugins.dll hors de la sortie
       du plugin, pour que la copie de l'hôte reste la seule identité de types. -->
  <ProjectReference Include="../orkeon/src/plugins/Orkeon.Plugins/Orkeon.Plugins.csproj"
                    ExcludeAssets="runtime" />
  <PackageReference Include="Orkeon" ExcludeAssets="runtime" />
</ItemGroup>
<PropertyGroup>
  <!-- émet le .deps.json utilisé pour résoudre les dépendances privées -->
  <EnableDynamicLoading>true</EnableDynamicLoading>
</PropertyGroup>
```

Côté hôte, même contrainte : aucun binaire livré par Orkeon n'appelle `AddOrkeonPlugins(...)`
— aucun projet sous `src/` ne référence `Orkeon.Plugins`. Le système de plugins est activé par un
hôte que vous construisez vous-même, in-tree ou contre votre propre clone.

## Découverte (VFS)

La découverte passe par `IFileSystemService` : `OrkeonPluginsOptions.Directory` est un
**chemin virtuel** (ex. `/plugins`) qui doit résoudre dans un mount configuré. Deux
dispositions sont reconnues, au premier niveau uniquement :

```
/plugins/
├── MyPlugin.dll                  # disposition « à plat »
└── WeatherPlugin/
    ├── WeatherPlugin.dll         # disposition « dossier par plugin » (<dir>/<dir>.dll)
    ├── WeatherPlugin.deps.json   # pilote la résolution des dépendances privées
    └── Newtonsoft.Json.dll       # dépendance privée, jamais traitée comme plugin
```

Les candidats doivent porter l'extension `.dll` et satisfaire `SearchPattern`
(`*.dll` par défaut). La résolution du chemin physique exigée par les API
`AssemblyLoadContext` utilise le mécanisme du VFS
(`IFileSystemService.ResolveAndValidate`, contrôle des mounts + `FileAccessRights`) ;
un fichier refusé par la politique de mount est simplement exclu. Les messages
d'erreur du système de plugins ne référencent que des chemins virtuels.

## Chargement et isolation

Chaque assembly plugin est chargé dans son propre `PluginLoadContext` :

- **collectible** (`isCollectible: true`) → déchargeable ;
- dépendances résolues par `AssemblyDependencyResolver` (`.deps.json` du plugin) →
  chaque plugin peut embarquer **ses propres versions** de dépendances ;
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

Cohérent avec le pattern des [sous-systèmes opt-in](../reference/opt-in-subsystems.md)
(R4.9) : **rien n'est chargé par défaut**, ni par `AddOrkeonApplication()`, ni par
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

Particularités :

- la découverte et le chargement sont **immédiats** (au moment de l'appel) : les
  plugins doivent contribuer leurs enregistrements à l'`IServiceCollection` avant la
  construction du provider ;
- un assembly managé sans implémentation d'`IOrkeonPlugin` est ignoré et son contexte
  immédiatement déchargé ;
- `ContinueOnError = true` consigne les échecs dans `IPluginRegistry.Failures` au lieu
  de lever `PluginLoadException` (sans rollback des enregistrements déjà contribués
  par le plugin fautif) ;
- `IPluginRegistry` (singleton) expose `Assemblies`, `Plugins` (nom/version) et
  `Failures` pour l'introspection.

## Limites v1

| Hors périmètre v1 | Piste (SPECIFICATION.md) |
|---|---|
| Sandbox / modèle de permissions | `PluginSecurityManager`, `PluginSandbox` |
| Manifeste `plugin.json` (métadonnées hors code) | `PluginManifest` |
| Hot-reload / rechargement à chaud | `PluginLoader.Reload` |
| Configuration persistée par plugin | `IPluginConfigurationStore` |
| Symlinks dans le répertoire de plugins | non suivis |

---

> **Voir aussi** : [Sécurité, résilience et plugins](../architecture/security.md) ·
> [Sous-systèmes opt-in](../reference/opt-in-subsystems.md) ·
> [Retour à l'index](../INDEX.md)
