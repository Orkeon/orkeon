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
| Exemples de référence | 103 exemples YAML dans `examples/` | Builders documentés dans la doc |

**Recommandation** : privilégier l'approche **YAML-first** pour le portage. Le YAML permet d'itérer rapidement sur les prompts (descriptions, backstories) sans toucher au code. Les outils custom restent en C# et sont enregistrés via DI.

### Workflow YAML-first

```
1. Écrire config.yaml (agents, tasks, process type)
2. Identifier les outils manquants
3. Coder les outils custom (ToolBase<TReq, TRes>)
4. Enregistrer via DI (AddSingleton<IBaseTool, MonTool>())
5. Charger et exécuter :
   var crew = await crewFactory.CreateFromFileAsync("config.yaml");
   var output = await orchestrator.KickoffAsync(crew.Id, input);
6. Itérer sur les prompts dans le YAML
```

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

Repérer les points où l'application nécessite une intervention humaine (validation, saisie, approbation). Ces points deviendront des tasks avec `HumanInput = true` dans Orkeon.

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
| Service avec logique de branching complexe | Crew `Hierarchical` avec `LlmBasedManager` |

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

| Besoin | Outil existant | Package |
|--------|---------------|---------|
| Lire un fichier texte/JSON/XML | `FileReadTool` | `Orkeon.Tools.FileSystem` |
| Écrire un fichier | `FileWriteTool` | `Orkeon.Tools.FileSystem` |
| Lister un répertoire | `DirectoryReadTool` | `Orkeon.Tools.FileSystem` |
| Rechercher dans des fichiers | `DirectorySearchTool` | `Orkeon.Tools.FileSystem` |
| Lire un CSV | `CsvReaderTool` | `Orkeon.Tools.Data` |
| Lire un PDF | `PdfReaderTool` | `Orkeon.Tools.Data` |
| Lire un DOCX | `DocxReadTool` | `Orkeon.Tools.Data` |
| Manipuler du JSON | `JsonTool` | `Orkeon.Tools.Data` |
| Requête SQL | `RelationalDatabaseTool` | `Orkeon.Tools.Data` |
| Requête MongoDB | `MongoDbTool` | `Orkeon.Tools.Data` |
| Recherche web | `WebSearchTool` / `BraveSearchTool` | `Orkeon.Tools.Web` |
| Scraping web | `WebScrapeTool` | `Orkeon.Tools.Web` |
| Appel API REST | `HttpApiTool` | `Orkeon.Tools.Web` |
| Exécuter du code C# | `SecureCodeInterpreterTool` | `Orkeon.Infrastructure` |
| Demander à un collègue | `AskQuestionTool` | `Orkeon.Infrastructure` |
| Déléguer une tâche | `DelegateWorkTool` | `Orkeon.Infrastructure` |
| Recherche sémantique | `SearchTool` | `Orkeon.Infrastructure` |
| RAG sur documents | `RagTool` | `Orkeon.Infrastructure` |

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

### 4.3 Définir les dépendances

Utiliser `CrewTaskBuilder.DependsOn()` pour exprimer les pré-requis entre tasks. En mode `Sequential`, l'ordre de déclaration suffit. En mode `Parallel`, les dépendances explicites contrôlent le séquencement.

### 4.4 Configurer les options d'exécution

Pour chaque task, décider de : la priorité (`TaskPriority`), l'exécution asynchrone (`AsyncExecution`), l'intervention humaine (`HumanInput`), le schéma de validation de sortie (`OutputJson`), le fichier de sortie (`OutputFile`).

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
- La mémoire est activée si le contexte inter-tasks est nécessaire (`EnableMemory(true)`)
- Les contraintes de rate limiting (`MaxRpm`) sont compatibles avec les API externes utilisées

### 5.5 Setup DI minimal pour le portage

Tout portage nécessite ce setup d'injection de dépendances :

```csharp
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Requis — couches Application et Infrastructure
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();

        // Suites d'outils — ajouter uniquement celles nécessaires
        services.AddOrkeonFileSystemTools();   // Si agents lisent/écrivent des fichiers
        services.AddOrkeonDataTools();         // Si agents manipulent CSV, PDF, JSON, SQL, MongoDB
        services.AddOrkeonWebTools();          // Si agents font du web search, scraping, HTTP API
        services.AddOrkeonCodeTools();         // Si agents exécutent du shell

        // Outils custom identifiés en Étape 3
        services.AddSingleton<IBaseTool, MonOutilCustom1>();
        services.AddSingleton<IBaseTool, MonOutilCustom2>();

        // Configuration LLM (si pas de défaut)
        services.Configure<LlmConfig>(context.Configuration.GetSection("Llm"));
    })
    .Build();
```

### 5.6 Pattern d'exécution

```csharp
// Charger la crew depuis le YAML
var crewFactory = host.Services.GetRequiredService<ICrewFactory>();
var crew = await crewFactory.CreateFromFileAsync("config.yaml");

// Préparer l'input avec des variables typées
var variables = CrewVariables.Empty
    .Set("date", DateTime.Today.ToString("yyyy-MM-dd"))
    .Set("environment", "production");

var input = new CrewInput(
    initialContext: "Contexte initial pour l'exécution",
    variables: variables);

// Exécuter et récupérer les résultats
var orchestrator = host.Services.GetRequiredService<ICrewOrchestrationService>();
var output = await orchestrator.KickoffAsync(crew.Id, input);

// Exploiter les résultats
if (output.Success)
{
    // Succès — traiter le résultat final
    Console.WriteLine(output.Output);
}
else
{
    // Échec — identifier les tasks en erreur
    Console.Error.WriteLine($"Crew failed: {output.Error}");
    foreach (var failed in output.TaskOutputs.Where(t => !t.Success))
        Console.Error.WriteLine($"Task {failed.TaskId} failed: {failed.RawOutput}");
}
```
