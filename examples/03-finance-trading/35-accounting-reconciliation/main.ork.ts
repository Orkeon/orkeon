/// <reference orkeon-script="1.0" />
// orkeon-example: {"process":"parallel","agents":5,"tasks":5,"tools":["http_api","csv_reader","json_tool","relational_database_query","file_write","audit_trail","dashboard_metrics"]}
//
// 35. Multi-source accounting reconciliation — one agent per data source runs
// in parallel (bank, ERP, CRM), then everything converges on a Reconciliator
// performing three-way matching, and a Corrector proposes fixes for the
// discrepancies. Built-in tools are referenced by name; the governance tools
// come as TypeScript instances from the shared _tools module (EX-01).

import { pickTools } from "../_tools/index.ts";

const bankExtractor = agentBuilder()
    .name("bank_extractor")
    .role("Bank Data Extractor")
    .goal("Extract and normalize transaction data from banking systems")
    .backstory(`Banking integration specialist with expertise in SWIFT, ISO 20022, and bank API formats.
Extracts statements, transaction details, and balance confirmations,
normalizing diverse bank formats into a standardized ledger format.`)
    .tools(["http_api", "csv_reader", "json_tool"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const erpExtractor = agentBuilder()
    .name("erp_extractor")
    .role("ERP Data Extractor")
    .goal("Extract and normalize accounting entries from the ERP system")
    .backstory(`ERP integration specialist covering SAP, Oracle, and Microsoft Dynamics.
Extracts general ledger entries, accounts payable/receivable,
and journal entries into a standardized reconciliation format.`)
    .tools(["http_api", "relational_database_query", "json_tool"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const crmExtractor = agentBuilder()
    .name("crm_extractor")
    .role("CRM Data Extractor")
    .goal("Extract invoicing and payment data from CRM system")
    .backstory(`CRM data specialist who extracts invoice records, payment schedules,
and customer billing data. Maps CRM entries to accounting references
for cross-system matching.`)
    .tools(["http_api", "csv_reader", "json_tool"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const reconciliator = agentBuilder()
    .name("reconciliator")
    .role("Reconciliation Analyst")
    .goal("Match and reconcile entries across all three sources, identifying discrepancies")
    .backstory(`Senior reconciliation analyst with expertise in three-way matching.
Uses fuzzy matching on amounts, dates, and references to pair entries
across systems. Identifies unmatched items and discrepancies with root cause analysis.`)
    .tools(["json_tool", "csv_reader", "relational_database_query", "file_write"])
    .withAutonomousTools(pickTools("audit_trail"))
    .allowDelegation(false)
    .maxIterations(15)
    .verbose(true)
    .build();

const corrector = agentBuilder()
    .name("corrector")
    .role("Discrepancy Corrector")
    .goal("Investigate and propose corrections for reconciliation discrepancies")
    .backstory(`Accounting correction specialist who investigates unmatched items,
identifies root causes (timing differences, data entry errors, missing entries),
and proposes specific correction entries or actions.`)
    .tools(["json_tool", "file_write"])
    .withAutonomousTools(pickTools("dashboard_metrics"))
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const extractBank = taskBuilder()
    .name("extract_bank")
    .agent(bankExtractor)
    .description("Connect to banking APIs and extract all transactions for the reconciliation period. Normalize into standardized format with reference, amount, date, counterparty, and category.")
    .expectedOutput("JSON array of normalized bank transactions with standardized fields")
    .asyncExecution(true)
    .build();

const extractErp = taskBuilder()
    .name("extract_erp")
    .agent(erpExtractor)
    .description("Query ERP system for general ledger entries, AP/AR transactions, and journal entries for the reconciliation period. Normalize into standardized format.")
    .expectedOutput("JSON array of normalized ERP entries with standardized fields matching bank format")
    .asyncExecution(true)
    .build();

const extractCrm = taskBuilder()
    .name("extract_crm")
    .agent(crmExtractor)
    .description("Extract invoice and payment records from CRM for the reconciliation period. Map to accounting references and normalize format.")
    .expectedOutput("JSON array of normalized CRM records with accounting references and standardized fields")
    .asyncExecution(true)
    .build();

const reconcile = taskBuilder()
    .name("reconcile")
    .agent(reconciliator)
    .description("Perform three-way matching across bank, ERP, and CRM data. Match entries by amount, date proximity, and reference similarity. Identify matched pairs, partial matches, and unmatched items.")
    .expectedOutput("Reconciliation report with matched entries, partial matches requiring review, unmatched items per source, and summary statistics")
    .withContext(extractBank)
    .withContext(extractErp)
    .withContext(extractCrm)
    .build();

const correctDiscrepancies = taskBuilder()
    .name("correct_discrepancies")
    .agent(corrector)
    .description("Investigate unmatched and partially matched items. Determine root cause for each discrepancy and propose specific correction actions (timing adjustments, journal entries, or escalation).")
    .expectedOutput("Correction proposals with root cause analysis, recommended journal entries, and items requiring manual review")
    .withContext(reconcile)
    .build();

const crew = crewBuilder()
    .name("accounting-reconciliation")
    .goal("Reconcile financial records across bank, ERP, and CRM sources with parallel extraction and checkpoint-based recovery")
    .process("parallel")
    .memory(true)
    .verbose(true)
    .withAgents([bankExtractor, erpExtractor, crmExtractor, reconciliator, corrector])
    .withTasks([extractBank, extractErp, extractCrm, reconcile, correctDiscrepancies])
    .build();

(globalThis as any).crew = crew;
