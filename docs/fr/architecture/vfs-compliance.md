> 🇬🇧 [English version](../../architecture/vfs-compliance.md)

# Conformité VFS

Orkeon impose au code du framework un principe **Virtual File System (VFS) uniquement** : tout accès au système de fichiers doit passer par `IFileSystemService` afin que les frontières de mounts, les droits d'accès et la validation des chemins soient respectés uniformément. L'usage direct de `System.IO.File`, `System.IO.Directory`, `FileStream`, `FileInfo`, `DirectoryInfo` et `FileSystemWatcher` est interdit dans les assemblies du framework.

Ce document décrit :
- l'analyseur Roslyn `Orkeon.Compliance.Vfs` qui applique la règle à la compilation
- ses sept codes de diagnostic et leurs sévérités
- les périmètres exemptés et la manière d'opter pour une exception documentée
- les critères de sortie du programme de migration VFS

> **VFS-70 (2026-06-17) :** toutes les dépendances `IFileSystemService` des outils/services sont
> désormais **requises** (non-nullables) — les fallbacks `EXCEPTION-BACKCOMPAT`
> `if (_fs is null) { …System.IO… }` ont été éliminés, les shims `[Obsolete]` liés au FS supprimés,
> et le chemin d'ingestion de connaissances entièrement routé par le VFS (le `KnowledgeService`
> audité alors a été remplacé par les loaders `Orkeon.Rag` en RAG-02 — même règle VFS,
> `FileDocumentLoaderBase`). La persistance SQLite (`SqliteMemoryProvider`,
> `SqliteStateStore`) résout désormais son fichier `Data Source` via `ResolveAndValidate`
> (décision 2F-A) ; la découverte batch RaggableTree s'appuie sur
> `IFileSystemService.EnumerateFilesAsync` (décision 2E-A). Deux nouvelles règles (`ORKVFS006`,
> `ORKVFS007`) ferment les angles morts auparavant non détectés.

## L'analyseur

Projet : `src/analyzers/Orkeon.Compliance.Vfs/`
Cible : analyseur Roslyn `netstandard2.0`, câblé dans `src/Directory.Build.props` avec `OutputItemType=Analyzer`.

L'analyseur s'applique à chaque projet sous `src/` sauf lui-même, donc les violations cassent le build immédiatement. Les tests exécutent le même analyseur via le projet `Orkeon.Compliance.Vfs.Tests`.

## Codes de diagnostic

| Code | Sévérité | Détecte | Correction suggérée |
|---|---|---|---|
| `ORKVFS001` | Error | Appel direct à `System.IO.File.*` | Injecter `IFileSystemService` et utiliser `TryReadAllBytesAsync` / `WriteAllTextAsync` / `ExistsAsync` / … |
| `ORKVFS002` | Error | Appel direct à `System.IO.Directory.*` | Utiliser `CreateDirectoryAsync`, `DeleteAsync`, `EnumerateFilesAsync` |
| `ORKVFS003` | Error | `new FileStream(string …)`, `new FileInfo(string)`, `new DirectoryInfo(string)` | Utiliser `OpenReadStreamAsync`, `OpenWriteStreamAsync`, `TryGetEntryAsync` |
| `ORKVFS004` | Error | `Path.GetFullPath(…)` (peut contourner la validation des mounts) | Pour une entrée fournie par l'utilisateur, appeler `ResolveAndValidate` |
| `ORKVFS005` | Error | `new FileSystemWatcher(…)` | Utiliser l'abstraction watcher du VFS |
| `ORKVFS006` | Error | `new StreamReader(string)` / `new StreamWriter(string)` (surcharges par chemin) | Ouvrir via `OpenReadStreamAsync` / `OpenWriteStreamAsync` et envelopper le `Stream` retourné |
| `ORKVFS007` | Error | Champ ou paramètre `IFileSystemService?` nullable | Injecter `IFileSystemService` en dépendance requise (non-nullable) |

Tous les diagnostics sont définis dans `src/analyzers/Orkeon.Compliance.Vfs/DiagnosticDescriptors.cs` et émettent un lien d'aide stable.

## Périmètres exemptés (par chemin)

L'analyseur ignore les chemins de l'allowlist (`SystemIoUsageAnalyzer.IsExemptByPath`). Elle est volontairement étroite — les exemptions par dossier entier ont été remplacées par une allowlist explicite par fichier, pour qu'un *nouveau* fichier sous le même dossier ne soit **pas** exempté silencieusement.

Exemptions par dossier (`s_exemptFolderSegments`) :
- `/core/Orkeon.Domain/FileSystem/` — contrats publics du VFS (l'abstraction elle-même)
- `/core/Orkeon.Infrastructure/FileSystem/` — implémentation du VFS (disk/fake/watcher)
- `/tests/` — les fixtures reposent sur `DiskBackedFileSystemService` et le disque réel
- `/examples/` — hors périmètre de conformité du framework

Exemptions par fichier (`s_exemptFileSuffixes`) — chacune porte un marqueur inline `EXCEPTION-…` / `OUT-OF-SCOPE` :
- `Sandbox/SandboxMountBootstrapper.cs` — mounts enregistrés avant la DI (`EXCEPTION-BOOTSTRAP`)
- `Sandbox/ProcessIsolationSandbox.cs`, `Sandbox/DockerSandbox.cs` — sondage de binaires hôte (`OUT-OF-SCOPE`)
- `Security/PathValidator.cs` — la résolution symlink/realpath est son rôle

Tout autre fichier légitimement « brut » s'appuie sur un `[SuppressVfsCompliance]` étroit au site d'usage plutôt qu'une exemption par chemin (voir les exceptions permanentes ratifiées ci-dessous).

## Opt-out : `[SuppressVfsCompliance]`

Pour les exceptions documentées qui ne correspondent à aucune exemption par chemin, appliquer l'attribut défini dans `Orkeon.Domain.Attributes/SuppressVfsComplianceAttribute.cs` :

```csharp
[Orkeon.Compliance.Vfs.SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: mount registration runs before DI")]
public sealed class SandboxMountBootstrapper { … }

[Obsolete("Use ReadAsync(…) instead")]
[Orkeon.Compliance.Vfs.SuppressVfsCompliance("EXCEPTION-OBSOLETE: transitional API; callers should migrate")]
public static string LoadLegacy(string path) => File.ReadAllText(path);
```

Placement :
- **Assembly** — opt-out large pour un projet entier (à utiliser avec parcimonie, préférer un périmètre plus étroit).
- **Classe / struct** — exempte tous les membres d'un type (classes d'outils avec fallbacks `if (_fs is null)`).
- **Méthode / constructeur / propriété / champ** — périmètre le plus étroit.

L'argument `reason` est obligatoire et doit nommer la catégorie d'audit dont il relève :
- `EXCEPTION-BOOTSTRAP` — s'exécute avant l'enregistrement des mounts VFS.
- `EXCEPTION-WATCHER-BRIDGE` — adaptateur qui enveloppe `System.IO.FileSystemWatcher` pour implémenter un port watcher Orkeon.
- `OUT-OF-SCOPE` — sondage au niveau de l'hôte (chemin d'installation du SDK, découverte de binaires système) qui ne cible jamais un mount VFS.

> **Catégories retirées (VFS-70) :** `EXCEPTION-BACKCOMPAT` et `EXCEPTION-OBSOLETE` ne sont
> **plus acceptées**. Tous les outils/services exigent un `IFileSystemService` non-nullable (un
> nullable déclenche désormais `ORKVFS007`) et les shims `[Obsolete]` liés au FS ont été supprimés.
> Ne pas réintroduire ces catégories.

Toute nouvelle classe d'exception doit d'abord être consignée dans l'audit de conformité VFS (the maintainers' archive, `audit-vfs-compliance-*`) avant l'ajout d'une suppression.

### Exceptions permanentes ratifiées (2D)

Ces accès ne peuvent pas passer par le VFS par nature et sont **définitivement** autorisés via les marqueurs `[SuppressVfsCompliance]` inline (ou l'allowlist par chemin) :

| Domaine | Emplacement | Catégorie | Justification |
|---|---|---|---|
| Implémentation VFS | `Domain/FileSystem/*`, `Infrastructure/FileSystem/*`, `Scripting.Cli/CliFileSystemService.cs` | (l'abstraction) | C'*est* le VFS |
| Bootstrap (pré-DI) | `SandboxMountBootstrapper`, ConsoleApp `Cli*MountBootstrapper`, `Hosting/Runner*` | `EXCEPTION-BOOTSTRAP` | Lisent appsettings + montent avant que le VFS existe |
| Sondage hôte | `DockerSandbox`, `ProcessIsolationSandbox`, `ProcessGitDiffProvider` | `OUT-OF-SCOPE` | Découvrent `docker`/`dotnet`/`git` sur le PATH, jamais un mount |
| Toolchain | `Scripting/Toolchain/EsbuildTranspiler.cs` | `OUT-OF-SCOPE` | Localise le binaire `esbuild` + fichiers temp de transpilation ; chaîne d'outils hôte |
| Primitive sécurité | `Security/PathValidator.cs` | (allowlist) | résolution symlink/realpath = son rôle |
| Watcher bridge | `Analysis/.../FileSystemWatcherCodebaseWatcher.cs` | `EXCEPTION-WATCHER-BRIDGE` | Adapte `System.IO.FileSystemWatcher` vers `ICodebaseWatcher` |
| Sonde RaggableTree | `Analysis/Core/FileSystemDiscoverer.cs` (sonde diagnostique uniquement) | `OUT-OF-SCOPE` | La *découverte batch elle-même* utilise `EnumerateFilesAsync` ; la sonde compte délibérément les entrées physiques pour distinguer « le VFS n'a rien renvoyé » de « le disque est vide » |
| Persistance SQLite | `SqliteMemoryProvider`, `SqliteStateStore` | gouverné | Le moteur exige un chemin réel ; le `Data Source` est résolu via `ResolveAndValidate` (décision 2F-A) avant d'atteindre le driver |

## Ajouter un nouvel outil ou service

1. Déclarer un paramètre de constructeur `IFileSystemService` **requis, non-nullable** et l'injecter via la DI (un nullable déclenche `ORKVFS007`). Le valider avec `ArgumentNullException.ThrowIfNull`.
2. Travailler en chemins virtuels (`/workspace/...`, `/output/...`, `/tmp/...`).
3. Ne jamais ajouter de fallback `System.IO` par chemin `string`. Il n'existe pas de constructeur de rétro-compatibilité — la DI est la seule voie de construction.
4. Pour énumérer, streamer, copier ou observer, utiliser les méthodes dédiées de `IFileSystemService` plutôt que les primitives `System.IO` équivalentes (y compris `StreamReader`/`StreamWriter` — envelopper un `Stream` issu de `OpenReadStreamAsync`/`OpenWriteStreamAsync`, jamais un chemin).

## Comportement en CI

Les sept diagnostics (`ORKVFS001`–`ORKVFS007`) sont tous des **erreurs**, donc la CI bloque tout merge qui réintroduit un accès fichier `System.IO`, un `StreamReader`/`StreamWriter` par chemin, un `Path.GetFullPath` sur entrée utilisateur, ou un `IFileSystemService` nullable dans le code du framework sans attribut `[SuppressVfsCompliance]` justifié.

## Critères de sortie (du programme de migration VFS)

- [x] `VIOLATION-HISTORIC == 0` — les 23 violations historiques ont été éliminées dans P5-VFS-50.
- [x] `VIOLATION-NEW` réduit aux exceptions documentées derrière des gardes `[Obsolete]` / `if (_fs is null)`, toutes couvertes par `[SuppressVfsCompliance]`.
- [x] `EXCEPTION-BOOTSTRAP` ≤ 15 — actuellement 7, toutes légitimes (`SandboxMountBootstrapper`).
- [x] Analyseur Roslyn `Orkeon.Compliance.Vfs` en place et câblé dans `src/Directory.Build.props`.
- [x] Le build passe proprement ; un test négatif confirme qu'un `File.ReadAllText` délibéré dans le code du framework déclenche `ORKVFS001`.
- [x] Éliminer les suppressions `EXCEPTION-BACKCOMPAT` résiduelles en migrant tous les appelants d'outils vers la DI — **fait dans VFS-70** : 0 `EXCEPTION-BACKCOMPAT` et 0 `EXCEPTION-OBSOLETE` lié au FS ne subsistent dans `src/` ; tous les outils fichier + le chemin d'ingestion de connaissances (désormais les loaders `Orkeon.Rag`, RAG-02) exigent un `IFileSystemService` non-nullable ; SQLite gouverné via `ResolveAndValidate` ; `ORKVFS004` promu en erreur et `ORKVFS006`/`ORKVFS007` ajoutés pour fermer les angles morts `StreamReader/Writer(string)` et `IFileSystemService` nullable.
