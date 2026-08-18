> 🇬🇧 [English version](../../architecture/raggable-tree-adr.md)

> **Voir aussi** : [Guide RaggableTree](./raggable-tree.md) · [Retour à l'index](../INDEX.md)

# ADR — Graphe sémantique RaggableTree comme substrat de contexte des agents

**Statut** : Accepté · **Date** : 2026-04 · **Portée** : Orkeon.Analysis + Orkeon.Tools.Analysis

## Contexte

Les agents Orkeon raisonnent sur des bases de code réelles dépassant fréquemment les centaines de milliers de lignes sur plusieurs langages (TypeScript, C#, Python, Go, Rust). Trois contraintes matérielles :

1. **Budget tokens** — Le contexte LLM (même 1M tokens) ne peut pas absorber un monorepo ; il faut un index compact qui réponde à « quel symbole est affecté par X ? » sans recharger les sources.
2. **Granularité variable** — Un agent peut avoir besoin d'une vue package, d'un extrait de méthode, d'une chaîne d'appels ou d'une mesure de complexité selon la tâche. Une seule représentation (AST brut, embeddings plats, documents Markdown) ne couvre pas tous ces cas.
3. **Coût d'itération** — Un projet évolue en continu ; reconstruire l'index complet à chaque commit est inacceptable (mesuré : 20× plus lent qu'une réindexation incrémentale ciblée).

Les solutions naïves disponibles (lecture fichier via `file_read`, `grep` plein-texte, recherche RAG sur chunks arbitraires) ont été évaluées et considérées insuffisantes : elles ne préservent pas les relations sémantiques (imports, appels, héritage) et forcent l'agent à redécouvrir la structure à chaque requête.

## Décision

Construire un **graphe sémantique stratifié à 6 niveaux** alimenté par Tree-sitter, exposé aux agents via 13 tools structurels.

Les six niveaux (`NodeLevel`) :

- **L0 Monorepo** — racine du projet indexé
- **L1 Package** — unité de build détectée par marker (`package.json`, `*.csproj`, ...)
- **L2 Module** — fichier source
- **L3 Symbol** — classe, interface, fonction, méthode, enum, property, constant
- **L4 Statement** — if/try/loop/return extraits pour les requêtes de flow et de CFG
- **Edges** — Imports, Calls, Extends, Implements, Contains

Sept composants-clés implémentent le pipeline :

- `ILanguageAdapter` — adaptation par langage (queries Tree-sitter + heuristiques d'import)
- `RaggableTreeBuilder` — construction initiale (6 phases)
- `IncrementalReindexEngine` — réindexation diff (changed files only)
- `IRaggableStore` — requêtes sur le graphe construit
- `IEmbeddingProvider` + `IEmbeddingTextComposer` — recherche sémantique
- `IFrameworkFingerprinter` — tagging des nœuds par framework (Angular, NestJS, ASP.NET, Flask, FastAPI)
- `ICodebaseWatcher` + `IRaggableTreeEventBus` — synchronisation temps réel

## Alternatives rejetées

### LSP (Language Server Protocol) complet

Intégrer un serveur LSP par langage (tsserver, OmniSharp, pylsp, ...) donne des informations très précises sur les symboles et les références.

- **Rejeté car** : nécessite un runtime par langage (Node.js pour tsserver, MSBuild pour OmniSharp, Python venv pour pylsp) et une orchestration de processus longue-durée. Impraticable pour un pipeline d'indexation batch sur CI ou sur un serveur sans les SDK installés.

### Roslyn-only (C# uniquement)

Roslyn donne un AST parfait et un graphe de symboles résolus pour C#.

- **Rejeté car** : le portage Python/JavaScript/Go/Rust est une perte fonctionnelle majeure. La cible est un framework multi-langage par défaut.

### Recherche plein-texte + embeddings plats

Découper chaque fichier en chunks de 500 tokens, vectoriser, stocker dans un vector store, et laisser les agents faire uniquement de la recherche sémantique.

- **Rejeté car** : perd les relations structurelles (qui appelle quoi, qui hérite de quoi, quels imports). Les tools `flow_trace`, `impact_analysis`, `sub_graph` deviennent impossibles. La qualité des réponses chute sur les questions typées « si je modifie X, qu'est-ce qui casse ? ».

### Regex/ctags over AST

Option la plus rapide à implémenter mais la plus fragile : ne distingue pas les vrais appels des mentions en commentaire, ne résout pas les imports, rate les constructions modernes (async/await, decorators, spread).

- **Rejeté car** : la dette technique grandit avec chaque nouveau langage ou framework. Tree-sitter offre une grammaire officielle maintenue pour 40+ langages.

## Conséquences

### Positives

- **Contexte compact** — `ICodebaseContextProvider` produit un résumé < 500 tokens (markdown) ou < 200 tokens (compact) injectable dans tous les agents.
- **Requêtes typées** — Les 13 tools remplacent des dizaines de lignes de prompting « lis ce fichier et dis-moi... » par des appels déterministes.
- **Réindexation incrémentale** — Le moteur ne re-parse que les fichiers changés (objectif mesuré : < 5% du coût initial).
- **Multi-langage par construction** — Ajouter un langage = implémenter un `ILanguageAdapter`, pas étendre un pipeline C#-only.

### Négatives

- **Surface API importante** — 7 interfaces + 5 adaptateurs + 13 tools = 25 points d'extension à comprendre avant d'étendre le système. Atténué par les docs `docs/architecture/raggable-tree.md` et l'ADR présent.
- **Tree-sitter en dépendance native** — Les parsers sont compilés en C ; les builds musl ou ARM32 peuvent nécessiter du travail. Atténué par `TreeSitter.DotNet` qui wrappe la compilation pour la plupart des plateformes cibles.
- **Coût mémoire** — Un monorepo de 100k symboles avec embeddings 1536-dim pèse ~600 MB en mémoire. Atténué par la possibilité d'utiliser un vector store externe (Redis/LanceDB) plutôt que `InMemoryRaggableStore`.
- **Provider embedding optionnel mais stratégique** — Sans embedding, `codebase_search` et `semantic_search` sont désactivés. Le pipeline reste utile pour les tools structurels (map, detail, graph, flow) mais perd sa composante sémantique.

### Risques acceptés

- Les re-exports TypeScript (`export { Foo } from './bar'`) et les imports Python dynamiques peuvent produire des `UnresolvedRef`. Décision : documenter la limite plutôt que construire un résolveur lourd en V1.
- Le cache JSON est non versionné et donc invalide aux changements d'adapter. Décision : recalculer à la demande, sans migration automatique.

## Amendement — 2026-08-18

La surface d'outils est passée depuis des 13 tools décrits ici à **15**
(`Orkeon.Tools.Analysis`) ; le chiffre dérivé « 25 points d'extension » est
désormais 27. Les comptes ci-dessus sont conservés tels quels — cet ADR est un
enregistrement de décision gelé ; [le guide RaggableTree](./raggable-tree.md)
est l'inventaire de référence, maintenu.

---

> **Voir aussi** : [Guide RaggableTree](./raggable-tree.md) · [Retour à l'index](../INDEX.md)
