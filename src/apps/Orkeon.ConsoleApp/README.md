# Orkeon Console Application

A simple interactive console application for managing AI agents, crews, and tasks using the Orkeon framework.

## Features

- **Agent Management**: Create, list, and delete AI agents with specific roles and goals
- **Crew Management**: Create teams of agents, assign agents to crews
- **Task Management**: Create tasks with descriptions and expected outputs
- **Crew Execution**: Execute crews with assigned agents and tasks (simulation mode)
- **Quick Demo**: One-click demo setup with sample agents, crews, and tasks

## Concepts Fondamentaux

### Qu'est-ce qu'un Agent ?

Un **Agent** est une entité autonome intelligente avec :
- **Rôle** : Sa fonction spécifique dans l'équipe (ex: "Analyste", "Développeur", "Rédacteur")
- **Objectif** : Ce qu'il cherche à accomplir
- **Backstory** : Son contexte et expertise (optionnel mais recommandé)
- **Outils** : Les capacités dont il dispose (FileRead, WebSearch, HttpApi, etc.)
- **Capacités** : Peut déléguer, itérer sur les tâches, utiliser la mémoire

```csharp
// Exemple d'agent
var agent = new AgentConfiguration
{
    Id = "data_analyst",
    Role = "Data Analyst",
    Goal = "Analyser les données et identifier les tendances",
    Backstory = "Expert en analyse statistique avec 10 ans d'expérience",
    Tools = ["FileRead", "JsonParser", "DataAnalyzer"],
    MaxIterations = 15,
    AllowDelegation = true
};
```

### Qu'est-ce qu'une Task ?

Une **Task** (tâche) est une unité de travail avec :
- **Description** : Ce qui doit être accompli
- **Expected Output** : Le résultat attendu
- **Context** : Informations additionnelles nécessaires
- **Dependencies** : Autres tâches à compléter avant
- **Agent assigné** : L'agent responsable de cette tâche

```csharp
// Exemple de tâche
var task = new TaskConfiguration
{
    Id = "analyze_sales",
    Description = "Analyser les ventes du trimestre et identifier les produits performants",
    ExpectedOutput = "Rapport détaillé avec top 10 produits et recommandations",
    AssignedAgentId = "data_analyst",
    Context = { { "quarter", "Q4-2024" }, { "region", "Europe" } },
    TimeoutSeconds = 300
};
```

### Qu'est-ce qu'une Crew ?

Une **Crew** (équipe) est un groupe d'agents collaborant sur des tâches communes :
- **Composition** : Ensemble d'agents avec des rôles complémentaires
- **Process Type** : Comment les tâches sont exécutées
  - **Sequential** : Une tâche après l'autre
  - **Hierarchical** : Un manager coordonne les agents
  - **Consensual** : Les agents votent sur les décisions
  - **Parallel** : Tâches exécutées simultanément
- **Mémoire** : Partage de contexte entre agents
- **Planning** : Stratégie d'exécution adaptative

### Flux d'Exécution

```
┌─────────┐     ┌──────────┐     ┌────────┐
│  CREW   │────▶│  AGENTS  │────▶│ TASKS  │
└────┬────┘     └────┬─────┘     └───┬────┘
     │               │                │
     │   Assigne     │    Execute     │
     └──────────────▶├───────────────▶│
                     │                │
                     │   Résultats    │
                     ◀────────────────┘
```

## Création et Affectation des Tâches

### Qui peut créer des tâches ?

#### 1. **L'Utilisateur** (le plus courant)

```csharp
// Via l'interface console
var taskService = new TaskManagementService();
await taskService.CreateTaskAsync(
    "Analyser les données de vente",
    "Contexte: Q4 2024",
    "Rapport détaillé avec graphiques"
);

// Via configuration YAML
tasks:
  - id: analyze_data
    description: Analyser les données de vente
    expected_output: Rapport avec visualisations
    assigned_agent: data_analyst

// Par programmation directe
var task = Task.Create(
    new TaskDescription("Analyser les ventes"),
    "Rapport complet",
    TaskPriority.High
);
```

#### 2. **La Crew** (lors de l'initialisation)

```csharp
var crew = new CrewConfiguration
{
    Name = "Analytics Team",
    Tasks = new List<TaskConfiguration>
    {
        new() {
            Id = "task1",
            Description = "Collecter les données",
            AssignedAgentId = "collector_agent"
        },
        new() {
            Id = "task2",
            Description = "Analyser les tendances",
            AssignedAgentId = "analyst_agent",
            Dependencies = ["task1"] // Dépend de task1
        }
    }
};
```

#### 3. **Les Agents** (via délégation)

```csharp
// Un agent peut créer une sous-tâche et la déléguer
var delegateResult = await DelegateWorkTool.ExecuteAsync(new
{
    task = "Implémenter la fonction de validation",
    context = "Utiliser les règles métier définies",
    coworkerRole = "Developer"
});

// En mode hiérarchique, le ManagerAgent crée dynamiquement
if (crew.Process == ProcessType.Hierarchical)
{
    // Le manager analyse et distribue les tâches
    var managerDecision = await managerAgent.DecideTaskAssignment(
        availableAgents,
        pendingTasks
    );
}
```

### Règle Importante : Une Tâche = Un Agent

⚠️ **Une tâche ne peut être assignée qu'à UN SEUL agent à la fois**

```csharp
// Structure de la tâche
public class TaskConfiguration
{
    public string? AssignedAgentId { get; set; }  // UN SEUL agent, pas une liste
}

// Tentative de réassignation bloquée
task.AssignTo(agent1);  // ✅ OK
task.AssignTo(agent2);  // ❌ Exception: "Task is already assigned"
```

### Mécanismes d'Affectation selon le ProcessType

#### **Sequential** (Exécution linéaire)

```
Agent1 → Task1 → Complete
                     ↓
Agent2 → Task2 → Complete
                     ↓
Agent3 → Task3 → Complete
```

- Tâches pré-assignées dans la configuration
- Chaque agent attend la fin de la tâche précédente
- Ordre fixe et prévisible

#### **Hierarchical** (Manager coordonne)

```
        [Manager Agent]
             │
    ┌────────┼────────┐
    │        │        │
    ▼        ▼        ▼
[Agent1] [Agent2] [Agent3]
   │        │        │
  Task1   Task2   Task3
```

- Le **ManagerAgent** analyse les compétences de chaque agent
- Décide dynamiquement qui fait quoi basé sur :
  - Les capacités de l'agent (role, tools, expertise)
  - La charge de travail actuelle
  - L'historique de performance
- Peut réaffecter si un agent échoue

```csharp
// Le manager décide
var assignment = await managerAgent.EvaluateAndAssign(
    task: "Créer API REST",
    availableAgents: [backendDev, frontendDev, fullstackDev]
);
// Résultat: Assigne à backendDev car plus adapté
```

#### **Parallel** (Exécution simultanée)

```
Agent1 → Task1 ┐
Agent2 → Task2 ├── Exécution simultanée
Agent3 → Task3 ┘
```

- Tâches pré-assignées mais indépendantes
- Tous les agents travaillent en même temps
- Optimal pour tâches sans dépendances

#### **Consensual** (Décision collective)

```
   [Proposition]
        │
    ┌───▼───┐
    │ VOTE  │
    └───┬───┘
        │
Agent1: ✅ (pour)
Agent2: ✅ (pour)  → Consensus atteint
Agent3: ❌ (contre)
```

- Les agents votent pour décider qui prend quelle tâche
- Chaque agent propose ses préférences
- Affectation basée sur le consensus majoritaire

### Délégation et Collaboration entre Agents

#### Concept de Délégation

Bien qu'une tâche ne puisse être assignée qu'à un seul agent, les agents peuvent **collaborer** via la délégation :

```csharp
// Agent "Analyste" a une tâche complexe
public class DataAnalystAgent : Agent
{
    public async Task<TaskOutput> ExecuteTaskAsync(Task task)
    {
        // L'analyste fait sa partie
        var analysis = await AnalyzeData();

        // Puis délègue la visualisation à un spécialiste
        var visualization = await DelegateWorkTool.ExecuteAsync(new
        {
            task = "Créer graphiques interactifs pour ces données",
            context = analysis.ToJson(),
            coworkerRole = "Visualization Expert"
        });

        // Combine les résultats
        return CombineResults(analysis, visualization);
    }
}
```

#### Scénarios de Délégation

##### 1. **Délégation par Compétence**

```
[Frontend Dev] --délègue--> [UI Designer]
      │                           │
"Intégrer API"             "Créer maquettes"
```

##### 2. **Délégation par Charge**

```
[Senior Dev] --surchargé--> [Junior Dev]
      │                          │
"5 tâches"              "Prend 2 tâches simples"
```

##### 3. **Délégation Hiérarchique**

```
[Manager] --> [Team Lead] --> [Developer]
    │              │              │
"Stratégie"   "Planning"    "Implémentation"
```

#### Outils de Collaboration

```csharp
// 1. DelegateWorkTool - Déléguer une sous-tâche
await DelegateWorkTool.ExecuteAsync(new {
    task = "Implémenter auth OAuth",
    coworkerRole = "Security Expert"
});

// 2. AskQuestionTool - Demander de l'aide
var answer = await AskQuestionTool.ExecuteAsync(new {
    question = "Quelle est la meilleure approche pour ce problème?",
    coworkerRole = "Senior Architect"
});

// 3. Communication directe via IAgentCommunicationService
await _communicationService.SendMessageAsync(
    from: currentAgent,
    to: targetAgent,
    message: "Besoin de ton expertise sur ce point"
);
```

### Cycle de Vie d'une Tâche

```
   [Created]
       │
       ▼
   [Pending]     ← Tâche créée, en attente d'affectation
       │
       ▼
   [Assigned]    ← Assignée à UN agent spécifique
       │
       ▼
  [InProgress]   ← L'agent travaille dessus
       │
   ┌───┼───┐
   ▼       ▼
[Failed] [Completed]
   │
   ▼
[Retry/Reassign]
```

### Exemples Pratiques d'Affectation

#### Exemple 1 : Crew de Support Client

```csharp
var supportCrew = new CrewConfiguration
{
    Process = ProcessType.Hierarchical,
    ManagerAgentId = "support_manager"
};

// Le manager reçoit un ticket
var ticket = new TaskConfiguration
{
    Description = "Client ne peut pas se connecter",
    ExpectedOutput = "Problème résolu et client satisfait"
};

// Le manager analyse et décide
// - Si technique → assigne à "tech_support"
// - Si facturation → assigne à "billing_support"
// - Si simple → assigne à "junior_support"
```

#### Exemple 2 : Pipeline de Data Science

```csharp
var dataCrew = new CrewConfiguration
{
    Process = ProcessType.Sequential,  // Ordre important
    Tasks = [
        new() {
            Id = "collect",
            Description = "Collecter données depuis APIs",
            AssignedAgentId = "data_engineer"  // DOIT finir avant nettoyage
        },
        new() {
            Id = "clean",
            Description = "Nettoyer et normaliser données",
            AssignedAgentId = "data_engineer",
            Dependencies = ["collect"]  // Attend que collect soit fini
        },
        new() {
            Id = "analyze",
            Description = "Analyse statistique",
            AssignedAgentId = "data_scientist",
            Dependencies = ["clean"]
        },
        new() {
            Id = "visualize",
            Description = "Créer dashboard",
            AssignedAgentId = "viz_specialist",
            Dependencies = ["analyze"]
        }
    ]
};
```

## Building

```bash
# Build in Debug mode
dotnet build

# Build in Release mode
dotnet build -c Release
```

## Running

```bash
# Run from project directory
dotnet run

# Or execute the compiled binary
./bin/Debug/net9.0/orkeon
```

## Command structure (text-based REPL)

The numeric menu has been replaced by hierarchical text commands. Type `help` (or `?`, `h`) at any prompt to list available commands.

### Command mapping (migration from the previous numeric menu)

| Old menu | New command |
|----------|-------------|
| 1 → Manage Agents → 1. Create Agent | `agent create` |
| 1 → Manage Agents → 2. List Agents | `agent list` |
| 1 → Manage Agents → 3. Delete Agent | `agent delete` |
| 2 → Manage Crews → 1. Create Crew | `crew create` |
| 2 → Manage Crews → 2. List Crews | `crew list` |
| 2 → Manage Crews → 3. Add Agent to Crew | `crew add-agent` |
| 2 → Manage Crews → 4. Delete Crew | `crew delete` |
| 3 → Manage Tasks → 1. Create Task | `task create` |
| 3 → Manage Tasks → 2. List Tasks | `task list` |
| 3 → Manage Tasks → 3. Assign Task to Agent | `task assign` |
| 3 → Manage Tasks → 4. Delete Task | `task delete` |
| 4 → Execute Crew | `crew run` |
| 5 → Quick Demo | `demo` |
| 6 → Interactive Q&A | `qa` |
| 7 → Exit | `exit` (or `quit`, `q`) |

### Agent Management
- Create new agents with:
  - Name/ID
  - Role (e.g., "Research Specialist")
  - Goal (e.g., "Find accurate information")
  - Backstory (optional)
  - Tools (WebSearch, FileRead, etc.)
- List all agents
- Delete agents

### Crew Management
- Create crews with:
  - Name
  - Description
  - Process type (Sequential or Hierarchical)
- Add agents to crews
- List all crews with agent/task counts
- Delete crews

### Task Management
- Create tasks with:
  - Description
  - Context (optional)
  - Expected output
- List all tasks
- Assign tasks to agents
- Delete tasks

## Quick Demo

The Quick Demo option automatically creates:
1. Two sample agents:
   - **Researcher**: Research Specialist focused on finding information
   - **Writer**: Content Writer for creating engaging content
2. A **Content Creation Team** crew
3. A sample task: "Research and write about AI trends in 2025"

## Architecture

This console application demonstrates Clean Architecture principles:

- **Presentation Layer** (ConsoleApp): User interface and interaction
- **Application Layer**: Business logic and use cases (referenced)
- **Domain Layer**: Core entities and business rules (referenced)
- **Infrastructure Layer**: External services and data access (referenced)

## Exemples Concrets de Crews

### 1. Crew Gestionnaire de Mail

**Objectif** : Automatiser la gestion complète des emails entrants

#### Composition de l'équipe

```csharp
var emailCrew = new CrewConfiguration
{
    Name = "Email Management Team",
    Goal = "Gérer automatiquement les emails entrants avec réponses intelligentes",
    Process = ProcessType.Sequential,
    Memory = true,
    Planning = true
};

// Agent 1 : Surveillant d'emails
var emailMonitor = new AgentConfiguration
{
    Id = "email_monitor",
    Role = "Email Monitor",
    Goal = "Surveiller et récupérer les nouveaux emails",
    Backstory = "Expert en protocoles mail IMAP/POP3 et gestion de boîtes mail",
    Tools = ["EmailReader", "ImapConnector", "WebhookListener"],
    MaxIterations = 10
};

// Agent 2 : Classificateur d'emails
var emailClassifier = new AgentConfiguration
{
    Id = "email_classifier",
    Role = "Email Classifier",
    Goal = "Catégoriser les emails par priorité, sujet et sentiment",
    Backstory = "Spécialiste en NLP et classification de texte",
    Tools = ["TextAnalyzer", "SentimentAnalyzer", "CategoryMapper"],
    Verbose = true
};

// Agent 3 : Rédacteur de réponses
var responseWriter = new AgentConfiguration
{
    Id = "response_writer",
    Role = "Response Writer",
    Goal = "Générer des réponses pertinentes et personnalisées",
    Backstory = "Expert en communication écrite et relations client",
    Tools = ["TemplateEngine", "ContextAnalyzer", "PersonalizationEngine"],
    LlmConfig = new LlmConfig { Temperature = 0.7, MaxTokens = 500 }
};

// Agent 4 : Gestionnaire de fils
var threadManager = new AgentConfiguration
{
    Id = "thread_manager",
    Role = "Thread Manager",
    Goal = "Organiser les conversations en fils cohérents",
    Backstory = "Expert en gestion de conversations et historique",
    Tools = ["ThreadAnalyzer", "ConversationTracker", "HistoryManager"],
    AllowDelegation = true
};
```

#### Workflow

```
┌──────────────┐
│ Nouveaux     │
│ Emails       │
└──────┬───────┘
       │
       ▼
┌──────────────┐      ┌──────────────┐
│ Email        │─────▶│ Email        │
│ Monitor      │      │ Classifier   │
└──────────────┘      └──────┬───────┘
                             │
                    ┌────────▼────────┐
                    │  Priorité:      │
                    │  - Urgent       │
                    │  - Normal       │
                    │  - Spam         │
                    └────────┬────────┘
                             │
                  ┌──────────▼──────────┐
                  │  Response Writer    │
                  │  (si non-spam)      │
                  └──────────┬──────────┘
                             │
                  ┌──────────▼──────────┐
                  │  Thread Manager     │
                  │  (organisation)     │
                  └─────────────────────┘
```

#### Tâches associées

```csharp
var tasks = new List<TaskConfiguration>
{
    new() {
        Id = "fetch_emails",
        Description = "Récupérer les emails non lus des dernières 24h",
        ExpectedOutput = "Liste structurée des emails avec métadonnées",
        AssignedAgentId = "email_monitor"
    },
    new() {
        Id = "classify_emails",
        Description = "Classifier les emails par catégorie et priorité",
        ExpectedOutput = "Emails triés avec tags: urgent/normal/spam, catégorie",
        AssignedAgentId = "email_classifier",
        Dependencies = ["fetch_emails"]
    },
    new() {
        Id = "generate_responses",
        Description = "Créer des réponses appropriées pour emails non-spam",
        ExpectedOutput = "Brouillons de réponse personnalisés",
        AssignedAgentId = "response_writer",
        Dependencies = ["classify_emails"],
        HumanInput = true // Validation humaine avant envoi
    },
    new() {
        Id = "organize_threads",
        Description = "Organiser en fils de discussion cohérents",
        ExpectedOutput = "Structure de conversation mise à jour",
        AssignedAgentId = "thread_manager",
        Dependencies = ["generate_responses"]
    }
};
```

### 2. Crew Gestionnaire de Portefeuille d'Actions

**Objectif** : Gérer automatiquement un portefeuille d'investissement

#### Composition de l'équipe (Process Hiérarchique)

```csharp
var portfolioCrew = new CrewConfiguration
{
    Name = "Portfolio Management Team",
    Goal = "Optimiser le portefeuille par analyse continue et trading intelligent",
    Process = ProcessType.Hierarchical,
    ManagerAgentId = "portfolio_manager",
    Memory = true,
    ExecutionConfig = new ExecutionConfig
    {
        MaxRetries = 3,
        ParallelExecution = true
    }
};

// Agent Manager : Superviseur du portefeuille
var portfolioManager = new AgentConfiguration
{
    Id = "portfolio_manager",
    Role = "Portfolio Manager",
    Goal = "Superviser et coordonner toutes les décisions d'investissement",
    Backstory = "Gestionnaire senior avec 20 ans d'expérience en marchés financiers",
    Tools = ["PortfolioAnalyzer", "RiskCalculator", "PerformanceTracker"],
    AllowDelegation = true,
    SystemTemplate = "You are a senior portfolio manager overseeing a team..."
};

// Agent 1 : Scanner de marché
var marketScanner = new AgentConfiguration
{
    Id = "market_scanner",
    Role = "Market Scanner",
    Goal = "Identifier les opportunités d'investissement en temps réel",
    Backstory = "Analyste quantitatif spécialisé en détection de patterns",
    Tools = ["MarketDataAPI", "TechnicalIndicators", "NewsAggregator", "WebScraper"],
    MaxRPM = 60 // Limite de requêtes API
};

// Agent 2 : Analyste de marché
var marketAnalyst = new AgentConfiguration
{
    Id = "market_analyst",
    Role = "Market Analyst",
    Goal = "Analyser en profondeur les tendances et indicateurs",
    Backstory = "Expert en analyse fondamentale et technique",
    Tools = ["ChartAnalyzer", "FundamentalAnalysis", "MacroIndicators", "SectorAnalysis"],
    Verbose = true
};

// Agent 3 : Évaluateur de risques
var riskEvaluator = new AgentConfiguration
{
    Id = "risk_evaluator",
    Role = "Risk Evaluator",
    Goal = "Évaluer les risques de chaque stratégie d'investissement",
    Backstory = "Spécialiste en gestion des risques et modélisation",
    Tools = ["VaRCalculator", "MonteCarloSimulation", "StressTest", "CorrelationMatrix"],
    LlmConfig = new LlmConfig { Temperature = 0.2 } // Réponses conservatrices
};

// Agent 4 : Exécuteur de trades
var tradingExecutor = new AgentConfiguration
{
    Id = "trading_executor",
    Role = "Trading Executor",
    Goal = "Exécuter les ordres d'achat/vente de manière optimale",
    Backstory = "Trader algorithmique expert en exécution d'ordres",
    Tools = ["BrokerAPI", "OrderManager", "LiquidityAnalyzer", "SlippageCalculator"],
    MaxIterations = 5 // Limite pour éviter le sur-trading
};
```

#### Stratégies et Workflow

```
        ┌──────────────────┐
        │ Portfolio Manager│
        │   (Superviseur)  │
        └────────┬─────────┘
                 │
     ┌───────────┼───────────┐
     │           │           │
     ▼           ▼           ▼
┌──────────┐ ┌──────────┐ ┌──────────┐
│ Market   │ │ Market   │ │ Risk     │
│ Scanner  │ │ Analyst  │ │ Evaluator│
└────┬─────┘ └────┬─────┘ └────┬─────┘
     │           │           │
     └───────────┼───────────┘
                 │
                 ▼
        ┌──────────────┐
        │   Trading    │
        │   Executor   │
        └──────────────┘
```

#### Tâches et Stratégies

```csharp
var portfolioTasks = new List<TaskConfiguration>
{
    new() {
        Id = "scan_opportunities",
        Description = "Scanner le marché pour nouvelles opportunités (RSI<30, breakouts, etc.)",
        ExpectedOutput = "Liste de 10-20 actions avec signaux d'achat/vente",
        AssignedAgentId = "market_scanner",
        AsyncExecution = true,
        TimeoutSeconds = 120
    },
    new() {
        Id = "analyze_trends",
        Description = "Analyser les tendances macro et sectorielles",
        ExpectedOutput = "Rapport avec prévisions court/moyen/long terme",
        AssignedAgentId = "market_analyst",
        Context = { { "timeframes", "[1D, 1W, 1M]" }, { "sectors", "[tech, finance, energy]" } }
    },
    new() {
        Id = "evaluate_risks",
        Description = "Calculer le risque pour chaque opportunité identifiée",
        ExpectedOutput = "Matrice de risque avec VaR, Sharpe ratio, drawdown max",
        AssignedAgentId = "risk_evaluator",
        Dependencies = ["scan_opportunities", "analyze_trends"]
    },
    new() {
        Id = "portfolio_decision",
        Description = "Décider des ajustements du portefeuille",
        ExpectedOutput = "Plan d'action: achats, ventes, rééquilibrages",
        AssignedAgentId = "portfolio_manager",
        Dependencies = ["evaluate_risks"],
        HumanInput = true // Validation pour montants > 10000€
    },
    new() {
        Id = "execute_trades",
        Description = "Exécuter les ordres sur le marché",
        ExpectedOutput = "Confirmations d'exécution avec prix et volumes",
        AssignedAgentId = "trading_executor",
        Dependencies = ["portfolio_decision"]
    }
};
```

### 3. Crew Développement Informatique

**Objectif** : Automatiser le cycle complet de développement logiciel

#### Composition de l'équipe (Process Mixte: Séquentiel + Parallèle)

```csharp
var devCrew = new CrewConfiguration
{
    Name = "Software Development Team",
    Goal = "Transformer les besoins en solution logicielle testée et déployée",
    Process = ProcessType.Sequential, // Peut basculer en Parallel pour certaines phases
    Memory = true,
    Planning = true,
    Verbose = true
};

// Agent 1 : Analyste des besoins
var requirementsAnalyst = new AgentConfiguration
{
    Id = "requirements_analyst",
    Role = "Requirements Analyst",
    Goal = "Capturer et clarifier les besoins utilisateurs",
    Backstory = "Business analyst expert en méthodologies agiles",
    Tools = ["UserStoryGenerator", "UseCaseDiagrammer", "RequirementsValidator"],
    PromptTemplate = "Analyze the following user needs and create detailed requirements..."
};

// Agent 2 : Architecte fonctionnel
var functionalArchitect = new AgentConfiguration
{
    Id = "functional_architect",
    Role = "Functional Architect",
    Goal = "Créer les spécifications fonctionnelles détaillées",
    Backstory = "Architecte fonctionnel avec expertise en modélisation UML",
    Tools = ["UMLGenerator", "FlowchartCreator", "MockupDesigner", "SpecWriter"],
    MaxIterations = 15
};

// Agent 3 : Architecte technique
var technicalArchitect = new AgentConfiguration
{
    Id = "technical_architect",
    Role = "Technical Architect",
    Goal = "Définir l'architecture technique et les choix technologiques",
    Backstory = "Architecte senior expert en patterns et best practices",
    Tools = ["ArchitectureDiagrammer", "TechStackSelector", "DatabaseDesigner", "APISpecGenerator"],
    AllowDelegation = true
};

// Agent 4 : Développeur
var developer = new AgentConfiguration
{
    Id = "developer",
    Role = "Full Stack Developer",
    Goal = "Implémenter le code selon les spécifications",
    Backstory = "Développeur polyvalent maîtrisant multiples langages",
    Tools = ["CodeGenerator", "FileWriter", "GitManager", "DependencyManager"],
    LlmConfig = new LlmConfig { Temperature = 0.3, MaxTokens = 2000 }
};

// Agent 5 : Intégrateur
var integrationEngineer = new AgentConfiguration
{
    Id = "integration_engineer",
    Role = "DevOps Engineer",
    Goal = "Gérer l'intégration continue et le déploiement",
    Backstory = "Expert DevOps en CI/CD et automatisation",
    Tools = ["DockerManager", "KubernetesDeployer", "CICDPipeline", "ConfigManager"],
    MaxRPM = 30
};

// Agent 6 : Testeur QA
var qaTester = new AgentConfiguration
{
    Id = "qa_tester",
    Role = "QA Engineer",
    Goal = "Assurer la qualité par tests automatisés",
    Backstory = "Ingénieur QA spécialisé en automatisation de tests",
    Tools = ["TestRunner", "TestGenerator", "CoverageAnalyzer", "BugTracker"],
    ResponseTemplate = "Test results: {results}\nCoverage: {coverage}%\nIssues found: {issues}"
};
```

#### Workflow de Développement

```
┌─────────────┐
│   Besoins   │
│ Utilisateur │
└──────┬──────┘
       │
       ▼
┌─────────────────┐
│  Requirements   │
│    Analyst      │
└────────┬────────┘
         │
         ▼
┌─────────────────┐      ┌─────────────────┐
│   Functional    │─────▶│    Technical    │
│   Architect     │      │    Architect    │
└─────────────────┘      └────────┬────────┘
                                  │
                    ┌─────────────┴─────────────┐
                    │                           │
                    ▼                           ▼
            ┌─────────────┐            ┌─────────────┐
            │  Developer  │            │ QA Tester   │
            │  (Backend)  │            │ (Tests)     │
            └──────┬──────┘            └──────┬──────┘
                   │                           │
                   └─────────┬─────────────────┘
                             │
                             ▼
                    ┌─────────────────┐
                    │  Integration    │
                    │    Engineer     │
                    └─────────────────┘
```

#### Pipeline de Tâches

```csharp
var devTasks = new List<TaskConfiguration>
{
    // Phase 1: Analyse
    new() {
        Id = "gather_requirements",
        Description = "Analyser le besoin: 'Créer une API REST pour gestion de produits'",
        ExpectedOutput = "User stories détaillées avec critères d'acceptation",
        AssignedAgentId = "requirements_analyst",
        HumanInput = true // Validation avec le client
    },

    // Phase 2: Conception
    new() {
        Id = "functional_specs",
        Description = "Créer spécifications fonctionnelles avec cas d'usage",
        ExpectedOutput = "Document de specs avec diagrammes UML, maquettes",
        AssignedAgentId = "functional_architect",
        Dependencies = ["gather_requirements"]
    },
    new() {
        Id = "technical_specs",
        Description = "Définir architecture: microservices, base de données, APIs",
        ExpectedOutput = "Architecture diagram, choix techniques, schemas DB",
        AssignedAgentId = "technical_architect",
        Dependencies = ["functional_specs"]
    },

    // Phase 3: Implémentation (peut être parallélisée)
    new() {
        Id = "implement_backend",
        Description = "Développer API REST avec endpoints CRUD",
        ExpectedOutput = "Code source avec controllers, services, repositories",
        AssignedAgentId = "developer",
        Dependencies = ["technical_specs"],
        AsyncExecution = true // Peut s'exécuter en parallèle
    },
    new() {
        Id = "write_tests",
        Description = "Créer tests unitaires et d'intégration",
        ExpectedOutput = "Suite de tests avec couverture > 80%",
        AssignedAgentId = "qa_tester",
        Dependencies = ["technical_specs"],
        AsyncExecution = true // En parallèle avec le dev
    },

    // Phase 4: Intégration
    new() {
        Id = "setup_cicd",
        Description = "Configurer pipeline CI/CD avec Docker et Kubernetes",
        ExpectedOutput = "Pipeline fonctionnel avec stages: build, test, deploy",
        AssignedAgentId = "integration_engineer",
        Dependencies = ["implement_backend", "write_tests"]
    },

    // Phase 5: Validation
    new() {
        Id = "run_tests",
        Description = "Exécuter tous les tests et générer rapport qualité",
        ExpectedOutput = "Rapport de tests, couverture, métriques qualité",
        AssignedAgentId = "qa_tester",
        Dependencies = ["setup_cicd"],
        TimeoutSeconds = 600
    },
    new() {
        Id = "deploy_staging",
        Description = "Déployer en environnement de staging",
        ExpectedOutput = "Application déployée et accessible sur staging",
        AssignedAgentId = "integration_engineer",
        Dependencies = ["run_tests"]
    }
};
```

## Utilisation Pratique des Crews

### Configuration YAML

Les crews peuvent être configurées via YAML pour plus de flexibilité :

```yaml
# email-crew.yaml
name: Email Management Team
goal: Automated email handling with intelligent responses
process: sequential
memory: true
planning: true

agents:
  - id: email_monitor
    role: Email Monitor
    goal: Monitor and fetch new emails
    tools:
      - EmailReader
      - ImapConnector
    max_iterations: 10

tasks:
  - id: fetch_emails
    description: Fetch unread emails from last 24h
    expected_output: Structured email list
    assigned_agent: email_monitor
```

### Exécution d'une Crew

```csharp
// Charger et exécuter une crew
var crewService = serviceProvider.GetRequiredService<ICrewExecutionService>();
var crew = await crewService.LoadFromYamlAsync("email-crew.yaml");

// Configurer les inputs
var inputs = new Dictionary<string, object>
{
    { "email_account", "support@company.com" },
    { "time_range", "24h" }
};

// Exécuter avec monitoring
var result = await crewService.ExecuteAsync(crew, inputs, progress =>
{
    Console.WriteLine($"[{progress.CurrentTask}] {progress.Status}: {progress.Message}");
});

Console.WriteLine($"Crew execution completed: {result.Success}");
foreach (var taskResult in result.TaskResults)
{
    Console.WriteLine($"- {taskResult.TaskId}: {taskResult.Output}");
}
```

## Configuration

The application uses configuration classes from the Domain layer:
- `AgentConfiguration`: Defines agent properties
- `CrewConfiguration`: Defines crew structure
- `TaskConfiguration`: Defines task details

### Process Types Disponibles

| Type | Description | Cas d'usage |
|------|-------------|-------------|
| **Sequential** | Tâches exécutées une après l'autre | Workflows linéaires, pipelines |
| **Hierarchical** | Manager coordonne les agents | Équipes complexes, décisions centralisées |
| **Consensual** | Agents votent sur les décisions | Décisions critiques, validation collective |
| **Parallel** | Tâches simultanées quand possible | Performance, tâches indépendantes |

## Notes

- This is a demonstration application showing basic CRUD operations
- Crew execution is simulated (no actual LLM calls in demo mode)
- In production, integrate with real LLM providers (OpenAI, Ollama)
- Memory providers can be configured (Redis, SQLite, InMemory)

## Future Enhancements

- Integration with actual LLM providers
- Persistent storage of agents, crews, and tasks
- YAML configuration file support
- Export/import functionality
- Real task execution with tool integration