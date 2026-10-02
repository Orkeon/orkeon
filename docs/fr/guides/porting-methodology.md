> 🇬🇧 [English version](../../guides/porting-methodology.md)

> **Voir aussi** : [Exemple complet](./porting-example.md) · [Inventaire outils](../tools/inventory.md) · [Retour à l'index](../INDEX.md)

# Guide de portage — Méthodologie en 5 étapes

Ce guide détaille la méthodologie pour porter une application .NET existante vers une architecture d'équipes d'agents Orkeon.

## Vue d'ensemble

Le portage consiste à décomposer les responsabilités d'une application monolithique ou modulaire en rôles d'agents spécialisés, équipés d'outils, et orchestrés par une ou plusieurs Crews.

```
Application source → Analyse des responsabilités → Mapping vers agents
→ Identification des outils → Définition des tasks → Plan de portage
```

## Choix de l'approche : YAML-first ou Code-first

Orkeon supporte deux approches pour définir une crew. Le choix impacte le workflow de portage.

| Critère | YAML-first | Code-first (Fluent Builder) |
|---------|-----------|---------------------------|
| Modification sans recompilation | Oui — éditer le YAML suffit | Non — recompilation nécessaire |
| Type safety | Validation au runtime (CrewFactory) | Validation à la compilation |
| Prompt engineering itératif | Rapide — éditer descriptions/backstories | Plus lent |
| Outils custom avec dépendances | Nécessite enregistrement DI séparé | Instanciation directe possible |
| Partage de configurations | Fichier YAML portable | Code C# à intégrer |
| Exemples de référence | 100+ exemples YAML dans `examples/` (voir l'`examples/INDEX.md` généré) | Builders documentés dans la doc |

**Recommandation** : privilégier l'approche **YAML-first** pour le portage. Le YAML permet d'itérer rapidement sur les prompts (descriptions, backstories) sans toucher au code. Les outils custom restent en C# et sont enregistrés via DI.

### Workflow YAML-first

```
1. Écrire config.yaml (agents, tasks, process type — voir le schéma YAML)
2. Identifier les outils manquants
3. Coder les outils custom (ToolBase<TReq, TRes>)
4. Enregistrer via DI (AddSingleton<IBaseTool, MonOutil>()) — le registre d'outils
   par défaut d'AddOrkeonInfrastructure() le lit, donc les noms YAML se résolvent
5. Charger et exécuter — le chemin du YAML est un chemin virtuel, lu via le VFS :
   var crew = await crewFactory.CreateFromFileAsync("/crews/config.yaml");
   var output = await orchestrator.KickoffAsync(crew.Id, input);
6. Itérer sur les prompts dans le YAML
```

Tant que la crew n'utilise que des outils intégrés, aucun code hôte n'est nécessaire : `orkeon run config.yaml --validate` la charge avec une résolution stricte des outils (sans appel LLM), et `orkeon run config.yaml` l'exécute — le runner lit la section `Llm`, monte les dossiers et enregistre lui-même le registre d'outils adossé à la DI (voir la [référence CLI](../reference/cli.md#orkeon-run)). L'hôte de la section 5.5 devient nécessaire dès que des outils C# custom entrent dans le portage. Les clés YAML sont listées dans le [schéma YAML](../architecture/yaml-schema.md).

## Étape 1 — Analyser les responsabilités de l'application source

### 1.1 Inventorier les composants

Lister tous les services, controllers, handlers et modules de l'application. Pour chaque composant, noter sa responsabilité principale, ses dépendances entrantes et sortantes, et le type de données qu'il manipule.

### 1.2 Identifier les flux de données

Tracer le parcours des données à travers l'application : d'où viennent-elles (fichiers, API, base de données), quelles transformations subissent-elles, où arrivent-elles (stockage, API, UI).

### 1.3 Classer les responsabilités

Catégoriser chaque responsabilité selon cette grille :

| Catégorie | Description | Exemples |
|-----------|-------------|----------|
| **Collecte** | Acquisition de données depuis des sources externes | Appels API, lecture fichiers, scraping web, requêtes BDD |
| **Analyse** | Traitement, transformation, enrichissement des données | Parsing, calculs, agrégations, détection de patterns |
| **Décision** | Logique métier, règles, routing conditionnel | Validation, scoring, classification, priorisation |
| **Production** | Génération d'artefacts de sortie | Rapports, emails, fichiers, réponses API |
| **Coordination** | Orchestration de sous-processus | Workflow management, séquencement, parallélisation |

### 1.4 Identifier les interactions humaines

Repérer les points où l'application nécessite une intervention humaine (validation, saisie, approbation). Ces points deviendront des tasks avec `HumanInput = true` dans Orkeon (`humanInput: true` en YAML). Le drapeau donne à l'agent l'outil `human_input` pour cette task ; l'hôte doit donc en enregistrer un : `orkeon run` fait remonter la question sur son bus d'événements de run, et un hôte construit à la main appelle `AddOrkeonHumanInput<TProvider>()` avec son propre `IHumanInputProvider` (`AddOrkeonHumanInput()` sans paramètre approuve automatiquement). Sans outil `human_input` enregistré, le drapeau est sans effet.

## Étape 2 — Mapper les responsabilités vers des rôles d'agents

### 2.1 Principes de découpage

Un bon agent Orkeon respecte ces principes :

- **Responsabilité unique** : chaque agent a un rôle clair et spécialisé (défini par `AgentRole`)
- **Objectif mesurable** : l'objectif (`AgentGoal`) décrit un résultat concret et vérifiable
- **Autonomie** : l'agent doit pouvoir accomplir ses tasks avec ses outils sans dépendre d'un autre agent pour chaque opération
- **Granularité appropriée** : ni trop large (agent "fait tout") ni trop fin (agent qui fait une seule opération triviale)

### 2.2 Template de mapping

Pour chaque responsabilité identifiée en Étape 1, remplir ce template :

```
Responsabilité source : [description]
→ Rôle agent         : [AgentRole — nom concis du spécialiste]
→ Objectif agent     : [AgentGoal — résultat attendu]
→ Backstory          : [AgentBackstory — contexte et expertise]
→ Outils nécessaires : [liste des outils]
→ Contraintes        : [MaxIterations, MaxRpm, AllowDelegation]
```

### 2.3 Patterns de mapping courants

| Pattern source | Mapping Orkeon |
|---------------|----------------|
| Service qui lit et transforme des données | Agent "Data Analyst" avec outils fichier/BDD |
| Service qui appelle des API externes | Agent "API Integrator" avec `HttpApiTool` |
| Service qui génère des rapports | Agent "Report Writer" avec `FileWriteTool` |
| Controller qui orchestre un workflow | Crew avec `ProcessType.Sequential` |
| Service de validation / review | Agent "Quality Reviewer" avec validation output JSON |
| Scheduler / batch processor | Crew avec `KickoffForEachAsync` (batch) |
| Service qui route le travail vers des spécialistes à l'exécution | Crew `Hierarchical` avec `LlmBasedManager` |
| Service avec branchements explicites ou boucles de reprise | Crew `Graph` (`StateGraph<TState>`, arêtes conditionnelles, cycles contrôlés) |
| Travail ouvert dont les étapes ne sont pas connues à l'avance | Crew `Autonomous` (un LLM manager attribue chaque tâche ; une tâche en échec passe à un agent pair, dans une profondeur de délégation comptée à l'échelle de la crew ; des agents supplémentaires uniquement via un `SpawnAgentTool` enregistré par l'hôte ; le tout borné par `AgentExecutionBudget`) |

### 2.4 Quand créer un agent vs. un outil

| Créer un **agent** quand... | Créer un **outil** quand... |
|----------------------------|---------------------------|
| La responsabilité nécessite du raisonnement, de l'analyse ou de la créativité | L'opération est déterministe et mécanique |
| Le résultat varie selon le contexte et les données | L'opération suit toujours le même algorithme |
| Plusieurs étapes de réflexion sont nécessaires | C'est une opération atomique (entrée → sortie) |
| L'interaction avec un LLM apporte de la valeur | Un LLM n'apporterait rien de plus qu'un algorithme |

## Étape 3 — Identifier les outils nécessaires

### 3.1 Inventorier les besoins en outils

Pour chaque agent défini en Étape 2, lister les opérations concrètes qu'il doit effectuer. Pour chaque opération, vérifier si un outil existant la couvre (voir [Inventaire des outils](../tools/inventory.md)).

### 3.2 Matrice de décision : réutiliser vs. créer

| Critère | Réutiliser l'existant | Créer un nouvel outil |
|---------|----------------------|----------------------|
| L'opération est couverte par un outil Orkeon | Oui | — |
| L'opération existante est presque adaptée mais manque un paramètre | Envisager une contribution/extension | — |
| L'opération nécessite un appel à une API métier spécifique | — | Oui — hériter de `HttpToolBase<>` |
| L'opération manipule un format de fichier non supporté | — | Oui — hériter de `FileToolBase<>` |
| L'opération est un calcul métier pur | — | Oui — hériter de `ToolBase<>` |

### 3.3 Outils existants par besoin courant

Le nom YAML est celui qu'une crew liste sous les `tools:` d'un agent.

| Besoin | Outil existant | Nom YAML | Package / enregistrement |
|--------|---------------|-----------|---------|
| Lire un fichier texte/JSON/XML | `FileReadTool` | `file_read` | `Orkeon.Tools.FileSystem` — `AddOrkeonFileSystemTools()` |
| Écrire un fichier | `FileWriteTool` | `file_write` | `Orkeon.Tools.FileSystem` — `AddOrkeonFileSystemTools()` |
| Lister un répertoire | `DirectoryReadTool` | `directory_read` | `Orkeon.Tools.FileSystem` — `AddOrkeonFileSystemTools()` |
| Rechercher dans des fichiers | `DirectorySearchTool` | `directory_search` | `Orkeon.Tools.FileSystem` — `AddOrkeonFileSystemTools()` |
| Lire un CSV | `CsvReaderTool` | `csv_reader` | `Orkeon.Tools.Data` — `AddOrkeonDataTools()` |
| Lire un PDF | `PdfReaderTool` | `pdf_reader` | `Orkeon.Tools.Data` — `AddOrkeonDataTools()` |
| Lire un DOCX | `DocxReadTool` | `docx_reader` | `Orkeon.Tools.Data` — `AddOrkeonDataTools()` |
| Lire / écrire un Excel (.xlsx) | `XlsxReadTool` / `XlsxWriteTool` | `xlsx_reader` / `xlsx_writer` | `Orkeon.Tools.Data` — `AddOrkeonDataTools()` |
| Manipuler du JSON | `JsonTool` | `json_tool` | `Orkeon.Tools.Data` — `AddOrkeonDataTools()` |
| Requête SQL | `RelationalDatabaseTool` | `relational_database_query` | `Orkeon.Tools.Data` — `AddOrkeonDataTools()` |
| Requête MongoDB | `MongoDbTool` | `mongodb_query` | `Orkeon.Tools.Data` — `AddOrkeonDataTools()` |
| Recherche web | `WebSearchTool` / `BraveSearchTool` | `web_search` / `brave_search` | `Orkeon.Tools.Web` — opt-in `AddOrkeonWebSearchTool()` / `AddOrkeonBraveSearchTool(apiKey)` |
| Scraping web | `WebScrapeTool` | `web_scrape` | `Orkeon.Tools.Web` — `AddOrkeonWebTools()` |
| Appel API REST | `HttpApiTool` | `http_api` | `Orkeon.Tools.Web` — `AddOrkeonWebTools()` |
| Exécuter une commande shell | `ShellCommandTool` | `shell_command` | `Orkeon.Tools.Code` — `AddOrkeonCodeTools()` |
| Exécuter du code C# | `SecureCodeInterpreterTool` | `code_interpreter` | `Orkeon.Infrastructure` — enregistré comme type concret uniquement : ajouter `services.AddSingleton<IBaseTool>(sp => sp.GetRequiredService<SecureCodeInterpreterTool>())` pour qu'une crew YAML le résolve par son nom |
| Lire, préparer ou envoyer des e-mails | les 13 outils `email_*` | `email_read`, `email_draft`, `email_send`, … | `Orkeon.Tools.Email` — `AddOrkeonEmailTools(configuration)` (voir [Outils e-mail](./email.md)) |
| Demander à un collègue | `AskQuestionTool` | `ask_question_to_coworker` | `Orkeon.Infrastructure` — fourni par agent quand `AllowDelegation` est actif |
| Déléguer une tâche | `DelegateWorkTool` | `delegate_work_to_coworker` | `Orkeon.Infrastructure` — fourni par agent quand `AllowDelegation` est actif |
| Recherche sémantique | `SearchTool` | `semantic_search` | `Orkeon.Infrastructure` — opt-in `AddSemanticSearchTool()` (`Orkeon.Hosting` ; `orkeon run` l'appelle) |
| RAG sur documents | `RagSearchTool` | `rag_search` | `Orkeon.Tools.Rag` (opt-in : `AddOrkeonRag` + `AddOrkeonRagTools`) |

## Étape 4 — Définir les tasks et le flux d'orchestration

### 4.1 Décomposer en tasks

Chaque sortie attendue de la crew devient une `CrewTask`. Une task est définie par sa `TaskDescription` (ce que l'agent doit faire) et son `ExpectedOutput` (format et contenu du résultat attendu).

### 4.2 Choisir le ProcessType

| Situation | ProcessType recommandé |
|-----------|----------------------|
| Les étapes doivent s'enchaîner, chaque sortie alimentant l'entrée suivante | `Sequential` |
| Un manager doit router dynamiquement les tasks vers les agents les plus compétents | `Hierarchical` |
| Plusieurs tâches indépendantes peuvent s'exécuter simultanément | `Parallel` |
| Les agents doivent voter et atteindre un consensus | `Consensual` |
| Le flux bifurque selon des conditions ou boucle jusqu'à ce qu'une vérification passe | `Graph` |
| Un LLM manager route chaque tâche, une tâche en échec a une seconde chance avec un pair, le tout dans un budget | `Autonomous` |

En YAML, le mode est la clé `process:` de la crew (`sequential`, `hierarchical`, `parallel`, `consensual`, `graph`, `autonomous`) ; avec le builder, `.Sequential()`, `.Hierarchical()`, `.Parallel()`, `.Consensual()` ou `.Process(ProcessType.Graph)` / `.Process(ProcessType.Autonomous)`. Voir les guides d'orchestration [Graph](../orchestration/graph.md) et [Autonomous](../orchestration/autonomous.md) pour les deux derniers.

### 4.3 Définir les dépendances

Utiliser `CrewTaskBuilder.DependsOn()` (YAML : `dependencies:`) pour exprimer les pré-requis entre tasks. En mode `Sequential`, l'ordre de déclaration suffit, et les dépendances déclarées le réordonnent lorsqu'elles le contredisent. En mode `Parallel`, les dépendances découpent les tasks en vagues : les tasks dont les pré-requis sont satisfaits s'exécutent ensemble, et la vague suivante démarre quand elles sont terminées, avec leurs sorties en contexte.

### 4.4 Configurer les options d'exécution

Pour chaque task, décider de : la priorité (`TaskPriority`), l'intervention humaine (`HumanInput`, voir 1.4), le schéma de validation de sortie (`OutputJson`), et le fichier que le framework écrit à partir du résultat (le bloc YAML `deliverable:` — `path`, `format`, schéma optionnel). `AsyncExecution` (`asyncExecution:`, l'`async_execution` de CrewAI) est honoré par `process: sequential` — la tâche tourne pendant les tâches qui la suivent, et une tâche qui en dépend l'attend — et accepté sans effet propre par `process: parallel` ; les quatre autres modes le refusent au chargement (voir [Tâches asynchrones](../orchestration/process-types.md#tâches-asynchrones-asyncexecution)).

## Étape 5 — Produire le plan de portage

### 5.1 Template du plan

| Composant source | Responsabilité | Agent cible | Rôle | Outil(s) requis | Statut | Effort |
|-----------------|----------------|-------------|------|-----------------|--------|--------|
| `OrderService` | Validation commandes | Order Validator | "Order Validation Specialist" | `RelationalDatabaseTool`, outil custom `ValidateOrderTool` | À créer (outil custom) | M |
| `PricingEngine` | Calcul des prix | Pricing Analyst | "Pricing Specialist" | `CsvReaderTool`, `JsonTool` | Prêt (outils existants) | S |
| ... | ... | ... | ... | ... | ... | ... |

### 5.2 Légende effort

| Code | Signification | Durée estimée |
|------|--------------|---------------|
| **XS** | Mapping direct vers outil existant, aucun code | < 1h |
| **S** | Agent simple avec outils existants | 1-4h |
| **M** | Agent + 1 outil custom simple | 0.5-1 jour |
| **L** | Agent + outil custom complexe ou intégration externe | 1-3 jours |
| **XL** | Refactoring significatif, multiple outils custom | > 3 jours |

### 5.3 Colonnes Statut

| Statut | Signification |
|--------|--------------|
| Prêt | Tous les outils existent, configuration uniquement |
| À créer (outil) | Un ou plusieurs outils custom doivent être développés |
| À créer (agent) | L'agent nécessite un backstory/prompt engineering spécifique |
| Bloqué | Dépendance externe non résolue |

### 5.4 Checklist de validation du plan

Avant de démarrer l'implémentation, vérifier que :

- Chaque responsabilité de l'application source est couverte par au moins un agent
- Chaque agent a au moins une task assignée
- Toutes les dépendances entre tasks sont explicites
- Les outils manquants sont identifiés avec un effort estimé
- Le `ProcessType` est justifié par la nature du workflow
- Les points d'intervention humaine sont identifiés (`HumanInput = true`)
- La mémoire est activée si la crew doit se souvenir de ses runs précédents (`EnableMemory(true)`, `memory: true`) — pas pour le contexte entre les tâches d'un même run, que les sorties précédentes portent déjà dans chaque prompt
- Les contraintes de rate limiting (`MaxRpm`) sont compatibles avec les API externes utilisées

### 5.5 Setup DI minimal pour le portage

Un portage qui apporte ses propres outils C# s'exécute dans son propre hôte. Tout hôte de ce type nécessite ce setup d'injection de dépendances :

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Application.DependencyInjection;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Hosting;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Tools.Code.DependencyInjection;
using Orkeon.Tools.Data.DependencyInjection;
using Orkeon.Tools.FileSystem.DependencyInjection;
using Orkeon.Tools.Web.DependencyInjection;

var host = Host.CreateDefaultBuilder(args)
    // Même surcouche d'environnement que les runners : ORKEON_Llm__ApiKey → Llm:ApiKey
    .ConfigureAppConfiguration(config => config.AddEnvironmentVariables("ORKEON_"))
    .ConfigureServices((context, services) =>
    {
        // Requis — couches Application et Infrastructure
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure(context.Configuration);

        // Requis — le système de fichiers virtuel par lequel passent tous les outils et le
        // chargeur YAML (montages déclarés sous Orkeon:FileSystem:Mounts, ex. "/srv/app/crews:/crews:ro")
        services.AddOrkeonFileSystem(context.Configuration);

        // Suites d'outils — ajouter uniquement celles nécessaires
        services.AddOrkeonFileSystemTools();   // Si agents lisent/écrivent des fichiers
        services.AddOrkeonDataTools();         // Si agents manipulent CSV, PDF, JSON, SQL, MongoDB
        services.AddOrkeonWebTools();          // Si agents scrapent des pages ou appellent des API HTTP
        services.AddOrkeonCodeTools();         // Si agents exécutent du shell

        // Outils custom identifiés en Étape 3
        services.AddSingleton<IBaseTool, MonOutilCustom1>();
        services.AddSingleton<IBaseTool, MonOutilCustom2>();

        // Les noms d'outils YAML se résolvent contre ces enregistrements IBaseTool : le
        // ToolRegistry par défaut d'AddOrkeonInfrastructure() les lit — rien de plus à enregistrer.

        // LLM — un hôte construit à la main ne lit aucune section Llm de lui-même : construire
        // le provider ici. Utiliser la classe provider du fournisseur (OpenAIProvider, AnthropicLlmProvider, ...).
        var llm = LlmConfig.Create(
            context.Configuration["Llm:Model"] ?? "gpt-5.6-sol",
            context.Configuration["Llm:ApiKey"]);
        services.AddOrkeonLlmProvider(
            sp => new OpenAIProvider(llm,
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ILogger<OpenAIProvider>>()),
            llm);
    })
    .Build();
```

`AddOrkeonLlmProvider` vient **après** `AddOrkeonInfrastructure` : il enregistre le provider sur les trois surfaces que consomme le runtime (`ILlmProvider`, `IBasicLlmProvider`, `IChatClient`), avec comptage, et le dernier enregistrement l'emporte. Un montage dont le dossier est hors du répertoire de travail doit aussi figurer sous `PathSecurity:AdditionalAllowedDirectories`. `orkeon run` fait tout ce qui précède à partir du fichier de settings (voir [Configuration](../reference/configuration.md)).

### 5.6 Pattern d'exécution

```csharp
// ICrewFactory est scoped : le résoudre depuis un scope
using var scope = host.Services.CreateScope();
var services = scope.ServiceProvider;

// Charger la crew depuis le YAML (un chemin virtuel, sous un montage déclaré)
var crewFactory = services.GetRequiredService<ICrewFactory>();
var crew = await crewFactory.CreateFromFileAsync("/crews/config.yaml");

// Préparer l'input avec des variables d'exécution (templates de task {date}, {environment})
var input = CrewInput.WithStringVariables(
    "Contexte initial pour l'exécution",
    new Dictionary<string, string>
    {
        ["date"] = DateTime.Today.ToString("yyyy-MM-dd"),
        ["environment"] = "production"
    });

// Exécuter et récupérer les résultats — KickoffAsync ne lève jamais d'exception : un échec
// revient avec Succeeded = false et sa raison dans Error
var orchestrator = services.GetRequiredService<ICrewOrchestrationService>();
var output = await orchestrator.KickoffAsync(crew.Id, input);

// Exploiter les résultats
if (output.Succeeded)
{
    // Succès — traiter le résultat final
    Console.WriteLine(output.FinalOutput);
}
else
{
    // Échec — la raison, puis les tasks en erreur
    Console.Error.WriteLine($"Crew failed: {output.Error}");
    foreach (var failed in output.TaskOutputs.Where(t => !t.Success))
        Console.Error.WriteLine($"Task {failed.TaskId} failed: {failed.RawOutput}");
}
```
