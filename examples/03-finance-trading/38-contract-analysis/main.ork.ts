/// <reference orkeon-script="1.0" />
// orkeon-example: {"process":"parallel","agents":3,"tasks":3,"tools":["pdf_reader","json_tool","csv_reader","file_write","regulatory_reporting","audit_trail"]}
//
// 38. Contract analysis with chunking — legal and financial reviews run in
// parallel (asyncExecution) over a chunked contract, then a Negotiation
// Strategist synthesizes both into alternative clauses. Built-in tools are
// referenced by name; the governance tools come as TypeScript instances from
// the shared _tools module (EX-01).

import { pickTools } from "../_tools/index.ts";

const legalAnalyst = agentBuilder()
    .name("legal_analyst")
    .role("Legal Analyst")
    .goal("Analyze contract clauses for legal risks, obligations, and compliance issues")
    .backstory(`Corporate lawyer with 15 years experience in contract review. Specializes in
identifying unfavorable clauses, hidden obligations, termination risks,
liability exposure, and regulatory compliance gaps. Uses ITextChunker
to process contracts exceeding context window limits.`)
    .tools(["pdf_reader", "json_tool"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const financialAnalyst = agentBuilder()
    .name("financial_analyst")
    .role("Financial Analyst")
    .goal("Evaluate financial terms, payment structures, and economic implications of contract")
    .backstory(`Financial analyst specializing in contract economics. Evaluates pricing models,
payment terms, penalties, escalation clauses, currency exposure,
and total cost of ownership. Identifies hidden costs and unfavorable financial terms.`)
    .tools(["pdf_reader", "json_tool", "csv_reader"])
    .withAutonomousTools(pickTools("regulatory_reporting", "audit_trail"))
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const negotiator = agentBuilder()
    .name("negotiator")
    .role("Negotiation Strategist")
    .goal("Propose alternative clauses and negotiation strategies based on legal and financial analysis")
    .backstory(`Experienced contract negotiator who synthesizes legal and financial findings
into actionable negotiation strategies. Proposes alternative clause wording,
identifies leverage points, and prioritizes negotiation items by impact.`)
    .tools(["json_tool", "file_write"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const analyzeLegal = taskBuilder()
    .name("analyze_legal")
    .agent(legalAnalyst)
    .description("Review the contract for legal risks. Use chunking to process large documents. Identify unfavorable clauses, hidden obligations, liability exposure, termination conditions, and compliance gaps. Rate each finding by severity.")
    .expectedOutput("JSON report with categorized legal findings, severity ratings (Critical/High/Medium/Low), affected clauses, and risk summary")
    .asyncExecution(true)
    .build();

const analyzeFinancial = taskBuilder()
    .name("analyze_financial")
    .agent(financialAnalyst)
    .description("Evaluate financial terms of the contract. Analyze pricing structure, payment terms, penalties, escalation mechanisms, currency exposure, and total cost projection over contract lifetime.")
    .expectedOutput("JSON financial analysis with cost breakdown, penalty exposure, escalation projections, and financial risk assessment")
    .asyncExecution(true)
    .build();

const proposeAlternatives = taskBuilder()
    .name("propose_alternatives")
    .agent(negotiator)
    .description("Based on legal and financial analyses, propose alternative clause wording for high-risk items. Develop negotiation strategy with prioritized items, leverage points, and fallback positions.")
    .expectedOutput("Negotiation playbook with alternative clause proposals, priority ranking, leverage analysis, and recommended negotiation sequence")
    .withContext(analyzeLegal)
    .withContext(analyzeFinancial)
    .build();

const crew = crewBuilder()
    .name("contract-analysis")
    .goal("Analyze contracts with intelligent chunking for large documents, parallel legal and financial review, and alternative clause suggestions")
    .process("parallel")
    .verbose(true)
    .withAgents([legalAnalyst, financialAnalyst, negotiator])
    .withTasks([analyzeLegal, analyzeFinancial, proposeAlternatives])
    .build();

globalThis.crew = crew;
