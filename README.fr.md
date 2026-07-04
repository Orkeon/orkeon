> 🇬🇧 [English version](README.md)

# Orkeon

**Construisez et orchestrez des équipes d'agents IA en .NET**

[![NuGet](https://img.shields.io/nuget/v/Orkeon.Domain.svg)](https://www.nuget.org/packages/Orkeon.Domain/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.md)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Build](https://github.com/Orkeon/orkeon/actions/workflows/ci.yml/badge.svg)](https://github.com/Orkeon/orkeon/actions/workflows/ci.yml)

---

## Qu'est-ce qu'Orkeon ?

Orkeon est un framework C# pour créer et gérer des équipes collaboratives d'agents IA qui s'attaquent à des tâches complexes en plusieurs étapes à l'aide de grands modèles de langage. Les agents sont organisés en crews, chacun avec un rôle, un objectif et un ensemble d'outils définis, et travaillent ensemble via l'une des six stratégies d'orchestration (séquentielle, hiérarchique, parallèle, consensuelle, graphe ou autonome). Bâti sur les principes de la Clean Architecture, Orkeon fournit une fondation entièrement typée et extensible pour des workflows agentiques de qualité production en .NET.

---

## Démarrage rapide

> Pour le guide pas à pas complet, voir [Getting Started — Overview](docs/getting-started/overview.md).

```csharp
using Orkeon.Domain.Builders;

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
```

---

## Installation

```bash
dotnet add package Orkeon.Domain --version 0.9.0-beta
dotnet add package Orkeon.Application --version 0.9.0-beta
dotnet add package Orkeon.Infrastructure --version 0.9.0-beta
```

---

## Fonctionnalités

| Capacité | Détails |
|---|---|
| **70+ outils intégrés** | Système de fichiers, web scraping (AngleSharp), APIs HTTP, JSON/CSV/XML/PDF, bases de données, exécution de code sécurisée, RAG et recherche sémantique, messagerie EventHub, analyse de code RaggableTree, délégation/collaboration — voir l'[inventaire des outils](docs/tools/inventory.md) |
| **11 fournisseurs LLM** | OpenAI, Ollama, Anthropic, Azure OpenAI, Groq, Together AI, Qwen, DeepSeek, Kimi (Moonshot), HuggingFace et Mistral AI — tous basés sur HTTP, étendant `HttpLlmProviderBase` |
| **Vision / multimodal** | Le contenu image circule de bout en bout (`MultiModalContent` → blocs image Anthropic / `image_url` OpenAI) avec un chargeur adossé au VFS ; opt-in via `AddOrkeonMultiModal(...)` — voir le [guide multimodal](docs/guides/multimodal.md) |
| **6 fournisseurs de mémoire** | Redis (recherche vectorielle), SQLite, InMemory, ChromaDB (REST API v2), Pinecone, LanceDB (serveur REST distant) — tous composables avec le décorateur de chiffrement au repos AES-256-GCM |
| **6 stratégies d'orchestration** | Sequential, Hierarchical, Parallel, Consensual (stratégies de vote Majority / SuperMajority / Unanimity), Graph (style LangGraph), Autonomous (budget d'exécution multi-dimensionnel) — voir le [guide des process types](docs/orchestration/process-types.md) |
| **Système de plugins** | Assemblies drop-in implémentant `IOrkeonPlugin`, découvertes dans un répertoire de plugins, chargées dans des `AssemblyLoadContext` collectables et isolés, activées explicitement via `AddOrkeonPlugins(...)` — voir [plugins](docs/architecture/plugins.md) |
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
| **Sous-systèmes opt-in** | A2A, monitoring, conformité NIST, DLP, rate-limiting des outils, rotation de clés, benchmarking, multimodal, hooks de kickoff — aucun n'est enregistré par défaut, chacun s'active via son extension dédiée `AddOrkeonXxx()` — voir la [référence des opt-in](docs/reference/opt-in-subsystems.md) |

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

Autour du cœur, des paquets dédiés couvrent l'hébergement (`Orkeon.Hosting`), les plugins (`Orkeon.Plugins`), les générateurs de source Roslyn (`Orkeon.Generators`), l'analyseur de conformité VFS (`Orkeon.Compliance.Vfs`), le DSL de scripting à syntaxe TypeScript (`Orkeon.Scripting` plus le tool CLI `orkeon`), les packs d'outils (`Orkeon.Tools.*`) et le moteur d'analyse sémantique de code RaggableTree (`Orkeon.Analysis`).

Pour une présentation détaillée, voir l'[index de la documentation](docs/INDEX.md).

---

## Pourquoi Orkeon ?

| Caractéristique | Orkeon | Semantic Kernel |
|---|---|---|
| Langage | C# / .NET 10 | C# / .NET 8+ |
| Multi-agents | Crews natifs | Plugins |
| Outils | 70+ intégrés | Via plugins |
| Architecture | Clean Architecture | Pattern Kernel |
| Fournisseurs LLM | 11 intégrés | 3+ via connecteurs |

---

## Statut du projet

Orkeon est en **0.9.0-beta** sur .NET 10, en route vers la V1. Les ajouts récents incluent le système de plugins, le paquet de bootstrap `Orkeon.Hosting`, les générateurs de source Roslyn, la persistance de l'état d'exécution avec reprise, les stratégies de vote Consensual, la rotation de clés et le support natif de la vision.

Chaque pull request est contrôlée en CI :

- la couverture de lignes fusionnée doit rester supérieure ou égale à **70 %** (montée à 75 % planifiée)
- une **quality gate SonarQube bloquante** (« Orkeon Transitional ») avec une trajectoire de durcissement documentée — voir la [politique de quality gate](docs/guides/quality-gate.md)

Les contraintes connues sont suivies dans [docs/reference/limitations.md](docs/reference/limitations.md).

---

## Docker

Exécutez Orkeon avec Docker et Ollama (LLM local gratuit) :

```bash
# Using Ollama (free, local)
docker compose up

# Using OpenAI
OPENAI_API_KEY=your-key docker compose up
```

Construire l'image manuellement :

```bash
docker build -t orkeon .
docker run -e OPENAI_API_KEY=your-key orkeon
```

---

## Contribuer

Les contributions sont les bienvenues. Merci d'ouvrir une issue pour discuter de tout changement significatif avant de soumettre une pull request. Assurez-vous que tous les tests passent (`dotnet test Orkeon.sln`) et que le nouveau code suit les conventions de Clean Architecture décrites dans [CLAUDE.md](CLAUDE.md).

### Compiler depuis les sources

La couche de scripting (support `.ork.ts`) embarque une petite toolchain esbuild amorcée au premier `dotnet build`. Sur un clone frais, le projet `Orkeon.Scripting` exécute `npm ci` (strictement depuis le `tools/scripting-esbuild/package-lock.json` commité, le lockfile n'est donc jamais modifié) sous `tools/scripting-esbuild/` pour provisionner `node_modules/`. C'est une étape unique par clone, avec accès réseau.

Pour la sauter entièrement (p. ex. en CI ou pour empaqueter des consommateurs NuGet qui n'ont pas besoin d'esbuild), passez le flag d'opt-out :

```bash
dotnet build Orkeon.sln -p:SkipScriptingNpmInstall=true
```

Si npm est indisponible, le build réussit quand même ; esbuild est alors résolu depuis le `PATH` à l'exécution.

---

## Licence

Orkeon est publié sous [licence MIT](LICENSE.md).

## Remerciements

Orkeon a débuté comme un portage C# de [CrewAI](https://github.com/crewAIInc/crewAI), distribué sous licence MIT, et a depuis évolué en un framework .NET indépendant. Nous remercions chaleureusement les auteurs de CrewAI.
