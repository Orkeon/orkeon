/// <reference orkeon-script="1.0" />
// orkeon-example: {"process":"parallel","agents":4,"tasks":4,"tools":["http_api","json_tool","relational_database_query","semantic_search","file_write","pattern_recognition","correlation_analysis","regulatory_reporting","audit_trail"]}
//
// 41. Detection de Delit d'Initie
// Use case: ObserverAgent correle les transactions inhabituelles avec les evenements
// d'entreprise en temps reel
// Source: project/marketing/content-strategy/101-USE-CASES.md #41

import { pickTools } from "../_tools/index.ts";

const transactionObserver = agentBuilder()
    .name("transaction_observer")
    .role("Transaction Surveillance Observer")
    .goal("Monitor stock transactions in real-time and flag unusual trading patterns")
    .backstory(`Market surveillance specialist running continuous monitoring on transaction feeds.
Detects unusual volume spikes, abnormal price movements before announcements,
and trading patterns that deviate from historical baselines for specific insiders.`)
    .tools(["http_api", "json_tool"])
    .allowDelegation(false)
    .maxIterations(15)
    .verbose(true)
    .build();

const eventCorrelator = agentBuilder()
    .name("event_correlator")
    .role("Corporate Event Correlator")
    .goal("Correlate flagged transactions with upcoming corporate events and material information")
    .backstory(`Financial intelligence analyst who cross-references flagged trading activity
with corporate event calendars, regulatory filings, M&A rumors, earnings
announcements, and insider access logs. Identifies temporal correlations
that suggest potential information asymmetry.`)
    .tools(["http_api", "relational_database_query", "json_tool"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const behaviorAnalyst = agentBuilder()
    .name("behavior_analyst")
    .role("Behavioral Pattern Analyst")
    .goal("Analyze trading behavior patterns of flagged individuals against their historical norms")
    .backstory(`Behavioral analytics specialist who profiles trading patterns of corporate insiders.
Compares current activity against established baselines. Detects strategy changes,
unusual counterparties, and timing anomalies using semantic vector comparison
in encrypted memory.`)
    .tools(["relational_database_query", "json_tool", "semantic_search"])
    .withAutonomousTools(pickTools("pattern_recognition", "correlation_analysis"))
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const regulatoryReporter = agentBuilder()
    .name("regulatory_reporter")
    .role("Regulatory Reporting Agent")
    .goal("Generate regulatory-compliant suspicious transaction reports with full evidence chain")
    .backstory(`Regulatory reporting expert who compiles investigation findings into
STR (Suspicious Transaction Report) format compliant with SEC, FCA,
and AMF requirements. Ensures complete evidence chain and audit trail.`)
    .tools(["json_tool", "file_write"])
    .withAutonomousTools(pickTools("regulatory_reporting", "audit_trail"))
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const monitorTransactions = taskBuilder()
    .name("monitor_transactions")
    .agent(transactionObserver)
    .description("Monitor real-time transaction feeds for unusual patterns: volume anomalies, price-sensitive timing, concentration in specific securities, and deviations from normal trading behavior.")
    .expectedOutput("JSON array of flagged transactions with anomaly type, severity score, and baseline deviation metrics")
    .asyncExecution(true)
    .build();

const correlateEvents = taskBuilder()
    .name("correlate_events")
    .agent(eventCorrelator)
    .description("For each flagged transaction, search for temporal correlation with corporate events: earnings announcements, M&A activity, regulatory filings, board meetings, and material disclosures within a configurable time window.")
    .expectedOutput("JSON correlation report with matched event-transaction pairs, temporal proximity scores, and information access assessment")
    .withContext(monitorTransactions)
    .build();

const analyzeBehavior = taskBuilder()
    .name("analyze_behavior")
    .agent(behaviorAnalyst)
    .description("Profile the trading behavior of flagged individuals. Compare against historical baselines using semantic vector search. Identify strategy changes, unusual timing patterns, and network connections.")
    .expectedOutput("Behavioral analysis report with historical comparison, deviation metrics, network analysis, and insider risk score")
    .withContext(monitorTransactions)
    .build();

const generateRegulatoryReport = taskBuilder()
    .name("generate_regulatory_report")
    .agent(regulatoryReporter)
    .description("Compile all findings into a regulatory-compliant suspicious transaction report. Include complete evidence chain, timeline reconstruction, and recommended regulatory actions.")
    .expectedOutput("STR-format report with evidence summary, timeline, correlation analysis, risk assessment, and recommended actions")
    .withContext(correlateEvents)
    .withContext(analyzeBehavior)
    .build();

const crew = crewBuilder()
    .name("insider-trading-detection")
    .goal("Detect insider trading by correlating unusual transactions with corporate events in real-time using encrypted audit trail")
    .process("parallel")
    .memory(true)
    .verbose(true)
    .withAgents([transactionObserver, eventCorrelator, behaviorAnalyst, regulatoryReporter])
    .withTasks([monitorTransactions, correlateEvents, analyzeBehavior, generateRegulatoryReport])
    .build();

(globalThis as any).crew = crew;
