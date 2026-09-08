/// <reference orkeon-script="1.0" />
// orkeon-example: {"process":"consensual","agents":4,"tasks":4,"tools":["http_api","csv_reader","json_tool","file_write","technical_indicators","correlation_analysis","mean_variance_optimization","risk_parity","hierarchical_risk_parity","black_litterman"]}
//
// 34. Optimisation Portefeuille par Consensus — three analysts with different
// investment philosophies must agree, votes weighted by historical track
// record; no manager (consensual process). The data/ folder feeds the
// assessments (/data/fundamentals.csv, /data/price-history.csv).
// Built-in tools are referenced by name; the trading tools come as TypeScript
// instances from the shared _tools module (EX-01).

import { pickTools } from "../_tools/index.ts";

const fundamentalAnalyst = agentBuilder()
    .name("fundamental_analyst")
    .role("Fundamental Analyst")
    .goal("Evaluate investment opportunities based on intrinsic value, earnings quality, and financial health")
    .backstory(`Value investor with 15 years experience in bottom-up fundamental analysis.
Focuses on discounted cash flow models, earnings quality, balance sheet strength,
and competitive moats. Track record weighted by historical prediction accuracy.`)
    .tools(["http_api", "csv_reader", "json_tool"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const technicalAnalyst = agentBuilder()
    .name("technical_analyst")
    .role("Technical Analyst")
    .goal("Identify optimal entry/exit points and trend direction using price action and indicators")
    .backstory(`Chartered Market Technician specializing in multi-timeframe technical analysis.
Uses momentum indicators, support/resistance levels, and volume analysis.
Track record weighted by historical signal accuracy.`)
    .tools(["http_api", "csv_reader", "json_tool"])
    .withAutonomousTools(pickTools("technical_indicators", "correlation_analysis"))
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const macroAnalyst = agentBuilder()
    .name("macro_analyst")
    .role("Macro Strategist")
    .goal("Assess macro-economic environment and its impact on asset allocation")
    .backstory(`Global macro strategist covering monetary policy, fiscal trends, geopolitical risks,
and cross-asset correlations. Provides top-down allocation guidance based on
economic cycle positioning. Track record weighted by historical forecast accuracy.`)
    .tools(["http_api", "csv_reader", "json_tool"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const riskValidator = agentBuilder()
    .name("risk_validator")
    .role("Risk Manager Validator")
    .goal("Validate consensus allocation against risk constraints and regulatory limits")
    .backstory(`Portfolio risk manager ensuring the consensus allocation respects maximum drawdown limits,
concentration caps, liquidity requirements, and regulatory constraints.
Acts as the final validation gate before implementation.`)
    .tools(["csv_reader", "json_tool", "file_write"])
    .withAutonomousTools(pickTools("mean_variance_optimization", "risk_parity", "hierarchical_risk_parity", "black_litterman"))
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const fundamentalAssessment = taskBuilder()
    .name("fundamental_assessment")
    .agent(fundamentalAnalyst)
    .description("Perform fundamental analysis on the investment universe. Read per-asset fundamentals from /data/fundamentals.csv (csv_reader; columns: ticker, sector, pe_ratio, pb_ratio, roe, debt_to_equity, market_cap_bn, dividend_yield_pct). Evaluate each asset on valuation, earnings quality, balance sheet, and competitive position. Produce ranked recommendations with target allocations.")
    .expectedOutput("JSON with ranked assets, intrinsic value estimates, conviction scores, and recommended allocation weights")
    .build();

const technicalAssessment = taskBuilder()
    .name("technical_assessment")
    .agent(technicalAnalyst)
    .description("Run technical analysis on the investment universe. Read the 60-day daily close price history from /data/price-history.csv (csv_reader; columns: date, ticker, close) and feed it to the technical_indicators and correlation_analysis tools. Identify trend direction, momentum, key levels, and optimal timing signals. Produce ranked recommendations with target allocations.")
    .expectedOutput("JSON with ranked assets, trend scores, momentum readings, entry/exit levels, and recommended allocation weights")
    .build();

const macroAssessment = taskBuilder()
    .name("macro_assessment")
    .agent(macroAnalyst)
    .description("Assess macro-economic environment and determine optimal asset class and geographic allocation. Consider economic cycle, monetary policy, and geopolitical risks.")
    .expectedOutput("JSON with macro outlook, asset class preferences, geographic tilts, and recommended allocation weights")
    .build();

const validateAllocation = taskBuilder()
    .name("validate_allocation")
    .agent(riskValidator)
    .description("Validate the consensus-weighted allocation against risk limits. Check maximum drawdown, concentration, liquidity, and regulatory constraints. Adjust if needed.")
    .expectedOutput("Final validated allocation with risk metrics, any adjustments made, and compliance confirmation")
    .withContext(fundamentalAssessment)
    .withContext(technicalAssessment)
    .withContext(macroAssessment)
    .build();

const crew = crewBuilder()
    .name("portfolio-consensus")
    .goal("Optimize portfolio allocation through weighted consensus among analysts with different investment philosophies")
    .process("consensual")
    .memory(true)
    .verbose(true)
    .withAgents([fundamentalAnalyst, technicalAnalyst, macroAnalyst, riskValidator])
    .withTasks([fundamentalAssessment, technicalAssessment, macroAssessment, validateAllocation])
    .build();

globalThis.crew = crew;
