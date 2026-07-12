> 🇫🇷 [Version française](../fr/reference/examples-catalog.md)

> **See also**: [Back to index](../INDEX.md)

# Catalog of the 104 examples

The project provides 104 ready-to-use YAML configurations in the `examples/` directory, organized into 9 business categories:

| Category | Folder | Count | Focus |
|-----------|---------|--------|-------|
| **01 - Enterprise** | `01-Enterprise/` | 15 configs | CRM, ERP, supply chain, HR management, compliance |
| **02 - Science & Research** | `02-Science-Research/` | 15 configs | Computational biology, molecular dynamics, meta-analyses, open science |
| **03 - Finance & Trading** | `03-Finance-Trading/` | 15 configs | Arbitrage, risk management, portfolio optimization, fraud detection |
| **04 - Health & Wellness** | `04-Health-Wellness/` | 10 configs | Diagnostic support, clinical trials, personalized medicine |
| **05 - Education** | `05-Education/` | 10 configs | Course design, student assessment, tutoring, adaptive learning |
| **06 - Engineering & DevOps** | `06-Engineering-DevOps/` | 10 configs | CI/CD automation, infrastructure as code, code review, testing |
| **07 - Creative & Media** | `07-Creative-Media/` | 10 configs | Content generation, video scripting, design, music composition |
| **08 - IoT & Smart Systems** | `08-IoT-Smart-Systems/` | 10 configs | Monitoring, predictive maintenance, anomaly detection |
| **09 - Experimental** | `09-Experimental/` | 6 configs | Exploration, prototypes, advanced research, graph orchestration |

## Notable examples

**Example 1: Enterprise CRM Crew** (`01-Enterprise/crm-customer-analysis.yaml`)
- Process: Hierarchical (manager selects specialists)
- Agents: DataAnalyst, CustomerServiceSpecialist, BusinessStrategist
- Tools: file_read, http_api, database_query, web_scrape
- Memory: Redis (LongTerm + Episodic)
- Demonstrates: dynamic routing, multi-agent collaboration, memory persistence

**Example 2: Science Literature Meta-Analysis** (`02-Science-Research/meta-analysis-crew.yaml`)
- Process: Sequential (strict dependencies)
- Agents: PaperFetcher, BiasDetector, SynthesisWriter
- Tools: web_scrape, pdf_reader, json_parser, ask_question
- Memory: ChromaDB (Episodic for citations)
- Demonstrates: linear pipelines, document parsing, RAG integration

**Example 3: Financial Portfolio Optimization** (`03-Finance-Trading/portfolio-optimizer.yaml`)
- Process: Parallel (task independence)
- Agents: EquityAnalyst, BondSpecialist, CryptoExpert, RiskManager
- Tools: http_api, database_query, code_interpreter, secure_code_sandbox
- Memory: In-Memory (short context, high frequency)
- Demonstrates: parallel execution, financial calculations, code isolation

**Example 4: DevOps CI/CD Automation** (`06-Engineering-DevOps/cicd-orchestrator.yaml`)
- Process: Sequential (build → test → deploy)
- Agents: Builder, Tester, Deployer, Monitor
- Tools: code_reader, bash_executor, docker_manager, health_checker
- Memory: Pinecone (anomalies, historical patterns)
- Demonstrates: deployment pipelines, artifact management, monitoring

Each example includes:
- Complete YAML file ready to load
- Inline documentation (comments)
- Customizable configuration points (models, API keys)
- Use cases and architectural patterns

Loading an example:

```csharp
// crewFactory : ICrewFactory, orchestrator : ICrewOrchestrationService (via DI)
var crew = await crewFactory.CreateFromDirectoryAsync(
    "examples/03-Finance-Trading/portfolio-optimizer/", ct);
var result = await orchestrator.KickoffAsync(crew.Id, CrewInput.Empty(), ct);
```
