> 🇬🇧 [English version](README.md)

# <img src="docs/assets/orkeon-mascot.png" alt="Mascotte Orkeon — un caméléon curieux" width="96" align="absmiddle"> Orkeon

**Construisez et orchestrez des équipes d'agents IA — décrites en YAML déclaratif, en TypeScript programmatique (`.ork.ts`) ou en pur C# ; une seule stack d'exécution full .NET les fait toutes tourner**

[![Release](https://img.shields.io/github/v/release/Orkeon/orkeon?include_prereleases)](https://github.com/Orkeon/orkeon/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.md)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Build](https://github.com/Orkeon/orkeon/actions/workflows/ci.yml/badge.svg)](https://github.com/Orkeon/orkeon/actions/workflows/ci.yml)

---

## Qu'est-ce qu'Orkeon ?

Orkeon est un framework C# pour créer et gérer des équipes collaboratives d'agents IA qui s'attaquent à des tâches complexes en plusieurs étapes à l'aide de grands modèles de langage. Les agents sont organisés en crews, chacun avec un rôle, un objectif et un ensemble d'outils définis, et travaillent ensemble via l'une des six stratégies d'orchestration (séquentielle, hiérarchique, parallèle, consensuelle, graphe ou autonome). Bâti sur les principes de la Clean Architecture, Orkeon fournit une fondation entièrement typée et extensible pour des workflows agentiques de qualité production en .NET.

---

## Démarrage rapide — un crew, trois écritures

Le même crew, écrit à trois niveaux d'abstraction. Choisissez celui qui vous va — ou mélangez-les : tous s'exécutent sur le même moteur .NET.

**1. YAML déclaratif** — zéro code, zéro build : éditez le fichier, relancez (`crew.yaml`) :

```yaml
name: "research-crew"
goal: "Research AI trends for 2026"
process: "sequential"

agents:
  researcher:
    role: "Researcher"
    goal: "Find and summarize information about AI trends"
    verbose: true

tasks:
  research:
    description: "Search for the latest AI developments and trends"
    expectedOutput: "A comprehensive summary report"
    agent: "researcher"
```

```bash
orkeon run crew.yaml
```

**2. TypeScript programmatique** — l'ergonomie d'un langage de script (bodies d'agents, hooks, spawn dynamique, littéraux FSM/graphe), le runtime .NET dessous — et toujours zéro rebuild : les scripts sont transpilés à la volée (`crew.ork.ts`) :

```typescript
/// <reference orkeon-script="1.0" />

const researcher = agentBuilder()
    .name("Researcher").role("Researcher")
    .goal("Find and summarize information about AI trends")
    .build();

const crew = crewBuilder()
    .name("research-crew")
    .goal("Research AI trends for 2026")
    .withAgent(researcher)
    .withTask({
        description: "Search for the latest AI developments and trends",
        expectedOutput: "A comprehensive summary report",
    })
    .build();

await crew.run();
```

```bash
orkeon run crew.ork.ts
```

**3. C# pur** — l'API builder embarquée dans votre propre application, fortement typée de bout en bout :

```csharp
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;

var agent = new AgentBuilder()
    .Role("Researcher")
    .Goal("Find and summarize information about AI trends")
    .Verbose()
    .Build();

var crew = new CrewBuilder()
    .Goal("Research AI trends for 2026")
    .Sequential()
    .WithAgent(agent)
    .WithTask(t => t
        .Description("Search for the latest AI developments and trends")
        .ExpectedOutput("A comprehensive summary report"))
    .Build();

// câbler l'hôte et lancer l'exécution — voir docs/getting-started/bootstrap.md
```

**Pas de clé API ?** Tout tourne sur un modèle installé sur votre machine
(Docker Model Runner, Ollama, ou un modèle embarqué dans l'image conteneur) —
voir le [guide des modèles locaux](docs/fr/guides/local-models.md). Guides
complets : [Trois façons d'exécuter Orkeon](docs/fr/getting-started/three-ways-to-run-orkeon.md) ·
[Lancez votre premier exemple](docs/fr/getting-started/run-your-first-example.md).

---

## Installation

| Vous voulez… | Faites | Détails |
|---|---|---|
| **Exécuter des crews sans rien installer** | `docker run -it --rm -e ORKEON_RUNNER=shell ghcr.io/orkeon/orkeon-runners` — shell interactif, 105 exemples embarqués (`orkeon-example run 1`), prêt pour les modèles locaux | [Guide conteneur](docs/fr/getting-started/three-ways-to-run-orkeon.md#3-conteneur) |
| **Installer la CLI `orkeon`** | Windows et Debian/Ubuntu : les démarrages rapides ci-dessous. Autres plateformes : prenez l'archive dans les [releases](https://github.com/Orkeon/orkeon/releases) (`linux-arm64`, `osx-x64/arm64`), puis `./install.sh` | [Binaires de release](docs/fr/getting-started/three-ways-to-run-orkeon.md#2-binaire-de-release) |
| **Embarquer Orkeon dans votre app** | `dotnet add package Orkeon.Domain --prerelease` (+ `Orkeon.Application`, `Orkeon.Infrastructure`). Les packs opt-in (`Orkeon.Rag`, `Orkeon.Tools.*`, `Orkeon.Hosting`, …) sont publiés sur le [feed GitHub Packages](https://github.com/orgs/Orkeon/packages) — voir la [matrice de publication](docs/fr/reference/publication-matrix.md) | [Bootstrap et exécution](docs/getting-started/bootstrap.md) |
| **Contribuer au framework** | `git clone` + `dotnet build Orkeon.sln` | [Depuis les sources](docs/fr/getting-started/three-ways-to-run-orkeon.md#1-depuis-les-sources) · [Contribuer](#contribuer) |

**Windows** — téléchargez `orkeon-cli-<version>-win-x64.zip` (ou le `.msi`) depuis les [releases](https://github.com/Orkeon/orkeon/releases) ; l'artefact est self-contained, aucun .NET requis :

```powershell
Expand-Archive orkeon-cli-<version>-win-x64.zip -DestinationPath .; cd orkeon-cli-<version>-win-x64
.\install.ps1        # ou : msiexec /i orkeon-<version>-win-x64.msi -- un seul canal, pas les deux
orkeon init          # dans un NOUVEAU terminal : choisissez le fournisseur LLM et le modèle
orkeon run crew.yaml
```

**Debian / Ubuntu** — téléchargez `orkeon_<version>_amd64.deb` ; self-contained lui aussi, aucun paquet `dotnet-runtime` tiré :

```bash
sudo apt install ./orkeon_<version>_amd64.deb
orkeon init          # écrit ~/.config/Orkeon/appsettings.json
orkeon run crew.yaml
```

**macOS** — Homebrew deviendra la voie recommandée dès que le dépôt `Orkeon/homebrew-tap` sera publié, à la première release taguée :

```bash
brew tap orkeon/tap && brew install orkeon    # une fois le tap publié
orkeon init
orkeon run crew.yaml
```

En attendant (et sur n'importe quelle machine), l'archive self-contained — `osx-arm64` pour Apple Silicon, `osx-x64` pour Intel. `install.sh` retire pour vous l'attribut de quarantaine Gatekeeper :

```bash
curl -fsSL -O https://github.com/Orkeon/orkeon/releases/latest/download/orkeon-cli-<version>-osx-arm64.tar.gz
tar -xzf orkeon-cli-<version>-osx-arm64.tar.gz && cd orkeon-cli-<version>-osx-arm64 && ./install.sh
orkeon init
```

`orkeon doctor` diagnostique l'installation (runtime, config, joignabilité du LLM, esbuild, grammaires) dès que quelque chose cloche.

---

## Fonctionnalités

| Capacité | Détails |
|---|---|
| **75+ outils intégrés** | Système de fichiers, web scraping (AngleSharp), APIs HTTP, JSON/CSV/XML/PDF/Office (DOCX & XLSX lecture/écriture), bases de données, exécution de code sécurisée, RAG et recherche sémantique, messagerie EventHub, analyse de code RaggableTree, délégation/collaboration — voir l'[inventaire des outils](docs/fr/tools/inventory.md) |
| **13 fournisseurs LLM** | OpenAI, Ollama, Anthropic, Azure OpenAI, Groq, Mistral AI, DeepSeek, Kimi (Moonshot), Qwen, Together AI, HuggingFace, Z.AI (GLM) et Google Gemini — tous basés sur HTTP, étendant `HttpLlmProviderBase` ; modèles locaux via Docker Model Runner, Ollama ou llama.cpp embarqué — voir le [guide des modèles locaux](docs/fr/guides/local-models.md) |
| **Vision / multimodal** | Le contenu image circule de bout en bout (`MultiModalContent` → blocs image Anthropic / `image_url` OpenAI) avec un chargeur adossé au VFS ; opt-in via `AddOrkeonMultiModal(...)` — voir le [guide multimodal](docs/fr/guides/multimodal.md) |
| **6 fournisseurs de mémoire** | Redis (recherche vectorielle), SQLite, InMemory, ChromaDB (REST API v2), Pinecone, LanceDB (serveur REST distant) — tous composables avec le décorateur de chiffrement au repos AES-256-GCM |
| **6 stratégies d'orchestration** | Sequential, Hierarchical, Parallel, Consensual (stratégies de vote Majority / SuperMajority / Unanimity / WeightedConsensus / BordaCount), Graph (style LangGraph), Autonomous (budget d'exécution multi-dimensionnel) — voir le [guide des process types](docs/fr/orchestration/process-types.md) |
| **Système de plugins** | Assemblies drop-in implémentant `IOrkeonPlugin`, découvertes dans un répertoire de plugins, chargées dans des `AssemblyLoadContext` collectables et isolés, activées explicitement via `AddOrkeonPlugins(...)` — voir [plugins](docs/fr/architecture/plugins.md) |
| **Bootstrap d'hôte & scripting** | `Orkeon.Hosting` (`RunnerHost`) câble la pile complète pour les runners/CLI (appsettings, montages VFS, providers, outils) ; le dotnet tool `orkeon` exécute des scripts de crew `.ork.ts` à syntaxe TypeScript |
| **Générateurs de source** | `Orkeon.Generators` émet la plomberie wrapper/builder `[TypedDictionary]`, libérant de tout boilerplate les APIs fortement typées écrites à la main |
| **Architecture en pipeline typé** | `ComponentBase<TRequest, TResponse>` élimine `Dictionary<string, object>` dans toute la pile |
| **Configuration YAML** | Export/import aller-retour complet pour les agents, tâches, crews et schémas d'outils |
| **API Fluent Builder** | `AgentBuilder`, `CrewBuilder`, `CrewTaskBuilder` pour une construction ergonomique et découvrable |
| **Clean Architecture** | Séparation stricte Domain / Application / Infrastructure sans fuite entre couches |
| **Pipeline CQRS** | Commandes et requêtes pour tous les agrégats ; décorateur `ValidatingCommandHandler` ; intégration `UnitOfWork` |
| **Sélection sémantique d'agents** | Appariement par similarité d'embeddings pour router les tâches vers l'agent le plus adapté |
| **Checkpointing & reprise** | État d'exécution persisté dans des state stores enfichables (InMemory, fichier JSON, SQLite, PostgreSQL) ; time-travel via `CheckpointManager` (fork, replay, diff) et `ResumeEngine` pour reprendre les exécutions interrompues |
| **Communication A2A** | Protocole Agent-to-Agent avec découverte, `A2AClient`/`A2AServer`, un repository d'agents scopé au-dessus d'un store d'enregistrement partagé, et application optionnelle de mTLS / schémas d'authentification (certificat client + `RequireMutualTls` / `AllowedAuthSchemes` côté serveur) |
| **Sécurité d'entreprise** | Chiffrement de la mémoire au repos (AES-256-GCM), rotation de clés avec re-chiffrement atomique en deux phases, détection DLP/PII, authentification Azure AD et OIDC |
| **Sous-systèmes opt-in** | A2A, monitoring, conformité NIST, DLP, rate-limiting des outils, rotation de clés, benchmarking, multimodal, hooks de kickoff — aucun n'est enregistré par défaut, chacun s'active via son extension dédiée `AddOrkeonXxx()` — voir la [référence des opt-in](docs/fr/reference/opt-in-subsystems.md) |

---

## Architecture

Orkeon suit la Clean Architecture avec trois couches concentriques :

```
+----------------------------------------------------------+
|  Infrastructure  (outer)                                 |
|  LLM providers, memory stores, tools, HTTP clients       |
|                                                          |
|  +------------------------------------------------+      |
|  |  Application  (middle)                         |      |
|  |  Use cases, orchestrators, service interfaces  |      |
|  |                                                |      |
|  |  +----------------------------------------+   |      |
|  |  |  Domain  (inner)                       |   |      |
|  |  |  Agents, Crews, Tasks, Tools, LLMs     |   |      |
|  |  |  Pure business logic, no dependencies  |   |      |
|  |  +----------------------------------------+   |      |
|  +------------------------------------------------+      |
+----------------------------------------------------------+
```

- **Domain** : entités cœur et value objects (`Agent`, `Crew`, `CrewTask`, `IBaseTool`, `ILlmProvider`). Aucune dépendance externe.
- **Application** : cas d'usage et logique d'orchestration. Définit les interfaces (ports) implémentées par l'Infrastructure.
- **Infrastructure** : fournisseurs LLM, stores de mémoire, implémentations d'outils et toutes les intégrations externes.

Autour du cœur, des paquets dédiés couvrent l'hébergement (`Orkeon.Hosting`, plus le daemon de service `orkeon-host` d'`Orkeon.Host` et le bus d'événements de run `orkeon run --events jsonl`), les plugins (`Orkeon.Plugins`), les générateurs de source Roslyn (`Orkeon.Generators`), l'analyseur de conformité VFS (`Orkeon.Compliance.Vfs`), le DSL de scripting à syntaxe TypeScript (`Orkeon.Scripting` plus le tool CLI `orkeon`), les packs d'outils (`Orkeon.Tools.*`) et le moteur d'analyse sémantique de code RaggableTree (`Orkeon.Analysis`).

---

## Documentation

| Vous cherchez… | Allez à |
|---|---|
| **Le premier run, pas à pas** | [Vue d'ensemble getting-started](docs/fr/getting-started/overview.md) · [Lancez votre premier exemple](docs/fr/getting-started/run-your-first-example.md) |
| **Les trois façons d'exécuter Orkeon** (source / binaire / conteneur) | [Trois façons d'exécuter Orkeon](docs/fr/getting-started/three-ways-to-run-orkeon.md) |
| **Les modèles locaux** (Docker Model Runner, Ollama, embarqué, contextes 128K) | [Guide des modèles locaux](docs/fr/guides/local-models.md) |
| **Les 105 exemples exécutables** (9 catégories thématiques + `orkeon-example`) | [Exemples](https://github.com/Orkeon/orkeon/tree/main/examples) · [Catalogue](docs/fr/reference/examples-catalog.md) |
| **Écrire des crews** : YAML vs builders, câblage de l'hôte, exécution | [YAML & builders](docs/fr/getting-started/yaml-and-builders.md) · [Bootstrap et exécution](docs/fr/getting-started/bootstrap.md) |
| **Les modes d'orchestration** (dont FSM et graphe en profondeur) | [Process types](docs/fr/orchestration/process-types.md) · [FSM](docs/fr/orchestration/fsm.md) · [Graph](docs/fr/orchestration/graph.md) |
| **Écrire vos propres outils** | [New tool pattern](docs/fr/tools/new-tool-pattern.md) · [Inventaire des outils](docs/fr/tools/inventory.md) |
| **L'architecture en profondeur** (plugins, scripting, VFS, sécurité, RaggableTree) | [Docs d'architecture](docs/fr/INDEX.md) · [ADRs](docs/fr/adr/README.md) |
| **Tout le reste** | [Index de la documentation](docs/fr/INDEX.md) *(aussi disponible [en anglais](docs/INDEX.md))* |

---

## Pourquoi Orkeon ?

- **Trois surfaces d'écriture, un moteur** — le même crew peut être un fichier YAML qu'édite un analyste, un script TypeScript qu'itère un développeur (tous deux exécutés sans aucun rebuild), ou du C# embarqué dans votre produit. Aucune réécriture en changeant de niveau.
- **Une orchestration au-delà des pipelines** — six stratégies, dont les graphes d'états style LangGraph à arêtes conditionnelles et un mode entièrement autonome où les agents délèguent, se dupliquent et communiquent sous un budget d'exécution multi-dimensionnel (appels d'outils, profondeur, temps, tokens, spawns).
- **Batteries incluses** — 75+ outils, 13 fournisseurs LLM, 6 stores de mémoire, vision, RAG, analyse de code : utilisables immédiatement, remplaçables via les ports de la Clean Architecture.
- **Local d'abord** — chaque exemple tourne sur un modèle installé sur votre machine (Docker Model Runner, Ollama, ou llama.cpp embarqué dans l'image conteneur). Aucune clé API nécessaire pour évaluer.
- **Posture production** — un système de fichiers virtuel audité par droits sandboxe chaque accès fichier ; des circuit breakers arrêtent les agents en dérive ; l'état d'exécution se checkpointe et se reprend ; la mémoire se chiffre au repos ; DLP et rate limiting sont à un `AddOrkeonXxx()` près.
- **Typé jusqu'au bout** — pas de plomberie `Dictionary<string, object>` ; les générateurs de source gardent la surface typée sans boilerplate.

---

## État du projet

Orkeon est en **1.0.0-rc.2** sur .NET 10 — la release candidate de la V1. Jalons récents : la CLI `orkeon` et l'image conteneur `orkeon-runners` avec 105 exemples embarqués et les workflows de modèles locaux ; l'orchestration FSM et Graph ; le process Autonomous avec budgets d'exécution ; le DSL de scripting TypeScript ; l'analyse sémantique de code RaggableTree (15 outils agents) ; le système de plugins ; checkpoint/reprise ; le client et serveur MCP bi-ère ; la persistance des tâches A2A ; une surface d'API publique mécaniquement gelée ; et le 13ᵉ fournisseur LLM (Google Gemini).

Chaque pull request est gardée en CI :

- le build compile avec **`-warnaserror` et le jeu complet d'analyseurs .NET** — tout nouveau warning compilateur, analyseur ou audit NuGet fait échouer le build
- la **surface d'API publique est gelée** (Microsoft.CodeAnalysis.PublicApiAnalyzers ; un changement d'API non déclaré est une erreur de build)
- les suites de tests unitaires et rapides (les suites Integration/Slow tournent chaque nuit dans un workflow dédié) et la gate de parité documentaire EN/FR ; des gates filtrées par chemin ajoutent les linters d'exemples sur les PR touchant `examples/`, et un build docfx strict (`--warningsAsErrors`) sur les PR touchant les sources ou la doc

L'analyse qualité tourne sur un SonarQube local via `scripts/sonar-analyze.sh` — voir la [politique de quality gate](docs/fr/guides/quality-gate.md). Dernière passe de couverture (août 2026) : projets cœur mesurés entre **83 et 90 %** de couverture de lignes, **0 vulnérabilité, 0 code smell**.

Les contraintes connues sont suivies dans [docs/fr/reference/limitations.md](docs/fr/reference/limitations.md).

---

## Contribuer

Les contributions sont bienvenues. Ouvrez une issue pour discuter des changements significatifs avant de soumettre une pull request. Assurez-vous que tous les tests passent (`dotnet test Orkeon.sln`) et que le nouveau code suit les conventions de Clean Architecture décrites dans [CONTRIBUTING.fr.md](CONTRIBUTING.fr.md).

### Compiler depuis les sources

La couche scripting (support `.ork.ts`) embarque une petite toolchain esbuild provisionnée au premier `dotnet build`. Sur un clone frais, le projet `Orkeon.Scripting` exécute `npm ci` (strictement depuis le `tools/scripting-esbuild/package-lock.json` commité, le lockfile n'est jamais modifié) sous `tools/scripting-esbuild/` pour créer `node_modules/`. C'est une étape unique par clone, qui touche le réseau.

Pour la sauter entièrement (p. ex. en CI ou pour empaqueter des consommateurs NuGet qui n'ont pas besoin d'esbuild) :

```bash
dotnet build Orkeon.sln -p:SkipScriptingNpmInstall=true
```

Si npm est indisponible, le build réussit quand même ; esbuild est alors résolu depuis le `PATH` à l'exécution.

---

## Licence

Orkeon est publié sous [licence MIT](LICENSE.md).

