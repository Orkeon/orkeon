> 🇬🇧 [English version](../../adr/ADR-006-rag-subsystem.md)

> **Voir aussi** : [ADR-002 — Tool abstractions shared kernel](./ADR-002-tool-abstractions-shared-kernel.md) · [ADR-003 — Shared kernels secondaires](./ADR-003-shared-kernels-secondaires.md) · [ADR-004 — Jumeaux de nommage scripting](./ADR-004-jumeaux-de-nommage-scripting.md) · [Retour à l'index](../INDEX.md)

# ADR-006 — Sous-système RAG : shared kernel `Rag.Abstractions` et câblage DI `Rag`

**Statut** : Accepté · **Date** : 2026-07 · **Portée** : `Orkeon.Application` → `Orkeon.Rag.Abstractions` ; `Orkeon.Infrastructure` → `Orkeon.Rag`

## Contexte

Le RAG est promu de `Orkeon.Infrastructure/Knowledge` +
`Orkeon.Application/{Interfaces/Rag,Rag}` vers un sous-système de premier rang `src/rag/`, sur le
modèle éprouvé de `src/analysis/` (RaggableTree — voir
[ADR-003](./ADR-003-shared-kernels-secondaires.md)) :

- **`src/rag/Orkeon.Rag.Abstractions`** — contrats, DTOs et options
  (`IChunkingStrategy`, `IDocumentLoader`, `IDocumentStore`, `IQueryTransformer`, `IReranker`,
  `IRetrievalEvaluator`, `IGroundednessChecker`, `IQueryComplexityClassifier`,
  `IIngestionPipeline`, `IRagPipeline`, `RagAnswer`…). Dépend **uniquement** de `Orkeon.Domain`.
- **`src/rag/Orkeon.Rag`** — implémentations (chunkers, loaders, retrieval, reranking,
  ingestion, évaluation) et factories par nom.
- **`src/tools/Orkeon.Tools.Rag`** — tools agents (`rag_search`, `rag_ingest`, `rag_eval`),
  dans la famille `Tools.*` conformément à
  [ADR-004](./ADR-004-jumeaux-de-nommage-scripting.md) (`Orkeon.Tools.Rag`, **pas**
  `Orkeon.Rag.Tools`).

Deux couplages transversaux à l'oignon sont nécessaires pour brancher le sous-système au cœur,
exactement comme pour RaggableTree. Cet ADR les acte **par anticipation** : le squelette de
projets et les contrats arrivent d'abord (RAG-02 / C1-C2) ; les références ci-dessous sont
ajoutées par les lots de migration suivants.

1. **`Orkeon.Application → Orkeon.Rag.Abstractions`** — la couche Application a besoin des
   ports RAG (p. ex. `IRagPipeline` pour l'injection de connaissances crew/agent) sans voir la
   moindre implémentation.
2. **`Orkeon.Infrastructure → Orkeon.Rag`** (concret, pas seulement les abstractions) —
   **exclusivement** comme câblage de composition confiné à un seul fichier DI
   (`DependencyInjection/RagInfrastructureExtensions.cs`, miroir de
   `RaggableTreeInfrastructureExtensions.cs`), notamment pour envelopper les appels d'embedding
   dans `LlmLoggingDelegatingHandler`.

## Décision

- `Orkeon.Rag.Abstractions` est un **shared kernel secondaire** (même statut que
  `Tools.Abstractions` dans [ADR-002](./ADR-002-tool-abstractions-shared-kernel.md) et
  `Analysis.Abstractions` dans [ADR-003](./ADR-003-shared-kernels-secondaires.md)) : un projet
  d'abstractions ne dépendant que de `Domain`, donc consommable par `Application` sans cycle ni
  inversion du sens des dépendances.
- La référence concrète `Infrastructure → Orkeon.Rag` est acceptée comme **câblage de
  composition** localisé dans un seul fichier d'extensions DI ; l'Infrastructure assume son rôle
  de racine de composition pour le sous-système RAG. `AddOrkeonRag()` est un **opt-in**
  explicite (auto-suffisant, `TryAdd*` partout — l'hôte gagne), jamais appelé
  inconditionnellement depuis `AddOrkeonInfrastructure`.
- **Identité de types pour les plugins** : `Orkeon.Rag.Abstractions` figure dans
  `OrkeonPluginsOptions.SharedAssemblyPrefixes` pour que les rerankers/chunkers/loaders fournis
  par des plugins gardent une identité de type unique à travers les frontières
  d'`AssemblyLoadContext`.

## Conséquences

- **Positif** : les contrats RAG gagnent la visibilité d'un RaggableTree, un opt-in propre et
  l'extensibilité plugins ; les couplages sont traçables et contestables au lieu d'être
  renégociés à chaque revue.
- **Vigilance** : `Orkeon.Rag.Abstractions` doit **continuer à ne dépendre que de
  `Orkeon.Domain`** — garanti par
  `tests/rag/Orkeon.Rag.Abstractions.Tests/ArchitectureTests.cs`. Toute extension de l'usage
  concret d'`Orkeon.Rag` dans l'Infrastructure au-delà du fichier DI unique doit rouvrir cet
  ADR.
- **Rupture** : les anciens namespaces (`Orkeon.Application.Interfaces.Rag.*`,
  `Orkeon.Application.Rag.*`, `Orkeon.Infrastructure.Knowledge.*`) sont supprimés sans shims
  (rupture assumée, version `0.9.x-beta` ; table de migration au `CHANGELOG.md`).
  **Fait en RAG-02/C5 (2026-07-25)** — `rag_search` vit désormais dans `Orkeon.Tools.Rag`
  (`RagSearchTool` + `AddOrkeonRagTools()`), et l'opt-in du sous-système est
  `AddOrkeonRag(configuration)` dans `Orkeon.Rag.DependencyInjection`.

## Amendement — 2026-07-25 (RAG-02/C3)

Le lot de migration qui porte les implémentations dans `Orkeon.Rag` ajoute deux couplages
sortants **du projet concret `Orkeon.Rag`** (jamais d'`Orkeon.Rag.Abstractions`, dont la
règle « Domain uniquement » reste inchangée) :

1. **`Orkeon.Rag → Orkeon.Application`** — `Orkeon.Rag` est un projet d'implémentation de
   l'anneau externe (même anneau qu'`Orkeon.Infrastructure`) et consomme directement les
   ports Application : `Orkeon.Application.Interfaces.Ports.IEmbeddingProvider` (interface
   d'embedding canonique, plan §4.1) pour les pipelines d'ingestion/requête, et les contrats
   de validation `Orkeon.Application.Interfaces.Security` (`IDataValidator`,
   `IProvenanceTracker`, `DataValidationResult`…) pour la validation du chemin d'ingestion.
   Le sens de l'oignon est respecté (anneau externe → Application) et aucun cycle n'apparaît :
   `Application` ne référence que `Rag.Abstractions`, jamais `Orkeon.Rag`.
2. **`Orkeon.Rag → Orkeon.Analysis.Abstractions`** — héberge `AnalysisEmbeddingProviderAdapter`
   (sorti d'`Orkeon.Infrastructure/LLMs/Embeddings/`), le pont entre l'abstraction d'embedding
   Analysis et le port Application (« dans `Orkeon.Rag`, qui référence les deux mondes »,
   plan §4.1).

Point de vigilance : outre le fichier de câblage DI, `Orkeon.Infrastructure` utilise aussi
`Orkeon.Rag.Embeddings.AnalysisEmbeddingProviderAdapter` depuis
`LLMs/Embeddings/DefaultEmbeddingProviderResolver.cs` — logique de résolution de composition
invoquée par `AddOrkeonInfrastructure`. C'est accepté au titre du rôle de racine de
composition ; tout usage d'`Orkeon.Rag` depuis du code **runtime** (hors composition) de
l'Infrastructure exige toujours de rouvrir cet ADR.
