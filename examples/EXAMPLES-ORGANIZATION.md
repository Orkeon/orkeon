# Orkeon Examples — Organisation des Sous-Dossiers

> Plan de restructuration du dossier `examples/` base sur les 101 cas d'usage et l'etat actuel du repository.
> Ce document est concu pour etre **directement actionnable par Claude Code** : chaque section contient les informations exactes (noms de classes, champs YAML, methodes builder) issues du codebase.

---

## 1. Etat Actuel

Le dossier `examples/` contient actuellement 5 exemples a plat, sans categorisation :

| Dossier | Correspond au cas d'usage | Statut |
|---------|--------------------------|--------|
| `classictrading/` | #31 Trading Algorithmique Multi-Strategies | Projet complet (solution multi-projets, inclut `.vs/` et `bin/`) |
| `code-review/` | #2 Revue de Code Automatisee | Projet minimal (Program.cs + csproj) |
| `email-management/` | #3 Pipeline de Traitement d'Emails | Projet minimal |
| `research-assistant/` | #1 Assistant de Recherche Multi-Sources | Projet minimal |
| `streaming-demo/` | Demo streaming (transverse) | Projet minimal |
| `tasks/` | Taches de migration (non-exemple) | A deplacer hors de `examples/` |

**Problemes identifies** : pas de categorisation, nommage incoherent, 5 exemples sur 101, pas de convention pour les fichiers YAML/Builder, le dossier `tasks/` n'est pas un exemple, des artefacts de build commites (`.vs/`, `bin/`, `obj/`).

---

## 2. Categories Detectees (9 parties + 1 transverse)

Extraites directement du document `project/marketing/content-strategy/101-USE-CASES.md` :

| # | Categorie | Cas d'usage | Slug dossier |
|---|-----------|-------------|--------------|
| 1 | Classiques Entreprise | 1-15 | `01-enterprise` |
| 2 | Sciences & Recherche | 16-30 | `02-science-research` |
| 3 | Finance & Trading | 31-45 | `03-finance-trading` |
| 4 | Sante & Bien-etre | 46-55 | `04-health-wellness` |
| 5 | Education & Formation | 56-65 | `05-education` |
| 6 | Ingenierie & DevOps | 66-75 | `06-engineering-devops` |
| 7 | Creativite & Media | 76-85 | `07-creative-media` |
| 8 | IoT, Monde Physique & Smart Systems | 86-95 | `08-iot-smart-systems` |
| 9 | Avant-Garde & Experimental | 96-101 | `09-experimental` |
| — | Ressources transverses | — | `_shared/` |

---

## 3. Architecture Solution — Runners Executables

### 3.1 Principe

Chaque exemple est **un dossier de donnees** (`config.yaml` + `appsettings.json`), pas un projet C#.
Un **runner** est un exe .NET qui charge le YAML, instancie les outils via DI, cree la crew
et l'execute. Plusieurs runners coexistent selon les outils necessaires.

```
config.yaml + appsettings.json  →  Runner exe  →  Crew  →  Execution
```

### 3.2 Runners Necessaires

L'analyse des dependances d'outils donne 2 runners distincts (extensible) :

| Runner | Projet | Outils enregistres | Exemples couverts |
|--------|--------|--------------------|-------------------|
| **Standard** | `Orkeon.Examples.Runner` | 15 outils built-in (FileSystem 4 + Data 7 + Web 3 + Code 1) + 2 optionnels (web_search, brave_search) | ~90 cas (1-30, 46-101 sauf trading) |
| **Trading** | `Orkeon.Examples.Trading.Runner` | 15 standard + 2 optionnels + 44 outils trading custom (MathNet, YahooFinance) | Cas 31-45 (Finance & Trading) |

> **Extensibilite** : si un nouveau domaine (ex: medical, IoT) necessite des outils custom,
> on cree un nouveau runner sans toucher aux existants.

### 3.3 Pourquoi cette separation

| Critere | Un seul exe | Plusieurs runners |
|---------|-------------|-------------------|
| Dependances NuGet | Tous les NuGet charges (MathNet, YahooFinance, etc.) meme pour un hello-world | Chaque runner ne tire que ce dont il a besoin |
| Temps de build | Long (44 outils trading a compiler pour tester un email-pipeline) | Rapide (runner standard compile en secondes) |
| `IToolRegistry` | Doit resoudre 50+ outils | Chaque runner resout uniquement ses outils |
| Ajout de domaine | Modifie l'exe commun (risque de regression) | Nouveau projet isole |

### 3.4 Fonctionnement d'un Runner

Chaque runner est un projet console qui :

1. Lit le chemin du `config.yaml` en argument CLI (`--config path/to/config.yaml`)
2. Charge `appsettings.json` depuis le meme dossier (ou un path en argument `--settings`)
3. Enregistre les outils de son domaine dans le conteneur DI
4. Implemente `IToolRegistry` pour resoudre les noms d'outils YAML → instances
5. Appelle `ICrewFactory.CreateFromFileAsync()` puis `ICrewOrchestrationService.KickoffAsync()`

**Commande utilisateur** :
```bash
# Depuis la racine du repo
dotnet run --project examples/runners/standard -- --config examples/01-enterprise/01-research-assistant/config.yaml

# Ou avec un alias/script
./examples/run-example.sh 01-enterprise/01-research-assistant

# Pour le trading
dotnet run --project examples/runners/trading -- --config examples/03-finance-trading/31-algo-trading/config.yaml
```

### 3.5 DI de Chaque Runner

#### Runner Standard (`Orkeon.Examples.Runner`)

```csharp
// Program.cs — Runner Standard
services.AddOrkeonApplication();
services.AddOrkeonInfrastructure();

// 15 outils built-in (enregistres comme IBaseTool dans le DI)
services.AddOrkeonFileSystemTools();   // file_read, file_write, directory_read, email_parser (4)
services.AddOrkeonDataTools();         // json_tool, csv_reader, pdf_reader, xml_parser, database_query, docx_reader, docx_writer (7)
services.AddOrkeonWebTools();          // http_api, web_scrape, github (3)
services.AddOrkeonCodeTools();         // shell_command (1)

// Optionnel : search tools conditionnes par cles API (+2 max)
if (config["Search:TavilyApiKey"] is string tavilyKey)
    services.AddOrkeonWebSearchTool(tavilyKey);       // web_search
if (config["Search:BraveApiKey"] is string braveKey)
    services.AddOrkeonBraveSearchTool(braveKey);      // brave_search

// Resolution outils YAML → instances
services.AddSingleton<IToolRegistry, ServiceProviderToolRegistry>();
```

#### Runner Trading (`Orkeon.Examples.Trading.Runner`)

```csharp
// Program.cs — Runner Trading (herite du standard + ajoute 44 outils custom)
services.AddOrkeonApplication();
services.AddOrkeonInfrastructure();
services.AddOrkeonFileSystemTools();   // 4
services.AddOrkeonDataTools();         // 7
services.AddOrkeonWebTools();          // 3
services.AddOrkeonCodeTools();         // 1

// 44 outils trading custom
services.AddTradingTools();            // CorrelationAnalysis, MarketRegime, TWAP, VWAP, etc.

// Resolution outils
services.AddSingleton<IToolRegistry, ServiceProviderToolRegistry>();
```

### 3.6 `ServiceProviderToolRegistry` — Implementation Commune

L'implementation de `IToolRegistry` collecte tous les `IBaseTool` enregistres dans le DI :

```csharp
/// <summary>
/// Resout les noms d'outils YAML vers les instances enregistrees en DI.
/// Utilise par CrewFactory pour creer les agents avec leurs outils.
/// Implemente l'interface complete IToolRegistry (10 methodes).
/// </summary>
public class ServiceProviderToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, IBaseTool> _tools;

    public ServiceProviderToolRegistry(IEnumerable<IBaseTool> tools)
    {
        _tools = tools.ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);
    }

    public Task<bool> RegisterToolAsync(IBaseTool tool)
    {
        _tools[tool.Name] = tool;
        return Task.FromResult(true);
    }

    public Task<bool> UnregisterToolAsync(string toolId)
        => Task.FromResult(_tools.Remove(toolId));

    public Task<IBaseTool?> GetToolAsync(string toolId)
        => Task.FromResult(_tools.GetValueOrDefault(toolId));

    public Task<IBaseTool?> GetToolByNameAsync(string name)
        => Task.FromResult(_tools.GetValueOrDefault(name));

    public Task<IReadOnlyList<IBaseTool>> GetAllToolsAsync()
        => Task.FromResult<IReadOnlyList<IBaseTool>>(_tools.Values.ToList());

    // IBaseTool n'expose pas de propriete Tags — retourne une liste vide.
    // TODO: si un futur IBaseTool ajoute Tags, filtrer ici.
    public Task<IReadOnlyList<IBaseTool>> GetToolsByTagsAsync(params string[] tags)
        => Task.FromResult<IReadOnlyList<IBaseTool>>(Array.Empty<IBaseTool>());

    public Task<bool> IsRegisteredAsync(string toolId)
        => Task.FromResult(_tools.ContainsKey(toolId));

    // IBaseTool n'expose pas de propriete Capabilities — retourne une liste vide.
    // TODO: si un futur IBaseTool ajoute Capabilities, filtrer ici.
    public Task<IReadOnlyList<IBaseTool>> GetToolsByCapabilityAsync(string capability)
        => Task.FromResult<IReadOnlyList<IBaseTool>>(Array.Empty<IBaseTool>());

    public Task<IReadOnlyList<IBaseTool>> GetToolsAsync(IEnumerable<ITool> tools)
    {
        var names = tools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Task.FromResult<IReadOnlyList<IBaseTool>>(
            _tools.Values.Where(t => names.Contains(t.Name)).ToList());
    }

    public Task ClearAsync()
    {
        _tools.Clear();
        return Task.CompletedTask;
    }
}
```

> **Note** : cette classe est a creer dans `examples/runners/_shared/` et referencee par tous les runners.
> Le pattern est identique au `TradingToolFactory` existant dans `classictrading/`.

### 3.7 Solution File

```
examples/
├── Orkeon.Examples.sln                  ← Solution des exemples
├── runners/
│   ├── _shared/
│   │   ├── Orkeon.Examples.Shared.csproj  ← ServiceProviderToolRegistry + CLI parsing + helpers
│   │   └── ServiceProviderToolRegistry.cs
│   ├── standard/
│   │   ├── Orkeon.Examples.Runner.csproj  ← Runner standard (15 outils + 2 optionnels)
│   │   └── Program.cs
│   └── trading/
│       ├── Orkeon.Examples.Trading.Runner.csproj  ← Runner trading (15+2 + 44 outils custom)
│       └── Program.cs
├── run-example.sh                       ← Script helper (voir 3.4)
├── run-example.ps1                      ← Idem PowerShell
```

---

## 4. Structure Cible des Exemples

> Chaque exemple est maintenant **un dossier de configuration** (pas un projet .csproj).
> Le runner correspondant est determine par la categorie.

```
examples/
├── Orkeon.Examples.sln                  ← Solution regroupant les runners
├── README.md                           ← Index general avec liens vers chaque categorie
├── .gitignore                          ← Exclut .vs/, bin/, obj/, logs/, *.user
├── run-example.sh                      ← Script : ./run-example.sh 01-enterprise/01-research-assistant
├── run-example.ps1                     ← Idem PowerShell
│
├── runners/                            ← Projets executables
│   ├── _shared/                        ← Code commun (ServiceProviderToolRegistry, CLI parsing)
│   ├── standard/                       ← Runner standard (15 outils built-in + 2 optionnels)
│   └── trading/                        ← Runner trading (15+2 standard + 44 outils custom)
│
├── 01-enterprise/
│   ├── README.md                       ← Description categorie + liste des exemples
│   ├── 01-research-assistant/          ← #1 Assistant de Recherche Multi-Sources
│   │   ├── config.yaml                 ← Configuration YAML declarative
│   │   ├── appsettings.json            ← Cles API / provider LLM (template)
│   │   └── README.md                   ← Description + commande d'execution exacte
│   ├── 02-code-review/                 ← #2 Revue de Code Automatisee
│   │   ├── config.yaml
│   │   ├── appsettings.json
│   │   └── README.md
│   ├── 03-email-pipeline/              ← #3 Pipeline de Traitement d'Emails
│   ├── 04-financial-reports/           ← #4 Generation de Rapports Financiers
│   ├── ...                             ← (meme structure : config.yaml + appsettings.json + README.md)
│   └── 15-commercial-proposals/        ← #15 Redaction de Propositions Commerciales
│
├── 02-science-research/
│   ├── README.md
│   ├── 16-prisma-meta-analysis/        ← #16 Meta-Analyse Scientifique PRISMA
│   ├── 17-scientific-debate/           ← #17 Simulation de Debat Scientifique
│   ├── 18-academic-writing/            ← #18 Assistant de Redaction Academique
│   ├── 19-experimental-data/           ← #19 Analyse de Donnees Experimentales
│   ├── 20-patent-monitoring/           ← #20 Veille Brevets Deduplication Semantique
│   ├── 21-peer-review-calibration/     ← #21 Peer Review Simule avec Calibration
│   ├── 22-clinical-trials-encrypted/   ← #22 Monitoring d'Essais Cliniques Chiffre
│   ├── 23-genomic-analysis/            ← #23 Analyse Genomique avec Chunking
│   ├── 24-grant-writing/               ← #24 Redaction de Demandes de Subventions
│   ├── 25-lab-assistant-resume/        ← #25 Assistant de Laboratoire avec Reprise
│   ├── 26-knowledge-graph/             ← #26 Construction de Graphe de Connaissances
│   ├── 27-trend-prediction/            ← #27 Prediction de Tendances Auto-Calibration
│   ├── 28-triple-validation/           ← #28 Validation Croisee Triple Analyse
│   ├── 29-hypothesis-generation/       ← #29 Generation et Test d'Hypotheses Cyclique
│   └── 30-adaptive-summary/            ← #30 Resume Adaptatif au Profil Lecteur
│
├── 03-finance-trading/
│   ├── README.md
│   ├── 31-algo-trading/                ← #31 Trading Algorithmique Multi-Strategies (= classictrading actuel)
│   ├── 32-fraud-detection/             ← #32 Detection de Fraude en Temps Reel
│   ├── 33-credit-scoring/              ← #33 Scoring de Credit Validation Humaine
│   ├── 34-portfolio-consensus/         ← #34 Optimisation Portefeuille par Consensus
│   ├── 35-accounting-reconciliation/   ← #35 Reconciliation Comptable Multi-Sources
│   ├── 36-cash-flow-forecast/          ← #36 Prevision de Tresorerie Auto-Corrective
│   ├── 37-kyc-aml-compliance/          ← #37 Conformite KYC/AML Triple Securite
│   ├── 38-contract-analysis/           ← #38 Analyse de Contrats avec Chunking
│   ├── 39-robo-advisor/                ← #39 Robo-Advisor Profilage Interactif
│   ├── 40-invoice-processing/          ← #40 Traitement Automatise de Factures
│   ├── 41-insider-trading-detection/   ← #41 Detection de Delit d'Initie
│   ├── 42-dynamic-pricing/             ← #42 Pricing Dynamique Arbitrage Hierarchique
│   ├── 43-tax-optimization/            ← #43 Optimisation Fiscale Multi-Juridictions
│   ├── 44-esg-scoring/                 ← #44 Analyse ESG Scoring Reproductible
│   └── 45-stress-testing/              ← #45 Stress Testing Reglementaire Rejouable
│
├── 04-health-wellness/
│   ├── README.md
│   ├── 46-diagnostic-assistant/        ← #46 Aide au Diagnostic — Humain Systematique
│   ├── 47-nutrition-planner/           ← #47 Planification Nutritionnelle YAML-Driven
│   ├── 48-mental-health-monitoring/    ← #48 Monitoring Bien-etre Mental
│   ├── 49-medical-records-fhir/        ← #49 Structuration Dossiers Medicaux FHIR
│   ├── 50-radiology-assistant/         ← #50 Imagerie Medicale — Radiologue Dernier Mot
│   ├── 51-care-coordination/           ← #51 Coordination de Soins Broadcast
│   ├── 52-pharmacovigilance/           ← #52 Pharmacovigilance Deduplication Semantique
│   ├── 53-clinical-trials-nist/        ← #53 Essais Cliniques Checkpoint + Audit NIST
│   ├── 54-sports-coach/                ← #54 Coach Sportif Adaptatif par Episodique
│   └── 55-telemedicine-streaming/      ← #55 Telemedecine Augmentee Streaming
│
├── 05-education/
│   ├── README.md
│   ├── 56-adaptive-tutor/              ← #56 Tuteur Adaptatif Modele Apprenant
│   ├── 57-exam-generation/             ← #57 Generation d'Examens Calibres
│   ├── 58-multi-criteria-grading/      ← #58 Correction Multi-Criteres Reproductible
│   ├── 59-nonlinear-learning-path/     ← #59 Parcours de Formation Non-Lineaire
│   ├── 60-case-study-simulation/       ← #60 Simulation de Cas Pratiques Immersive
│   ├── 61-gamified-learning/           ← #61 Plateforme Learning Gamifie avec Hooks
│   ├── 62-plagiarism-detection/        ← #62 Detection de Plagiat Multi-Couches
│   ├── 63-skills-gap-mapping/          ← #63 Cartographie Lacunes Competences
│   ├── 64-accessibility/               ← #64 Accessibilite Universelle des Contenus
│   └── 65-developer-mentoring/         ← #65 Mentorat IA pour Developpeurs
│
├── 06-engineering-devops/
│   ├── README.md
│   ├── 66-cicd-pipeline/               ← #66 Pipeline CI/CD avec Rollback
│   ├── 67-incident-response/           ← #67 Incident Response Humain Valide
│   ├── 68-database-migration/          ← #68 Migration de Base de Donnees
│   ├── 69-performance-analysis/        ← #69 Analyse de Performance 4 Couches
│   ├── 70-versioned-documentation/     ← #70 Documentation Technique Versionnee
│   ├── 71-cloud-audit-nist/            ← #71 Audit Cloud Multi-Piliers NIST
│   ├── 72-load-testing/                ← #72 Test de Charge Trending Historique
│   ├── 73-secure-refactoring/          ← #73 Refactoring Securise Sandbox
│   ├── 74-dependency-management/       ← #74 Gestion des Dependances CVE
│   └── 75-chaos-engineering/           ← #75 Chaos Engineering Humain Approuve
│
├── 07-creative-media/
│   ├── README.md
│   ├── 76-narrative-studio/            ← #76 Studio Narratif FlowEngine Cyclique
│   ├── 77-podcast-production/          ← #77 Production de Podcast Pipeline Type
│   ├── 78-synthetic-data/              ← #78 Donnees Synthetiques Privacy-Safe
│   ├── 79-music-composition/           ← #79 Composition Musicale par Consensus
│   ├── 80-art-direction/               ← #80 Direction Artistique Delegation Report
│   ├── 81-worldbuilding/               ← #81 Worldbuilding Coherent Consensus Croise
│   ├── 82-newsletter-curation/         ← #82 Curation de Newsletter Scraping
│   ├── 83-interactive-fiction/          ← #83 Fiction Interactive FlowEngine
│   ├── 84-multi-perspective-critique/  ← #84 Critique Multi-Perspectives
│   └── 85-cross-media-adaptation/      ← #85 Adaptation Cross-Media Pipeline Type
│
├── 08-iot-smart-systems/
│   ├── README.md
│   ├── 86-smart-home-a2a/              ← #86 Maison Intelligente A2A Natif
│   ├── 87-fleet-management/            ← #87 Gestion de Flotte Checkpoint Missions
│   ├── 88-precision-agriculture/       ← #88 Agriculture de Precision Humain Valide
│   ├── 89-environmental-monitoring/    ← #89 Monitoring Environnemental ObserverAgent
│   ├── 90-energy-management/           ← #90 Gestion Energetique Cycle FlowEngine
│   ├── 91-predictive-maintenance/      ← #91 Maintenance Predictive ObserverAgent
│   ├── 92-warehouse-logistics/         ← #92 Logistique Warehouse Batch Performance
│   ├── 93-visual-quality-control/      ← #93 Controle Qualite Visuel Auto-Calibration
│   ├── 94-smart-city-traffic/          ← #94 Smart City Trafic A2A Inter-Zones
│   └── 95-crisis-management/           ← #95 Gestion de Crise Broadcast + Priorite
│
├── 09-experimental/
│   ├── README.md
│   ├── 96-self-adaptive-crew/          ← #96 Crew Evolutive Auto-Adaptative
│   ├── 97-multi-party-negotiation/     ← #97 Negociation Multi-Parties Budget
│   ├── 98-legacy-code-archaeology/     ← #98 Archeologie Numerique Codebase Legacy
│   ├── 99-ethics-jury/                 ← #99 Jury Ethique Multi-Perspectives
│   ├── 100-civilization-simulator/     ← #100 Simulateur de Civilisation Emergente
│   └── 101-crew-of-crews/              ← #101 Crew de Crews — L'Orchestre des Orchestres
│
└── _shared/                             ← Ressources transverses
    ├── README.md
    ├── tools/                           ← Outils reutilisables cross-categories
    ├── templates/                       ← Templates YAML/CS communs
    └── streaming-demo/                  ← Demo streaming (transverse, ex-racine)
```

---

## 4. Convention de Nommage

### Dossier categorie
```
{NN}-{category-slug}/
```
Exemple : `01-enterprise/`, `03-finance-trading/`

### Dossier exemple
```
{NN}-{use-case-slug}/
```
Exemple : `01-research-assistant/`, `31-algo-trading/`

Le numero correspond exactement au numero du cas d'usage dans `101-USE-CASES.md`, ce qui garantit la tracabilite.

### Fichiers par exemple (dossier de donnees — pas de .csproj)

| Fichier | Description | Obligatoire |
|---------|-------------|-------------|
| `config.yaml` | Configuration YAML declarative (conforme au schema `CrewConfiguration`) | Oui |
| `appsettings.json` | Configuration runtime : provider LLM, cles API (template avec placeholders) | Oui |
| `README.md` | Description + architecture + commande d'execution exacte + features cles | Oui |

> **Note** : les exemples n'ont plus de `Program.cs`, `Example.cs` ni `.csproj`.
> Le code executable est dans les **runners** (section 3). Les exemples sont de purs dossiers de configuration.

### Mapping categorie → runner

| Categories | Runner |
|------------|--------|
| 01-enterprise, 02-science-research, 04-health-wellness, 05-education, 06-engineering-devops, 07-creative-media, 08-iot-smart-systems, 09-experimental | `Orkeon.Examples.Runner` (standard) |
| 03-finance-trading | `Orkeon.Examples.Trading.Runner` (trading) |

---

## 5. Reference Codebase — API Disponible

> **IMPORTANT** : cette section est le contrat entre le document `101-USE-CASES.md` et le code reel.
> Claude Code doit utiliser **uniquement** les classes, methodes et champs listes ci-dessous.
> Ne jamais inventer de methodes ou de champs YAML qui n'existent pas dans le codebase.

### 5.1 ProcessType (enum)

Source : `src/core/Orkeon.Domain/Shared/ValueObjects/ProcessType.cs`

```csharp
public enum ProcessType
{
    Sequential,     // Taches executees l'une apres l'autre
    Hierarchical,   // Taches gerees par un ManagerAgent
    Consensual,     // Agents atteignent un consensus avant de continuer
    Parallel        // Taches executees en parallele
}
```

> Note : `FlowEngine` et `A2A` ne sont pas des `ProcessType` mais des mecanismes complementaires
> geres par `IFlowEngine` / `FlowDefinitionBuilder` et le protocole A2A respectivement.

### 5.2 AgentType (enum — Application Layer)

Source : `src/core/Orkeon.Application/Configuration/ImmutableConfigurations.cs`

> **Attention** : cet enum est dans la couche **Application**, pas Domain.
> Il sert aux configurations programmatiques (`ImmutableConfigurations`).

```csharp
public enum AgentType { Worker, Manager, Observer, Human }
```

### 5.3 TaskPriority (enum)

Source : `src/core/Orkeon.Domain/Task/ValueObjects/TaskPriority.cs`

```csharp
public enum TaskPriority
{
    Low = 1,       // Basse priorite — executee quand des ressources sont disponibles
    Normal = 2,    // Priorite par defaut (defaut du CrewTaskBuilder)
    High = 3,      // Priorite elevee
    Critical = 4,  // Priorite critique — execution immediate
    Urgent = 5     // Urgence maximale — execution d'urgence
}
```

> **Note** : le defaut du `CrewTaskBuilder` est `TaskPriority.Normal` (pas `Medium`).
> Il existe aussi un enum `TaskPriority` dans Application (`TaskPatternMatching.cs`) avec
> des valeurs differentes (`Low, Medium, High, Critical`) — ne pas confondre avec le Domain.

### 5.4 VerbosityLevel (enum — Application Layer)

Source : `src/core/Orkeon.Application/Configuration/ImmutableConfigurations.cs`

> **Attention** : cet enum est dans la couche **Application**, pas Domain.
> Le champ YAML `verbose` est un simple `bool`, pas un `VerbosityLevel`.

```csharp
public enum VerbosityLevel { Silent, Normal, Verbose, Debug }
```

### 5.5 Fluent Builders (Domain Layer)

#### AgentBuilder

Source : `src/core/Orkeon.Domain/Agent/AgentBuilder.cs`

```csharp
new AgentBuilder()
    .Role("...")                          // string (ou AgentRole) — requis
    .Goal("...")                          // string (ou AgentGoal) — requis
    .Backstory("...")                     // string (ou AgentBackstory) — recommande
    .WithTool(tool)                       // ITool
    .WithTools(tool1, tool2)              // params ITool[]
    .WithTools(toolList)                  // IEnumerable<ITool>
    .AllowDelegation(true)                // bool, defaut false en builder / true en YAML
    .MaxIterations(10)                    // int
    .MaxRpm(30)                           // int
    .Verbose()                            // bool, defaut true
    .MaxExecutionTime(TimeSpan)           // TimeSpan
    .CacheEnabled()                       // bool, defaut true
    .SystemTemplate("...")                // string
    .PromptTemplate("...")                // string
    .ResponseTemplate("...")              // string
    .MaxRetryLimit(3)                     // int
    .WithLlm(llmProvider)                 // ILlmProvider
    .WithStepCallback(callback)           // IStepCallback
    .Build();                             // → Agent
```

#### CrewBuilder

Source : `src/core/Orkeon.Domain/Crew/CrewBuilder.cs`

```csharp
new CrewBuilder()
    .Goal("...")                          // string — requis
    .Sequential()                         // raccourci ProcessType
    .Hierarchical(managerAgent?)          // optionnel : Agent manager
    .Hierarchical(managerAgentId)         // overload avec AgentId
    .Parallel()                           // raccourci ProcessType
    .Consensual()                         // raccourci ProcessType
    .Process(ProcessType.Sequential)      // explicite
    .WithAgent(agent)                     // Agent
    .WithAgent(a => a.Role("...").Goal("..."))  // Action<AgentBuilder>
    .WithAgents(agents)                   // IEnumerable<Agent>
    .WithTask(task)                       // CrewTask
    .WithTask(t => t.Description("..."))  // Action<CrewTaskBuilder>
    .WithTasks(tasks)                     // IEnumerable<CrewTask>
    .Verbose()                            // bool
    .Planning()                           // bool
    .WithPlanningLlm(llm)                // ILlmProvider
    .MaxRpm(30)                           // int
    .Language("fr")                       // string
    .FullOutput()                         // bool
    .EnableMemory()                       // bool
    .ShareCrew()                          // bool
    .WithManager(agent)                   // Agent
    .WithManagerId(agentId)               // AgentId
    .WithManagerLlm(llm)                  // ILlmProvider
    .WithStepCallback(callback)           // IStepCallback
    .WithTaskCallback(callback)           // ITaskCallback
    .OutputLogFile("path")                // string
    .Build();                             // → Crew
```

#### CrewTaskBuilder

Source : `src/core/Orkeon.Domain/Task/CrewTaskBuilder.cs`

```csharp
new CrewTaskBuilder()
    .Description("...")                   // string — requis
    .ExpectedOutput("...")                // string — requis
    .Priority(TaskPriority.High)          // TaskPriority
    .DependsOn(previousTask)              // CrewTask
    .DependsOn(taskId)                    // TaskId
    .DependsOn(task1, task2)              // params CrewTask[]
    .RequiresTool(toolId)                 // ToolId
    .WithContext("key", value)            // string, object
    .Async()                              // bool
    .OutputFile("path")                   // string
    .OutputJson(jsonSchema)               // JsonSchema
    .OutputPydantic(type)                 // Type — schema de sortie structure
    .HumanInput()                         // bool
    .WithCallback(callback)               // ITaskCallback
    .AssignTo(agent)                      // Agent
    .AssignTo(agentId)                    // AgentId
    .Build();                             // → CrewTask
```

#### FlowDefinitionBuilder (Application Layer)

Source : `src/core/Orkeon.Application/Flows/FlowDefinitionBuilder.cs`

```csharp
new FlowDefinitionBuilder()
    .WithId("flow-id")
    .WithName("Flow Name")
    .WithDescription("...")
    .AsSequential() | .AsParallel() | .AsConditional() | .AsLoop()
    .WithTimeout(TimeSpan)
    .WithMaxRetries(3)
    .AddStep("name", "type", step => step
        .DependsOn("step1", "step2")
        .WithTimeout(TimeSpan)
        .WithMaxRetries(3)
        .WithParameter("key", value))
    .AddCrewStep("name", "crewConfigPath")
    .AddLlmStep("name", "promptTemplate")
    .AddToolStep("name", "toolName")
    .Build();                             // → IFlowDefinition
```

### 5.6 Configuration YAML — Format Reel du Loader

Source : `src/core/Orkeon.Infrastructure/Configuration/YamlCrewDefinitionLoader.cs`
Serialisation : YamlDotNet avec `CamelCaseNamingConvention`

> **IMPORTANT** : Le format YAML utilise des **dictionnaires nommes** pour les agents et tasks
> (les cles servent d'identifiants), pas des listes.
> **ATTENTION** : les noms de champs sont en **camelCase** (pas snake_case) car le serialiseur
> utilise `CamelCaseNamingConvention`. Ex: `allowDelegation`, `maxIter`, `asyncExecution`.

#### Format fichier unique (`config.yaml`)

> **IMPORTANT** : le format fichier unique est **plat** — pas de cle `crew:` racine.
> Les champs `name`, `goal`, `process`, `agents`, `tasks` sont tous au niveau racine.

```yaml
# Racine du fichier — champs crew + agents + tasks au meme niveau (PAS de cle "crew:")
name: "research-assistant"                # requis — identifiant de la crew
goal: "Produire un rapport de synthese"   # requis — objectif global
process: "sequential"                     # sequential | hierarchical | consensual | parallel
verbose: true                             # bool
memory: false                             # bool — active la memoire
planning: false                           # bool — active le mode planning
# managerAgent: "agent_key"              # string — cle de l'agent manager (hierarchical seulement)

# Agents = dictionnaire (cle = identifiant unique de l'agent)
agents:
  web_researcher:                         # ← cle = identifiant agent (snake_case recommande)
    role: "Web Researcher"                # requis
    goal: "Find comprehensive info"       # requis
    backstory: |                          # requis — texte multi-ligne
      You are a seasoned research analyst...
    tools:                                # liste de noms d'outils (voir 5.7)
      - "web_scrape"
      - "http_api"
    allowDelegation: false                # bool (camelCase !)
    maxIter: 10                           # int — nombre max d'iterations
    maxRpm: 30                            # int — rate limit
    verbose: true                         # bool
    llm:                                  # optionnel — override LLM par agent
      model: "gpt-4o"
      temperature: 0.7
      maxTokens: 4096

  analyst:
    role: "Data Analyst"
    goal: "Extract key insights"
    backstory: "You are an expert at data analysis..."
    tools:
      - "csv_reader"
      - "json_tool"

# Tasks = dictionnaire (cle = identifiant unique de la tache)
tasks:
  gather_data:                            # ← cle = identifiant tache (snake_case recommande)
    description: "Research the topic..."  # requis
    expectedOutput: "Structured report"   # requis (camelCase !)
    agent: "web_researcher"               # string — cle de l'agent assigne
    dependencies: []                      # liste de cles de taches
    asyncExecution: false                 # bool (camelCase !)
    humanInput: false                     # bool — demande validation humaine
    context: {}                           # dictionnaire libre

  analyze_results:
    description: "Analyze the gathered data..."
    expectedOutput: "Analysis report with key findings"
    agent: "analyst"
    dependencies:
      - "gather_data"                     # ← reference par cle de tache
```

#### Format multi-fichiers (alternative)

`ICrewDefinitionLoader.LoadFromDirectoryAsync(path)` charge 3 fichiers :
- `crew.yaml` — configuration crew uniquement
- `agents.yaml` — dictionnaire des agents
- `tasks.yaml` — dictionnaire des taches

#### `CrewConfiguration` — Modele intermediaire (Domain Layer)

Source : `src/core/Orkeon.Domain/Configuration/CrewConfiguration.cs`

> C'est le record utilise par `YamlCrewDefinitionLoader` et `CrewFactory`.
> **Attention** : il existe aussi un `CrewConfiguration` dans Application Layer (`ImmutableConfigurations.cs`)
> avec des champs differents — ne pas confondre.

```csharp
// Domain.Configuration.CrewConfiguration — utilise par le loader YAML
public sealed record CrewConfiguration
{
    public string Name { get; init; } = "";
    public string Goal { get; init; } = "";
    public ProcessType Process { get; init; } = ProcessType.Sequential;
    public bool Verbose { get; init; }
    public bool Memory { get; init; }
    public bool Planning { get; init; }
    public AgentId? ManagerAgentId { get; init; }
    public IReadOnlyList<AgentConfiguration> Agents { get; init; }
    public IReadOnlyList<TaskConfiguration> Tasks { get; init; }
    public ExecutionConfig? ExecutionConfig { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = [];
}

// Domain.Configuration.AgentConfiguration
public sealed record AgentConfiguration
{
    public AgentId Id { get; init; }
    public string Role { get; init; } = "";
    public string Goal { get; init; } = "";
    public string Backstory { get; init; } = "";
    public IReadOnlyList<string> Tools { get; init; }      // noms d'outils (section 5.7)
    public bool AllowDelegation { get; init; } = true;
    public int MaxIterations { get; init; } = 20;
    public int MaxRPM { get; init; } = 10;
    public bool Verbose { get; init; }
    public LlmConfig? LlmConfig { get; init; }
    public string? SystemTemplate { get; init; }
    public string? PromptTemplate { get; init; }
    public string? ResponseTemplate { get; init; }
}

// Domain.Configuration.TaskConfiguration
public sealed record TaskConfiguration
{
    public TaskId Id { get; init; }
    public string Description { get; init; } = "";
    public string ExpectedOutput { get; init; } = "";
    public AgentId? AssignedAgentId { get; init; }
    public IReadOnlyList<TaskId> Dependencies { get; init; }
    public Dictionary<string, object> Context { get; init; } = [];
    public bool AsyncExecution { get; init; }
    public bool HumanInput { get; init; }
    public int? TimeoutSeconds { get; init; }
}
```

#### `CrewConfiguration` supplementaire (Application Layer)

Source : `src/core/Orkeon.Application/Configuration/ImmutableConfigurations.cs`

Le record Application expose des champs supplementaires pour le mode programmatique :

```
memory.provider         : InMemory | Redis | SQLite | EncryptedRedis | EncryptedSQLite | ChromaDb | Pinecone | LanceDb
memory.enable_short_term : bool (defaut true)
memory.enable_long_term  : bool (defaut true)
memory.enable_episodic   : bool (defaut false)
memory.provider_settings : dictionnaire libre (connection strings, etc.)
retry.max_attempts       : int (defaut 3)
retry.initial_delay      : TimeSpan
retry.backoff_multiplier : double (defaut 2.0)
```

### 5.7 Outils Disponibles — Noms Exacts

Source : `src/tools/Orkeon.Tools.*/`

| Nom outil (pour YAML `tools:`) | Classe | Package |
|-------------------------------|--------|---------|
| `file_read` | `FileReadTool` | `Orkeon.Tools.FileSystem` |
| `file_write` | `FileWriteTool` | `Orkeon.Tools.FileSystem` |
| `directory_read` | `DirectoryReadTool` | `Orkeon.Tools.FileSystem` |
| `email_parser` | `EmailParserTool` | `Orkeon.Tools.FileSystem` |
| `csv_reader` | `CsvReaderTool` | `Orkeon.Tools.Data` |
| `pdf_reader` | `PdfReaderTool` | `Orkeon.Tools.Data` |
| `json_tool` | `JsonTool` | `Orkeon.Tools.Data` |
| `xml_parser` | `XmlParserTool` | `Orkeon.Tools.Data` |
| `database_query` | `DatabaseQueryTool` | `Orkeon.Tools.Data` |
| `docx_reader` | `DOCXReadTool` | `Orkeon.Tools.Data` |
| `docx_writer` | `DOCXWriteTool` | `Orkeon.Tools.Data` |
| `http_api` | `HttpApiTool` | `Orkeon.Tools.Web` |
| `web_scrape` | `WebScrapeTool` | `Orkeon.Tools.Web` |
| `web_search` | `WebSearchTool` | `Orkeon.Tools.Web` |
| `brave_search` | `BraveSearchTool` | `Orkeon.Tools.Web` |
| `github` | `GitHubTool` | `Orkeon.Tools.Web` |
| `shell_command` | `ShellCommandTool` | `Orkeon.Tools.Code` |

**Outils Infrastructure** (enregistres par `AddOrkeonInfrastructure()`, pas par les packages Tools) :

| Nom outil | Classe | Source | Enregistrement DI |
|-----------|--------|--------|-------------------|
| `code_interpreter` | `SecureCodeInterpreterTool` | `Orkeon.Infrastructure` (Sandbox) | `AddOrkeonCodeSandbox()` — Singleton (type concret, pas `IBaseTool`) |
| `delegate_work` | `DelegateWorkTool` | `Orkeon.Infrastructure` (Tools) | Non enregistre automatiquement — a instancier manuellement |
| `ask_question` | `AskQuestionTool` | `Orkeon.Infrastructure` (Tools) | Non enregistre automatiquement — a instancier manuellement |
| `semantic_search` | `SearchTool` | `Orkeon.Infrastructure` (Tools/Search) | Non enregistre automatiquement — a instancier manuellement |

> **Important** : `DelegateWorkTool`, `AskQuestionTool` et `SearchTool` ne sont **pas** enregistres
> comme `IBaseTool` dans le DI par `AddOrkeonInfrastructure()`. Si un exemple YAML les reference
> via `tools: ["delegate_work"]`, le runner doit les enregistrer explicitement :
> ```csharp
> services.AddTransient<IBaseTool, DelegateWorkTool>();
> services.AddTransient<IBaseTool, AskQuestionTool>();
> services.AddTransient<IBaseTool>(sp => sp.GetRequiredService<SecureCodeInterpreterTool>());
> ```

> **Regle** : dans `config.yaml`, utiliser uniquement les noms de la colonne "Nom outil".
> En mode builder fluent (C#), instancier la classe correspondante (ex: `new FileReadTool()`).

### 5.7b IBaseTool — Interface des Outils

Source : `src/core/Orkeon.Domain/Tools/IBaseTool.cs`

> Cette interface est le contrat minimal pour tous les outils. **Pas de propriete `Tags` ni `Capabilities`.**

```csharp
public interface IBaseTool
{
    string Name { get; }                    // nom unique (ex: "file_read")
    string Description { get; }             // description pour le LLM
    ToolSchema Schema { get; }              // schema JSON des parametres
    Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken ct = default);  // nouvelle API
    Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default);                 // legacy
    bool ValidateInput(string input);
}
```

### 5.7c IToolRegistry — Interface Complete

Source : `src/core/Orkeon.Domain/Tools/IToolRegistry.cs`

> **IMPORTANT** : cette interface n'a **aucune implementation dans le framework**.
> Les runners doivent fournir `ServiceProviderToolRegistry` (voir section 3.6).

```csharp
public interface IToolRegistry
{
    Task<bool> RegisterToolAsync(IBaseTool tool);
    Task<bool> UnregisterToolAsync(string toolId);
    Task<IBaseTool?> GetToolAsync(string toolId);
    Task<IBaseTool?> GetToolByNameAsync(string name);
    Task<IReadOnlyList<IBaseTool>> GetAllToolsAsync();
    Task<IReadOnlyList<IBaseTool>> GetToolsByTagsAsync(params string[] tags);
    Task<bool> IsRegisteredAsync(string toolId);
    Task<IReadOnlyList<IBaseTool>> GetToolsByCapabilityAsync(string capability);
    Task<IReadOnlyList<IBaseTool>> GetToolsAsync(IEnumerable<ITool> tools);
    Task ClearAsync();
}
```

> Note : les methodes n'ont **pas** de `CancellationToken` — c'est le design actuel de l'interface.

### 5.8 Memory Providers — Noms Exacts

Source : `src/core/Orkeon.Infrastructure/Memory/`

| Valeur YAML `provider:` | Classe | Description |
|--------------------------|--------|-------------|
| `InMemory` | `InMemoryProvider` | Memoire volatile, dev/test |
| `Redis` | `RedisMemoryProvider` | Redis standard |
| `EncryptedRedis` | `EncryptedRedisMemoryProvider` | Redis + chiffrement au repos |
| `SQLite` | `EncryptedSqliteMemoryProvider` | SQLite persistent |
| `EncryptedSQLite` | `EncryptedSqliteMemoryProvider` | SQLite + chiffrement |
| `ChromaDb` | `ChromaDbMemoryProvider` | ChromaDB vector store (REST API) |
| `Pinecone` | `PineconeMemoryProvider` | Pinecone cloud vector DB |
| `LanceDb` | `LanceDbMemoryProvider` | LanceDB vector store |

### 5.9 LLM Providers

Source : `src/core/Orkeon.Infrastructure/LLMs/`

| Provider | Classe | Package requis |
|----------|--------|---------------|
| `openai` | `OpenAIProvider` | `Orkeon.Infrastructure` |
| `ollama` | `OllamaLlmProvider` | `Orkeon.Infrastructure` |
| `anthropic` | `AnthropicLlmProvider` | `Orkeon.Infrastructure` |
| `azure_openai` | `AzureOpenAILlmProvider` | `Orkeon.Infrastructure` |
| `groq` | `GroqLlmProvider` | `Orkeon.Infrastructure` |

### 5.10 Chargement YAML → Crew → Execution

Source : `src/core/Orkeon.Infrastructure/Configuration/CrewFactory.cs`

**Pipeline complet** :
```
config.yaml → IYamlSerializer (YamlDotNet) → YamlCrewDefinitionLoader → CrewConfiguration → CrewFactory → Crew → ICrewOrchestrationService.KickoffAsync()
```

**Interfaces cles** :

```csharp
// Source : src/core/Orkeon.Application/Interfaces/ICrewFactory.cs
public interface ICrewFactory
{
    Task<Crew> CreateFromConfigAsync(CrewConfiguration config, CancellationToken ct = default);
    Task<Crew> CreateFromFileAsync(string yamlFilePath, CancellationToken ct = default);
    Task<Crew> CreateFromDirectoryAsync(string directoryPath, CancellationToken ct = default);
}
```

- `ICrewDefinitionLoader` — charge YAML en `CrewConfiguration`
  - `LoadFromFileAsync(filePath)` — fichier unique
  - `LoadFromDirectoryAsync(directoryPath)` — format 3 fichiers (crew.yaml, agents.yaml, tasks.yaml)
  - `LoadFromStringAsync(yamlContent)` — string directe
- `ICrewOrchestrationService` — execute la crew
  - `KickoffAsync(crewId, input)` — lance l'execution

**DI** (enregistre automatiquement par `services.AddOrkeonInfrastructure()`) :
```csharp
services.TryAddSingleton<ICrewDefinitionLoader, YamlCrewDefinitionLoader>();
services.TryAddScoped<ICrewFactory, CrewFactory>();
```

### 5.11 Template `Program.cs` — Runner Standard

Le runner est le seul projet executable. Il prend `--config` et `--settings` en arguments CLI
et execute n'importe quel `config.yaml` compatible avec ses outils enregistres.

```csharp
using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.AI;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Domain.Shared;
using Orkeon.Domain.Tools;
// Tools packages
using Orkeon.Tools.FileSystem.DependencyInjection;
using Orkeon.Tools.Data.DependencyInjection;
using Orkeon.Tools.Web.DependencyInjection;
using Orkeon.Tools.Code.DependencyInjection;

class Options
{
    [Option('c', "config", Required = true, HelpText = "Path to config.yaml")]
    public string ConfigPath { get; set; } = "";

    [Option('s', "settings", Required = false, HelpText = "Path to appsettings.json (default: same dir as config)")]
    public string? SettingsPath { get; set; }
}

await Parser.Default.ParseArguments<Options>(args).WithParsedAsync(async opts =>
{
    var configDir = Path.GetDirectoryName(Path.GetFullPath(opts.ConfigPath))!;
    var settingsPath = opts.SettingsPath ?? Path.Combine(configDir, "appsettings.json");

    var host = Host.CreateDefaultBuilder()
        .ConfigureAppConfiguration(b => b.AddJsonFile(settingsPath, optional: true))
        .ConfigureServices((context, services) =>
        {
            services.AddLogging(b => { b.AddConsole(); b.SetMinimumLevel(LogLevel.Information); });

            // LLM provider depuis appsettings.json
            var llmSection = context.Configuration.GetSection("Llm");
            var llmConfig = new LlmConfig
            {
                Model = llmSection["Model"] ?? "gpt-4o",
                BaseUrl = llmSection["BaseUrl"],
                ApiKey = llmSection["ApiKey"],
                Temperature = double.TryParse(llmSection["Temperature"], out var t) ? t : 0.7,
                MaxTokens = int.TryParse(llmSection["MaxTokens"], out var m) ? m : 4096
            };
            services.AddHttpClient();
            services.AddSingleton<ILlmProvider>(sp =>
                new OpenAIProvider(llmConfig, sp.GetRequiredService<IHttpClientFactory>(),
                    sp.GetRequiredService<ILogger<OpenAIProvider>>()));
            services.AddSingleton<IBasicLlmProvider>(sp =>
                new LlmProviderAdapter(sp.GetRequiredService<ILlmProvider>()));
            services.AddSingleton<IChatClient>(sp =>
                new LlmProviderToChatClientAdapter(sp.GetRequiredService<ILlmProvider>()));

            services.AddOrkeonApplication();
            services.AddOrkeonInfrastructure();

            // --- Outils standard (15 enregistres comme IBaseTool) ---
            services.AddOrkeonFileSystemTools();   // 4 : file_read, file_write, directory_read, email_parser
            services.AddOrkeonDataTools();         // 7 : json_tool, csv_reader, pdf_reader, xml_parser, database_query, docx_reader, docx_writer
            services.AddOrkeonWebTools();          // 3 : http_api, web_scrape, github
            services.AddOrkeonCodeTools();         // 1 : shell_command

            // Optionnel : search tools conditionnes par cles API
            var config = context.Configuration;
            if (config["Search:TavilyApiKey"] is string tavilyKey)
                services.AddOrkeonWebSearchTool(tavilyKey);
            if (config["Search:BraveApiKey"] is string braveKey)
                services.AddOrkeonBraveSearchTool(braveKey);

            // Resolution outils YAML → instances DI
            services.AddSingleton<IToolRegistry, ServiceProviderToolRegistry>();
        })
        .Build();

    // === Charge config.yaml et cree la crew ===
    var crewFactory = host.Services.GetRequiredService<ICrewFactory>();
    var crew = await crewFactory.CreateFromFileAsync(opts.ConfigPath, CancellationToken.None);

    // === Execution ===
    var orchestrator = host.Services.GetRequiredService<ICrewOrchestrationService>();
    var input = CrewInput.Empty();
    var output = await orchestrator.KickoffAsync(crew.Id, input);

    // === Resultats ===
    Console.WriteLine($"Duration: {output.Duration.TotalSeconds:F1}s");
    Console.WriteLine($"Tasks completed: {output.TaskOutputs.Count}");
    Console.WriteLine(output.FinalOutput);
});
```

**Commande d'execution** (depuis la racine du repo) :
```bash
# Exemple standard
dotnet run --project examples/runners/standard -- --config examples/01-enterprise/01-research-assistant/config.yaml

# Exemple trading
dotnet run --project examples/runners/trading -- --config examples/03-finance-trading/31-algo-trading/config.yaml

# Avec script helper
./examples/run-example.sh 01-enterprise/01-research-assistant
```

> **Note** : les exemples existants (`research-assistant`, `code-review`, `email-management`)
> utilisent le mode Builder Fluent dans leur `Program.cs`. Lors de la migration, leur logique builder
> sera conservee dans un fichier `Example.cs` de reference dans `_shared/templates/` mais les exemples
> eux-memes passeront au mode YAML + runner.

---

## 6. Plan de Migration

### 6.1 Etape 1 — Creer les runners (prerequis)

```bash
# Creer la structure des runners
mkdir -p examples/runners/{_shared,standard,trading}

# Creer ServiceProviderToolRegistry.cs dans _shared/ (voir section 3.6)
# Creer Program.cs du runner standard dans standard/ (voir section 5.11)
# Creer Program.cs du runner trading dans trading/ (voir section 3.5)
# Creer les .csproj correspondants (voir section 3.7)
# Creer Orkeon.Examples.sln referencant les 3 projets
```

### 6.2 Etape 2 — Creer la structure de categories

```bash
mkdir -p examples/{01-enterprise,02-science-research,03-finance-trading,04-health-wellness}
mkdir -p examples/{05-education,06-engineering-devops,07-creative-media,08-iot-smart-systems}
mkdir -p examples/{09-experimental,_shared}
```

### 6.3 Etape 3 — Migrer les exemples existants

| Existant | Destination | Actions |
|----------|-------------|---------|
| `research-assistant/` | `01-enterprise/01-research-assistant/` | 1. Extraire la config en `config.yaml` depuis le `Program.cs` builder 2. Creer `appsettings.json` (template LLM) 3. Creer `README.md` conforme 4. Supprimer `Program.cs`, `.csproj` (le runner les remplace) |
| `code-review/` | `01-enterprise/02-code-review/` | Idem : extraire config.yaml, supprimer .csproj/Program.cs |
| `email-management/` | `01-enterprise/03-email-pipeline/` | Idem |
| `classictrading/` | `03-finance-trading/31-algo-trading/` | 1. Nettoyer `.vs/`, `bin/`, `obj/`, `logs/` 2. Extraire config.yaml 3. Migrer les outils trading vers le runner trading 4. Supprimer les anciens .csproj/.sln |
| `streaming-demo/` | `_shared/streaming-demo/` | Deplacer (transverse, pas un cas numerote) |
| `tasks/` | `docs/migration-tasks/` | Deplacer hors de `examples/` |

**Commandes Git** :

```bash
# Nettoyage classictrading
git rm -r --cached examples/classictrading/.vs
git rm -r --cached examples/classictrading/*/bin examples/classictrading/*/obj

# Deplacements
git mv examples/research-assistant examples/01-enterprise/01-research-assistant
git mv examples/code-review examples/01-enterprise/02-code-review
git mv examples/email-management examples/01-enterprise/03-email-pipeline
git mv examples/classictrading examples/03-finance-trading/31-algo-trading
git mv examples/streaming-demo examples/_shared/streaming-demo
git mv examples/tasks docs/migration-tasks

# Post-migration : dans chaque dossier migre, supprimer Program.cs et .csproj
# et creer config.yaml + appsettings.json + README.md conformes
```

### 6.4 Etape 4 — Nettoyage post-migration

Pour chaque exemple migre, supprimer les fichiers devenus obsoletes :
- `Program.cs` (la logique est dans le runner)
- `Example.cs` (archiver dans `_shared/templates/` si valeur de reference)
- `{Slug}.csproj` (remplace par le runner)
- `*.sln` locaux (remplace par `Orkeon.Examples.sln`)

---

## 7. Priorisation de Creation

### Phase 1 — Pilotes (1 par categorie, 9 exemples)

Objectif : valider la structure avec un exemple representatif par categorie.

| Categorie | Exemple pilote | Justification |
|-----------|---------------|---------------|
| 01-enterprise | #1 research-assistant | Existe deja, archetype `Sequential` 3 agents |
| 02-science-research | #16 prisma-meta-analysis | `Sequential` + memoire episodique + `EvaluationSuite` |
| 03-finance-trading | #31 algo-trading | Existe deja, showcase maximale 40+ outils |
| 04-health-wellness | #46 diagnostic-assistant | Securite maximale, `HumanInput` systematique |
| 05-education | #56 adaptive-tutor | `AgentMemory.LongTerm` persistante multi-session |
| 06-engineering-devops | #66 cicd-pipeline | `ICheckpointManager` + rollback + `CrewHooks` |
| 07-creative-media | #77 podcast-production | Pipeline type `ToolBase<TReq, TRes>` bout-en-bout |
| 08-iot-smart-systems | #86 smart-home-a2a | Protocole A2A natif (`AgentCard`, `AgentSkill`) |
| 09-experimental | #101 crew-of-crews | Showcase ultime des 4 qualites |

### Phase 2 — Couverture des Process Types (15 exemples supplementaires)

Objectif : chaque type de process est couvert par au moins 2 exemples.

| Process Type | Exemples a prioriser |
|-------------|---------------------|
| `Sequential` | #1 (P1), #14 etl-pipeline, #40 invoice-processing |
| `Hierarchical` | #2 code-review (existe), #9 agile-scrum-master |
| `Consensual` | #11 translation-consensus, #34 portfolio-consensus |
| `Parallel` → `Sequential` | #4 financial-reports, #35 accounting-reconciliation |
| `FlowEngine` | #26 knowledge-graph, #59 nonlinear-learning-path |
| A2A | #86 (P1), #94 smart-city-traffic |

### Phase 3 — Couverture des Features Cles (par batch de 10)

Objectif : chaque feature framework majeure est demontree par au moins un exemple.

| Feature | Exemples prioritaires | Status codebase |
|---------|----------------------|-----------------|
| `ICheckpointManager` / `IResumeEngine` | #4, #25, #68 | Implemente |
| `EvaluationSuite` / `LlmJudgeEvaluator` | #21, #57, #58 | Implemente |
| `EncryptedMemory` (Redis ou SQLite) | #7, #37, #48 | Implemente |
| `INistComplianceReporter` | #7, #45, #53 | Implemente |
| `HumanInputContext` (multi-types) | #3, #39, #75 | Implemente |
| `ObserverAgent` (via `AgentType.Observer`) | #6, #32, #89 | Implemente |
| `IKnowledgeSource` / `ITextChunker` | #23, #30, #38 | Implemente |
| `PromptSecurityTypes` | #13, #46, #78 | Implemente |
| `ICodeSandbox` / `ICodeSecurityAnalyzer` | #65, #73, #74 | Implemente |
| `CrewHooks` | #61, #66, #92 | Implemente |
| `BenchmarkRunner` | #27, #72, #93 | Implemente |
| Streaming | #31 (P1), #55, #67 | Implemente |
| `Composite` Memory | #76, #98, #100 | 🔮 Planifie |
| `IConfigurationVersioning` / Diff / Rollback | #43, #96 | 🔮 Planifie |

### Phase 4 — Exemples Restants

Completer les cas d'usage restants par batch de 10, categorie par categorie.
Apres chaque batch : produire un resume `[N/101 completes — categorie : X]`.

---

## 8. Templates Fichiers

### 8.1 Template `README.md` (par exemple)

````markdown
# {Numero}. {Titre}

> {Description courte du cas d'usage — 1 a 2 phrases reprises de 101-USE-CASES.md}

## Qualite mise en avant

{Emoji + qualite} — {explication courte}

## Architecture

- **Process** : `{Sequential|Hierarchical|Consensual|Parallel}` {+ FlowEngine si applicable}
- **Agents** : {nombre} — {liste des roles avec AgentType entre parentheses}
- **Outils** : `{file_read}`, `{web_scrape}`, ... (noms exacts section 5.7)
- **Memoire** : `{provider}` (nom exact section 5.8)
- **Features cles** : {liste des interfaces/classes utilisees}
- **Runner** : `{standard|trading}` (voir section 3)

## Prerequis

1. .NET 10 SDK
2. Configurer `appsettings.json` avec votre cle API LLM :
```json
{
  "Llm": {
    "Model": "gpt-4o",
    "BaseUrl": "https://api.openai.com/v1",
    "ApiKey": "YOUR_API_KEY"
  }
}
```

## Execution

```bash
# Depuis la racine du repo
dotnet run --project examples/runners/{standard|trading} -- --config examples/{categorie}/{slug}/config.yaml

# Ou avec le script helper
./examples/run-example.sh {categorie}/{slug}
```

Le runner charge `config.yaml` via `ICrewFactory.CreateFromFileAsync()`,
cree la crew et l'execute via `ICrewOrchestrationService.KickoffAsync()`.

## Ce que cet exemple demontre

- {point 1 — feature framework specifique}
- {point 2 — pattern architectural}
- {point 3 — qualite mise en evidence}

## Lien vers le cas d'usage

Voir [101-USE-CASES.md #N](../../../project/marketing/content-strategy/101-USE-CASES.md)
````

### 8.2 Template `config.yaml`

> **IMPORTANT** : ce format correspond au `YamlCrewDefinitionLoader` reel du codebase.
> Les agents et tasks sont des **dictionnaires nommes** (cle = identifiant snake_case).
> Les champs multi-mots sont en **camelCase** (serialiseur `CamelCaseNamingConvention`).

```yaml
# {Numero}. {Titre}
# Cas d'usage : {description courte}
# Source : project/marketing/content-strategy/101-USE-CASES.md #{numero}

# Format plat (pas de cle "crew:" racine)
name: "{slug}"
goal: "{objectif global de la crew}"
process: "{sequential|hierarchical|consensual|parallel}"
verbose: true
memory: false
planning: false
# managerAgent: "{agent_key}"            # decommenter pour process hierarchical

# Agents — dictionnaire (cle = identifiant unique snake_case)
agents:
  {agent_key_1}:
    role: "{role de l'agent}"
    goal: "{objectif de l'agent}"
    backstory: |
      {contexte et expertise de l'agent — texte multi-ligne}
    tools:
      - "{nom_outil_exact}"              # voir section 5.7
      - "{nom_outil_exact}"
    allowDelegation: false
    maxIter: 10
    verbose: true
    # llm:                               # decommenter pour overrider le LLM par defaut
    #   model: "gpt-4o"
    #   temperature: 0.7
    #   maxTokens: 4096

  {agent_key_2}:
    role: "{role}"
    goal: "{objectif}"
    backstory: |
      {contexte}
    tools:
      - "{nom_outil_exact}"

# Tasks — dictionnaire (cle = identifiant unique snake_case)
tasks:
  {task_key_1}:
    description: "{description detaillee de la tache}"
    expectedOutput: "{format et contenu attendu en sortie}"
    agent: "{agent_key_1}"               # reference par cle d'agent
    dependencies: []
    asyncExecution: false
    humanInput: false

  {task_key_2}:
    description: "{description detaillee}"
    expectedOutput: "{format attendu}"
    agent: "{agent_key_2}"
    dependencies:
      - "{task_key_1}"                   # reference par cle de tache
```

### 8.3 Template `Orkeon.Examples.Runner.csproj` (runner standard)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Orkeon.Examples.Runner</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../_shared/Orkeon.Examples.Shared.csproj" />
    <ProjectReference Include="../../../src/core/Orkeon.Infrastructure/Orkeon.Infrastructure.csproj" />
    <ProjectReference Include="../../../src/tools/Orkeon.Tools.FileSystem/Orkeon.Tools.FileSystem.csproj" />
    <ProjectReference Include="../../../src/tools/Orkeon.Tools.Web/Orkeon.Tools.Web.csproj" />
    <ProjectReference Include="../../../src/tools/Orkeon.Tools.Data/Orkeon.Tools.Data.csproj" />
    <ProjectReference Include="../../../src/tools/Orkeon.Tools.Code/Orkeon.Tools.Code.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="CommandLineParser" Version="2.9.*" />
  </ItemGroup>
</Project>
```

### 8.4 Template `Orkeon.Examples.Trading.Runner.csproj` (runner trading)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Orkeon.Examples.Trading.Runner</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../_shared/Orkeon.Examples.Shared.csproj" />
    <ProjectReference Include="../../../src/core/Orkeon.Infrastructure/Orkeon.Infrastructure.csproj" />
    <ProjectReference Include="../../../src/tools/Orkeon.Tools.FileSystem/Orkeon.Tools.FileSystem.csproj" />
    <ProjectReference Include="../../../src/tools/Orkeon.Tools.Web/Orkeon.Tools.Web.csproj" />
    <ProjectReference Include="../../../src/tools/Orkeon.Tools.Data/Orkeon.Tools.Data.csproj" />
    <ProjectReference Include="../../../src/tools/Orkeon.Tools.Code/Orkeon.Tools.Code.csproj" />
    <!-- Trading tools (MathNet.Numerics, YahooFinanceApi) -->
    <!-- Ajuster le chemin apres migration (actuellement examples/classictrading/Orkeon.Trading.Tools/) -->
    <ProjectReference Include="../../03-finance-trading/31-algo-trading/Orkeon.Trading.Tools/Orkeon.Trading.Tools.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="CommandLineParser" Version="2.9.*" />
  </ItemGroup>
</Project>
```

> **Note** : les exemples individuels n'ont plus de `.csproj`. Seuls les runners en ont.

### 8.5 Template `Orkeon.Examples.Shared.csproj` (code commun)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Orkeon.Examples.Shared</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../../../src/core/Orkeon.Domain/Orkeon.Domain.csproj" />
  </ItemGroup>
</Project>
```

### 8.6 Template `run-example.sh`

```bash
#!/usr/bin/env bash
# Usage: ./examples/run-example.sh <category/example>
# Ex:    ./examples/run-example.sh 01-enterprise/01-research-assistant
set -euo pipefail

EXAMPLE_PATH="$1"
CONFIG_PATH="examples/${EXAMPLE_PATH}/config.yaml"

if [ ! -f "$CONFIG_PATH" ]; then
  echo "ERROR: config.yaml not found at $CONFIG_PATH" >&2
  exit 1
fi

# Determine runner: 03-finance-trading → trading, others → standard
CATEGORY=$(echo "$EXAMPLE_PATH" | cut -d'/' -f1)
if [ "$CATEGORY" = "03-finance-trading" ]; then
  RUNNER="examples/runners/trading"
else
  RUNNER="examples/runners/standard"
fi

echo "Running $EXAMPLE_PATH with runner $(basename $RUNNER)..."
dotnet run --project "$RUNNER" -- --config "$CONFIG_PATH"
```

### 8.7 Template `appsettings.json` (par exemple)

> Ce fichier est charge par le runner via `ConfigurationBuilder.AddJsonFile()`.
> Les cles correspondent a `context.Configuration.GetSection("Llm")` dans le runner.

```json
{
  "Llm": {
    "Model": "gpt-4o",
    "BaseUrl": "https://api.openai.com/v1",
    "ApiKey": "YOUR_OPENAI_API_KEY",
    "Temperature": 0.7,
    "MaxTokens": 4096
  },
  "Search": {
    "TavilyApiKey": "",
    "BraveApiKey": ""
  }
}
```

> **Note** : pour utiliser Docker Model Runner (Ollama local), remplacer :
> ```json
> "Model": "ai/llama3.2",
> "BaseUrl": "http://localhost:12434/engines/llama.cpp/v1",
> "ApiKey": "not-needed"
> ```

---

## 9. Regles de Gouvernance

1. **Numerotation stable** : le numero d'un exemple correspond toujours au numero dans `101-USE-CASES.md`. On ne renumerote jamais.

2. **Pas de binaires dans le repo** : ajouter dans `examples/.gitignore` :
   ```
   .vs/
   bin/
   obj/
   logs/
   *.user
   *.suo
   ```

3. **Chaque exemple est autonome** : un developpeur doit pouvoir pointer le runner vers n'importe quel dossier d'exemple (`--config path/to/config.yaml`) et obtenir un resultat. Le dossier contient tout le necessaire : `config.yaml` + `appsettings.json`.

4. **Marquage 🔮** : les exemples utilisant des features planifiees portent le marqueur 🔮 dans leur `README.md` et un commentaire `// TODO: feature planifiee — {interface}` dans le code. La liste des features 🔮 : `Composite Memory`, `IConfigurationVersioning`, `IConfigurationDiffService`, `IConfigurationRollbackService`, `AgentStep`.

5. **Validation continue** : un script `scripts/validate-examples.sh` verifie que les runners compilent et que chaque `config.yaml` est valide :
   ```bash
   # Compiler les runners
   dotnet build examples/Orkeon.Examples.sln --no-restore || exit 1

   # Valider chaque config.yaml (dry-run sans execution LLM)
   for config in examples/0*-*/*/config.yaml; do
     echo "Validating $config..."
     dotnet run --project examples/runners/standard -- --config "$config" --validate-only || exit 1
   done
   ```

6. **Coherence avec 101-USE-CASES.md** : si un cas d'usage est ambigu ou en contradiction avec le codebase, laisser un commentaire `// TODO: clarifier — {question}` plutot que d'inventer.

7. **Noms d'outils** : utiliser exclusivement les noms de la section 5.7 dans les fichiers `config.yaml`. Ne jamais inventer de noms d'outils.

---

## 10. Table de Reference Rapide — 101 Cas d'Usage

> Cette table permet a Claude Code de retrouver instantanement le chemin cible pour chaque cas.

| # | Slug | Categorie | Process | Qualite | Runner | 🔮 |
|---|------|-----------|---------|---------|--------|-----|
| 1 | `01-enterprise/01-research-assistant` | Enterprise | Sequential | 🎯 | standard | |
| 2 | `01-enterprise/02-code-review` | Enterprise | Hierarchical | 💪 | standard | |
| 3 | `01-enterprise/03-email-pipeline` | Enterprise | Sequential | 🔒 | standard | |
| 4 | `01-enterprise/04-financial-reports` | Enterprise | Parallel→Seq | ✅ | standard | |
| 5 | `01-enterprise/05-customer-support` | Enterprise | Hierarchical | 💪 | standard | |
| 6 | `01-enterprise/06-competitive-intelligence` | Enterprise | Parallel | ✅ | standard | |
| 7 | `01-enterprise/07-due-diligence-nist` | Enterprise | Hierarchical | 🔒 | standard | |
| 8 | `01-enterprise/08-content-marketing` | Enterprise | Sequential | 🎯 | standard | |
| 9 | `01-enterprise/09-agile-scrum-master` | Enterprise | Hierarchical | 💪 | standard | |
| 10 | `01-enterprise/10-sentiment-analysis` | Enterprise | Parallel→Seq | ✅ | standard | 🔮 |
| 11 | `01-enterprise/11-translation-consensus` | Enterprise | Consensual | 💪 | standard | |
| 12 | `01-enterprise/12-employee-onboarding` | Enterprise | Sequential | 🎯 | standard | |
| 13 | `01-enterprise/13-security-audit` | Enterprise | Hierarchical | 🔒 | standard | |
| 14 | `01-enterprise/14-etl-pipeline` | Enterprise | Sequential | ✅ | standard | |
| 15 | `01-enterprise/15-commercial-proposals` | Enterprise | Sequential | 🔒 | standard | |
| 16 | `02-science-research/16-prisma-meta-analysis` | Science | Sequential | ✅ | standard | |
| 17 | `02-science-research/17-scientific-debate` | Science | Consensual | 💪 | standard | |
| 18 | `02-science-research/18-academic-writing` | Science | Sequential | 🎯 | standard | |
| 19 | `02-science-research/19-experimental-data` | Science | Parallel→Seq | 💪 | standard | |
| 20 | `02-science-research/20-patent-monitoring` | Science | Parallel | ✅ | standard | |
| 21 | `02-science-research/21-peer-review-calibration` | Science | Consensual | 💪 | standard | |
| 22 | `02-science-research/22-clinical-trials-encrypted` | Science | Parallel | 🔒 | standard | |
| 23 | `02-science-research/23-genomic-analysis` | Science | Hierarchical | 🔒 | standard | |
| 24 | `02-science-research/24-grant-writing` | Science | Sequential | 🎯 | standard | |
| 25 | `02-science-research/25-lab-assistant-resume` | Science | Sequential | ✅ | standard | |
| 26 | `02-science-research/26-knowledge-graph` | Science | FlowEngine | 💪 | standard | |
| 27 | `02-science-research/27-trend-prediction` | Science | Parallel→Seq | ✅ | standard | |
| 28 | `02-science-research/28-triple-validation` | Science | Consensual | 💪 | standard | |
| 29 | `02-science-research/29-hypothesis-generation` | Science | FlowEngine | 💪 | standard | |
| 30 | `02-science-research/30-adaptive-summary` | Science | Sequential | 🎯 | standard | |
| 31 | `03-finance-trading/31-algo-trading` | Finance | Hierarchical | 💪 | **trading** | |
| 32 | `03-finance-trading/32-fraud-detection` | Finance | Parallel | 🔒 | **trading** | |
| 33 | `03-finance-trading/33-credit-scoring` | Finance | Sequential | 🔒 | **trading** | |
| 34 | `03-finance-trading/34-portfolio-consensus` | Finance | Consensual | 💪 | **trading** | |
| 35 | `03-finance-trading/35-accounting-reconciliation` | Finance | Parallel→Seq | ✅ | **trading** | |
| 36 | `03-finance-trading/36-cash-flow-forecast` | Finance | Sequential | ✅ | **trading** | |
| 37 | `03-finance-trading/37-kyc-aml-compliance` | Finance | Hierarchical | 🔒 | **trading** | |
| 38 | `03-finance-trading/38-contract-analysis` | Finance | Parallel→Seq | 🎯 | **trading** | |
| 39 | `03-finance-trading/39-robo-advisor` | Finance | Sequential | 🔒 | **trading** | |
| 40 | `03-finance-trading/40-invoice-processing` | Finance | Sequential | ✅ | **trading** | |
| 41 | `03-finance-trading/41-insider-trading-detection` | Finance | Parallel→Seq | 🔒 | **trading** | |
| 42 | `03-finance-trading/42-dynamic-pricing` | Finance | Hierarchical | 💪 | **trading** | |
| 43 | `03-finance-trading/43-tax-optimization` | Finance | Parallel→Seq | 💪 | **trading** | 🔮 |
| 44 | `03-finance-trading/44-esg-scoring` | Finance | Parallel→Seq | 💪 | **trading** | |
| 45 | `03-finance-trading/45-stress-testing` | Finance | Sequential | 🔒 | **trading** | 🔮 |
| 46 | `04-health-wellness/46-diagnostic-assistant` | Sante | Sequential | 🔒 | standard | |
| 47 | `04-health-wellness/47-nutrition-planner` | Sante | Sequential | 🎯 | standard | |
| 48 | `04-health-wellness/48-mental-health-monitoring` | Sante | Sequential | 🔒 | standard | |
| 49 | `04-health-wellness/49-medical-records-fhir` | Sante | Sequential | 🔒 | standard | |
| 50 | `04-health-wellness/50-radiology-assistant` | Sante | Sequential | 🔒 | standard | |
| 51 | `04-health-wellness/51-care-coordination` | Sante | Hierarchical | 💪 | standard | |
| 52 | `04-health-wellness/52-pharmacovigilance` | Sante | Parallel | ✅ | standard | |
| 53 | `04-health-wellness/53-clinical-trials-nist` | Sante | Hierarchical | 🔒 | standard | |
| 54 | `04-health-wellness/54-sports-coach` | Sante | Sequential | 🎯 | standard | |
| 55 | `04-health-wellness/55-telemedicine-streaming` | Sante | Parallel | 🔒 | standard | |
| 56 | `05-education/56-adaptive-tutor` | Education | Sequential | ✅ | standard | |
| 57 | `05-education/57-exam-generation` | Education | Consensual | 💪 | standard | |
| 58 | `05-education/58-multi-criteria-grading` | Education | Parallel→Seq | ✅ | standard | |
| 59 | `05-education/59-nonlinear-learning-path` | Education | FlowEngine | 🎯 | standard | |
| 60 | `05-education/60-case-study-simulation` | Education | FlowEngine | 💪 | standard | |
| 61 | `05-education/61-gamified-learning` | Education | FlowEngine | 💪 | standard | |
| 62 | `05-education/62-plagiarism-detection` | Education | Parallel→Seq | ✅ | standard | |
| 63 | `05-education/63-skills-gap-mapping` | Education | Parallel→Seq | 💪 | standard | |
| 64 | `05-education/64-accessibility` | Education | Parallel | 💪 | standard | |
| 65 | `05-education/65-developer-mentoring` | Education | Parallel→Seq | 🎯 | standard | |
| 66 | `06-engineering-devops/66-cicd-pipeline` | DevOps | Sequential | ✅ | standard | |
| 67 | `06-engineering-devops/67-incident-response` | DevOps | Sequential | 🔒 | standard | |
| 68 | `06-engineering-devops/68-database-migration` | DevOps | Sequential | 💪 | standard | |
| 69 | `06-engineering-devops/69-performance-analysis` | DevOps | Parallel→Seq | 💪 | standard | |
| 70 | `06-engineering-devops/70-versioned-documentation` | DevOps | Sequential | 🎯 | standard | 🔮 |
| 71 | `06-engineering-devops/71-cloud-audit-nist` | DevOps | Parallel→Seq | 🔒 | standard | |
| 72 | `06-engineering-devops/72-load-testing` | DevOps | Sequential | ✅ | standard | |
| 73 | `06-engineering-devops/73-secure-refactoring` | DevOps | Sequential | 🔒 | standard | |
| 74 | `06-engineering-devops/74-dependency-management` | DevOps | Sequential | ✅ | standard | |
| 75 | `06-engineering-devops/75-chaos-engineering` | DevOps | Sequential | 🔒 | standard | |
| 76 | `07-creative-media/76-narrative-studio` | Creative | FlowEngine | 💪 | standard | 🔮 |
| 77 | `07-creative-media/77-podcast-production` | Creative | Sequential | 🎯 | standard | |
| 78 | `07-creative-media/78-synthetic-data` | Creative | Sequential | 🔒 | standard | |
| 79 | `07-creative-media/79-music-composition` | Creative | Consensual | 💪 | standard | |
| 80 | `07-creative-media/80-art-direction` | Creative | Hierarchical | 💪 | standard | |
| 81 | `07-creative-media/81-worldbuilding` | Creative | Parallel→Cons | ✅ | standard | 🔮 |
| 82 | `07-creative-media/82-newsletter-curation` | Creative | Parallel→Seq | 🎯 | standard | |
| 83 | `07-creative-media/83-interactive-fiction` | Creative | FlowEngine | 💪 | standard | |
| 84 | `07-creative-media/84-multi-perspective-critique` | Creative | Parallel→Seq | 💪 | standard | |
| 85 | `07-creative-media/85-cross-media-adaptation` | Creative | Sequential | 🎯 | standard | |
| 86 | `08-iot-smart-systems/86-smart-home-a2a` | IoT | Parallel | 💪 | standard | |
| 87 | `08-iot-smart-systems/87-fleet-management` | IoT | Hierarchical | ✅ | standard | |
| 88 | `08-iot-smart-systems/88-precision-agriculture` | IoT | Sequential | 🔒 | standard | |
| 89 | `08-iot-smart-systems/89-environmental-monitoring` | IoT | Parallel | ✅ | standard | |
| 90 | `08-iot-smart-systems/90-energy-management` | IoT | FlowEngine | ✅ | standard | |
| 91 | `08-iot-smart-systems/91-predictive-maintenance` | IoT | Parallel→Seq | ✅ | standard | |
| 92 | `08-iot-smart-systems/92-warehouse-logistics` | IoT | Parallel | 💪 | standard | |
| 93 | `08-iot-smart-systems/93-visual-quality-control` | IoT | Sequential | ✅ | standard | |
| 94 | `08-iot-smart-systems/94-smart-city-traffic` | IoT | Parallel+A2A | 💪 | standard | |
| 95 | `08-iot-smart-systems/95-crisis-management` | IoT | Hierarchical | 🔒 | standard | |
| 96 | `09-experimental/96-self-adaptive-crew` | Experimental | FlowEngine | 💪 | standard | 🔮 |
| 97 | `09-experimental/97-multi-party-negotiation` | Experimental | Consensual+Flow | 💪 | standard | |
| 98 | `09-experimental/98-legacy-code-archaeology` | Experimental | Seq+FlowEngine | 🎯 | standard | 🔮 |
| 99 | `09-experimental/99-ethics-jury` | Experimental | Parallel→Seq | 🔒 | standard | |
| 100 | `09-experimental/100-civilization-simulator` | Experimental | FlowEngine+A2A | 💪 | standard | 🔮 |
| 101 | `09-experimental/101-crew-of-crews` | Experimental | Hierarchical | 🎯🔒💪✅ | standard | 🔮 |

> **Colonne Runner** : `standard` = runner 15 outils built-in + 2 optionnels, `trading` = runner 15+2 standard + 44 outils trading custom (MathNet, YahooFinance).
> **Colonne 🔮** : marque les exemples utilisant au moins une feature planifiee (non encore implementee).
> Ces exemples doivent inclure un commentaire `# TODO: feature planifiee` dans leur `config.yaml`.

---

*Document genere le 2026-03-28 — base sur `project/marketing/content-strategy/101-USE-CASES.md` et l'etat actuel du codebase (`git HEAD`).*
*Architecture runner : exemples = dossiers de donnees (config.yaml + appsettings.json), runners = exe partages par groupe d'outils.*
*Derniere verification API : AgentBuilder, CrewBuilder, CrewTaskBuilder, FlowDefinitionBuilder, CrewConfiguration, ImmutableConfigurations.cs, IToolRegistry, tous les outils et memory providers.*
