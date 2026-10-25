> 🇬🇧 [English version](../../architecture/raggable-tree.md)

> **Voir aussi** : [Système de mémoire](./memory-system.md) · [Référence YAML](./yaml-schema.md) · [Inventaire des outils](../tools/inventory.md) · [Retour à l'index](../INDEX.md)

# RaggableTree

Graphe sémantique multi-langage du code source : parsing Tree-sitter, enrichissement embeddings, stockage vectoriel, et exposition aux agents via 19 tools. Remplace la lecture de fichiers brute par un index structuré à cinq niveaux stratifiés (L0 monorepo → L1 package → L2 module → L3 symbole → L4 statement) plus la couche d'arêtes entre eux.

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
services.AddRaggableTreeTools();   // les 28 tools agents (Orkeon.Tools.Analysis)
```

`AddRaggableTree` (`Orkeon.Analysis.DependencyInjection`) enregistre les douze
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

La section porte ces deux clés et rien d'autre : le runner refuse de démarrer sur
toute autre clé sous `RaggableTree`, en la nommant. Ce que couvre un index se règle à
chaque appel `index_codebase` (`exclude`, `root_alias`, `enrich_with_llm`,
`respect_gitignore`, `languages` — voir le [catalogue de tools](#catalogue-de-tools)),
jamais par l'hôte. Les langages en particulier ne se configurent **jamais** : ils sont
auto-détectés depuis la codebase et scellés par appel.

Le reste de `RaggableTreeOptions` est accessible en C# via `AddRaggableTree(options)` :
`Summarizer` (`Provider` = `None` | `Anthropic` — ce dernier enregistre
`LlmNodeSummarizer` sur l'`ILlmProvider` de l'hôte, et un appel `index_codebase` avec
`enrich_with_llm` résume alors chaque nœud ; plus `Model`, défaut `claude-haiku-4-5`, et
`Concurrency`, défaut 5) et `ValidateCitations` (défaut `true`, enregistre les
validateurs de citations `ICitationBlockValidator` / `IInlineFqnValidator`). Le runner
host ne positionne jamais de résumeur : aucun hôte livré ne génère donc de résumés LLM.
À noter : le défaut C# de `Embedding.Provider` est `None` ; seul le runner host prend
`LocalSmartComponents` par défaut.

Tous les champs sont des `record` immuables dans `Orkeon.Analysis.DependencyInjection.RaggableTreeOptions`.

## Pipeline en six phases

1. **Discovery** (`IFileSystemDiscoverer`) — parcours récursif, détection de packages via markers (`package.json`, `*.csproj`, `pyproject.toml`, `go.mod`, `Cargo.toml`, `pom.xml`, `build.gradle`, `build.gradle.kts`, `composer.json`, `Gemfile`, `CMakeLists.txt`), exclusion via patterns et `.gitignore`.
2. **Parse + Extract** (`UniversalSemanticMapper` + `ILanguageAdapter`) — Tree-sitter parse → application des queries de l'adaptateur → nœuds L2 (module) et L3 (symbole) avec `Fqn`, `Signature`, `SourceSnippet`, `Sha256` ; statements L4 (`StatementExtractor`), extraits à chaque construction.
3. **Dependency resolution** (`DependencyGraphBuilder` + `IReferenceResolver`) — résolution des imports, appels, héritage, implémentations en arêtes (`EdgeKind.Imports`, `Calls`, `Extends`, `Implements`). Les FQN non résolus deviennent des `UnresolvedRef`.
4. **Fingerprinting** (`IFrameworkFingerprinter`) — application des règles par décorateur/annotation (NestJS, Angular, ASP.NET, Flask, FastAPI) : pose de tags comme `http-endpoint`, `guard`, `service` sur les nœuds concernés.
5. **Enrichissement** — optionnellement un résumé LLM (`INodeSummarizer`, quand l'appel positionne `enrich_with_llm` et qu'un summarizer est enregistré), composition du `EmbeddingText` (`IEmbeddingTextComposer`), puis embedding batch (`IEmbeddingProvider`, sauté quand aucun n'est enregistré).
6. **Persistance** — l'arbre vit dans `InMemoryRaggableStore` (`index_codebase` en remplace le contenu), qui répond aussi à toutes les requêtes. Quand un `IVectorStoreProvider` est enregistré, `IndexAsync` reçoit en plus un miroir des embeddings — aucun hôte livré n'en enregistre (`MemoryProviderVectorStoreAdapter`, `Orkeon.Analysis.Vectors`, adapte n'importe quel `IMemoryProvider` pour un hôte qui le souhaite), et rien ne relit ce miroir. Le sérialiseur JSON `RaggableTreeCache` existe mais n'est câblé par aucun hôte livré.

Chaque phase rapporte sa progression (`IndexBuildPhase` : Discovery, Parse, Resolve, Enrich, Embed, Persist) à un `IProgress<IndexBuildProgress>` optionnel enregistré par l'hôte (le REPL le branche sur la barre de progression de sa ligne d'état).

La réindexation incrémentale (`IncrementalReindexEngine`) repart de la phase 2 pour les fichiers changés uniquement, réutilise les nœuds des fichiers inchangés, et ne lance les phases 4-6 que sur les `newNodes`.

### Documents Markdown

Markdown est un langage de premier rang de l'index, hors du chemin Tree-sitter : les fichiers `.md` ou `.markdown` sont découverts comme langage `markdown` et analysés avec Markdig par `MarkdownDocumentMapper` (`Orkeon.Analysis.Adapters`), une lecture par fichier. Chaque fichier produit :

- un nœud `Section` par titre (niveaux 1 à 6, FQN imbriqué `<fichier>::Titre/Sous`, niveau L3) dont la plage court jusqu'au titre suivant de même niveau ou supérieur ;
- un nœud `CodeFence` par bloc délimité (le langage est son tag ; un bloc `mermaid` porte aussi le tag `mermaid`), un nœud `Link` par lien (tag `target:<cible brute>`, plus `resolved:<fqn>` quand `MarkdownDocumentMapper.ResolveLinksAsync` retrouve la cible dans le store, par chemin ou par nom local ; un lien non résolu ne garde que sa cible brute) et un nœud `FrontMatter` pour un bloc YAML initial (les clés de premier niveau sont ses tags), tous de niveau L4.

Les plages sont des offsets en **octets** UTF-8 (`NodeRange.StartByte`/`EndByte`), comme pour les autres langages. Les quatre nouvelles valeurs de `UniversalNodeKind` (`Section`, `CodeFence`, `Link`, `FrontMatter`) sont ajoutées après `Unknown` car le graphe persisté stocke l'enum par nom. `codebase_search` restreint au langage `markdown` (son option `FilterLanguages`) renvoie les sections. Limites : un fichier `.md` n'a pas de nœud module dans l'arbre, donc un lien fichier vers fichier ne se résout que contre un store qui en contient un, et le build n'appelle pas encore `ResolveLinksAsync`.

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
| `access_cost` | Coût d'accès aux ports (lectures, écritures, appels en boucle, filtres en mémoire) des membres par `fqns` ou `changed_files`, restreint à `roots` | `{ "changed_files": ["/src/Orders/A.cs"], "roots": ["/src/Orders"] }` |
| `statement_query` | Requêtes structurelles L4 (if, try, return...) — chaque index porte les statements | `{ "parent_fqns": ["..."], "kinds": ["TryCatch"] }` |
| `node_outline` | Membres d'un fichier C# ou TypeScript avec sélecteur, genre, plages d'octets et de lignes | `{ "path": "/workspace/Calc.cs" }` |
| `node_read` | Un membre par sélecteur : texte, plage et SHA-256, lu sans coupure | `{ "path": "/workspace/Calc.cs", "selector": "Demo.Calc.Add(int)" }` |
| `node_replace` | Remplace un nœud par sélecteur et SHA-256 attendu (classe Edit) | `{ "path": "/workspace/Calc.cs", "selector": "Demo.Calc.Add(int)", "expected_hash": "<sha256>", "text": "..." }` |
| `node_insert` | Ajoute un nœud `inside`, `before` ou `after` un sélecteur d'ancrage (classe Edit) | `{ "path": "/workspace/Calc.cs", "selector": "Demo.Calc", "position": "inside", "text": "public int Zero() => 0;" }` |
| `edit_batch` | Plusieurs opérations `node_replace` / `node_insert` sur plusieurs fichiers en une transaction (classe Edit) | `{ "operations": [{ "kind": "node_replace", "path": "/workspace/Calc.cs", "selector": "Demo.Calc.Add(int)", "expected_hash": "<sha256>", "text": "..." }], "validate": "parse" }` |

Les 28 tools sont enregistrés via `AddRaggableTreeTools` (`Orkeon.Tools.Analysis.DependencyInjection.RaggableToolsExtensions`).

`index_codebase` porte les réglages de construction à chaque appel
(`IndexCodebaseRequest`) : `root_path` (obligatoire, un chemin virtuel), `languages`
(vide = auto-détection), `exclude` (défaut `node_modules`, `dist`, `.git`, `bin`,
`obj`), `respect_gitignore` (défaut `true`), `enrich_with_llm` (défaut `false` ; exige
un résumeur enregistré), `root_alias` (remplace le préfixe de racine virtuelle de chaque
FQN) et `embedding_model` — exactement ce que lit la construction. Les statements sont
toujours extraits, et un argument que la requête ne déclare pas est ignoré.
`incremental_reindex` accepte les mêmes `root_path`, `languages` et `enrich_with_llm`.

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
- **Borne** — la réindexation remplace les nœuds et arêtes de chaque fichier modifié et
  réutilise tous les autres nœuds : son coût est proportionnel aux fichiers modifiés, pas
  au dépôt ; un fichier de 2 000 lignes se réindexe en moins de 2 s. `incremental_reindex`
  rapporte les totaux de l'index (`node_count`, `edge_count`), les fichiers analysés et
  la durée mesurée par le moteur.
- **Barrière d'échec** — un rafraîchissement en échec sert l'index courant (périmé) et
  conserve la dette de saleté pour la tentative suivante : un résultat périmé vaut mieux
  qu'une recherche morte.

Note historique : une version antérieure de ce document décrivait trois modes de
synchronisation pilotés par le watcher (`frozen`/`live`/`breakOnChange` via
`RaggableTreeIndexMode`). Ces modes n'ont jamais été consommés par aucun code — l'enum
existait, rien ne la lisait, et elle a été supprimée avec la clé `IndexMode` (GAP-15). La conception de fraîcheur ci-dessus remplace cette fiction ;
`ICodebaseWatcher` (`FileSystemWatcherCodebaseWatcher`, qu'aucun hôte livré n'enregistre)
reste disponible pour les hôtes qui veulent une invalidation en push
par-dessus la passe paresseuse.

## Sélecteurs

`node_outline` et `node_read` adressent une déclaration d'un fichier par un sélecteur, sous l'une de trois formes :

- **Lisible** : `Ns.Type.Member(paramTypes)`, p. ex. `Demo.Calc.Add(int)`. Les types de paramètres sont optionnels et requis seulement quand des surcharges existent ; une surcharge ambiguë est une erreur qui liste les sélecteurs candidats.
- **Markdown** : `Section/Sub` (chemin de titres), servi pour les fichiers `.md` et `.markdown` : une section va de son titre à la ligne précédant le titre suivant de même niveau ou supérieur. Des titres frères de même nom sous un même parent se distinguent par un ordinal à partir de 1 sur n'importe quel segment : `A/Notes[2]`, `A[2]/Notes[1]`. Sans ordinal, un chemin ambigu est une erreur qui liste `A/Notes[1]`, `A/Notes[2]` ; le plan montre la forme avec ordinal pour les doublons (et `A[2]/Notes` pour leurs descendants) et la forme simple pour un nom unique. Un ordinal égal à 0 ou au-delà des frères est une erreur de sélecteur introuvable.
- **Repli** : `@ts:<node-path>`, le chemin tree-sitter `root/<index>/<kind>/...` (`index` la position à partir de 0 parmi les enfants nommés), pour un nœud que la forme lisible ne peut pas nommer.

Un sélecteur résout exactement un nœud ou échoue en listant les candidats. L'étendue d'un nœud inclut ses commentaires de tête et ses attributs ou décorateurs.

Grammaires livrées (`LanguageRegistry.Shipped`) : typescript (`.ts`, `.tsx`), python (`.py`), csharp (`.cs`), go (`.go`), rust (`.rs`), javascript (`.js`, `.jsx`, `.mjs`, `.cjs`), java (`.java`), c (`.c`), cpp (`.cpp`, `.cc`, `.cxx`, `.hpp`, `.hh`, `.hxx`, `.h`), php (`.php`), ruby (`.rb`), bash (`.sh`, `.bash`), markdown (`.md`, `.markdown`). Un en-tête `.h` est lu en C++, sur-ensemble qui parse aussi les en-têtes C. Le registre charge aussi agda, css, embedded-template, haskell, html, jsdoc, json, julia, ql, razor, scala et verilog, parsées à la demande seulement : aucun adaptateur ne les indexe et la découverte ne réclame pas leurs fichiers. OCaml (pas de build macOS dans le paquet) et les grammaires swift, toml et tsq, propres à Windows, sont élaguées à la publication. Les déclarations sont adressables aujourd'hui en C# et TypeScript, les sections en Markdown ; les autres grammaires livrées sont refusées par leur nom ; un fichier dont l'extension n'est réclamée par aucune grammaire est refusé par son nom.

`node_outline` liste les membres avec sélecteur, genre, plage d'octets et plage de lignes. `node_read` renvoie le texte d'un membre, sa plage et le SHA-256 de ses octets, sans coupure : l'outil porte sa propre borne de résultat (`node_read` = 256 000 caractères dans `AgentDefaults`), la coupure par défaut des résultats d'outil ne s'applique donc pas.

## Éditeur structurel

`node_replace` remplace un nœud (sélecteur de `node_outline`, hash de `node_read`) via `StructuralEditEngine` :

- **Épissure d'octets** : seuls les octets du nœud changent ; le reste du fichier est identique à l'octet près.
- **Refus d'un hash périmé** : le SHA-256 courant du nœud est comparé à `expected_hash` avant toute écriture ; un écart est refusé par une `StaleHashException` qui nomme le hash attendu et le hash réel.
- **Validation par ré-analyse** : le résultat est analysé de nouveau et les diagnostics comparés par position ; un remplacement est refusé s'il introduit un diagnostic absent avant (Markdown n'a pas de validation d'analyse).
- **Étendue remplacée** : elle inclut les commentaires et attributs de tête du nœud, donc un remplacement qui les omet les supprime. Une section Markdown remplace aussi ses sous-sections.
- **Fins de ligne** du remplacement normalisées sur celles du fichier.
- **Écriture** : protégée par une copie de sauvegarde restaurée en cas d'échec. Elle n'est pas atomique.
- Un refus (hash périmé, erreur d'analyse, montage en lecture seule, chemin hors de la racine de travail de l'agent) est un résultat en échec, fichier intact ; une écriture réussie marque l'index sale.

## Opérations d'intention

`node_ensure` et le champ `op` de `edit_batch` appliquent une opération idempotente ; chacune est un no-op qui n'écrit rien et renvoie `applied: false` quand sa cible est déjà satisfaite.

| Opération | Paramètres | Règle d'idempotence | Règle de placement |
|---|---|---|---|
| `ensure_using` | `name` | le using est déjà présent | dans le bloc des using, à sa place triée |
| `ensure_import` | `name`, `module` | l'import est déjà présent | dans le bloc des imports |
| `ensure_field` | `selector` (type), `text` | un membre de ce nom existe | près des champs du type |
| `ensure_member` | `selector` (type), `text` | un membre de ce nom existe | près des membres du type |
| `ensure_section` | `name` (chemin de titres), `level?` | chaque titre du chemin existe | titres manquants ajoutés en fin de fichier, chacun au niveau de sa profondeur |
| `insert_under_heading` | `selector` (titre), `text` | aucune : ajoute toujours | en fin de section, avant le titre suivant de même niveau ou supérieur |
| `replace_mermaid` | `selector` (section), `index` (base 0), `text` | le corps du bloc vaut déjà `text` | seul le corps du n-ième bloc `mermaid` (``` ou ~~~) de la section et de ses sous-sections change ; moins de blocs : refusé, fichier intact |

## Types de graphe

`GraphKindNames` (`Orkeon.Analysis.Abstractions`) fait correspondre des noms de workflow en minuscules aux énumérations existantes.

| Nom d'arête | `EdgeKind` |
|---|---|
| `contains` | `Contains` |
| `calls` | `Calls` |
| `imports` | `Imports` |
| `inherits` | `Extends` + `Implements` |
| `tests` | `Tests` |
| `documents` | `Documents` |
| `project_depends` | `ProjectDepends` |

| Nom de nœud | Nœud du graphe |
|---|---|
| `file` | nœud fichier (`Module`) |
| `project` | nœud `Package` d'un csproj ou package.json marqueur |
| `namespace` | `Namespace` |
| `type` | classe, interface, trait, struct, enum, record, type |
| `member` | fonction, méthode, constructeur, destructeur, opérateur, propriété, champ, constante, variable |
| `doc_section` | `Section` |
| `test` | nœud `Method` portant le tag `test` |

`EdgeKind` gagne `Tests`, `Documents` et `ProjectDepends` (flags `1<<5` à `1<<7`). Il n'y a pas de genre `references` : rien ne le produit. Les arêtes `documents` relient une section Markdown aux symboles qu'elle nomme (voir [Requêtes sur le graphe](#requêtes-sur-le-graphe)).

### Utiliser les noms

- `edge_kinds` et `node_kinds` de `dependency_graph` et `sub_graph` prennent les noms de workflow en minuscules. Les noms d'énumération historiques (`Imports`) restent acceptés, sans tenir compte de la casse. Un nom inconnu est refusé, en le nommant. Les réponses gardent les noms d'énumération.
- Quand `node_kinds` est donné, `dependency_graph` respecte toujours `Scope` : les genres et les tags passent dans la requête de nœuds, et `MaxNodes` borne le résultat.

### Construction du graphe

- `ProjectGraphBuilder` lit chaque csproj déjà trouvé par le découvreur (une lecture par csproj) et transforme chaque `ProjectReference` en arête `project_depends` (`\` et `..` sont normalisés), plus des arêtes `Contains` du parent vers l'enfant.
- `TestEdgeLinker` tague `test` les méthodes `[Fact]` et `[Theory]` (et le propriétaire d'un appel TypeScript `test` ou `it`), et relie par des arêtes `tests` les symboles qu'elles appellent dans les projets référencés.
- Les deux s'exécutent à la construction complète et à la réindexation incrémentale, où ils passent après les fingerprinters.

### Limites

- Le FQN ne porte que le nom : une arête `tests` vers un membre surchargé relie toutes les surcharges.
- Un test qui appelle un symbole récemment ajouté dans un autre fichier n'a pas d'arête `tests` avant une réindexation complète.
- Un callback de test TypeScript n'a pas de nœud : ses appels appartiennent au symbole englobant, sinon au nœud fichier. Aujourd'hui seul le tag est posé sur le nœud fichier ; les arêtes `tests` d'un test TypeScript au niveau fichier ne sont pas encore produites.

## Requêtes sur le graphe

Quatre tools de classe Read répondent à des questions sur le graphe sans lire le code. Tous reportent dans `unresolved` les noms qu'ils n'ont pas pu résoudre.

### `affected_tests`

Entrée : `changed_files` ou `base` (une ref git dont le diff donne les fichiers) ; sans l'un ni l'autre, `base` vaut `HEAD`, soit les changements de l'arbre de travail. Réponse : `all`, `test_projects` (dossiers), `test_classes` (atteintes par les arêtes `tests`), `unresolved`.

- Une extension neutre (`.md`, `.txt`, `.png`, ...) est exemptée. `.claude/` et le dossier de travail sont ignorés.
- `all` vaut `true` quand un fichier non neutre n'est pas dans l'index, n'a pas de projet propriétaire, ou quand l'expansion est tronquée.
- Limites : `project_depends` vient des seuls csproj ; les tests TypeScript sont étiquetés mais sans arêtes `tests` ; une arête `tests` entre fichiers n'existe qu'après une réindexation complète.
- Parité : la réponse correspond au script du harness `affected-tests.py`.

### `batch_anchors`

Entrée : `action`, puis `fqns` / `files` pour `closure`, ou `a` / `b` (chacun `{fqns, files}`) pour `overlap`.

- `closure` : les nœuds contenus sous les ancres, plus leurs appelants à profondeur 1, plus les `tests` entrants. Réponse : `nodes`, `unresolved`, `truncated`.
- `overlap` : liste dans `shared` les nœuds communs aux deux fermetures, ou renseigne `disjoint`. La réponse porte aussi `unresolved_a`, `unresolved_b`, `truncated`.
- Une fermeture tronquée, ou une ancre non résolue d'un côté, ne prouve jamais la disjonction ; une ancre non résolue présente des deux côtés figure dans `shared`.

### `documented_in` et `documents_of`

- `documented_in` (`fqn`) : `sections[]` avec `path`, `heading_path`, `start_line`.
- `documents_of` (`path`) : `symbols[]` avec `fqn`, `kind`, `file` (relatif au dépôt), `heading_path`.
- L'arête `Documents` va d'une `Section` vers un symbole. Elle est construite à partir des spans de code, textes de lien et titres égaux au nom local d'un symbole, résolus par nom local : un nom ambigu relie tous les candidats. `DocumentsEdgeLinker` construit les arêtes au build complet et à la réindexation incrémentale.
- Le mapper Markdown étiquette une `Section` `code:<text>` ou `link:<text>` pour chaque span ou lien de ce type.

## Recherche structurelle

`structural_search` (classe Read) trouve des nœuds de syntaxe par motif ou par règle. Le moteur est natif (aucun binaire ast-grep) : chaque fichier candidat est analysé de nouveau par le matcher, un résultat ne vient donc jamais d'un index périmé. Donner exactement un de `pattern`, `rule` (un objet) ou `rule_file`.

Sous-ensemble de règles :

| Clé | Signification |
|---|---|
| `kind` | Le genre du nœud (nom de la grammaire tree-sitter) |
| `pattern` | Un motif de code avec métavariables |
| `regex` | Une expression régulière sur le texte du nœud |
| `field` | Le nom du champ que le nœud occupe chez son parent |
| `inside` | Le nœud a un parent qui satisfait la sous-règle : le parent direct sauf avec `stopBy: end` |
| `has` | Le nœud a un enfant qui satisfait la sous-règle : un enfant direct sauf avec `stopBy: end` |
| `not` | La sous-règle ne doit pas correspondre |
| `all` / `any` | Toutes / au moins une des sous-règles correspondent |
| `stopBy` | Où `inside` / `has` cessent de monter ou de descendre : `neighbor` (défaut), `end`, ou un objet de règle |

Toute autre clé (`follows`, `precedes`, `matches`, `constraints`, `transform`, …) est refusée par son nom.

Métavariables : `$A` capture un nœud, et un nom répété doit correspondre à un texte égal ; `$$$ARGS` capture une suite de nœuds frères. Les captures sont renvoyées avec chaque correspondance.

Langages : C# (`cs`), TypeScript (`ts`), Markdown (`md`) et les autres grammaires livrées par l'index. En Markdown les genres sont `heading`, `fence`, `link` et `front_matter` ; `regex` porte sur le texte du titre ou sur la chaîne d'information de la clôture, et `pattern` n'est pas disponible.

Un fichier de règles YAML (`id`, `language`, `rule`, `message`, `severity`, `fix`) est lu via le VFS sous une racine montée ; `fix` est lu, jamais appliqué.

Les résultats sont bornés par `max_results` (100 par défaut) et `max_files` (500 par défaut) ; le résultat indique `truncated` et `files_scanned`. Chaque correspondance porte chemin, ligne, colonne, texte, symbole englobant et captures.

`node_insert` ajoute un nœud à une position relative à un sélecteur d'ancrage :

- **`inside`** ajoute le texte comme dernier membre d'un corps de classe, struct, interface ou namespace (C#, TypeScript), ou à la fin de la section d'un titre Markdown ; les membres d'enum reçoivent la virgule de séparation. L'indentation est celle du premier frère, sinon celle du parent plus l'unité détectée du fichier (espaces ou tabulations) ; un corps vide utilise la seconde règle.
- **`before`** insère au-dessus des commentaires et attributs de tête de l'ancre, **`after`** en dessous ; un texte multi-ligne est ré-indenté ligne à ligne sur l'indentation de l'ancre. Aucun hash d'ancrage n'est requis pour `before`/`after`.
- Le résultat est ré-analysé comme pour `node_replace` et refusé s'il introduit un diagnostic.

`edit_batch` applique des opérations `node_replace` et `node_insert` sur plusieurs fichiers en une transaction (`EditTransaction`) :

- `expected_hash` est requis pour `node_replace`, absent pour `node_insert`. Les opérations d'un même fichier s'appliquent dans l'ordre sur les octets déjà modifiés : le second hash est vérifié contre l'état après la première.
- Le droit d'écriture de chaque chemin est vérifié avant toute écriture ; chaque fichier est ré-analysé en mémoire (`validate: "parse"`) avant la première écriture.
- Avant la première écriture, un journal `/output/edit-batch/<id>.journal` garde les octets d'origine de chaque fichier ; il est supprimé en cas de succès, et le `edit_batch` suivant sur le montage rejoue un journal resté. Un échec d'écriture restaure les fichiers déjà écrits depuis la mémoire ; si cette restauration échoue aussi, le journal reste et l'appel lève une `AggregateException`. Un `edit_batch` concurrent sur un fichier tenu est refusé (le verrou est dans le processus).
- `validate: "build"` écrit les fichiers, puis `DotnetBuildValidator` construit le `.csproj`/`.sln` le plus proche au-dessus des fichiers modifiés (`dotnet build --no-restore`, délai maximal, processus tué à l'annulation). Un build rouge restaure les octets d'origine ; sans projet trouvé, la vérification est ignorée et signalée.
- Le résultat liste le chemin et la plage de chaque opération, et l'issue de la validation.

## Réécriture structurelle

`structural_rewrite` (classe Edit) réécrit chaque nœud trouvé par un `pattern` ou une `rule` avec un modèle `fix`, sur les fichiers de `paths` / `glob` (bornés par `max_files`).

- `dry_run` vaut `true` par défaut : rien n'est écrit, le résultat porte un diff unifié par fichier, le nombre de correspondances et le nombre appliqué.
- Chaque fichier réécrit est ré-analysé en mémoire ; un fichier dont la réécriture ne s'analyse pas est refusé seul (`refused_reason`), les autres sont appliqués.
- C# et TypeScript uniquement : les fichiers Markdown sont refusés.

## Extensibilité

### Ajouter un langage

Implémenter `ILanguageAdapter` (`Orkeon.Analysis.Abstractions.Interfaces`) : fournir `LanguageName`, `FileExtensions`, les sept queries Tree-sitter (`DeclarationQuery`, `ImportQuery`, `CallQuery`, `InheritanceQuery`, `DocCommentQuery`, `DecoratorQuery`, `StatementQuery`), les mappings `MapNodeKind` et `MapStatementKind`, et les extracteurs `ExtractSignature` / `ResolveImportPath` (`RefineKind`, `ResolveDocComment`, `ExtractName` et `GetExtraModifiers` ont une implémentation par défaut). Enregistrer l'adaptateur via DI (`services.AddSingleton<ILanguageAdapter, MyAdapter>()`) : le builder et le moteur incrémental prennent tous les `ILanguageAdapter` enregistrés, à côté des douze intégrés (`LanguageAdapterFactory` : TypeScript, Python, C#, Go, Rust, JavaScript, Java, C, C++, PHP, Ruby, Bash) ; un adaptateur enregistré après `AddRaggableTree` avec le `LanguageName` d'un intégré le remplace. Un adaptateur Java exécutable, écrit avant que Java soit intégré et gardé comme exemple minimal du contrat, vit dans [`examples/raggable-tree/custom-adapter/`](https://github.com/Orkeon/orkeon/blob/main/examples/raggable-tree/custom-adapter/README.md).

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
- Le résolveur d'imports est heuristique pour les langages dynamiques (Python, TypeScript, JavaScript, Ruby, PHP) : les re-exports et les monkey patches peuvent produire des `UnresolvedRef`.
- Les arêtes d'import entre fichiers ne viennent que des chemins relatifs : un `import` Java, un `use` PHP, un `require` Ruby (à la différence de `require_relative`) et un `#include <...>` C nomment un paquet, un namespace ou un en-tête système, pas un fichier, et ne tracent aucune arête vers un fichier de l'arbre ; un `source` Bash dont le chemin contient une variable n'est pas résolu. Un chemin PHP qui commence par `/` (`__DIR__ . '/lib/a.php'`) se lit depuis le dossier du fichier.
- Les arêtes se résolvent dans un groupe de graphe (`DependencyGraphBuilder.GraphGroupOf`) : C et C++ en partagent un, si bien qu'un fichier `.c` atteint l'en-tête `.h` qu'il inclut (parsé en C++) et les fonctions qui y sont déclarées ; JavaScript et TypeScript en partagent un autre. Les autres langages se résolvent chacun dans le leur.
- Le summarizer se configure uniquement en C# (`Summarizer.Provider` + un `ILlmProvider`) ; les embeddings OpenAI/Ollama exigent leur endpoint, alors que le défaut on-device du runner host ne demande rien. Le pipeline fonctionne sans embeddings (`codebase_search` classe alors par BM25 seul).
- L'index est local au processus : `InMemoryRaggableStore` est reconstruit par `index_codebase` dans chaque processus, et le miroir vector store optionnel n'est jamais relu.
- `FileSystemWatcherCodebaseWatcher` (l'implémentation d'`ICodebaseWatcher`, qu'aucun hôte livré n'enregistre) se base sur `System.IO.FileSystemWatcher` — sous Linux/WSL, les événements de rename peuvent arriver décomposés (observés comme Deleted puis Created).
- Le cache JSON (`RaggableTreeCache`, câblé par aucun hôte livré) n'est pas versionné : une mise à jour d'adapter peut invalider les caches existants, qui sont alors reconstruits à la demande.

---

> **Voir aussi** : [ADR RaggableTree](./raggable-tree-adr.md) · [Inventaire des outils](../tools/inventory.md) · [Retour à l'index](../INDEX.md)
