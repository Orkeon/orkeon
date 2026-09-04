/// <reference orkeon-script="1.0" />
// orkeon-example: {"process":"sequential","agents":4,"tasks":4,"tools":["csv_reader","json_tool","relational_database_query","http_api","file_write","arima_prediction","prophet_prediction","ensemble_prediction"]}
//
// 36. Self-correcting cash flow forecasting — a sequential pipeline whose models
// refine themselves through long-term memory comparing past predictions with
// actual outcomes. Built-in tools are referenced by name; the prediction tools
// come as TypeScript instances from the shared _tools module (EX-01).

import { pickTools } from "../_tools/index.ts";

const patternHistorian = agentBuilder()
    .name("pattern_historian")
    .role("Cash Flow Pattern Historian")
    .goal("Analyze historical cash flow patterns to identify seasonal trends and recurring cycles")
    .backstory(`Time series analyst specializing in financial cash flow patterns.
Identifies seasonal effects, cyclical patterns, and structural trends
from historical data. Compares past predictions with actual outcomes
to calibrate bias corrections using long-term memory.`)
    .tools(["csv_reader", "json_tool", "relational_database_query"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const invoiceCollector = agentBuilder()
    .name("invoice_collector")
    .role("Invoice and Commitment Collector")
    .goal("Gather outstanding invoices, commitments, and scheduled payments")
    .backstory(`Accounts specialist who collects all known future cash movements:
outstanding receivables, payables, loan repayments, salary commitments,
tax deadlines, and contractual obligations from ERP and banking systems.`)
    .tools(["http_api", "csv_reader", "json_tool"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const flowModeler = agentBuilder()
    .name("flow_modeler")
    .role("Cash Flow Modeler")
    .goal("Build and run cash flow forecast models with confidence intervals")
    .backstory(`Financial modeler who combines historical patterns with known commitments
to produce probabilistic cash flow forecasts. Applies bias corrections
learned from past prediction errors stored in long-term memory.`)
    .tools(["csv_reader", "json_tool", "file_write"])
    .withAutonomousTools(pickTools("arima_prediction", "prophet_prediction", "ensemble_prediction"))
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const stressScenarist = agentBuilder()
    .name("stress_scenarist")
    .role("Stress Scenario Planner")
    .goal("Generate stress scenarios and their impact on cash flow projections")
    .backstory(`Risk analyst who creates adverse scenarios (delayed receivables, accelerated payables,
revenue shortfall) and quantifies their impact on liquidity position.
Produces actionable contingency recommendations.`)
    .tools(["json_tool", "csv_reader", "file_write"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const analyzePatterns = taskBuilder()
    .name("analyze_patterns")
    .agent(patternHistorian)
    .description("Analyze 24 months of historical cash flow data. Identify seasonal patterns, cyclical trends, and structural shifts. Compare past predictions vs actual outcomes to calculate bias corrections.")
    .expectedOutput("JSON with seasonal indices, trend components, prediction accuracy metrics, and calculated bias corrections")
    .build();

const collectCommitments = taskBuilder()
    .name("collect_commitments")
    .agent(invoiceCollector)
    .description("Gather all known future cash movements: outstanding receivables with expected collection dates, scheduled payables, loan repayments, salary obligations, and tax deadlines.")
    .expectedOutput("JSON timeline of known cash inflows and outflows with dates, amounts, and probability of on-time settlement")
    .build();

const forecastCashFlow = taskBuilder()
    .name("forecast_cash_flow")
    .agent(flowModeler)
    .description("Build 13-week cash flow forecast combining historical patterns with known commitments. Apply bias corrections from long-term memory. Produce base case with P10/P50/P90 confidence bands.")
    .expectedOutput("Weekly cash flow forecast with base case, confidence intervals, key assumptions, and comparison to previous forecast")
    .withContext(analyzePatterns)
    .withContext(collectCommitments)
    .build();

const stressTest = taskBuilder()
    .name("stress_test")
    .agent(stressScenarist)
    .description("Generate three stress scenarios (mild, moderate, severe) and their impact on the cash flow forecast. Identify week of potential liquidity shortfall for each scenario. Recommend contingency actions.")
    .expectedOutput("Stress test report with scenario definitions, impact on cash position, liquidity shortfall dates, and contingency recommendations")
    .withContext(forecastCashFlow)
    .build();

const crew = crewBuilder()
    .name("cash-flow-forecast")
    .goal("Forecast cash flow with self-correcting models that learn from prediction accuracy over time")
    .process("sequential")
    .memory(true)
    .verbose(true)
    .withAgents([patternHistorian, invoiceCollector, flowModeler, stressScenarist])
    .withTasks([analyzePatterns, collectCommitments, forecastCashFlow, stressTest])
    .build();

(globalThis as any).crew = crew;
