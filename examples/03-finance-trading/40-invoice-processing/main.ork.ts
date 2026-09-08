/// <reference orkeon-script="1.0" />
// orkeon-example: {"process":"sequential","agents":3,"tasks":3,"tools":["pdf_reader","csv_reader","json_tool","file_write","relational_database_query","audit_trail","dashboard_metrics"]}
//
// 40. Automated invoice processing — a sequential pipeline that extracts raw
// data from mixed-format invoices (PDF and CSV under /data), structures it
// into a canonical JSON schema through the typed pipeline with tolerant
// converters, and three-way matches it against purchase orders and goods
// receipts. Built-in tools are referenced by name; the governance tools come
// as TypeScript instances from the shared _tools module (EX-01).

import { pickTools } from "../_tools/index.ts";

const extractor = agentBuilder()
    .name("extractor")
    .role("Invoice OCR/PDF Extractor")
    .goal("Extract raw data from invoices in various formats (PDF, scanned images, electronic)")
    .backstory(`Document processing specialist with expertise in OCR and PDF parsing.
Handles diverse invoice formats from different vendors: structured PDFs,
scanned documents, XML-based e-invoices. Uses tolerant JSON converters
to handle format variations automatically.`)
    .tools(["pdf_reader", "csv_reader", "json_tool"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const structurer = agentBuilder()
    .name("structurer")
    .role("Invoice Data Structurer")
    .goal("Normalize extracted invoice data into a standard JSON schema")
    .backstory(`Data normalization expert who maps diverse invoice fields into a standardized
schema. Handles variations in field naming, date formats, amount representations,
and tax structures. Uses ComponentBase typed pipeline for consistency.`)
    .tools(["json_tool", "file_write"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const reconciler = agentBuilder()
    .name("reconciler")
    .role("Accounting Reconciler")
    .goal("Match structured invoices against purchase orders and receiving records")
    .backstory(`Accounts payable specialist performing three-way matching between invoices,
purchase orders, and goods receipt. Identifies discrepancies in quantities,
prices, and totals. Flags exceptions for review.`)
    .tools(["relational_database_query", "csv_reader", "json_tool", "file_write"])
    .withAutonomousTools(pickTools("audit_trail", "dashboard_metrics"))
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const extractInvoices = taskBuilder()
    .name("extract_invoices")
    .agent(extractor)
    .description("Process incoming invoices from /data/invoices: two PDF invoices (/data/invoices/INV-2024-0001.pdf and /data/invoices/INV-2024-0002.pdf, read with pdf_reader) and a CSV batch of electronic invoices (/data/invoices/invoices-batch.csv, read with csv_reader). Extract all fields: vendor, invoice number, date, line items, amounts, tax, payment terms. Handle format variations using tolerant converters.")
    .expectedOutput("JSON array of raw extracted invoice data with confidence scores per field")
    .build();

const structureData = taskBuilder()
    .name("structure_data")
    .agent(structurer)
    .description("Normalize extracted invoice data into standardized JSON schema. Map vendor-specific field names to canonical fields. Validate amounts, dates, and tax calculations. Flag inconsistencies.")
    .expectedOutput("JSON array of structured invoices conforming to standard schema with validation status per invoice")
    .withContext(extractInvoices)
    .build();

const reconcileAccounts = taskBuilder()
    .name("reconcile_accounts")
    .agent(reconciler)
    .description("Match each structured invoice against purchase orders at /data/purchase-orders.csv and receiving records at /data/goods-receipts.csv (read with csv_reader). Perform three-way match on quantities, unit prices, and totals. Generate exception report for mismatches.")
    .expectedOutput("Reconciliation report with matched invoices, exception list with discrepancy details, and recommended actions per exception")
    .withContext(structureData)
    .build();

const crew = crewBuilder()
    .name("invoice-processing")
    .goal("Extract, structure, and reconcile invoices from multiple formats using typed pipeline and tolerant converters")
    .process("sequential")
    .memory(true)
    .verbose(true)
    .withAgents([extractor, structurer, reconciler])
    .withTasks([extractInvoices, structureData, reconcileAccounts])
    .build();

globalThis.crew = crew;
