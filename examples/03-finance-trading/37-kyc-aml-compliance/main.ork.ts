/// <reference orkeon-script="1.0" />
// orkeon-example: {"process":"hierarchical","agents":4,"tasks":3,"tools":["http_api","pdf_reader","json_tool","compliance_check","regulatory_reporting","audit_trail"]}
//
// 37. KYC/AML compliance with triple-layer security — a hierarchical crew where
// a Compliance Manager coordinates identity verification, sanctions screening,
// and country risk assessment (encryption, NIST audit, prompt injection
// protection). Built-in tools are referenced by name; the governance tools come
// as TypeScript instances from the shared _tools module (EX-01).

import { pickTools } from "../_tools/index.ts";

const complianceManager = agentBuilder()
    .name("compliance_manager")
    .role("Compliance Manager")
    .goal("Coordinate KYC/AML verification workflow and ensure triple-layer security at every step")
    .backstory(`Chief Compliance Officer with 20 years experience in anti-money laundering
and know-your-customer regulations. Ensures every verification step meets
regulatory standards with full audit trail and data protection.`)
    .withAutonomousTools(pickTools("compliance_check", "regulatory_reporting"))
    .allowDelegation(true)
    .maxIterations(15)
    .verbose(true)
    .build();

const identityVerifier = agentBuilder()
    .name("identity_verifier")
    .role("Identity Verification Specialist")
    .goal("Verify client identity documents and perform identity authentication")
    .backstory(`Identity verification expert specializing in document authentication,
biometric matching, and cross-referencing identity databases.
Validates passports, national IDs, and proof of address against
authoritative sources.`)
    .tools(["http_api", "pdf_reader", "json_tool"])
    .withAutonomousTools(pickTools("audit_trail"))
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const sanctionsScreener = agentBuilder()
    .name("sanctions_screener")
    .role("Sanctions Screening Analyst")
    .goal("Screen clients against global sanctions lists, PEP databases, and adverse media")
    .backstory(`Sanctions compliance specialist who screens against OFAC, EU, UN,
and national sanctions lists. Also checks Politically Exposed Persons (PEP)
databases and adverse media for reputational risk indicators.`)
    .tools(["http_api", "json_tool"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const countryRiskAnalyst = agentBuilder()
    .name("country_risk_analyst")
    .role("Country Risk Analyst")
    .goal("Assess jurisdiction-specific AML risk factors and regulatory requirements")
    .backstory(`Geopolitical risk analyst covering FATF grey/black lists, Transparency International
CPI scores, and jurisdiction-specific AML requirements. Calculates composite
country risk score for transaction monitoring thresholds.`)
    .tools(["http_api", "json_tool"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const verifyIdentity = taskBuilder()
    .name("verify_identity")
    .agent(identityVerifier)
    .description("Perform identity verification: validate identity documents (passport, national ID), cross-reference against identity databases, and authenticate document integrity. All data stored encrypted.")
    .expectedOutput("JSON identity verification report with document validation results, database match scores, and overall identity confidence level")
    .build();

const screenSanctions = taskBuilder()
    .name("screen_sanctions")
    .agent(sanctionsScreener)
    .description("Screen the verified identity against global sanctions lists (OFAC, EU, UN), PEP databases, and adverse media sources. Record all screening results with timestamps for audit trail.")
    .expectedOutput("JSON sanctions screening report with match/no-match results per list, PEP status, adverse media findings, and composite screening score")
    .withContext(verifyIdentity)
    .build();

const assessCountryRisk = taskBuilder()
    .name("assess_country_risk")
    .agent(countryRiskAnalyst)
    .description("Evaluate AML risk based on client jurisdictions (residence, nationality, transaction origins). Calculate composite country risk score using FATF ratings, CPI scores, and bilateral treaty coverage.")
    .expectedOutput("JSON country risk assessment with per-jurisdiction scores, FATF status, CPI ranking, and overall AML risk rating")
    .withContext(verifyIdentity)
    .build();

const crew = crewBuilder()
    .name("kyc-aml-compliance")
    .goal("Perform KYC/AML compliance checks with triple-layer security: encryption, NIST audit, and prompt injection protection")
    .process("hierarchical")
    .manager(complianceManager)
    .memory(true)
    .verbose(true)
    .withAgents([complianceManager, identityVerifier, sanctionsScreener, countryRiskAnalyst])
    .withTasks([verifyIdentity, screenSanctions, assessCountryRisk])
    .build();

(globalThis as any).crew = crew;
