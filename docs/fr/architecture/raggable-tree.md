> 🇬🇧 [English version](../../architecture/raggable-tree.md)

> **Voir aussi** : [Système de mémoire](../architecture/memory-system.md) · [Référence YAML](./yaml-schema.md) · [Inventaire des outils](../tools/inventory.md) · [Retour à l'index](../INDEX.md)

# RaggableTree

Graphe sémantique multi-langage du code source : parsing Tree-sitter, enrichissement embeddings, stockage vectoriel, et exposition aux agents via 15 tools. Remplace la lecture de fichiers brute par un index structuré à six niveaux de zoom (monorepo → package → module → symbole → déclaration → statement).

## Vue d'ensemble

Un agent qui n'a que `file_read` et `directory_read` doit lire des fichiers entiers, deviner leurs relations et surcharger son contexte LLM. Le RaggableTree précalcule ces éléments :

- **Structure** : chaque fichier est décomposé en classes, méthodes, fonctions, avec des Fully-Qualified Names stables (`pkg::Module::Class::method`).
- **Relations** : imports, appels, héritage et implémentations sont résolus à partir de l'AST et stockés comme arêtes du graphe.
- **Sémantique** : signatures, docstrings, extraits de code et, optionnellement, résumés LLM sont composés en `EmbeddingText` puis vectorisés.
- **Évolution** : un moteur de réindexation incrémentale met à jour l'index après chaque commit ou événement watcher.

Les tools exposés (`codebase_map`, `symbol_detail`, `flow_trace`, `impact_analysis`, etc.) donnent à l'agent des requêtes structurelles au lieu de grep sur du plain-text.

## Quick start

```csharp
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Domain.FileSystem;

// fileSystem : IFileSystemService (VFS) avec le projet monté sur /src
var builder = new RaggableTreeBuilder(new TypeScriptAdapter(), fileSystem);
var result = await builder.BuildAsync(
    "/src",
    new IndexCodebaseRequest { RootPath = "/src" },
    CancellationToken.None);

Console.WriteLine($"{result.Tree.Nodes.Count} nodes, {result.Tree.Edges.Count} edges, indexId={result.IndexId}");
```

Pour un usage en DI :

```csharp
services.AddRaggableTree(new RaggableTreeOptions
{
    Languages = ["typescript", "python"],
    Embedding = new EmbeddingOptions { Provider = EmbeddingProviderKind.OpenAI, ApiKey = apiKey },
});
```

## Configuration YAML

```yaml
raggableTree:
  enabled: true                    # false pour désactiver entièrement la feature
  languages: [typescript, csharp]  # [] = auto-détection via les adaptateurs enregistrés
  exclude: [node_modules, dist, .git, bin, obj]
  indexMode: frozen                # frozen | live | breakOnChange (§31.3)
  enrichWithLlm: false             # true pour générer SemanticSummary via summarizer
  includeStatements: false         # true pour indexer L4 (CFG par statement)

  embedding:
    provider: openai               # none | openai | ollama | onnx
    model: text-embedding-3-small
    apiKey: ${OPENAI_API_KEY}
    baseUrl: https://api.openai.com/
    dimensions: 1536

  summarizer:
    provider: anthropic            # none | anthropic
    model: claude-haiku-4-5
    concurrency: 5

  vectorStore:
    provider: inMemory             # inMemory | redis | chromaDb | pinecone | lanceDb | sqlite

  cache:
    enabled: true
    path: .orkeon/raggable-tree.json
```

Tous les champs sont des `record` immuables dans `Orkeon.Analysis.DependencyInjection.RaggableTreeOptions`.

## Pipeline en six phases

1. **Discovery** (`IFileSystemDiscoverer`) — parcours récursif, détection de packages via markers (`package.json`, `*.csproj`, `pyproject.toml`, `go.mod`, `Cargo.toml`), exclusion via patterns et `.gitignore`.
2. **Parse + Extract** (`UniversalSemanticMapper` + `ILanguageAdapter`) — Tree-sitter parse → application des queries de l'adaptateur → nœuds L2 (module) et L3 (symbole) avec `Fqn`, `Signature`, `SourceSnippet`, `Sha256`.
3. **Dependency resolution** (`DependencyGraphBuilder` + `IReferenceResolver`) — résolution des imports, appels, héritage, implémentations en arêtes (`EdgeKind.Imports`, `Calls`, `Extends`, `Implements`). Les FQN non résolus deviennent des `UnresolvedRef`.
4. **Fingerprinting** (`IFrameworkFingerprinter`) — application des règles par décorateur/annotation (NestJS, Angular, ASP.NET, Flask, FastAPI) : pose de tags comme `http-endpoint`, `guard`, `service` sur les nœuds concernés.
5. **Enrichissement** — composition du `EmbeddingText` (`IEmbeddingTextComposer`), optionnellement résumé LLM (`INodeSummarizer`), puis embedding batch (`IEmbeddingProvider`).
6. **Persistance** — `IVectorStoreProvider.IndexAsync` pour les embeddings, cache JSON sérialisé de l'arbre complet (`RaggableTreeCache`).

La réindexation incrémentale (`IncrementalReindexEngine`) repart de la phase 2 pour les fichiers changés uniquement, réutilise les nœuds des fichiers inchangés, et ne lance les phases 4-6 que sur les `newNodes`.

## Catalogue de tools

| Tool | Usage | Requête type |
|------|-------|---------------|
| `index_codebase` | Construit l'index initial | `{ "root_path": "/src" }` |
| `incremental_reindex` | Met à jour l'index après modif de fichiers | `{ "changed_file_paths": ["src/a.ts"] }` |
| `index_status` | Liste les racines virtuelles indexées (date, comptes de nœuds/arêtes) | `{}` |
| `is_path_indexed` | Vérifie si un chemin est couvert par une racine indexée | `{ "virtual_path": "/src/app/main.ts" }` |
| `codebase_map` | Vue d'ensemble : packages, compte de fichiers/symboles | `{ "depth": 2 }` |
| `package_summary` | Résumé d'un package (modules, dépendances, symboles clés) | `{ "package_fqn": "app::core" }` |
| `symbol_detail` | Détail d'un symbole (signature, doc, callers, callees) | `{ "fqn": "app::UserService::create" }` |
| `symbol_source` | Extrait du code source (signature, body, span complet) | `{ "fqn": "...", "mode": "SignatureAndBody" }` |
| `codebase_search` | Recherche sémantique par embedding | `{ "query": "session expiration", "top_k": 10 }` |
| `dependency_graph` | Arêtes entrantes/sortantes d'un nœud | `{ "fqn": "...", "kinds": ["Imports", "Calls"] }` |
| `sub_graph` | Expansion BFS autour de seeds | `{ "seeds": ["pkg::X"], "max_depth": 3 }` |
| `flow_trace` | Chemins d'appel entre deux FQN | `{ "from": "A", "to": "B", "max_paths": 5 }` |
| `impact_analysis` | Impacts transitifs d'un changement | `{ "fqn": "...", "direction": "Backward" }` |
| `complexity_report` | Top-N par métrique (Cyclomatic, LoC, Callers...) | `{ "metric": "Cyclomatic", "top_n": 20 }` |
| `statement_query` | Requêtes structurelles L4 (if, try, return...) | `{ "parent_fqn": "...", "kinds": ["TryCatch"] }` |

Les 15 tools sont enregistrés via `AddRaggableTreeTools` (`Orkeon.Tools.Analysis.DependencyInjection.RaggableToolsExtensions`).

## Stratégies d'agents

Le framework expose `ICodebaseContextProvider` qui produit un résumé compact du codebase (packages, top complexité, top couplage, patterns détectés) à injecter dans le system prompt d'un agent au démarrage. Trois formats : `markdown` (défaut, ~300 tokens), `compact` (~150 tokens), `json` (~400 tokens). L'injection n'a lieu que si un index est présent et si l'agent possède au moins un tool RaggableTree.

Trois modes de synchronisation gouvernent la réaction aux événements watcher (`ICodebaseWatcher` → `IRaggableTreeEventBus`) :

| Mode | Comportement |
|------|---------------|
| `frozen` (défaut) | L'agent ignore les changements ; il reste sur son `IndexId` d'origine. |
| `live` | L'agent invalide les résultats de tools pour les FQN impactés et interroge à nouveau l'index. |
| `breakOnChange` | L'agent avorte sa tâche proprement avec un statut `IndexChanged`. |

## Extensibilité

### Ajouter un langage

Implémenter `ILanguageAdapter` (`Orkeon.Analysis.Abstractions.Interfaces`) : fournir `LanguageName`, `FileExtensions`, les sept queries Tree-sitter (`DeclarationQuery`, `ImportQuery`, `CallQuery`, `InheritanceQuery`, `DocCommentQuery`, `DecoratorQuery`, `StatementQuery`), le mapping `MapNodeKind` et les extracteurs `ExtractSignature` / `ResolveImportPath`. Enregistrer l'adaptateur via DI (`services.AddSingleton<ILanguageAdapter, MyAdapter>()`) ; `AddRaggableTree` le découvre automatiquement.

### Ajouter un fingerprinter

Créer un `IReadOnlyList<FingerprintRule>` listant les décorateurs/annotations à matcher avec leurs tags puis construire un `FrameworkFingerprinter(framework, rules)`. Voir `Orkeon.Analysis.Fingerprinters.NestJsRules` comme référence (15 règles).

### Ajouter un embedding provider

Implémenter `IEmbeddingProvider.EmbedBatchAsync(texts, ct)` → `IReadOnlyList<ReadOnlyMemory<float>>`. Trois implémentations de référence : `OpenAIEmbeddingProvider`, `OllamaEmbeddingProvider`, et `LocalEmbeddingProvider` (cf. section ci-dessous). L'extension method `EmbedAsync(nodes, model, ct)` (`Orkeon.Analysis.Vectors`) remplit `node.Embedding` à partir du `EmbeddingText` composé.

## Embedding providers

Quatre options sont disponibles via `EmbeddingProviderKind` :

| Provider | Réseau | Clé API | Dims | Notes |
|----------|--------|---------|------|-------|
| `None` | n/a | n/a | n/a | Pipeline sans embedding (phases 5-6 désactivées) |
| `OpenAI` | requis | requis | 1536 (`text-embedding-3-small`) | Qualité maximale, MTEB ~62.3 |
| `Ollama` | local (HTTP) | non | dépend du modèle | Daemon Ollama / llama.cpp local requis |
| `LocalSmartComponents` | non | non | 384 (BGE-micro-v2) | In-process, ONNX CPU, démarrage à froid ~200 ms |

### Provider local `LocalSmartComponents`

Implémenté par `Orkeon.Tools.Embeddings.Local` (package opt-in, package upstream `SmartComponents.LocalEmbeddings` v0.1.0-preview10148, modèle BGE-micro-v2). Adapté aux contextes CLI dev, indexation CI offline, démos sans clé d'API.

**Trade-off qualité / coût** (cf. spec §10) :

- 384 dimensions vs 1536 (OpenAI) — recherche sémantique moins fine sur du Q&A factuel hors-domaine.
- Score MTEB ~58.5 vs ~62.3 — acceptable pour l'indexation de code (les signatures, FQN et docstrings sont des signaux structurés moins ambigus que du texte long).
- Coût d'API → 0 (modèle embarqué dans le NuGet, ~22 MB).
- Aucune dépendance réseau, aucune clé à gérer.
- Démarrage à froid ~150-300 ms (chargement ONNX en mémoire) ; ~5-15 ms / texte sur CPU x86_64 moderne.
- Empreinte RAM ~200 MB une fois le modèle chargé.

#### Configuration minimale (`appsettings.json`, cf. spec §7.2)

```jsonc
{
  "RaggableTree": {
    "Embedding": {
      "Provider": "LocalSmartComponents"
      // Dimensions, BaseUrl, ApiKey, Model : tous optionnels et ignorés.
      // Defaults : 384 dims (BGE-micro-v2), CPU, in-process.
    }
  }
}
```

Côté code, l'enregistrement DI est :

```csharp
services.AddOrkeonLocalEmbeddings();        // package Orkeon.Tools.Embeddings.Local
services.AddRaggableTree(new RaggableTreeOptions
{
    Embedding = new EmbeddingOptions
    {
        Provider = EmbeddingProviderKind.LocalSmartComponents,
    },
});
```

#### Configuration étendue avec `ModelPath` VFS (cf. spec §7.3)

Pour pointer vers un autre modèle ONNX que le BGE-micro-v2 embarqué, déclarer un mount VFS lecture seule et passer un chemin **virtuel** dans `Local.ModelPath`. Les chemins physiques (`C:/...`, `/var/...`) sont rejetés par `IFileSystemService` au démarrage — voir `vfs-compliance.md`.

```jsonc
{
  "Orkeon": {
    "FileSystem": {
      "Mounts": [
        "/models:r=C:\\data\\embedding-models"     // mount lecture seule (r=)
      ]
    }
  },
  "RaggableTree": {
    "Embedding": {
      "Provider": "LocalSmartComponents",
      "Dimensions": 384,
      "MaxTextChars": 2000,
      "Local": {
        "ModelPath": "/models/my-custom.onnx",     // chemin virtuel — résolu par IFileSystemService
        "MaxConcurrency": 8
      }
    }
  }
}
```

Les licences du modèle et du package upstream sont tracées dans [`THIRD-PARTY-NOTICES.md`](../../../THIRD-PARTY-NOTICES.md). Un exemple console minimal vit dans [`examples/local-embeddings/`](https://github.com/Orkeon/orkeon/tree/main/examples/local-embeddings).

### Ajouter un vector store

Implémenter `IVectorStoreProvider` (`IndexAsync`, `SearchAsync`, `DeleteAsync`). Voir les six providers mémoire existants (`Redis`, `SQLite`, `ChromaDB`, `Pinecone`, `LanceDB`, `InMemory`) pour les patterns de mapping `VectorDocument`/`VectorMetadata`.

## Limitations V1

- Pas de support des macros C++ / templates pleinement résolus, ni des méta-programmations Ruby/Elixir.
- Le résolveur d'imports est heuristique pour les langages dynamiques (Python, TypeScript) : les re-exports et les monkey patches peuvent produire des `UnresolvedRef`.
- L'embedding et le summarizer nécessitent un provider externe (clés API ou endpoint Ollama local) pour être activés ; le pipeline fonctionne sans (phases 5-6 désactivées).
- Le watcher se base sur `System.IO.FileSystemWatcher` — sous Linux/WSL, les événements de rename peuvent arriver décomposés (observés comme Deleted puis Created).
- Le cache JSON n'est pas versionné : une mise à jour d'adapter peut invalider les caches existants, qui sont alors reconstruits à la demande.

---

> **Voir aussi** : [ADR RaggableTree](./raggable-tree-adr.md) · [Inventaire des outils](../tools/inventory.md) · [Retour à l'index](../INDEX.md)
