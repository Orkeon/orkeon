/// <reference orkeon-script="1.0" />
// orkeon-example: {"process":"sequential","agents":4,"tasks":4,"tools":["csv_reader","http_api","json_tool","relational_database_query","file_write","stress_testing","var_calculation","cvar_calculation","factor_exposure","correlation_analysis","regulatory_reporting","audit_trail"]}
//
// 45. Stress Testing Reglementaire Rejouable
// Use case: Scenarios de stress versionnes et rejouables avec conformite NIST complete
// TODO: feature planifiee — IConfigurationVersioning, IConfigurationRollbackService

import { pickTools } from "../_tools/index.ts";

const scenarioDesigner = agentBuilder()
    .name("scenario_designer")
    .role("Stress Scenario Designer")
    .goal("Design regulatory stress test scenarios based on Basel III/IV, CCAR, and EBA requirements")
    .backstory(`Regulatory stress testing expert who designs scenarios aligned with
Basel III/IV pillar 2, CCAR (Fed), and EBA guidelines. Creates adverse
and severely adverse scenarios with macroeconomic variable paths,
market shocks, and credit deterioration assumptions.`)
    .tools(["csv_reader", "http_api", "json_tool"])
    .withAutonomousTools(pickTools("stress_testing"))
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const modeler = agentBuilder()
    .name("modeler")
    .role("Stress Test Modeler")
    .goal("Run stress test models and calculate capital impact under each scenario")
    .backstory(`Quantitative modeler specializing in stress testing frameworks.
Applies PD/LGD/EAD models under stress, calculates capital adequacy ratios,
and projects P&L impact across business lines. Ensures model reproducibility
with version-controlled parameters.`)
    .tools(["csv_reader", "json_tool", "relational_database_query", "file_write"])
    .withAutonomousTools(pickTools("var_calculation", "cvar_calculation", "factor_exposure", "correlation_analysis"))
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const regulatoryReporter = agentBuilder()
    .name("regulatory_reporter")
    .role("Regulatory Report Writer")
    .goal("Produce regulatory-compliant stress test reports with full methodology documentation")
    .backstory(`Regulatory reporting specialist who formats stress test results into
submissions compliant with EBA templates, CCAR reporting formats,
and local regulatory requirements. Ensures complete methodology documentation.`)
    .tools(["json_tool", "file_write"])
    .withAutonomousTools(pickTools("regulatory_reporting", "audit_trail"))
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const complianceAgent = agentBuilder()
    .name("compliance_agent")
    .role("Compliance and Audit Agent")
    .goal("Ensure NIST compliance of the entire stress testing process with full audit trail")
    .backstory(`Internal audit specialist who verifies the stress testing process meets
NIST cybersecurity framework requirements. Validates data lineage,
model governance, access controls, and produces a complete audit trail
with timestamps and hash verification.`)
    .tools(["json_tool", "file_write"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const designScenarios = taskBuilder()
    .name("design_scenarios")
    .agent(scenarioDesigner)
    .description("Design stress test scenarios: baseline, adverse, and severely adverse. Define macroeconomic variable paths (GDP, unemployment, interest rates, equity markets), credit deterioration curves, and market shock magnitudes.")
    .expectedOutput("JSON scenario definitions with variable paths per scenario, shock magnitudes, time horizons, and regulatory alignment mapping")
    .build();

const runModels = taskBuilder()
    .name("run_models")
    .agent(modeler)
    .description("Execute stress test models under each scenario. Calculate projected losses, capital adequacy ratios (CET1, Tier 1, Total Capital), P&L impact, and RWA evolution over the stress horizon.")
    .expectedOutput("JSON model outputs with capital ratios per quarter per scenario, loss projections by portfolio, and capital shortfall analysis")
    .withContext(designScenarios)
    .build();

const writeRegulatoryReport = taskBuilder()
    .name("write_regulatory_report")
    .agent(regulatoryReporter)
    .description("Format stress test results into regulatory submission templates. Include methodology documentation, key assumptions, sensitivity analysis, and management actions. Ensure format compliance with target regulator.")
    .expectedOutput("Regulatory stress test report with executive summary, detailed results tables, methodology appendix, and management action plan")
    .withContext(runModels)
    .build();

const auditCompliance = taskBuilder()
    .name("audit_compliance")
    .agent(complianceAgent)
    .description("Perform NIST compliance audit of the entire process. Verify data lineage, model governance, scenario versioning, access controls, and result integrity. Produce signed audit trail.")
    .expectedOutput("NIST compliance audit report with controls assessment, data lineage verification, hash integrity checks, and overall compliance status")
    .withContext(writeRegulatoryReport)
    .build();

const crew = crewBuilder()
    .name("stress-testing")
    .goal("Execute regulatory stress tests with versioned replayable scenarios, NIST audit compliance, and encrypted data")
    .process("sequential")
    .memory(true)
    .verbose(true)
    .withAgents([scenarioDesigner, modeler, regulatoryReporter, complianceAgent])
    .withTasks([designScenarios, runModels, writeRegulatoryReport, auditCompliance])
    .build();

(globalThis as any).crew = crew;
