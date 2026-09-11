> 🇬🇧 [English version](README.md)

# <img src="docs/assets/orkeon-mascot.png" alt="Mascotte Orkeon — un caméléon curieux" width="96" align="absmiddle"> Orkeon

**Des équipes d'agents IA qui restent dans les clous — chaque fichier, endpoint et budget qu'un agent peut toucher est déclaré, puis appliqué. Décrites en YAML, TypeScript (`.ork.ts`) ou C# ; un seul runtime .NET exécute les trois.**

[![Release](https://img.shields.io/github/v/release/Orkeon/orkeon?include_prereleases)](https://github.com/Orkeon/orkeon/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.md)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Build](https://github.com/Orkeon/orkeon/actions/workflows/ci.yml/badge.svg)](https://github.com/Orkeon/orkeon/actions/workflows/ci.yml)

---

## Le problème

Vous lâchez un agent de code sur un dépôt. Il lit ce dont il a besoin, puis il écrit — un fichier deux répertoires plus haut, un `~/.config` qui ne le regardait pas, un script dans `/tmp` qu'il exécute dans la foulée. Rien ne l'a arrêté parce que rien n'était là pour l'arrêter : ses outils appelaient `File.WriteAllText` sur le chemin que le modèle avait produit.

Orkeon place la frontière devant le modèle, pas derrière :

- **Un système de fichiers virtuel.** Les agents ne voient jamais que des chemins virtuels (`/workspace`, `/output`) ; chacun est un montage que vous avez déclaré, avec les droits que vous lui avez donnés (`ro`/`rw`). Un chemin hors montage est refusé avant qu'un seul octet n'atteigne le disque.
- **Un sandbox pour le code**, un **budget d'exécution** pour l'autonomie — appels d'outils, profondeur, temps, tokens, agents engendrés — et des **circuit breakers** contre les boucles. L'agent épuise sa permission avant d'épuiser ses idées.
- **Un analyseur Roslyn pour votre propre code.** [`Orkeon.Compliance.Vfs`](docs/fr/architecture/vfs-compliance.md) est un paquet NuGet autonome sans dépendance à Orkeon : ajoutez-le à n'importe quel projet C# et tout appel direct à `System.IO` devient une erreur de compilation — la ligne qu'un agent (ou un collègue) aurait glissée ne compile pas.

Autour de cette frontière, un framework d'équipes d'agents complet : des crews d'agents avec rôles, objectifs et outils, six stratégies d'orchestration (séquentielle, hiérarchique, parallèle, consensuelle, graphe, autonome), 14 fournisseurs LLM, mémoire, RAG, analyse sémantique de code — typé de bout en bout, Clean Architecture, .NET 10.

## Essayez-le en deux minutes — sans clé API

Un modèle local, un agent, un seul montage en écriture. Depuis un clone de ce dépôt (`git clone --depth 1 https://github.com/Orkeon/orkeon && cd orkeon`), avec [Ollama](https://ollama.com) installé et le SDK .NET 10 :

<!-- quickstart:begin -->
```bash
ollama pull llama3.2:1b
dotnet tool install -g Orkeon.Scripting.Cli --prerelease
mkdir -p out && orkeon run examples/quickstart/crew.yaml --mount ./out:/output:rw
```
<!-- quickstart:end -->

L'agent écrit `./out/hello.md` — et seulement là : `/output` est l'unique montage, en `rw`. Changez la tâche du crew pour écrire ailleurs et regardez le service de fichiers refuser. Le bloc ci-dessus est exécuté littéralement par la CI à chaque changement ([workflow Quickstart](https://github.com/Orkeon/orkeon/actions/workflows/quickstart.yml)) : s'il cesse de fonctionner, le build passe au rouge avant que vous ne le découvriez. Tout sur les modèles locaux — Docker Model Runner, Ollama, un modèle embarqué dans l'image conteneur — est dans le [guide des modèles locaux](docs/fr/guides/local-models.md).

## Forger une équipe à partir d'un besoin

Vous n'avez pas à écrire le crew. Décrivez le besoin ; `orkeon forge` vous interroge, ébauche l'équipe, la rend (YAML ou `.ork.ts`), la valide, **l'exécute dans un sandbox**, diagnostique le run et demande votre verdict — puis promeut le résultat dans votre projet quand vous le dites :

```bash
orkeon init                                  # une fois : choisir un fournisseur et un modèle (Ollama inclus)
orkeon forge "une équipe qui trie les issues d'un dépôt GitHub chaque matin"
orkeon forge list                            # chaque session sur disque, reprenable
orkeon forge promote <slug> --to ./crews     # adopter le crew qui a réussi
```

Le cycle est *brief → blueprint → render → validate → test → diagnose → verdict*, avec des sessions que vous pouvez reprendre, éditer et retester. Il exige un modèle configuré : sans lui il s'arrête à la porte avec `FORGE-LLM-UNAVAILABLE` et vous renvoie vers `orkeon init`. Pas à pas : [Forger une équipe à partir d'un besoin](docs/fr/getting-started/forge-a-team-from-a-need.md).

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

// câbler l'hôte et lancer l'exécution — voir docs/fr/getting-started/bootstrap.md
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
| **Installer la CLI `orkeon`** | Windows et Debian/Ubuntu : les démarrages rapides ci-dessous. macOS : l'archive CLI ci-dessous (`osx-arm64`, `osx-x64`). Pour `linux-arm64` — et pour qui veut aussi le REPL ou le host de service — c'est l'archive multi-applications `orkeon-<version>-<rid>.tar.gz` des [releases](https://github.com/Orkeon/orkeon/releases), puis `./install.sh` | [Binaires de release](docs/fr/getting-started/three-ways-to-run-orkeon.md#2-binaire-de-release) |
| **Embarquer Orkeon dans votre app** | `dotnet add package Orkeon --prerelease` — le framework complet en un seul paquet. Ajoutez au besoin `Orkeon.Tools` (les familles d'outils intégrés) et les opt-ins (`Orkeon.Rag.Onnx`, `Orkeon.Tools.Embeddings.Local` — ce dernier épingle un amont en préversion, `SmartComponents.LocalEmbeddings`, et continuera après la 1.0 : voir les [limitations](docs/fr/reference/limitations.md)) — voir la [matrice de publication](docs/fr/reference/publication-matrix.md). Le tool CLI `orkeon` et l'image conteneur ci-dessus sont inchangés | [Bootstrap et exécution](docs/fr/getting-started/bootstrap.md) |
| **Vérifier ce que vous téléchargez** | Chaque paquet et installeur porte une attestation de provenance de build signée par GitHub et une ligne `SHA256SUMS` : `gh attestation verify <fichier> --repo Orkeon/orkeon` — aucune confiance en cette page n'est requise | [Vérifier ce que vous installez](docs/fr/guides/verify-what-you-install.md) |
| **Contribuer au framework** | `git clone` (**sans** `--recursive`) + `dotnet build Orkeon.sln` | [Depuis les sources](docs/fr/getting-started/three-ways-to-run-orkeon.md#1-depuis-les-sources) · [Contribuer](#contribuer) |

> **Clonez sans `--recursive`.** Ce dépôt déclare des **sous-modules privés de
> mainteneurs** : ils ne sont pas disponibles dans un clone
> public. Ni le build, ni les tests, ni le workflow de contribution n'en ont besoin, et
> un `git submodule update` en échec sur ces chemins est attendu et sans
> conséquence — voir [CONTRIBUTING.fr.md](CONTRIBUTING.fr.md).

**Windows** — téléchargez `orkeon-cli-<version>-win-x64.zip` (ou le `.msi`) depuis les [releases](https://github.com/Orkeon/orkeon/releases) ; l'artefact est self-contained, aucun .NET requis :

```powershell
# Vérifiez d'abord le téléchargement : chaque release publie un asset SHA256SUMS (SHA256SUMS.msi pour le .msi)
(Get-FileHash orkeon-cli-<version>-win-x64.zip -Algorithm SHA256).Hash.ToLower()
Select-String -Path SHA256SUMS -Pattern 'win-x64\.zip'   # les deux empreintes doivent coïncider

Expand-Archive orkeon-cli-<version>-win-x64.zip -DestinationPath .; cd orkeon-cli-<version>-win-x64
.\install.ps1        # ou : msiexec /i orkeon-<version>-win-x64.msi -- un seul canal, pas les deux
orkeon init          # dans un NOUVEAU terminal : choisissez le fournisseur LLM et le modèle
orkeon run crew.yaml
```

**Debian / Ubuntu** — téléchargez `orkeon_<version>_amd64.deb` ; self-contained lui aussi, aucun paquet `dotnet-runtime` tiré :

```bash
# SHA256SUMS est un asset de release lui aussi — téléchargez-le à côté du .deb et vérifiez
grep " orkeon_<version>_amd64.deb$" SHA256SUMS | sha256sum --check   # attendu : OK

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
# Le nom de l'asset porte la version, et le raccourci `latest/download/` de GitHub
# ignore les préversions — résolvez donc d'abord le tag le plus récent (ou copiez le
# lien de l'asset depuis la page des releases, ce qui revient au même à la main).
TAG=$(curl -fsSL https://api.github.com/repos/Orkeon/orkeon/releases | grep -m1 '"tag_name"' | cut -d'"' -f4)
VER=${TAG#v}; BASE=https://github.com/Orkeon/orkeon/releases/download/$TAG

curl -fsSL -O "$BASE/orkeon-cli-$VER-osx-arm64.tar.gz"     # osx-x64 sur Intel
curl -fsSL -O "$BASE/SHA256SUMS"
grep " orkeon-cli-$VER-osx-arm64.tar.gz$" SHA256SUMS | shasum -a 256 --check -   # attendu : OK

tar -xzf "orkeon-cli-$VER-osx-arm64.tar.gz" && cd "orkeon-cli-$VER-osx-arm64" && ./install.sh
orkeon init
```

`orkeon doctor` diagnostique l'installation (runtime, config, joignabilité du LLM, esbuild, grammaires) dès que quelque chose cloche.

---

## Fonctionnalités

Chaque nombre ci-dessous est recompté depuis l'arborescence à chaque run CI — `bash scripts/count-surface.sh` les affiche à côté de la règle qui les compte, et un README en désaccord fait échouer le build.

| Capacité | Détails |
|---|---|
| **79 outils intégrés** | Système de fichiers, web scraping (AngleSharp), APIs HTTP, JSON/CSV/XML/PDF/Office (DOCX & XLSX lecture/écriture), bases de données, exécution de code sécurisée, RAG et recherche sémantique, messagerie EventHub, analyse de code RaggableTree, délégation/collaboration — voir l'[inventaire des outils](docs/fr/tools/inventory.md) |
| **14 fournisseurs LLM** | OpenAI, Ollama, Anthropic, Azure OpenAI, Mistral AI, DeepSeek, Kimi (Moonshot), Qwen, Together AI, HuggingFace, Z.AI (GLM), Google Gemini, Grok (x.AI) et MiniMax — tous basés sur HTTP, étendant `HttpLlmProviderBase` ; modèles locaux via Docker Model Runner, Ollama ou llama.cpp embarqué — voir le [guide des modèles locaux](docs/fr/guides/local-models.md) |
| **Vision / multimodal** | Le contenu image circule de bout en bout (`MultiModalContent` → blocs image Anthropic / `image_url` OpenAI) avec un chargeur adossé au VFS ; opt-in via `AddOrkeonMultiModal(...)` — voir le [guide multimodal](docs/fr/guides/multimodal.md) |
| **6 fournisseurs de mémoire** | Redis (recherche vectorielle), SQLite, InMemory, ChromaDB (REST API v2), Pinecone, LanceDB (serveur REST distant) — un seul port `IMemoryProvider`, décorateurs composables |
| **6 stratégies d'orchestration** | Sequential, Hierarchical, Parallel, Consensual (stratégies de vote Majority / SuperMajority / Unanimity / WeightedConsensus / BordaCount), Graph (style LangGraph), Autonomous (budget d'exécution multi-dimensionnel) — voir le [guide des process types](docs/fr/orchestration/process-types.md) |
| **Interop Microsoft Agent Framework** | `Orkeon.Interop.AgentFramework` : un crew Orkeon tourne comme `AIAgent` MAF ; un `AIAgent` MAF devient le cerveau (`WithAgentFrameworkAgent`) ou un outil (`WithAgentFrameworkTool`) d'un agent Orkeon — voir [ADR-010](docs/fr/adr/ADR-010-agent-framework-interop.md) et `examples/interop/agent-framework/` |
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
| **Sous-systèmes opt-in** | A2A, monitoring, rate-limiting des outils, benchmarking, multimodal, hooks de kickoff et d'autres — aucun n'est enregistré par défaut, chacun s'active via son extension dédiée `AddOrkeonXxx()` — voir la [référence des opt-in](docs/fr/reference/opt-in-subsystems.md) |

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
|  |  |  Pure business logic, one satellite of constants  |   |      |
|  |  +----------------------------------------+   |      |
|  +------------------------------------------------+      |
+----------------------------------------------------------+
```

- **Domain** : entités cœur et value objects (`Agent`, `Crew`, `CrewTask`, `IBaseTool`, `ILlmProvider`). Aucune dépendance externe.
- **Application** : cas d'usage et logique d'orchestration. Définit les interfaces (ports) implémentées par l'Infrastructure.
- **Infrastructure** : fournisseurs LLM, stores de mémoire, implémentations d'outils et toutes les intégrations externes.

Autour du cœur, des projets dédiés couvrent l'hébergement (`Orkeon.Hosting`, plus le daemon de service `orkeon-host` d'`Orkeon.Host` et le bus d'événements de run `orkeon run --events jsonl`), les plugins (`Orkeon.Plugins`), les générateurs de source Roslyn (`Orkeon.Generators`), l'analyseur de conformité VFS (`Orkeon.Compliance.Vfs` — un paquet NuGet autonome sans dépendance à Orkeon : `<PackageReference Include="Orkeon.Compliance.Vfs" PrivateAssets="all" />` fait de tout `System.IO` direct une erreur de compilation dans n'importe quel projet C#), le DSL de scripting à syntaxe TypeScript (`Orkeon.Scripting` plus le tool CLI `orkeon`), les familles d'outils (`Orkeon.Tools.*`, livrées ensemble dans le paquet `Orkeon.Tools`) et le moteur d'analyse sémantique de code RaggableTree (`Orkeon.Analysis`, livré dans le paquet `Orkeon`).

---

## Documentation

| Vous cherchez… | Allez à |
|---|---|
| **Le premier run, pas à pas** | [Vue d'ensemble getting-started](docs/fr/getting-started/overview.md) · [Lancez votre premier exemple](docs/fr/getting-started/run-your-first-example.md) |
| **Les trois façons d'exécuter Orkeon** (source / binaire / conteneur) | [Trois façons d'exécuter Orkeon](docs/fr/getting-started/three-ways-to-run-orkeon.md) |
| **Les modèles locaux** (Docker Model Runner, Ollama, embarqué, contextes 128K) | [Guide des modèles locaux](docs/fr/guides/local-models.md) |
| **Les 105 exemples exécutables** (9 catégories thématiques + `orkeon-example`) | [Exemples](https://github.com/Orkeon/orkeon/tree/main/examples) · [Catalogue](docs/fr/reference/examples-catalog.md) |
| **Écrire des crews** : YAML, builders, TypeScript, câblage de l'hôte, exécution | [YAML & builders](docs/fr/getting-started/yaml-and-builders.md) · [Écrire une crew en TypeScript](docs/fr/guides/write-a-crew-in-typescript.md) · [Bootstrap et exécution](docs/fr/getting-started/bootstrap.md) |
| **Les modes d'orchestration** (dont FSM et graphe en profondeur) | [Process types](docs/fr/orchestration/process-types.md) · [FSM](docs/fr/orchestration/fsm.md) · [Graph](docs/fr/orchestration/graph.md) |
| **Écrire vos propres outils** | [New tool pattern](docs/fr/tools/new-tool-pattern.md) · [Inventaire des outils](docs/fr/tools/inventory.md) |
| **L'architecture en profondeur** (plugins, scripting, VFS, sécurité, RaggableTree) | [Docs d'architecture](docs/fr/INDEX.md) · [ADRs](docs/fr/adr/README.md) |
| **Tout le reste** | [Index de la documentation](docs/fr/INDEX.md) *(aussi disponible [en anglais](docs/INDEX.md))* |

Ces pages, avec la référence d'API générée, sont publiées sous forme de site consultable à
l'adresse **<https://orkeon.github.io/orkeon/>**. `docs.yml` le déploie à chaque tag `v*` : le site
paraît donc avec la première release taguée et documente toujours une version taguée.

---

## Pourquoi Orkeon ?

- **Trois surfaces d'écriture, un moteur** — le même crew peut être un fichier YAML qu'édite un analyste, un script TypeScript qu'itère un développeur (tous deux exécutés sans aucun rebuild), ou du C# embarqué dans votre produit. Aucune réécriture en changeant de niveau.
- **Une orchestration au-delà des pipelines** — six stratégies, dont les graphes d'états style LangGraph à arêtes conditionnelles et un mode entièrement autonome où les agents délèguent, se dupliquent et communiquent sous un budget d'exécution multi-dimensionnel (appels d'outils, profondeur, temps, tokens, spawns).
- **Batteries incluses** — 79 outils, 14 fournisseurs LLM, 6 stores de mémoire, vision, RAG, analyse de code : utilisables immédiatement, remplaçables via les ports de la Clean Architecture.
- **Local d'abord** — chaque exemple tourne sur un modèle installé sur votre machine (Docker Model Runner, Ollama, ou llama.cpp embarqué dans l'image conteneur). Aucune clé API nécessaire pour évaluer.
- **La frontière est le produit** — un système de fichiers virtuel audité par droits devant chaque accès fichier, un analyseur Roslyn qui refuse le `System.IO` brut dans votre propre code, des budgets d'exécution et des circuit breakers pour l'autonomie, checkpoint et reprise pour les longs runs. Rate limiting, monitoring et le reste sont à un `AddOrkeonXxx()` près.
- **Typé jusqu'au bout** — pas de plomberie `Dictionary<string, object>` ; les générateurs de source gardent la surface typée sans boilerplate.

---

## État du projet

La version courante est la **1.0.0-rc.3** sur .NET 10 — la release candidate de la V1 (`src/Directory.Build.props` est la seule source de vérité ; le badge Release ci-dessus et `git tag` disent ce qui est tagué). Jalons récents : la distribution NuGet consolidée en un seul paquet `Orkeon` (plus `Orkeon.Tools` et quelques opt-ins — voir la [matrice de publication](docs/fr/reference/publication-matrix.md)) ; la CLI `orkeon` et l'image conteneur `orkeon-runners` avec 105 exemples embarqués et les workflows de modèles locaux ; l'orchestration FSM et Graph ; le process Autonomous avec budgets d'exécution ; le DSL de scripting TypeScript ; l'analyse sémantique de code RaggableTree (15 outils agents) ; le système de plugins ; checkpoint/reprise ; le client et serveur MCP bi-ère ; la persistance des tâches A2A ; une surface d'API publique mécaniquement gelée ; et la flotte LLM portée à 14 fournisseurs, chacun sous preuve de campagne en exécution réelle (dernières arrivées : Google Gemini, Grok/x.AI, MiniMax).

Chaque pull request est gardée en CI :

- le build compile avec **`-warnaserror` et le jeu complet d'analyseurs .NET** — tout nouveau warning compilateur, analyseur ou audit NuGet fait échouer le build
- la **surface d'API publique est gelée** (Microsoft.CodeAnalysis.PublicApiAnalyzers ; un changement d'API non déclaré est une erreur de build)
- les suites de tests unitaires et rapides (les suites Integration/Slow tournent chaque nuit dans un workflow dédié) et la gate de parité documentaire EN/FR ; des gates filtrées par chemin ajoutent les linters d'exemples sur les PR touchant `examples/`, et un build docfx strict (`--warningsAsErrors`) sur les PR touchant les sources ou la doc

**La couverture de lignes est mesurée en public.** Le [workflow Coverage](https://github.com/Orkeon/orkeon/actions/workflows/coverage.yml) exécute les suites unitaires et rapides sous `dotnet-coverage` à chaque push sur `main` (et chaque semaine) : le chiffre est sur la page de résumé de chaque run, le fichier Cobertura et un rapport HTML sont son artefact `coverage`. Aucun nombre n'est cité ici — un nombre tapé dans un README est une affirmation, un run est une mesure. L'analyse statique tourne sur un SonarQube local via `scripts/sonar-analyze.sh` selon la [politique de quality gate](docs/fr/guides/quality-gate.md) ; ses rapports ne sont pas versionnés, donc ses chiffres ne sont pas cités ici non plus.

Les contraintes connues sont suivies dans [docs/fr/reference/limitations.md](docs/fr/reference/limitations.md). Ce qui se passe si le projet s'arrête — MIT, build reproductible, aucune infrastructure privée, forkable par quiconque — est écrit dans [SUPPORT.fr.md](SUPPORT.fr.md#si-le-projet-sarrête).

---

## Contribuer

Les contributions sont bienvenues. Ouvrez une issue pour discuter des changements significatifs avant de soumettre une pull request. Assurez-vous que les tests joués par la CI passent (`dotnet test Orkeon.sln --filter "Category!=Integration&Category!=Slow"` — [CONTRIBUTING.fr.md](CONTRIBUTING.fr.md) explique pourquoi le filtre n'est pas optionnel) et que le nouveau code suit les conventions de Clean Architecture décrites dans [CONTRIBUTING.fr.md](CONTRIBUTING.fr.md).

### Compiler depuis les sources

La couche scripting (support `.ork.ts`) embarque une petite toolchain esbuild provisionnée au premier `dotnet build`. Sur un clone frais, le projet `Orkeon.Scripting` exécute `npm ci` (strictement depuis le `tools/scripting-esbuild/package-lock.json` commité, le lockfile n'est jamais modifié) sous `tools/scripting-esbuild/` pour créer `node_modules/`. C'est une étape unique par clone, qui touche le réseau.

Pour la sauter entièrement (p. ex. en CI ou pour empaqueter des consommateurs NuGet qui n'ont pas besoin d'esbuild) :

```bash
dotnet build Orkeon.sln -p:SkipScriptingNpmInstall=true
```

Si npm est indisponible, le build réussit quand même ; esbuild est alors résolu depuis le `PATH` à l'exécution.

---

## Communauté et support

- **Obtenir de l'aide** — [SUPPORT.fr.md](SUPPORT.fr.md) nomme les lieux : les [GitHub Discussions](https://github.com/Orkeon/orkeon/discussions) pour les questions et les retours d'usage, les [formulaires d'issue](https://github.com/Orkeon/orkeon/issues/new/choose) pour les bugs et les demandes de fonctionnalité. Il n'y a ni Discord ni Slack.
- **Signaler une vulnérabilité** — [SECURITY.fr.md](SECURITY.fr.md). Jamais d'issue publique : passez par le GitHub Private Vulnerability Reporting (dépôt → *Security* → *Report a vulnerability*).
- **Règles de vie commune** — le [code de conduite](CODE_OF_CONDUCT.fr.md) (Contributor Covenant 2.1) s'applique à tous les espaces du projet.

---

## Licence

Orkeon est publié sous [licence MIT](LICENSE.md).

