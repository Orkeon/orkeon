> 🇬🇧 [English version](../../guides/porting-example.md)

> **Voir aussi** : [Méthodologie](./porting-methodology.md) · [Retour à l'index](../INDEX.md)

# Exemple de plan de portage — Pipeline de traitement de commandes

Cet exemple applique la méthodologie du fichier [Méthodologie de portage](./porting-methodology.md) à une application fictive de traitement de commandes e-commerce.

## L'application source

L'application "OrderFlow" est un service .NET qui traite les commandes d'un site e-commerce. Elle est composée de cinq services principaux :

| Service | Responsabilité |
|---------|---------------|
| `OrderIngestionService` | Lit les nouvelles commandes depuis une file RabbitMQ et les stocke en base PostgreSQL |
| `InventoryCheckService` | Vérifie la disponibilité des produits dans le catalogue (API REST interne) |
| `FraudDetectionService` | Analyse le risque de fraude en croisant historique client et montant commande |
| `PricingService` | Applique les promotions, calcule les réductions et la TVA |
| `NotificationService` | Génère et envoie un email de confirmation avec le récapitulatif commande |

Le flux séquentiel est : Ingestion → Vérification stock → Détection fraude → Calcul prix → Notification.

## Étape 1 — Analyse des responsabilités

### Inventaire des composants

| Composant | Catégorie | Entrées | Sorties | Dépendances externes |
|-----------|-----------|---------|---------|---------------------|
| `OrderIngestionService` | Collecte | Messages RabbitMQ | Commande en BDD | PostgreSQL, RabbitMQ |
| `InventoryCheckService` | Décision | ID produits | Statut disponibilité | API Catalogue interne |
| `FraudDetectionService` | Analyse | Commande + historique client | Score de risque (0-100) | PostgreSQL (historique) |
| `PricingService` | Analyse | Commande + règles promo | Montants finaux (HT, TVA, TTC) | Fichier CSV des promotions |
| `NotificationService` | Production | Commande validée + prix | Email HTML de confirmation | Serveur SMTP |

### Interactions humaines identifiées

Le `FraudDetectionService` flag les commandes avec un score > 80 pour revue manuelle. Ce point deviendra une task avec `HumanInput = true` — ce qui donne à l'agent l'outil `human_input`, auquel répond l'`IHumanInputProvider` que l'hôte enregistre (Étape 4c).

## Étape 2 — Mapping vers des agents

### Agent 1 : Order Collector

```
Responsabilité source : OrderIngestionService
→ Rôle agent         : "Order Data Collector"
→ Objectif agent     : "Retrieve and structure new order data from the database"
→ Backstory          : "Experienced data engineer specialized in order management systems"
→ Outils nécessaires : RelationalDatabaseTool (PostgreSQL)
→ Contraintes        : MaxIterations=5, MaxRpm=10
```

**Décision clé** : On ne porte pas la lecture RabbitMQ. L'agent lit directement les commandes en attente en base PostgreSQL. La consommation de la file reste hors du périmètre agent — c'est une opération d'infrastructure purement mécanique (pas de raisonnement LLM nécessaire).

### Agent 2 : Inventory Checker

```
Responsabilité source : InventoryCheckService
→ Rôle agent         : "Inventory Verification Specialist"
→ Objectif agent     : "Verify product availability and flag out-of-stock items"
→ Backstory          : "Supply chain analyst with deep knowledge of inventory systems"
→ Outils nécessaires : HttpApiTool (API Catalogue)
→ Contraintes        : MaxIterations=10, MaxRpm=20
```

### Agent 3 : Fraud Analyst

```
Responsabilité source : FraudDetectionService
→ Rôle agent         : "Fraud Detection Analyst"
→ Objectif agent     : "Analyze order risk based on customer history and transaction patterns"
→ Backstory          : "Senior fraud prevention specialist with expertise in e-commerce transaction patterns"
→ Outils nécessaires : RelationalDatabaseTool (historique client), JsonTool (structuration résultat)
→ Contraintes        : MaxIterations=10, MaxRpm=10
```

**Décision clé** : Le LLM apporte une valeur réelle ici — il peut raisonner sur des patterns de fraude complexes au-delà des règles statiques du service original. Le score de risque est produit avec une justification textuelle que le service original ne fournissait pas.

### Agent 4 : Pricing Specialist

```
Responsabilité source : PricingService
→ Rôle agent         : "Pricing Calculation Specialist"
→ Objectif agent     : "Apply promotions and compute final pricing (HT, TVA, TTC)"
→ Backstory          : "Pricing analyst expert in French tax regulations and promotional strategies"
→ Outils nécessaires : CsvReaderTool (fichier promotions), SecureCodeInterpreterTool (calculs)
→ Contraintes        : MaxIterations=5, MaxRpm=10
```

**Décision clé** : Utilisation de `SecureCodeInterpreterTool` pour les calculs de TVA plutôt qu'un outil custom. Le LLM génère le code C# de calcul qui est exécuté dans le sandbox — cela donne la flexibilité de gérer des règles de promotion complexes sans hardcoder la logique. Deux conditions : l'outil n'est enregistré que comme type concret, l'hôte l'expose donc sous `IBaseTool` pour que le nom YAML `code_interpreter` se résolve (Étape 4c) ; et le sandbox exécute le code dans Docker — sans Docker, l'exécution est refusée sauf si `SandboxOptions.AllowHostExecution` est explicitement activé.

### Agent 5 : Notification Writer

```
Responsabilité source : NotificationService
→ Rôle agent         : "Customer Communication Specialist"
→ Objectif agent     : "Generate personalized order confirmation content"
→ Backstory          : "Customer experience writer skilled in e-commerce communication"
→ Outils nécessaires : FileWriteTool (génération du contenu)
→ Contraintes        : MaxIterations=3, MaxRpm=5
```

**Décision clé** : L'envoi SMTP reste hors périmètre agent — seule la génération du contenu de l'email est portée. L'envoi effectif est une opération mécanique qui sera déclenchée par le système appelant après réception du résultat de la Crew. Si l'envoi doit être intégré, l'outil intégré `email_send` le fait, limité aux destinataires qu'un opérateur autorise — ou `email_draft` laisse chaque message dans les brouillons pour qu'un humain l'envoie (voir [Outils e-mail](./email.md)).

## Étape 3 — Identification des outils

| Agent | Outil nécessaire | Existant ? | Action |
|-------|-----------------|------------|--------|
| Order Collector | `RelationalDatabaseTool` (PostgreSQL) | Oui — `Orkeon.Tools.Data` | Réutiliser |
| Inventory Checker | `HttpApiTool` | Oui — `Orkeon.Tools.Web` | Réutiliser |
| Fraud Analyst | `RelationalDatabaseTool` | Oui — `Orkeon.Tools.Data` | Réutiliser |
| Fraud Analyst | `JsonTool` | Oui — `Orkeon.Tools.Data` | Réutiliser |
| Pricing Specialist | `CsvReaderTool` | Oui — `Orkeon.Tools.Data` | Réutiliser |
| Pricing Specialist | `SecureCodeInterpreterTool` | Oui — `Orkeon.Infrastructure` | Réutiliser (enregistrer sous `IBaseTool`) |
| Notification Writer | `FileWriteTool` | Oui — `Orkeon.Tools.FileSystem` | Réutiliser |

Tous les outils nécessaires existent déjà. Aucun outil custom n'est requis pour ce portage — seulement une ligne d'enregistrement pour `code_interpreter`.

## Étape 4 — Tasks et orchestration

### ProcessType choisi : `Sequential`

**Justification** : Le flux de traitement de commande est intrinsèquement séquentiel — chaque étape dépend du résultat de la précédente (on ne peut pas calculer le prix sans vérifier le stock, ni notifier sans connaître le prix).

### Définition des agents (approche Fluent Builder)

Le builder prend des instances d'outils ; les suites d'outils enregistrées à l'Étape 4c les fournissent.

```csharp
// Les outils par leur nom YAML, depuis le registre adossé à la DI de l'Étape 4c
var registry = host.Services.GetRequiredService<IToolRegistry>();
async Task<ITool> Tool(string name) =>
    await registry.GetToolByNameAsync(name) as ITool
    ?? throw new InvalidOperationException($"Tool '{name}' is not registered.");

var orderCollector = new AgentBuilder()
    .Role("Order Data Collector")
    .Goal("Retrieve and structure new order data from the database")
    .Backstory("Experienced data engineer specialized in order management systems")
    .WithTool(await Tool("relational_database_query"))
    .MaxIterations(5)
    .MaxRpm(10)
    .AllowDelegation(false)
    .Build();

// inventoryChecker, fraudAnalyst, pricingSpecialist et notificationWriter
// suivent le même modèle avec les outils et contraintes de l'Étape 2.
```

### Définition des tasks (approche Fluent Builder)

```csharp
// Task 1 — Collecte des données commande
var collectTask = new CrewTaskBuilder()
    .Description("Query the orders database for pending orders with status 'NEW'. " +
                 "For each order, retrieve: order_id, customer_id, product_ids, " +
                 "quantities, and order_date. Return structured JSON.")
    .ExpectedOutput("JSON array of pending orders with all fields populated")
    .Priority(TaskPriority.High)
    .AssignTo(orderCollector)
    .Build();

// Task 2 — Vérification de stock
var stockTask = new CrewTaskBuilder()
    .Description("For each order from the previous task, call the inventory API " +
                 "at https://api.internal/catalog/v1/stock to verify product availability. " +
                 "Flag any out-of-stock items. Return the order list with availability status.")
    .ExpectedOutput("JSON array of orders enriched with stock availability per product")
    .Priority(TaskPriority.High)
    .DependsOn(collectTask)  // Accepte CrewTask ou TaskId
    .AssignTo(inventoryChecker)
    .Build();

// Task 3 — Analyse fraude
var fraudTask = new CrewTaskBuilder()
    .Description("For each order with available stock, analyze fraud risk. " +
                 "Query customer purchase history from the database. " +
                 "Evaluate: order amount vs. average, shipping address changes, " +
                 "payment method risk. Produce a risk score (0-100) with justification.")
    .ExpectedOutput("JSON array of orders with risk_score (0-100) and risk_justification")
    .Priority(TaskPriority.High)
    .DependsOn(stockTask)
    .HumanInput(true)  // Revue manuelle si score > 80
    .AssignTo(fraudAnalyst)
    .Build();

// Task 4 — Calcul prix
var pricingTask = new CrewTaskBuilder()
    .Description("For each approved order (risk_score <= 80 or manually approved), " +
                 "read the promotions CSV file at /data/promotions.csv. " +
                 "Apply applicable promotions, compute HT, TVA (20%), and TTC. " +
                 "Use the code interpreter for precise calculations.")
    .ExpectedOutput("JSON array of orders with price_ht, tva_amount, price_ttc, applied_promotions")
    .Priority(TaskPriority.Normal)
    .DependsOn(fraudTask)
    .AssignTo(pricingSpecialist)
    .Build();

// Task 5 — Génération notification
var notifTask = new CrewTaskBuilder()
    .Description("For each priced order, generate a personalized confirmation email " +
                 "in French. Include: order summary, items with prices, total TTC, " +
                 "estimated delivery date. Write output to /output/confirmations/.")
    .ExpectedOutput("One confirmation file per order in /output/confirmations/{order_id}.html")
    .Priority(TaskPriority.Normal)
    .DependsOn(pricingTask)
    // Un fichier par commande : l'agent écrit chacun avec file_write
    // (un résultat en un seul fichier utiliserait plutôt un bloc YAML deliverable:)
    .AssignTo(notificationWriter)
    .Build();
```

### Construction de la Crew (approche Fluent Builder)

```csharp
var orderProcessingCrew = new CrewBuilder()
    .Goal("Process pending e-commerce orders: verify stock, detect fraud, " +
          "calculate pricing, and generate confirmation emails")
    .Sequential()
    .WithAgent(orderCollector)
    .WithAgent(inventoryChecker)
    .WithAgent(fraudAnalyst)
    .WithAgent(pricingSpecialist)
    .WithAgent(notificationWriter)
    .WithTask(collectTask)
    .WithTask(stockTask)
    .WithTask(fraudTask)
    .WithTask(pricingTask)
    .WithTask(notifTask)
    .Verbose(true)
    .EnableMemory(true)
    .Planning(true)
    .Language("fr")
    .Build();
```

Une crew construite au builder s'exécute une fois qu'elle, ses agents et ses tasks sont ajoutés aux repositories (`IAgentRepository`, `ITaskRepository`, `ICrewRepository`) — voir [Bootstrap et exécution](../getting-started/bootstrap.md). La version YAML ci-dessous saute cette étape : `ICrewFactory` stocke ce qu'il construit.

## Étape 4b — Exécution complète : de la config au résultat

### Version YAML complète

La crew peut aussi être définie entièrement en YAML et chargée dynamiquement. Ceci facilite la maintenance et l'évolution sans recompiler le code.

**Fichier : `config.yaml`**

```yaml
name: "order-processing-crew"
goal: "Process pending e-commerce orders: verify stock, detect fraud, calculate pricing, and generate confirmation emails"
process: "sequential"
verbose: true
memory: true
planning: true

agents:
  order_collector:
    role: "Order Data Collector"
    goal: "Retrieve and structure new order data from the database"
    backstory: |
      Experienced data engineer specialized in order management systems.
      Expert at writing efficient SQL queries and structuring data for downstream processing.
      You ensure all order data is complete, correctly formatted, and ready for analysis.
    tools:
      - "relational_database_query"
    maxIter: 5
    maxRpm: 10
    allowDelegation: false

  inventory_checker:
    role: "Inventory Verification Specialist"
    goal: "Verify product availability and flag out-of-stock items"
    backstory: |
      Supply chain analyst with deep knowledge of inventory systems.
      Methodical approach to stock verification across multiple warehouses.
      You provide accurate, real-time inventory status to prevent overselling.
    tools:
      - "http_api"
    maxIter: 10
    maxRpm: 20
    allowDelegation: false

  fraud_analyst:
    role: "Fraud Detection Analyst"
    goal: "Analyze order risk based on customer history and transaction patterns"
    backstory: |
      Senior fraud prevention specialist with expertise in e-commerce transaction patterns.
      Combines statistical analysis with behavioral indicators to produce accurate risk assessments.
      You flag suspicious orders for manual review while allowing legitimate transactions to proceed.
    tools:
      - "relational_database_query"
      - "json_tool"
    maxIter: 10
    maxRpm: 10
    allowDelegation: false

  pricing_specialist:
    role: "Pricing Calculation Specialist"
    goal: "Apply promotions and compute final pricing (HT, TVA, TTC)"
    backstory: |
      Pricing analyst expert in French tax regulations and promotional strategies.
      Ensures precise calculations with full audit trail for compliance.
      You deliver accurate pricing that respects all promotional rules and tax obligations.
    tools:
      - "csv_reader"
      - "code_interpreter"
    maxIter: 5
    maxRpm: 10
    allowDelegation: false

  notification_writer:
    role: "Customer Communication Specialist"
    goal: "Generate personalized order confirmation content"
    backstory: |
      Customer experience writer skilled in e-commerce communication.
      Creates warm, professional emails that reinforce brand trust.
      You ensure every customer receives clear, detailed, and reassuring order confirmation.
    tools:
      - "file_write"
    maxIter: 3
    maxRpm: 5
    allowDelegation: false

tasks:
  collect_orders:
    description: |
      Query the orders database for pending orders with status 'NEW'.
      For each order, retrieve: order_id, customer_id, product_ids,
      quantities, and order_date. Return structured JSON.
    expectedOutput: "JSON array of pending orders with all fields populated"
    agent: "order_collector"

  check_inventory:
    description: |
      For each order from the previous task, call the inventory API
      at https://api.internal/catalog/v1/stock to verify product availability.
      Flag any out-of-stock items. Return the order list with availability status.
    expectedOutput: "JSON array of orders enriched with stock availability per product"
    agent: "inventory_checker"
    dependencies:
      - "collect_orders"

  analyze_fraud:
    description: |
      For each order with available stock, analyze fraud risk.
      Query customer purchase history from the database.
      Evaluate: order amount vs. average, shipping address changes,
      payment method risk. Produce a risk score (0-100) with justification.
    expectedOutput: "JSON array of orders with risk_score (0-100) and risk_justification"
    agent: "fraud_analyst"
    dependencies:
      - "check_inventory"
    humanInput: true

  calculate_pricing:
    description: |
      For each approved order (risk_score <= 80 or manually approved),
      read the promotions CSV file at /data/promotions.csv.
      Apply applicable promotions, compute HT, TVA (20%), and TTC.
      Use the code interpreter for precise calculations.
    expectedOutput: "JSON array of orders with price_ht, tva_amount, price_ttc, applied_promotions"
    agent: "pricing_specialist"
    dependencies:
      - "analyze_fraud"

  generate_notifications:
    description: |
      For each priced order, generate a personalized confirmation email
      in French. Include: order summary, items with prices, total TTC,
      estimated delivery date. Write output to /output/confirmations/.
    expectedOutput: "One confirmation file per order in /output/confirmations/{order_id}.html"
    agent: "notification_writer"
    dependencies:
      - "calculate_pricing"
```

### Avantages de l'approche YAML

- **Séparation responsabilités** : la configuration métier est séparée du code C#
- **Maintenance simplifiée** : modifier un backstory ou une description ne demande pas de recompilation
- **Déploiement flexible** : plusieurs variantes de crew (ex. production, test, debug) sans dupliquer le code
- **Onboarding** : les non-développeurs (PM, Product Owners) peuvent lire et comprendre la crew

## Étape 4c — Bootstrap de l'application

### Configuration

**Fichier : `Program.cs`**

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
using Orkeon.Infrastructure.Sandbox;
using Orkeon.Tools.Code.DependencyInjection;
using Orkeon.Tools.Data.DependencyInjection;
using Orkeon.Tools.FileSystem.DependencyInjection;
using Orkeon.Tools.Web.DependencyInjection;

var host = Host.CreateDefaultBuilder(args)
    // ORKEON_Llm__ApiKey dans l'environnement → Llm:ApiKey, comme avec les runners
    .ConfigureAppConfiguration(config => config.AddEnvironmentVariables("ORKEON_"))
    .ConfigureServices((context, services) =>
    {
        services.AddOrkeonApplication();
        // L'overload avec IConfiguration câble aussi télémétrie, MCP et recherche vectorielle,
        // et les stores dont la section existe (Orkeon:ChromaDb, Orkeon:Pinecone). Le RAG reste opt-in.
        services.AddOrkeonInfrastructure(context.Configuration);

        // Le système de fichiers virtuel : /crews, /data et /output (Orkeon:FileSystem:Mounts)
        services.AddOrkeonFileSystem(context.Configuration);

        services.AddOrkeonFileSystemTools();   // file_write
        services.AddOrkeonDataTools();         // relational_database_query, json_tool, csv_reader
        services.AddOrkeonWebTools();          // http_api
        services.AddOrkeonCodeTools();

        // code_interpreter n'est enregistré que comme type concret : l'exposer par son nom
        services.AddSingleton<IBaseTool>(sp => sp.GetRequiredService<SecureCodeInterpreterTool>());

        // Les noms d'outils YAML se résolvent contre les enregistrements IBaseTool ci-dessus
        services.AddSingleton<IToolRegistry, ServiceProviderToolRegistry>();

        // humanInput: true sur analyze_fraud — la file de revue de l'équipe fraude répond
        // (FraudReviewQueueProvider est à vous : il implémente IHumanInputProvider)
        services.AddOrkeonHumanInput<FraudReviewQueueProvider>();

        // Le LLM : un hôte construit à la main ne lit aucune section Llm de lui-même
        var llm = LlmConfig.Create(
            context.Configuration["Llm:Model"] ?? "gpt-5.6-sol",
            context.Configuration["Llm:ApiKey"]) with
        {
            Temperature = context.Configuration.GetValue("Llm:Temperature", 0.7)
        };
        services.AddOrkeonLlmProvider(
            sp => new OpenAIProvider(llm,
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ILogger<OpenAIProvider>>()),
            llm);

        services.AddScoped<OrderProcessingService>();
    })
    .Build();

await host.StartAsync();
```

### Chargement et exécution de la crew

**Fichier : `OrderProcessingService.cs`**

```csharp
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.FileSystem;

public class OrderProcessingService
{
    private readonly ICrewFactory _crewFactory;
    private readonly ICrewOrchestrationService _orchestrator;
    private readonly IFileSystemService _fileSystem;
    private readonly ILogger<OrderProcessingService> _logger;

    public OrderProcessingService(
        ICrewFactory crewFactory,
        ICrewOrchestrationService orchestrator,
        IFileSystemService fileSystem,
        ILogger<OrderProcessingService> logger)
    {
        _crewFactory = crewFactory;
        _orchestrator = orchestrator;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task ProcessOrdersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Charger la crew depuis le fichier YAML (un chemin virtuel, sous le montage /crews)
            _logger.LogInformation("Loading order processing crew from config.yaml");
            var crew = await _crewFactory.CreateFromFileAsync(
                "/crews/order-processing/config.yaml",
                cancellationToken);

            // 2. Préparer l'entrée
            var input = CrewInput.Empty(
                "Process all pending orders from today with fraud analysis and pricing");

            // 3. Exécuter la crew
            _logger.LogInformation("Starting crew execution: {CrewId}", crew.Id);
            var output = await _orchestrator.KickoffAsync(crew.Id, input, cancellationToken);

            // 4. Exploiter les résultats — KickoffAsync ne lève jamais d'exception : un run en
            //    échec revient avec Succeeded = false et sa raison dans Error
            _logger.LogInformation("Crew execution completed in {Duration}ms",
                output.Duration.TotalMilliseconds);
            if (!output.Succeeded)
            {
                _logger.LogError("Crew failed: {Error}", output.Error);
                return;
            }
            _logger.LogInformation("Final output:\n{Output}", output.FinalOutput);

            // 5. Traiter les sorties de chaque task
            foreach (var taskOutput in output.TaskOutputs)
            {
                _logger.LogInformation(
                    "Task {TaskId} - Success: {Success}, Duration: {ExecutionTime}ms",
                    taskOutput.TaskId,
                    taskOutput.Success,
                    taskOutput.ExecutionTime.TotalMilliseconds);

                if (!taskOutput.Success)
                {
                    _logger.LogError("Task failed: {Output}", taskOutput.RawOutput);
                }
            }

            // 6. Post-traitement : envoi des notifications générées
            await SendConfirmationEmailsAsync(output, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during order processing");
            throw;
        }
    }

    private async Task SendConfirmationEmailsAsync(CrewOutput output, CancellationToken cancellationToken)
    {
        // Lire les fichiers de confirmation écrits par notification_writer. /output/confirmations
        // est un chemin virtuel : le lire via le VFS, comme l'agent l'a écrit — sur disque, il se
        // trouve sous le dossier vers lequel pointe le montage /output.
        const string confirmationDir = "/output/confirmations";
        if (!await _fileSystem.ExistsAsync(confirmationDir, cancellationToken))
        {
            _logger.LogWarning("No confirmation directory found");
            return;
        }

        var htmlFiles = new List<string>();
        await foreach (var entry in _fileSystem.EnumerateFilesAsync(confirmationDir, null, cancellationToken))
        {
            if (entry.Kind == VirtualEntryKind.File
                && entry.VirtualPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                htmlFiles.Add(entry.VirtualPath);
        }
        _logger.LogInformation("Sending {Count} confirmation emails", htmlFiles.Count);

        foreach (var file in htmlFiles)
        {
            try
            {
                var orderId = Path.GetFileNameWithoutExtension(file);
                var htmlContent = await _fileSystem.TryReadAllTextAsync(file, cancellationToken);

                // Appeler votre service SMTP (hors Orkeon)
                // await _emailService.SendAsync(
                //     recipient: customer.Email,
                //     subject: $"Confirmation de commande {orderId}",
                //     htmlBody: htmlContent,
                //     cancellationToken: cancellationToken);

                _logger.LogInformation("Confirmation email sent for order {OrderId}", orderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send confirmation email for {File}", file);
            }
        }
    }
}
```

### Intégration dans une application existante

**Exemple : déclenchement par un événement RabbitMQ**

```csharp
public class OrderProcessingMessageConsumer : IMessageHandler
{
    private readonly OrderProcessingService _orderProcessing;
    private readonly ILogger<OrderProcessingMessageConsumer> _logger;

    public async Task HandleAsync(IMessage message, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received order processing event");

        // Les messages RabbitMQ déclenchent simplement l'appel à la crew
        await _orderProcessing.ProcessOrdersAsync(cancellationToken);
    }
}
```

### Configuration d'exécution (`appsettings.json`)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "Llm": {
    "Model": "gpt-5.6-sol",
    "Temperature": 0.3
    // La clé d'API n'est JAMAIS stockée ici — elle vient de l'environnement sous
    // ORKEON_Llm__ApiKey (OPENAI_API_KEY n'est pas lue)
  },
  "Orkeon": {
    "FileSystem": {
      "Mounts": [
        "/opt/orderflow/crews:/crews:ro",
        "/var/lib/orderflow/data:/data:ro",
        "/var/lib/orderflow/output:/output:rw"
      ]
    }
  },
  "PathSecurity": {
    // Les dossiers montés hors du répertoire de travail doivent être autorisés explicitement
    "AdditionalAllowedDirectories": [
      "/opt/orderflow/crews",
      "/var/lib/orderflow/data",
      "/var/lib/orderflow/output"
    ]
  },
  "Memory": {
    // Lu par la factory de providers mémoire (absent → in-memory)
    "Provider": "Redis",
    "ConnectionString": "localhost:6379"
  }
}
```

Le montage `/data` contient `promotions.csv`, lu par `csv_reader` ; `/output` reçoit les confirmations que produit `file_write`. Voir [Configuration](../reference/configuration.md) pour toutes les clés.

### Résultat d'exécution attendu

Lors de l'exécution, les logs ressemblent à ceci (illustratif — les lignes exactes dépendent de votre configuration de logging et de ce que répondent les agents) :

```
[INFO] Loading order processing crew from config.yaml
[INFO] Starting crew execution: 01JQ7ZK3M5N6P7Q8R9S0T1V2W3
[INFO] Agent order_collector executing task collect_orders
[INFO] Agent inventory_checker executing task check_inventory
[INFO] Agent fraud_analyst executing task analyze_fraud
[WARN] Task analyze_fraud flagged 3 orders for manual review (risk > 80)
[INFO] Waiting for human input on flagged orders...
[INFO] Human input received: 2 orders approved, 1 order rejected
[INFO] Agent pricing_specialist executing task calculate_pricing
[INFO] Agent notification_writer executing task generate_notifications
[INFO] Crew execution completed in 45230ms
[INFO] Sending 47 confirmation emails
[INFO] Confirmation email sent for order ORD-2025-0001
[INFO] Confirmation email sent for order ORD-2025-0002
...
```

## Étape 5 — Plan de portage final

| Composant source | Responsabilité | Agent cible | Outil(s) requis | Statut | Effort |
|-----------------|----------------|-------------|-----------------|--------|--------|
| `OrderIngestionService` | Lecture commandes BDD | Order Collector | `RelationalDatabaseTool` | Prêt | S |
| `InventoryCheckService` | Vérification stock API | Inventory Checker | `HttpApiTool` | Prêt | S |
| `FraudDetectionService` | Analyse risque fraude | Fraud Analyst | `RelationalDatabaseTool`, `JsonTool` | Prêt | S |
| `PricingService` | Calcul prix + promos | Pricing Specialist | `CsvReaderTool`, `SecureCodeInterpreterTool` | Prêt | S |
| `NotificationService` (contenu) | Génération email | Notification Writer | `FileWriteTool` | Prêt | S |
| `NotificationService` (envoi) | Envoi SMTP | — | Non porté (hors périmètre agent) | — | — |
| Consommation RabbitMQ | Déclenchement pipeline | — | Non porté (infrastructure) | — | — |

### Effort total estimé : **S-M** (1-2 jours)

Le portage est principalement un travail de configuration (agents, tasks, prompts) puisque tous les outils nécessaires existent. L'effort principal réside dans le prompt engineering des backstories et descriptions de tasks pour obtenir des résultats de qualité.

### Éléments restant hors périmètre agent

- Consommation de la file RabbitMQ (déclencheur externe → appelle `KickoffAsync`)
- Envoi SMTP (action post-crew → déclenché par le système appelant)
- Monitoring et alerting opérationnel (utiliser les callbacks `IStepCallback` / `ITaskCallback` pour intégrer)

### Gains attendus du portage

- **Flexibilité** : les règles de fraude et de pricing peuvent évoluer sans modifier le code, via les prompts
- **Traçabilité** : chaque décision d'agent est documentée avec justification textuelle (vs. score brut)
- **Extensibilité** : ajouter une étape (ex. : vérification adresse) = ajouter un agent + une task, sans modifier le code existant
- **Intervention humaine** : la revue manuelle fraude est nativement intégrée via `HumanInput = true`
