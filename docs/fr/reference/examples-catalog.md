> 🇬🇧 [English version](../../reference/examples-catalog.md)

> **Voir aussi** : [Retour à l'index](../INDEX.md)

# Catalogue des 104 exemples

Le projet fournit 104 configurations YAML prêtes à l'emploi dans le répertoire `examples/`, organisées en 9 catégories métier :

| Catégorie | Dossier | Nombre | Focus |
|-----------|---------|--------|-------|
| **01 - Enterprise** | `01-Enterprise/` | 15 configs | CRM, ERP, chaîne d'approvisionnement, gestion RH, compliance |
| **02 - Science & Research** | `02-Science-Research/` | 15 configs | Biologie computationnelle, dynamique moléculaire, méta-analyses, open science |
| **03 - Finance & Trading** | `03-Finance-Trading/` | 15 configs | Arbitrage, risk management, portfolio optimization, fraud detection |
| **04 - Health & Wellness** | `04-Health-Wellness/` | 10 configs | Diagnostic support, clinical trials, personalized medicine |
| **05 - Education** | `05-Education/` | 10 configs | Course design, student assessment, tutoring, adaptive learning |
| **06 - Engineering & DevOps** | `06-Engineering-DevOps/` | 10 configs | CI/CD automation, infrastructure as code, code review, testing |
| **07 - Creative & Media** | `07-Creative-Media/` | 10 configs | Content generation, video scripting, design, music composition |
| **08 - IoT & Smart Systems** | `08-IoT-Smart-Systems/` | 10 configs | Monitoring, predictive maintenance, anomaly detection |
| **09 - Experimental** | `09-Experimental/` | 6 configs | Exploration, prototypes, recherche avancée, graph orchestration |

## Exemples notables

**Example 1 : Enterprise CRM Crew** (`01-Enterprise/crm-customer-analysis.yaml`)
- Process : Hierarchical (manager sélectionne spécialistes)
- Agents : DataAnalyst, CustomerServiceSpecialist, BusinessStrategist
- Tools : file_read, http_api, database_query, web_scrape
- Memory : Redis (LongTerm + Episodic)
- Démontre : routing dynamique, multi-agent collaboration, persistance mémoire

**Example 2 : Science Literature Meta-Analysis** (`02-Science-Research/meta-analysis-crew.yaml`)
- Process : Sequential (dépendances strictes)
- Agents : PaperFetcher, BiasDetector, SynthesisWriter
- Tools : web_scrape, pdf_reader, json_parser, ask_question
- Memory : ChromaDB (Episodic pour citations)
- Démontre : pipelines linéaires, document parsing, RAG integration

**Example 3 : Financial Portfolio Optimization** (`03-Finance-Trading/portfolio-optimizer.yaml`)
- Process : Parallel (indépendance tâches)
- Agents : EquityAnalyst, BondSpecialist, CryptoExpert, RiskManager
- Tools : http_api, database_query, code_interpreter, secure_code_sandbox
- Memory : In-Memory (contexte court, haute fréquence)
- Démontre : exécution parallèle, calculations financières, isolation code

**Example 4 : DevOps CI/CD Automation** (`06-Engineering-DevOps/cicd-orchestrator.yaml`)
- Process : Sequential (build → test → deploy)
- Agents : Builder, Tester, Deployer, Monitor
- Tools : code_reader, bash_executor, docker_manager, health_checker
- Memory : Pinecone (anomalies, patterns historiques)
- Démontre : deployment pipelines, artifact management, monitoring

Chaque exemple inclut :
- Fichier YAML complet prêt à charger
- Documentation inline (commentaires)
- Points de configuration personnalisables (modèles, clés API)
- Cas d'usage et patterns architecturaux

Charger un exemple :

```csharp
var crew = await YamlCrewDefinitionLoader.LoadFromDirectoryAsync(
    "examples/03-Finance-Trading/portfolio-optimizer/"
);
var result = await orchestrator.KickoffAsync(crew, cancellationToken: ct);
```
