/// <reference orkeon-script="1.0" />
// orkeon-example: {"process":"parallel","agents":5,"tasks":5,"tools":["http_api","pdf_reader","json_tool","csv_reader","file_write","regulatory_reporting","audit_trail"]}
//
// 43. Multi-jurisdiction tax optimization — jurisdiction specialists (US, EU,
// APAC) analyze tax implications in parallel, a consolidator synthesizes the
// optimal global strategy with scenario comparison, and a senior advisor gives
// final approval with human validation. Built-in tools are referenced by name;
// the governance tools come as TypeScript instances from the shared _tools
// module (EX-01).
//
// TODO: feature planifiee — IConfigurationVersioning, IConfigurationDiffService

import { pickTools } from "../_tools/index.ts";

const jurisdictionUs = agentBuilder()
    .name("jurisdiction_us")
    .role("US Tax Specialist")
    .goal("Analyze US federal and state tax implications and optimize structure")
    .backstory(`US tax attorney specializing in corporate tax, transfer pricing,
and international tax treaties. Analyzes implications under IRC,
state-specific rules, and bilateral tax treaties.`)
    .tools(["http_api", "pdf_reader", "json_tool"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const jurisdictionEu = agentBuilder()
    .name("jurisdiction_eu")
    .role("EU Tax Specialist")
    .goal("Analyze EU tax implications across member states and optimize structure")
    .backstory(`European tax advisor covering the Anti-Tax Avoidance Directives (ATAD I/II),
VAT regulations, and country-specific corporate tax regimes.
Specializes in holding structures and IP box regimes.`)
    .tools(["http_api", "pdf_reader", "json_tool"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const jurisdictionApac = agentBuilder()
    .name("jurisdiction_apac")
    .role("APAC Tax Specialist")
    .goal("Analyze Asia-Pacific tax implications and optimize regional structure")
    .backstory(`Asia-Pacific tax specialist covering Singapore, Hong Kong, Japan,
and Australia. Expert in regional incentives, withholding taxes,
and cross-border structuring in the APAC region.`)
    .tools(["http_api", "pdf_reader", "json_tool"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const consolidator = agentBuilder()
    .name("consolidator")
    .role("Tax Strategy Consolidator")
    .goal("Consolidate multi-jurisdiction analyses into optimal global tax strategy")
    .backstory(`International tax strategist who synthesizes jurisdiction-specific analyses
into a coherent global strategy. Compares scenarios using configuration diff,
optimizes effective global tax rate, and ensures transfer pricing compliance.`)
    .tools(["csv_reader", "json_tool", "file_write"])
    .withAutonomousTools(pickTools("regulatory_reporting"))
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const taxSenior = agentBuilder()
    .name("tax_senior")
    .role("Senior Tax Advisor")
    .goal("Review and approve the proposed tax optimization strategy")
    .backstory(`Partner-level tax advisor who reviews the consolidated strategy
for compliance risks, reputational considerations, and practical
implementation feasibility. Final human approval required.`)
    .tools(["json_tool"])
    .withAutonomousTools(pickTools("audit_trail"))
    .allowDelegation(false)
    .maxIterations(5)
    .verbose(true)
    .build();

const analyzeUs = taskBuilder()
    .name("analyze_us")
    .agent(jurisdictionUs)
    .description("Analyze US tax implications: federal corporate tax, state nexus, transfer pricing rules, tax treaty benefits, and available credits/incentives. Produce optimization recommendations.")
    .expectedOutput("JSON US tax analysis with effective rate, optimization opportunities, treaty benefits, and risk ratings")
    .asyncExecution(true)
    .build();

const analyzeEu = taskBuilder()
    .name("analyze_eu")
    .agent(jurisdictionEu)
    .description("Analyze EU tax implications: corporate rates per member state, ATAD compliance, VAT structure, IP box eligibility, and holding company optimization. Produce optimization recommendations.")
    .expectedOutput("JSON EU tax analysis with per-country rates, ATAD compliance status, VAT optimization, and structural recommendations")
    .asyncExecution(true)
    .build();

const analyzeApac = taskBuilder()
    .name("analyze_apac")
    .agent(jurisdictionApac)
    .description("Analyze APAC tax implications: regional incentives, withholding tax treaties, transfer pricing rules, and operational structuring. Produce optimization recommendations.")
    .expectedOutput("JSON APAC tax analysis with regional rates, incentive eligibility, treaty network analysis, and structural recommendations")
    .asyncExecution(true)
    .build();

const consolidateStrategy = taskBuilder()
    .name("consolidate_strategy")
    .agent(consolidator)
    .description("Consolidate multi-jurisdiction analyses into a global tax strategy. Compare scenarios, calculate effective global tax rate under each scenario, and recommend optimal structure with implementation roadmap.")
    .expectedOutput("Global tax optimization strategy with scenario comparison, recommended structure, effective rates, implementation steps, and risk assessment")
    .withContext(analyzeUs)
    .withContext(analyzeEu)
    .withContext(analyzeApac)
    .build();

const seniorReview = taskBuilder()
    .name("senior_review")
    .agent(taxSenior)
    .description("Review the proposed global tax strategy for compliance risks, reputational considerations, and implementation feasibility. Approve or request modifications.")
    .expectedOutput("Senior review with approval/modification decision, compliance assessment, and implementation sign-off")
    .withContext(consolidateStrategy)
    .humanInput(true)
    .build();

const crew = crewBuilder()
    .name("tax-optimization")
    .goal("Optimize tax strategy across multiple jurisdictions with parallel analysis, scenario comparison, and human validation")
    .process("parallel")
    .memory(true)
    .verbose(true)
    .withAgents([jurisdictionUs, jurisdictionEu, jurisdictionApac, consolidator, taxSenior])
    .withTasks([analyzeUs, analyzeEu, analyzeApac, consolidateStrategy, seniorReview])
    .build();

(globalThis as any).crew = crew;
