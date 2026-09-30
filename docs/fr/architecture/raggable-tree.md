> 🇬🇧 [English version](../../architecture/raggable-tree.md)

> **Voir aussi** : [Système de mémoire](./memory-system.md) · [Référence YAML](./yaml-schema.md) · [Inventaire des outils](../tools/inventory.md) · [Retour à l'index](../INDEX.md)

# RaggableTree

Graphe sémantique multi-langage du code source : parsing Tree-sitter, enrichissement embeddings, stockage vectoriel, et exposition aux agents via 15 tools. Remplace la lecture de fichiers brute par un index structuré à cinq niveaux stratifiés (L0 monorepo → L1 package → L2 module → L3 symbole → L4 statement) plus la couche d'arêtes entre eux.

## Vue d'ensemble

Un agent qui n'a que `file_read` et `directory_read` doit lire des fichiers entiers, deviner leurs relations et surcharger son contexte LLM. Le RaggableTree précalcule ces éléments :

- **Structure** : chaque fichier est décomposé en classes, méthodes, fonctions, avec des Fully-Qualified Names stables (`pkg::Module::Class::method`).
- **Relations** : imports, appels, héritage et implémentations sont résolus à partir de l'AST et stockés comme arêtes du graphe.
- **Sémantique** : signatures, docstrings, extraits de code et, optionnellement, résumés LLM sont composés en `EmbeddingText` puis vectorisés.
- **Évolution** : un moteur de réindexation incrémentale met à jour l'index pour les seuls fichiers modifiés — à la demande (`incremental_reindex`, une liste de fichiers ou une plage de commits git) et paresseusement avant la réponse de chaque tool de lecture (voir [Fraîcheur](#fraîcheur-coordination-édition--recherche)).

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

Exemples exécutables : [`basic-indexing`](https://github.com/Orkeon/orkeon/blob/main/examples/raggable-tree/basic-indexing/README.md) (le builder ci-dessus, depuis le C#), [`crew-yaml`](https://github.com/Orkeon/orkeon/blob/main/examples/raggable-tree/crew-yaml/README.md) (une crew YAML qui utilise les tools via le CLI `orkeon` standard, qui les enregistre par défaut) et [`custom-adapter`](https://github.com/Orkeon/orkeon/blob/main/examples/raggable-tree/custom-adapter/README.md) (un nouveau langage).

Pour un usage en DI :

```csharp
services.AddRaggableTree(new RaggableTreeOptions
{
    Embedding = new EmbeddingOptions { Provider = EmbeddingProviderKind.OpenAI, ApiKey = apiKey },
});
services.AddRaggableTreeTools();   // les 15 tools agents (Orkeon.Tools.Analysis)
```

`AddRaggableTree` (`Orkeon.Analysis.DependencyInjection`) enregistre les cinq
adaptateurs de langage, les cinq fingerprinters de framework, le summarizer, le
provider d'embedding, le singleton `InMemoryRaggableStore` (exposé aussi comme
`IRaggableStore` et `IIndexInvalidation`), l'`IndexFreshnessService`, le provider de
diff git, l'`IRaggableTreeEventBus`, l'`ICodebaseContextProvider` et les validateurs de
citations. Le runner host appelle plutôt `AddRaggableTreeWithLogging(options)`
(`Orkeon.Infrastructure`) : mêmes enregistrements, avec le trafic HTTP d'embedding
OpenAI/Ollama acheminé vers le journal des échanges LLM. Le REPL `orkeon-repl` appelle
`AddRaggableTree` avec des embeddings on-device et ne lit aucune section de configuration.

## Configuration

La seule surface de configuration que les hôtes livrés lient est la **section
appsettings `RaggableTree`**, lue clé par clé par le runner host (`orkeon run` et les
autres runners) — il n'existe pas de clé `raggableTree:` dans le YAML de crew, et le REPL
ne lie rien. Le RaggableTree est **actif par défaut** dans les deux :

```jsonc
{
  "RaggableTree": {
    "Enabled": true,                    // false désactive entièrement la feature (opt-out du runner)
    "Embedding": {
      "Provider": "LocalSmartComponents", // le défaut du runner — ou None | OpenAI | Ollama (Onnx est un bras no-op réservé)
      "Model": "",                      // vide → le défaut du provider
      "ApiKey": null,                   // OpenAI uniquement
      "BaseUrl": null,                  // OpenAI / Ollama
      "Dimensions": null,               // OpenAI (défaut 1536) / Ollama (défaut 768)
      "MaxTextChars": null              // plafond par texte côté OpenAI
    }
  }
}
```

Le runner host lit aussi `Exclude`, `RootAlias`, `IncludeStatements`, `IndexMode` et
`EnrichWithLlm` dans `RaggableTreeOptions`, mais **l'index ne les voit jamais** : la
construction prend ces réglages dans chaque appel `index_codebase` (`exclude`,
`root_alias`, `include_statements`, `enrich_with_llm`, `respect_gitignore`,
`languages` — voir le [catalogue de tools](#catalogue-de-tools)). `IndexMode` n'est
consommé par rien (voir la note historique plus bas), et `EnrichWithLlm` n'a d'effet
qu'accompagné d'un `Summarizer.Provider` autre que `None`, que le runner host ne
positionne jamais — aucun hôte livré ne génère donc de résumés LLM.

Les langages ne se configurent **jamais** : ils sont auto-détectés depuis la codebase
et scellés par appel `index_codebase` (`RaggableTreeOptions.Languages` n'est lu par
rien). Le reste de `RaggableTreeOptions` est accessible en C# via
`AddRaggableTree(options)` : `Summarizer` (`Provider` = `None` | `Anthropic` — ce
dernier enregistre `LlmNodeSummarizer` sur l'`ILlmProvider` de l'hôte — plus `Model`,
défaut `claude-haiku-4-5`, et `Concurrency`, défaut 5) et `ValidateCitations` (défaut
`true`, enregistre les validateurs de citations `ICitationBlockValidator` /
`IInlineFqnValidator`). Les groupes d'options `VectorStore`/`Cache` existent sur le
record mais ne sont consommés par rien. À noter : le défaut C# de `Embedding.Provider`
est `None` ; seul le runner host prend `LocalSmartComponents` par défaut.

Tous les champs sont des `record` immuables dans `Orkeon.Analysis.DependencyInjection.RaggableTreeOptions`.

## Pipeline en six phases

1. **Discovery** (`IFileSystemDiscoverer`) — parcours récursif, détection de packages via markers (`package.json`, `*.csproj`, `pyproject.toml`, `go.mod`, `Cargo.toml`), exclusion via patterns et `.gitignore`.
2. **Parse + Extract** (`UniversalSemanticMapper` + `ILanguageAdapter`) — Tree-sitter parse → application des queries de l'adaptateur → nœuds L2 (module) et L3 (symbole) avec `Fqn`, `Signature`, `SourceSnippet`, `Sha256` ; statements L4 (`StatementExtractor`) uniquement quand l'appel positionne `include_statements`.
3. **Dependency resolution** (`DependencyGraphBuilder` + `IReferenceResolver`) — résolution des imports, appels, héritage, implémentations en arêtes (`EdgeKind.Imports`, `Calls`, `Extends`, `Implements`). Les FQN non résolus deviennent des `UnresolvedRef`.
4. **Fingerprinting** (`IFrameworkFingerprinter`) — application des règles par décorateur/annotation (NestJS, Angular, ASP.NET, Flask, FastAPI) : pose de tags comme `http-endpoint`, `guard`, `service` sur les nœuds concernés.
5. **Enrichissement** — optionnellement un résumé LLM (`INodeSummarizer`, quand l'appel positionne `enrich_with_llm` et qu'un summarizer est enregistré), composition du `EmbeddingText` (`IEmbeddingTextComposer`), puis embedding batch (`IEmbeddingProvider`, sauté quand aucun n'est enregistré).
6. **Persistance** — l'arbre vit dans `InMemoryRaggableStore` (`index_codebase` en remplace le contenu), qui répond aussi à toutes les requêtes. Quand un `IVectorStoreProvider` est enregistré, `IndexAsync` reçoit en plus un miroir des embeddings — aucun hôte livré n'en enregistre (`MemoryProviderVectorStoreAdapter`, `Orkeon.Analysis.Vectors`, adapte n'importe quel `IMemoryProvider` pour un hôte qui le souhaite), et rien ne relit ce miroir. Le sérialiseur JSON `RaggableTreeCache` existe mais n'est câblé par aucun hôte livré.

Chaque phase rapporte sa progression (`IndexBuildPhase` : Discovery, Parse, Resolve, Enrich, Embed, Persist) à un `IProgress<IndexBuildProgress>` optionnel enregistré par l'hôte (le REPL le branche sur la barre de progression de sa ligne d'état).

La réindexation incrémentale (`IncrementalReindexEngine`) repart de la phase 2 pour les fichiers changés uniquement, réutilise les nœuds des fichiers inchangés, et ne lance les phases 4-6 que sur les `newNodes`.

## Catalogue de tools

| Tool | Usage | Requête type |
|------|-------|---------------|
| `index_codebase` | Construit l'index initial | `{ "root_path": "/src" }` |
| `incremental_reindex` | Met à jour l'index après modif de fichiers (liste explicite, ou plage git `from_commit`/`to_commit`) | `{ "changed_files": ["src/a.ts"] }` |
| `index_status` | Liste les racines virtuelles indexées (date, comptes de nœuds/arêtes) | `{}` |
| `is_path_indexed` | Vérifie si un chemin est couvert par une racine indexée | `{ "virtual_path": "/src/app/main.ts" }` |
| `codebase_map` | Vue d'ensemble : packages, compte de fichiers/symboles | `{ "level": "L2_Module" }` |
| `package_summary` | Résumé d'un package (modules, dépendances, symboles clés) | `{ "fqn": "app::core" }` |
| `symbol_detail` | Détail d'un symbole (signature, doc, callers, callees) | `{ "fqn": "app::UserService::create" }` |
| `symbol_source` | Extrait du code source (signature, body, span complet) | `{ "fqn": "...", "mode": "SignatureAndBody" }` |
| `codebase_search` | Recherche hybride — embedding + BM25 fusionnés (voir [plus bas](#recherche-hybride)) | `{ "query": "session expiration", "top_k": 10 }` |
| `dependency_graph` | Graphe de dépendances à une portée (L1/L2/L3), rendu Mermaid/DOT | `{ "scope": "L2_Module", "edge_kinds": ["Imports", "Calls"] }` |
| `sub_graph` | Expansion BFS autour de seeds | `{ "seeds": ["pkg::X"], "depth": 3 }` |
| `flow_trace` | Chemins d'appel entre deux FQN | `{ "from": "A", "to": "B", "max_paths": 5 }` |
| `impact_analysis` | Impacts transitifs d'un changement | `{ "target": "...", "direction": "Backward" }` |
| `complexity_report` | Top-N par métrique (`Cyclomatic`, `NestingDepth`, `FanOut`, `LoC`, `Callers`) | `{ "metric": "Cyclomatic", "top_n": 20 }` |
| `statement_query` | Requêtes structurelles L4 (if, try, return...) — exige un index construit avec `include_statements` | `{ "parent_fqns": ["..."], "kinds": ["TryCatch"] }` |

Les 15 tools sont enregistrés via `AddRaggableTreeTools` (`Orkeon.Tools.Analysis.DependencyInjection.RaggableToolsExtensions`).

`index_codebase` porte les réglages de construction à chaque appel
(`IndexCodebaseRequest`) : `root_path` (obligatoire, un chemin virtuel), `languages`
(vide = auto-détection), `exclude` (défaut `node_modules`, `dist`, `.git`, `bin`,
`obj`), `respect_gitignore` (défaut `true`), `include_statements` (défaut `false`),
`enrich_with_llm` (défaut `false`), `root_alias` (remplace le préfixe de racine
virtuelle de chaque FQN) et `embedding_model`. `incremental_reindex` accepte les mêmes
`root_path`, `languages` et `enrich_with_llm`.

## Stratégies d'agents

Le framework expose `ICodebaseContextProvider` qui produit un résumé compact du codebase (packages, top complexité, top couplage, patterns détectés) adapté au system prompt d'un agent. Trois formats : `markdown` (défaut, ~300 tokens), `compact` (~150 tokens), `json` (~400 tokens). **Aucun composant du framework ne l'appelle automatiquement** — il est enregistré par `AddRaggableTree` et l'hôte le résout et injecte le résumé là où il le souhaite.

## Recherche hybride

`codebase_search` (et `IRaggableStore.SemanticSearchAsync`) est **hybride** depuis le
chantier fraîcheur : un classement cosinus par embeddings et un classement lexical BM25
sont fusionnés par Reciprocal Rank Fusion (`SemanticQuery.Mode` : `Hybrid` par défaut /
`Vector` / `Lexical`). Le versant lexical utilise un **tokenizer conscient du code**
(`CodeTokenizer`) : les identifiants sont découpés aux frontières camelCase / snake_case /
chiffres et indexés à la fois en sous-tokens et en entier
(`getUserById` → `get user by id getuserbyid`), de sorte que les requêtes par identifiant
exact gardent leur signal fort tandis que les requêtes conceptuelles gagnent en rappel.
Chaque `SearchHit` porte `MatchOrigin` (`hybrid`/`vector`/`bm25`) ; les scores hybrides
sont des **agrégats de rangs** RRF (pas des similarités — ne jamais les comparer entre
origines). Sans embedder câblé, `Hybrid` dégrade vers `Lexical` au lieu de renvoyer vide ;
un `Vector` explicite conserve le contrat historique.

## Fraîcheur (coordination édition ↔ recherche)

L'index reste fidèle à un workspace en cours d'édition grâce à une conception
**mark-dirty + réindexation paresseuse** :

- **Hook d'écriture** — `FileWriteTool` prend un `IIndexInvalidation` optionnel
  (enregistré par `AddRaggableTree`, implémenté par le store) : chaque écriture réussie
  marque son chemin comme sale. O(1), synchrone, no-op hors des racines indexées.
- **Passe paresseuse** — les tools de lecture (`codebase_search`, `symbol_source`,
  `flow_trace`, `codebase_map`) appellent `IndexFreshnessService.EnsureFreshAsync` avant
  de répondre : l'ensemble sale ∪ les **changements du working tree git** (attrape les
  éditions via `shell_command`) est réindexé incrémentalement — groupé, single-flight,
  avec debounce (une sonde git propre fait foi pendant 2 s). Les réponses de
  `codebase_search` rapportent `refreshed_files` ; `index_status` rapporte
  `dirty_count`/`dirty_paths`, de sorte que le « périmé » est observable.
- **Sûreté du store** — `InMemoryRaggableStore` détient un `ReaderWriterLockSlim` : les
  recherches énumèrent en sécurité PENDANT une réindexation incrémentale (auparavant une
  recherche concurrente pouvait lever sur les dictionnaires mutés). Le rafraîchissement
  publie `RaggableTreeUpdated` sur l'`IRaggableTreeEventBus`.
- **Barrière d'échec** — un rafraîchissement en échec sert l'index courant (périmé) et
  conserve la dette de saleté pour la tentative suivante : un résultat périmé vaut mieux
  qu'une recherche morte.

Note historique : une version antérieure de ce document décrivait trois modes de
synchronisation pilotés par le watcher (`frozen`/`live`/`breakOnChange` via
`RaggableTreeIndexMode`). Ces modes n'ont jamais été consommés par aucun code — l'enum
existait, rien ne la lisait. La conception de fraîcheur ci-dessus remplace cette fiction ;
`ICodebaseWatcher` (`FileSystemWatcherCodebaseWatcher`, qu'aucun hôte livré n'enregistre)
reste disponible pour les hôtes qui veulent une invalidation en push
par-dessus la passe paresseuse.

## Extensibilité

### Ajouter un langage

Implémenter `ILanguageAdapter` (`Orkeon.Analysis.Abstractions.Interfaces`) : fournir `LanguageName`, `FileExtensions`, les sept queries Tree-sitter (`DeclarationQuery`, `ImportQuery`, `CallQuery`, `InheritanceQuery`, `DocCommentQuery`, `DecoratorQuery`, `StatementQuery`), les mappings `MapNodeKind` et `MapStatementKind`, et les extracteurs `ExtractSignature` / `ResolveImportPath` (`RefineKind`, `ResolveDocComment`, `ExtractName` et `GetExtraModifiers` ont une implémentation par défaut). Enregistrer l'adaptateur via DI (`services.AddSingleton<ILanguageAdapter, MyAdapter>()`) : le builder et le moteur incrémental prennent tous les `ILanguageAdapter` enregistrés, à côté des cinq intégrés (`LanguageAdapterFactory`). Un adaptateur Java exécutable vit dans [`examples/raggable-tree/custom-adapter/`](https://github.com/Orkeon/orkeon/blob/main/examples/raggable-tree/custom-adapter/README.md).

### Ajouter un fingerprinter

Créer un `IReadOnlyList<FingerprintRule>` listant les décorateurs/annotations à matcher avec leurs tags puis construire un `FrameworkFingerprinter(framework, rules)`. Voir `Orkeon.Analysis.Fingerprinters.NestJsRules` comme référence (15 règles).

### Ajouter un embedding provider

Implémenter `IEmbeddingProvider.EmbedBatchAsync(texts, ct)` → `IReadOnlyList<ReadOnlyMemory<float>>`. Trois implémentations de référence : `OpenAIEmbeddingProvider`, `OllamaEmbeddingProvider`, et `LocalEmbeddingProvider` (cf. section ci-dessous). La méthode d'interface par défaut `EmbedAsync(nodes, model, ct)` (sur `IEmbeddingProvider`, `Orkeon.Analysis.Abstractions.Interfaces`) remplit `node.Embedding` à partir du `EmbeddingText` composé.

## Embedding providers

Cinq membres existent sur `EmbeddingProviderKind` :

| Provider | Réseau | Clé API | Dims | Notes |
|----------|--------|---------|------|-------|
| `None` | n/a | n/a | n/a | Aucun embedding (le défaut C#) : embedding et miroir sautés, `codebase_search` dégrade vers BM25 |
| `Onnx` | non | non | — | **Réservé** — actuellement un bras d'enregistrement no-op |
| `OpenAI` | requis | requis | 1536 (`text-embedding-3-small`) | Qualité maximale, MTEB ~62.3 |
| `Ollama` | local (HTTP) | non | dépend du modèle (768 supposé pour le `nomic-embed-text` par défaut) | Daemon Ollama / llama.cpp local requis (défaut `http://localhost:11434/`) |
| `LocalSmartComponents` | non | non | 384 (BGE-micro-v2) | In-process, ONNX CPU, démarrage à froid ~200 ms — le défaut du runner host |

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

Pour pointer vers un autre modèle ONNX que le BGE-micro-v2 embarqué, déclarer un mount VFS lecture seule et passer un chemin **virtuel** dans `LocalEmbeddingOptions.ModelPath` (`Orkeon.Analysis.Abstractions.DependencyInjection`). Les chemins physiques (`C:/...`, `/var/...`) sont rejetés par `IFileSystemService` — voir [vfs-compliance.md](./vfs-compliance.md). Ces options se positionnent **en C#** : le runner host ne lit pas de section `RaggableTree:Embedding:Local` et enregistre toujours le modèle embarqué avec ses défauts.

```jsonc
{
  "Orkeon": {
    "FileSystem": {
      "Mounts": [
        "C:\\data\\embedding-models:/models:ro"     // <physique>:<virtuel>:<droits>
      ]
    }
  }
}
```

```csharp
services.AddOrkeonLocalEmbeddings(new LocalEmbeddingOptions
{
    ModelPath = "/models/my-custom.onnx",   // chemin virtuel — résolu par IFileSystemService
    MaxConcurrency = 8,                     // défaut : Environment.ProcessorCount
    MaxTextChars = 2000,                    // défaut 2000 (BGE-micro-v2 : fenêtre de 512 tokens)
});
services.AddRaggableTree(new RaggableTreeOptions
{
    Embedding = new EmbeddingOptions { Provider = EmbeddingProviderKind.LocalSmartComponents },
});
```

Sans `AddOrkeonLocalEmbeddings`, `EmbeddingOptions.Local` porte les mêmes options et `AddRaggableTree` instancie le provider par réflexion — ce qui échoue bruyamment si l'assembly `Orkeon.Tools.Embeddings.Local` n'est pas référencée par l'hôte.

Les licences du modèle et du package upstream sont tracées dans [`THIRD-PARTY-NOTICES.md`](../../../THIRD-PARTY-NOTICES.md). Un exemple console minimal vit dans [`examples/local-embeddings/`](https://github.com/Orkeon/orkeon/tree/main/examples/local-embeddings).

### Ajouter un vector store

Implémenter `IVectorStoreProvider` (`IndexAsync`, `SearchAsync`, `DeleteAsync`) et l'enregistrer : le builder et le moteur incrémental y reflètent alors les embeddings (`DeleteAsync` pour les nœuds supprimés à la réindexation). L'implémentation livrée est `MemoryProviderVectorStoreAdapter` (`Orkeon.Analysis.Vectors`), qui projette `VectorDocument`/`VectorMetadata` sur n'importe lequel des six providers mémoire (`Redis`, `SQLite`, `ChromaDB`, `Pinecone`, `LanceDB`, `InMemory`) — la construire sur l'`IMemoryProvider` de son choix. Les requêtes continuent de s'exécuter sur `InMemoryRaggableStore` : le miroir est en écriture seule en V1.

## Limitations V1

- Pas de support des macros C++ / templates pleinement résolus, ni des méta-programmations Ruby/Elixir.
- Le résolveur d'imports est heuristique pour les langages dynamiques (Python, TypeScript) : les re-exports et les monkey patches peuvent produire des `UnresolvedRef`.
- Le summarizer se configure uniquement en C# (`Summarizer.Provider` + un `ILlmProvider`) ; les embeddings OpenAI/Ollama exigent leur endpoint, alors que le défaut on-device du runner host ne demande rien. Le pipeline fonctionne sans embeddings (`codebase_search` classe alors par BM25 seul).
- L'index est local au processus : `InMemoryRaggableStore` est reconstruit par `index_codebase` dans chaque processus, et le miroir vector store optionnel n'est jamais relu.
- `FileSystemWatcherCodebaseWatcher` (l'implémentation d'`ICodebaseWatcher`, qu'aucun hôte livré n'enregistre) se base sur `System.IO.FileSystemWatcher` — sous Linux/WSL, les événements de rename peuvent arriver décomposés (observés comme Deleted puis Created).
- Le cache JSON (`RaggableTreeCache`, câblé par aucun hôte livré) n'est pas versionné : une mise à jour d'adapter peut invalider les caches existants, qui sont alors reconstruits à la demande.

---

> **Voir aussi** : [ADR RaggableTree](./raggable-tree-adr.md) · [Inventaire des outils](../tools/inventory.md) · [Retour à l'index](../INDEX.md)
