> 🇫🇷 [Version française](../fr/guides/porting-example.md)

> **See also**: [Methodology](./porting-methodology.md) · [Back to index](../INDEX.md)

# Porting plan example — Order processing pipeline

This example applies the methodology from the [Porting methodology](./porting-methodology.md) file to a fictional e-commerce order processing application.

## The source application

The "OrderFlow" application is a .NET service that processes orders for an e-commerce site. It is composed of five main services:

| Service | Responsibility |
|---------|---------------|
| `OrderIngestionService` | Reads new orders from a RabbitMQ queue and stores them in a PostgreSQL database |
| `InventoryCheckService` | Checks product availability in the catalog (internal REST API) |
| `FraudDetectionService` | Analyzes fraud risk by cross-referencing customer history and order amount |
| `PricingService` | Applies promotions, computes discounts and VAT (TVA) |
| `NotificationService` | Generates and sends a confirmation email with the order summary |

The sequential flow is: Ingestion → Stock check → Fraud detection → Price computation → Notification.

## Step 1 — Responsibility analysis

### Component inventory

| Component | Category | Inputs | Outputs | External dependencies |
|-----------|-----------|---------|---------|---------------------|
| `OrderIngestionService` | Collection | RabbitMQ messages | Order in DB | PostgreSQL, RabbitMQ |
| `InventoryCheckService` | Decision | Product IDs | Availability status | Internal Catalog API |
| `FraudDetectionService` | Analysis | Order + customer history | Risk score (0-100) | PostgreSQL (history) |
| `PricingService` | Analysis | Order + promo rules | Final amounts (HT, TVA, TTC) | Promotions CSV file |
| `NotificationService` | Production | Validated order + price | Confirmation HTML email | SMTP server |

### Identified human interactions

The `FraudDetectionService` flags orders with a score > 80 for manual review. This point will become a task with `HumanInput = true`.

## Step 2 — Mapping to agents

### Agent 1: Order Collector

```
Responsabilité source : OrderIngestionService
→ Rôle agent         : "Order Data Collector"
→ Objectif agent     : "Retrieve and structure new order data from the database"
→ Backstory          : "Experienced data engineer specialized in order management systems"
→ Outils nécessaires : RelationalDatabaseTool (PostgreSQL)
→ Contraintes        : MaxIterations=5, MaxRpm=10
```

**Key decision**: We do not port the RabbitMQ reading. The agent reads pending orders directly from the PostgreSQL database. Queue consumption stays outside the agent perimeter — it is a purely mechanical infrastructure operation (no LLM reasoning needed).

### Agent 2: Inventory Checker

```
Responsabilité source : InventoryCheckService
→ Rôle agent         : "Inventory Verification Specialist"
→ Objectif agent     : "Verify product availability and flag out-of-stock items"
→ Backstory          : "Supply chain analyst with deep knowledge of inventory systems"
→ Outils nécessaires : HttpApiTool (API Catalogue)
→ Contraintes        : MaxIterations=10, MaxRpm=20
```

### Agent 3: Fraud Analyst

```
Responsabilité source : FraudDetectionService
→ Rôle agent         : "Fraud Detection Analyst"
→ Objectif agent     : "Analyze order risk based on customer history and transaction patterns"
→ Backstory          : "Senior fraud prevention specialist with expertise in e-commerce transaction patterns"
→ Outils nécessaires : RelationalDatabaseTool (historique client), JsonTool (structuration résultat)
→ Contraintes        : MaxIterations=10, MaxRpm=10
```

**Key decision**: The LLM brings real value here — it can reason about complex fraud patterns beyond the static rules of the original service. The risk score is produced with a textual justification the original service did not provide.

### Agent 4: Pricing Specialist

```
Responsabilité source : PricingService
→ Rôle agent         : "Pricing Calculation Specialist"
→ Objectif agent     : "Apply promotions and compute final pricing (HT, TVA, TTC)"
→ Backstory          : "Pricing analyst expert in French tax regulations and promotional strategies"
→ Outils nécessaires : CsvReaderTool (fichier promotions), SecureCodeInterpreterTool (calculs)
→ Contraintes        : MaxIterations=5, MaxRpm=10
```

**Key decision**: Use `SecureCodeInterpreterTool` for the VAT (TVA) calculations rather than a custom tool. The LLM generates the C# calculation code which is executed in the sandbox — this gives the flexibility to handle complex promotion rules without hardcoding the logic.

### Agent 5: Notification Writer

```
Responsabilité source : NotificationService
→ Rôle agent         : "Customer Communication Specialist"
→ Objectif agent     : "Generate personalized order confirmation content"
→ Backstory          : "Customer experience writer skilled in e-commerce communication"
→ Outils nécessaires : FileWriteTool (génération du contenu)
→ Contraintes        : MaxIterations=3, MaxRpm=5
```

**Key decision**: SMTP sending stays outside the agent perimeter — only the generation of the email content is ported. The actual sending is a mechanical operation that will be triggered by the calling system after receiving the Crew's result. A custom `SmtpSendTool` could be created if the sending must be integrated.

## Step 3 — Tool identification

| Agent | Required tool | Exists? | Action |
|-------|-----------------|------------|--------|
| Order Collector | `RelationalDatabaseTool` (PostgreSQL) | Yes — `Orkeon.Tools.Data` | Reuse |
| Inventory Checker | `HttpApiTool` | Yes — `Orkeon.Tools.Web` | Reuse |
| Fraud Analyst | `RelationalDatabaseTool` | Yes — `Orkeon.Tools.Data` | Reuse |
| Fraud Analyst | `JsonTool` | Yes — `Orkeon.Tools.Data` | Reuse |
| Pricing Specialist | `CsvReaderTool` | Yes — `Orkeon.Tools.Data` | Reuse |
| Pricing Specialist | `SecureCodeInterpreterTool` | Yes — `Orkeon.Infrastructure` | Reuse |
| Notification Writer | `FileWriteTool` | Yes — `Orkeon.Tools.FileSystem` | Reuse |

All the required tools already exist. No custom tool is needed for this port.

## Step 4 — Tasks and orchestration

### Chosen ProcessType: `Sequential`

**Justification**: The order processing flow is intrinsically sequential — each step depends on the result of the previous one (you cannot compute the price without checking the stock, nor notify without knowing the price).

### Task definition (Fluent Builder approach)

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
    .OutputFile("/output/confirmations/")
    .AssignTo(notificationWriter)
    .Build();
```

### Crew construction (Fluent Builder approach)

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

## Step 4b — Full execution: from config to result

### Complete YAML version

The crew can also be defined entirely in YAML and loaded dynamically. This eases maintenance and evolution without recompiling the code.

**File: `config.yaml`**

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

### Advantages of the YAML approach

- **Separation of responsibilities**: the business configuration is separated from the C# code
- **Simplified maintenance**: modifying a backstory or a description does not require recompilation
- **Flexible deployment**: several crew variants (e.g. production, test, debug) without duplicating code
- **Onboarding**: non-developers (PMs, Product Owners) can read and understand the crew

## Step 4c — Application bootstrap

### Configuration

**File: `Program.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Application.DependencyInjection;
using Orkeon.Infrastructure.DependencyInjection;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Enregistrer les services Orkeon
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        services.AddOrkeonFileSystemTools();
        services.AddOrkeonDataTools();
        services.AddOrkeonWebTools();
        services.AddOrkeonCodeTools();

        // Configurer les providers LLM
        services.Configure<OpenAIOptions>(context.Configuration.GetSection("OpenAI"));
        services.Configure<OllamaOptions>(context.Configuration.GetSection("Ollama"));

        // Configurer la mémoire
        services.Configure<RedisMemoryOptions>(context.Configuration.GetSection("Memory:Redis"));
    })
    .Build();

// Attendre que l'hôte soit construit
await host.StartAsync();
```

### Loading and running the crew

**File: `OrderProcessingService.cs`**

```csharp
using Orkeon.Application.Services;
using Orkeon.Domain.Crews;
using Orkeon.Domain.Crews.Factories;

public class OrderProcessingService
{
    private readonly ICrewFactory _crewFactory;
    private readonly ICrewOrchestrationService _orchestrator;
    private readonly ILogger<OrderProcessingService> _logger;

    public OrderProcessingService(
        ICrewFactory crewFactory,
        ICrewOrchestrationService orchestrator,
        ILogger<OrderProcessingService> logger)
    {
        _crewFactory = crewFactory;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public async Task ProcessOrdersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Charger la crew depuis le fichier YAML
            _logger.LogInformation("Loading order processing crew from config.yaml");
            var crew = await _crewFactory.CreateFromFileAsync(
                "config/order-processing/config.yaml",
                cancellationToken);

            // 2. Préparer l'entrée
            var input = new CrewInput(
                initialContext: "Process all pending orders from today with fraud analysis and pricing");

            // 3. Exécuter la crew
            _logger.LogInformation("Starting crew execution: {CrewId}", crew.Id);
            var output = await _orchestrator.KickoffAsync(crew.Id, input, cancellationToken);

            // 4. Exploiter les résultats
            _logger.LogInformation("Crew execution completed in {Duration}ms",
                output.ExecutionTime.TotalMilliseconds);
            _logger.LogInformation("Final output:\n{Output}", output.Output);

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
        // Lire les fichiers de confirmation générés par notification_writer
        var confirmationDir = new DirectoryInfo("/output/confirmations/");
        if (!confirmationDir.Exists)
        {
            _logger.LogWarning("No confirmation directory found");
            return;
        }

        var htmlFiles = confirmationDir.GetFiles("*.html");
        _logger.LogInformation("Sending {Count} confirmation emails", htmlFiles.Length);

        foreach (var file in htmlFiles)
        {
            try
            {
                var orderId = Path.GetFileNameWithoutExtension(file.Name);
                var htmlContent = await File.ReadAllTextAsync(file.FullName, cancellationToken);

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
                _logger.LogError(ex, "Failed to send confirmation email for {File}", file.Name);
            }
        }
    }
}
```

### Integration into an existing application

**Example: triggered by a RabbitMQ event**

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

### Runtime configuration (`appsettings.json`)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "OpenAI": {
    "ApiKey": "${OPENAI_API_KEY}",
    "Model": "gpt-4o-mini",
    "Temperature": 0.3
  },
  "Memory": {
    "Redis": {
      "ConnectionString": "localhost:6379",
      "VectorDimension": 1536,
      "EncryptionKey": "${REDIS_ENCRYPTION_KEY}"
    }
  }
}
```

### Expected execution result

During execution, you will see in the logs:

```
[INFO] Loading order processing crew from config.yaml
[INFO] Starting crew execution: crew-order-processing-2025-04-05-143025
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

## Step 5 — Final porting plan

| Source component | Responsibility | Target agent | Required tool(s) | Status | Effort |
|-----------------|----------------|-------------|-----------------|--------|--------|
| `OrderIngestionService` | DB order reading | Order Collector | `RelationalDatabaseTool` | Ready | S |
| `InventoryCheckService` | API stock check | Inventory Checker | `HttpApiTool` | Ready | S |
| `FraudDetectionService` | Fraud risk analysis | Fraud Analyst | `RelationalDatabaseTool`, `JsonTool` | Ready | S |
| `PricingService` | Price + promo computation | Pricing Specialist | `CsvReaderTool`, `SecureCodeInterpreterTool` | Ready | S |
| `NotificationService` (content) | Email generation | Notification Writer | `FileWriteTool` | Ready | S |
| `NotificationService` (sending) | SMTP sending | — | Not ported (outside agent perimeter) | — | — |
| RabbitMQ consumption | Pipeline triggering | — | Not ported (infrastructure) | — | — |

### Total estimated effort: **S-M** (1-2 days)

The port is mainly configuration work (agents, tasks, prompts) since all the required tools exist. The main effort lies in the prompt engineering of the backstories and task descriptions to obtain quality results.

### Elements remaining outside the agent perimeter

- RabbitMQ queue consumption (external trigger → calls `KickoffAsync`)
- SMTP sending (post-crew action → triggered by the calling system)
- Operational monitoring and alerting (use the `IStepCallback` / `ITaskCallback` callbacks to integrate)

### Expected gains from the port

- **Flexibility**: fraud and pricing rules can evolve without modifying the code, via the prompts
- **Traceability**: each agent decision is documented with a textual justification (vs. a raw score)
- **Extensibility**: adding a step (e.g. address verification) = adding an agent + a task, without modifying the existing code
- **Human intervention**: the manual fraud review is natively integrated via `HumanInput = true`
