> 🇬🇧 [English version](../../reference/publication-matrix.md)

# Matrice de publication NuGet

Ce fichier est la source de vérité unique pour **les projets publiés sur NuGet**, afin que les
workflows (`ci.yml` validation, `publish.yml` pack + push sur tag, `release.yml` installateurs)
ne divergent plus jamais (OSS-011 / R8.3).

> **Statut — proposition, en attente de confirmation du mainteneur.** Seules les trois
> bibliothèques cœur sont publiées aujourd'hui. L'extension au reste de l'écosystème est
> conditionnée à la décision **D3** (les deux jumeaux `Orkeon.Cli.Scripting` /
> `Orkeon.Scripting.Cli` ne doivent pas voir leurs noms verrouillés dans NuGet avant que la
> question du renommage soit tranchée — renommer après une première publication est un coût
> permanent) et à la confirmation par le mainteneur de l'intention produit ci-dessous.

## Publié en v1 (aujourd'hui)

| PackageId | Pourquoi |
|---|---|
| `Orkeon.Domain` | Entités et interfaces cœur — la racine des dépendances. |
| `Orkeon.Application` | Cas d'usage, ports, orchestration. |
| `Orkeon.Infrastructure` | Adaptateurs (LLM, mémoire, stratégies). Documenté comme installable dans le README ; c'est pourquoi `release.yml` a été corrigé pour le packager. |

## Proposé pour une version ultérieure (différé)

L'écosystème annoncé (la famille d'outils, le tool CLI `orkeon`, hosting, plugins) est censé être
installable, mais retenu jusqu'à ce que les paquets cœur soient éprouvés sur NuGet **et** que D3
soit tranchée. Chaque entrée ci-dessous est `IsPackable=true` et atterrit donc déjà sur le **feed
interne GitHub Packages** via `publish.yml` (voir plus bas), mais n'est **pas** poussée vers
NuGet.org par un workflow pour l'instant.

| PackageId | Note |
|---|---|
| `Orkeon.Tools.Abstractions`, `Orkeon.Tools.Analysis`, `Orkeon.Tools.Code`, `Orkeon.Tools.Data`, `Orkeon.Tools.Embeddings.Local`, `Orkeon.Tools.EventHub`, `Orkeon.Tools.FileSystem`, `Orkeon.Tools.Rag`, `Orkeon.Tools.Web` | Famille d'outils — publier en lot une fois le cœur stabilisé. |
| `Orkeon.Rag.Abstractions`, `Orkeon.Rag`, `Orkeon.Rag.Onnx`, `Orkeon.Rag.Onnx.Model` | Sous-système RAG (RAG-02…06, ADR-006). `Orkeon.Rag.Onnx` + `Orkeon.Rag.Onnx.Model` forment la paire cross-encoder opt-in (runtime + poids int8 embarqués) — publier les deux ensemble. |
| `Orkeon.Analysis`, `Orkeon.Analysis.Abstractions` | RaggableTree. |
| `Orkeon.Cli`, `Orkeon.Cli.Abstractions`, `Orkeon.Cli.TerminalGui` | Bibliothèques CLI. |
| `Orkeon.Cli.Scripting` | **Conditionné à D3** — la fenêtre de renommage des jumeaux se ferme à la première publication. |
| `Orkeon.Scripting`, `Orkeon.Scripting.Cli` | `Orkeon.Scripting.Cli` est le tool dotnet `orkeon` (`PackAsTool`). **Conditionné à D3.** |
| `Orkeon.Hosting` | Hôte d'empaquetage (créé par R1.5) — candidat sérieux à livrer avec le lot cœur. |
| `Orkeon.Plugins` | Système de plugins. |

## Publiés sur GitHub Packages pour `experiments/` (dotnet tools)

`publish.yml` (tag `v*`) packe `Orkeon.sln` et pousse chaque projet packable vers
**GitHub Packages** (`nuget.pkg.github.com/Orkeon`) avec `--skip-duplicate`. C'est ce feed que
`experiments/` consomme en mode packages. Les runners interactifs sont livrés en dotnet tools
pour qu'aucun launcher n'exige un clone source :

| PackageId | Commande tool | Projet source |
|---|---|---|
| `Orkeon.Runners.Shared` | — (bibliothèque) | `examples/runners/_shared` |
| `Orkeon.Runners.ClaimVerification` | `orkeon-claim-verify` | `examples/runners/interactive-claim-verification` |
| `Orkeon.Runners.InterviewSpecForge` | `orkeon-spec-forge` | `examples/runners/interactive-interview-spec-forge` |
| `Orkeon.ConsoleApp` | `orkeon-repl` | `src/apps/Orkeon.ConsoleApp` |
| `Orkeon.Scripting.Cli` | `orkeon` | `src/scripting/Orkeon.Scripting.Cli` |

## Archives d'installation (`release.yml`)

Sur un tag `v*`, `release.yml` exécute `scripts/package-installers.sh` pour attacher à la
GitHub Release des archives d'installation par OS (`orkeon-<version>-<rid>.tar.gz` / `.zip`).
Chaque archive embarque tous les launchers CLI plus un binaire esbuild partagé. Le runner
`orkeon-examples`, retiré, n'est **plus packagé** — le CLI `orkeon` le remplace
(`orkeon run crew.yaml` exécute les crews YAML de `examples/` ; `orkeon run script.ork.ts`
exécute le DSL de scripting).

Le CLI `orkeon` est distribué via **trois canaux** :

| Canal | Artefact | Runtime | Public |
|---|---|---|---|
| Tool dotnet NuGet | `Orkeon.Scripting.Cli` (`PackAsTool`, commande `orkeon`) | requiert le SDK .NET 10 (`dotnet tool install`) | développeurs .NET |
| Archive d'installation — slim | launcher `orkeon-slim` | framework-dependent (requiert le runtime .NET 10) | devs ayant déjà .NET 10 |
| Archive d'installation — self-contained | launcher `orkeon` | self-contained (runtime embarqué) | onboarding ; aucune install .NET requise |

Les deux variantes d'archive sont construites depuis le même csproj
`src/scripting/Orkeon.Scripting.Cli` et partagent l'unique esbuild embarqué.
`orkeon-trading` est également self-contained ; les autres launchers CLI restent
framework-dependent.

## Build-time / interne (pas des paquets autonomes)

| PackageId | Note |
|---|---|
| `Orkeon.Generators` | Source generator — consommé au build. |
| `Orkeon.Compliance.Vfs` | Analyseur Roslyn — consommé au build. |

## Câblage de la publication

- Tout le packaging et le push NuGet vivent dans **`publish.yml`** (tag `v*`) :
  `dotnet pack Orkeon.sln` (+ les tools runners) piloté par `IsPackable`, poussé vers
  **GitHub Packages** avec `--skip-duplicate` (ré-exécutions idempotentes). `ci.yml` valide
  (build + tests) et ne package rien ; `release.yml` construit les archives d'installation et
  l'image conteneur, sans packaging NuGet.
- **Rien n'est poussé vers NuGet.org aujourd'hui** — la matrice ci-dessus est la proposition
  pour cette promotion, conditionnée à D3 et à la confirmation du mainteneur.
- La version provient de `src/Directory.Build.props` (actuellement `0.9.2-beta`) ; aucun projet ne la surcharge.
