> 🇬🇧 [English version](../../reference/publication-matrix.md)

# Matrice de publication NuGet

Ce fichier est la source de vérité unique pour **les projets publiés sur NuGet**, afin que
`ci.yml` (validation) et `release.yml` (push sur tag) ne divergent plus jamais (OSS-011 / R8.3).

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
soit tranchée. Chaque entrée ci-dessous est `IsPackable=true` (elle construit donc un paquet en
local) mais n'est **pas** poussée par un workflow pour l'instant.

| PackageId | Note |
|---|---|
| `Orkeon.Tools.Abstractions`, `Orkeon.Tools.Analysis`, `Orkeon.Tools.Code`, `Orkeon.Tools.Data`, `Orkeon.Tools.Embeddings.Local`, `Orkeon.Tools.EventHub`, `Orkeon.Tools.FileSystem`, `Orkeon.Tools.Web` | Famille d'outils — publier en lot une fois le cœur stabilisé. |
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

- `ci.yml` et `release.yml` packagent la **même** liste explicite (les trois bibliothèques cœur).
  Quand la matrice sera confirmée et l'ensemble différé promu, basculer les deux sur un unique
  `dotnet pack Orkeon.sln -c Release` piloté par `IsPackable`, pour que le périmètre soit
  identique par construction.
- `--skip-duplicate` rend les ré-exécutions idempotentes ; les pushes sont conditionnés au tag
  (`refs/tags/`).
- La version provient de `src/Directory.Build.props` (`0.9.0-beta`) ; aucun projet ne la surcharge.
