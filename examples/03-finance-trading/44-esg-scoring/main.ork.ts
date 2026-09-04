/// <reference orkeon-script="1.0" />
// orkeon-example: {"process":"parallel","agents":5,"tasks":5,"tools":["http_api","pdf_reader","csv_reader","json_tool","file_write","alternative_data","dashboard_metrics"]}
//
// 44. Analyse ESG Scoring Reproductible — each ESG dimension is scored
// independently by a specialized agent, then consolidated via EvaluationSuite.
// Source: project/marketing/content-strategy/101-USE-CASES.md #44
// Built-in tools are referenced by name; the trading tools come as TypeScript
// instances from the shared _tools module (EX-01).

import { pickTools } from "../_tools/index.ts";

const esgCollector = agentBuilder()
    .name("esg_collector")
    .role("ESG Data Collector")
    .goal("Gather ESG data from multiple sources including company reports, databases, and news")
    .backstory(`ESG data specialist who collects information from sustainability reports,
regulatory filings, ESG rating agencies, news sources, and NGO databases.
Normalizes heterogeneous data into a structured assessment framework.`)
    .tools(["http_api", "pdf_reader", "csv_reader", "json_tool"])
    .withAutonomousTools(pickTools("alternative_data"))
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const envAnalyst = agentBuilder()
    .name("env_analyst")
    .role("Environmental Analyst")
    .goal("Score the Environmental dimension: emissions, resource use, biodiversity, and climate strategy")
    .backstory(`Environmental scientist specializing in corporate sustainability assessment.
Evaluates carbon footprint (Scope 1/2/3), resource efficiency, waste management,
biodiversity impact, and climate transition strategy against TCFD framework.`)
    .tools(["csv_reader", "json_tool"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const socialAnalyst = agentBuilder()
    .name("social_analyst")
    .role("Social Impact Analyst")
    .goal("Score the Social dimension: labor practices, diversity, community impact, and supply chain")
    .backstory(`Social impact specialist evaluating labor practices, workplace safety,
diversity and inclusion metrics, community engagement, and supply chain
social standards. Uses ILO conventions and UN Guiding Principles as framework.`)
    .tools(["csv_reader", "json_tool"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const governanceAnalyst = agentBuilder()
    .name("governance_analyst")
    .role("Governance Analyst")
    .goal("Score the Governance dimension: board structure, ethics, transparency, and risk management")
    .backstory(`Corporate governance expert evaluating board independence, executive compensation,
anti-corruption policies, shareholder rights, audit quality, and risk management
frameworks. Benchmarks against OECD Principles and local governance codes.`)
    .tools(["csv_reader", "json_tool"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const scoreIntegrator = agentBuilder()
    .name("score_integrator")
    .role("ESG Score Integrator")
    .goal("Integrate dimension scores into a composite ESG rating with normalized methodology")
    .backstory(`ESG methodology expert who integrates individual E, S, and G scores into
a composite rating. Applies sector-specific materiality weights,
normalizes scores for cross-company comparability, and produces
reproducible ratings with full methodology documentation.`)
    .tools(["json_tool", "csv_reader", "file_write"])
    .withAutonomousTools(pickTools("dashboard_metrics"))
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const collectEsgData = taskBuilder()
    .name("collect_esg_data")
    .agent(esgCollector)
    .description("Collect ESG data from company sustainability reports, regulatory filings, rating agencies (MSCI, Sustainalytics), and news. Structure into assessment-ready format per dimension.")
    .expectedOutput("JSON structured ESG data package with Environmental, Social, and Governance data points, sources, and data quality indicators")
    .build();

const scoreEnvironmental = taskBuilder()
    .name("score_environmental")
    .agent(envAnalyst)
    .description("Score the Environmental dimension on a 0-100 scale. Evaluate: GHG emissions (Scope 1/2/3), energy efficiency, water management, waste reduction, biodiversity, and climate transition plan.")
    .expectedOutput("JSON Environmental score with sub-scores per category, evidence references, and benchmarking against sector peers")
    .withContext(collectEsgData)
    .asyncExecution(true)
    .build();

const scoreSocial = taskBuilder()
    .name("score_social")
    .agent(socialAnalyst)
    .description("Score the Social dimension on a 0-100 scale. Evaluate: labor standards, workplace safety, diversity metrics, community engagement, product safety, and supply chain social audit.")
    .expectedOutput("JSON Social score with sub-scores per category, evidence references, and benchmarking against sector peers")
    .withContext(collectEsgData)
    .asyncExecution(true)
    .build();

const scoreGovernance = taskBuilder()
    .name("score_governance")
    .agent(governanceAnalyst)
    .description("Score the Governance dimension on a 0-100 scale. Evaluate: board independence, executive compensation alignment, anti-corruption, shareholder rights, audit quality, and risk management.")
    .expectedOutput("JSON Governance score with sub-scores per category, evidence references, and benchmarking against sector peers")
    .withContext(collectEsgData)
    .asyncExecution(true)
    .build();

const integrateScores = taskBuilder()
    .name("integrate_scores")
    .agent(scoreIntegrator)
    .description("Integrate E, S, G dimension scores into a composite ESG rating. Apply sector-specific materiality weights. Normalize for cross-company comparison. Document full methodology for reproducibility.")
    .expectedOutput("Composite ESG report with final rating (AAA to CCC), dimension scores, materiality weights, peer comparison, and methodology documentation")
    .withContext(scoreEnvironmental)
    .withContext(scoreSocial)
    .withContext(scoreGovernance)
    .build();

const crew = crewBuilder()
    .name("esg-scoring")
    .goal("Produce reproducible ESG scores through independent evaluation of Environmental, Social, and Governance dimensions")
    .process("parallel")
    .memory(true)
    .verbose(true)
    .withAgents([esgCollector, envAnalyst, socialAnalyst, governanceAnalyst, scoreIntegrator])
    .withTasks([collectEsgData, scoreEnvironmental, scoreSocial, scoreGovernance, integrateScores])
    .build();

(globalThis as any).crew = crew;
