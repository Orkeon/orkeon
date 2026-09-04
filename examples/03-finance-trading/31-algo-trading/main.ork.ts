/// <reference orkeon-script="1.0" />
// orkeon-example: {"process":"hierarchical","agents":8,"tasks":7,"tools":["http_api","json_tool","csv_reader","file_write","relational_database_query","technical_indicators","market_regime_classification","arima_prediction","ensemble_prediction","backtesting","var_calculation","cvar_calculation","mean_variance_optimization","portfolio_rebalancing","twap_execution","vwap_execution","smart_order_routing"]}
//
// 31. Multi-strategy algorithmic trading — the flagship hierarchical showcase:
// a CIO delegates to seven specialized desks (data, technicals, quant, risk,
// portfolio, execution, compliance) over a dependency DAG with one fan-out.
// Built-in tools are referenced by name; the trading tools come as TypeScript
// instances from the shared _tools module (EX-01).

import { pickTools } from "../_tools/index.ts";

const cio = agentBuilder()
    .name("cio")
    .role("Chief Investment Officer")
    .goal("Coordinate all trading operations, balancing risk and return across strategies")
    .backstory(`Senior CIO with 20+ years experience managing multi-strategy hedge funds.
Expert at capital allocation, risk oversight, and regulatory compliance.
Delegates analysis and execution to specialized teams while maintaining portfolio-level view.`)
    .allowDelegation(true)
    .maxIterations(15)
    .verbose(true)
    .build();

const dataOrchestrator = agentBuilder()
    .name("data_orchestrator")
    .role("Data Orchestrator")
    .goal("Collect and normalize market data from multiple sources in real-time")
    .backstory(`Data engineer specializing in financial market feeds. Aggregates tick data,
order book snapshots, and alternative data into a unified format for downstream analysis.`)
    .tools(["http_api", "json_tool", "csv_reader"])
    .maxIterations(10)
    .verbose(true)
    .build();

const technicalAnalyst = agentBuilder()
    .name("technical_analyst")
    .role("Technical Analyst")
    .goal("Identify trading signals using technical indicators and chart patterns")
    .backstory(`Chartered Market Technician with expertise in momentum, mean-reversion,
and breakout strategies. Applies multi-timeframe analysis to generate actionable signals.`)
    .tools(["http_api", "csv_reader", "json_tool"])
    .withAutonomousTools(pickTools("technical_indicators", "market_regime_classification"))
    .maxIterations(10)
    .verbose(true)
    .build();

const quantForecaster = agentBuilder()
    .name("quant_forecaster")
    .role("Quantitative Forecaster")
    .goal("Build and run quantitative models for price and volatility forecasting")
    .backstory(`PhD in financial mathematics with deep expertise in stochastic models,
machine learning for finance, and statistical arbitrage strategies.`)
    .tools(["csv_reader", "json_tool"])
    .withAutonomousTools(pickTools("arima_prediction", "ensemble_prediction", "backtesting"))
    .maxIterations(10)
    .verbose(true)
    .build();

const riskOfficer = agentBuilder()
    .name("risk_officer")
    .role("Risk Officer")
    .goal("Monitor and enforce risk limits across all strategies and positions")
    .backstory(`Former bank risk manager specializing in VaR, stress testing, and position limits.
Ensures no single strategy or position exceeds pre-defined risk thresholds.`)
    .tools(["csv_reader", "json_tool", "relational_database_query"])
    .withAutonomousTools(pickTools("var_calculation", "cvar_calculation"))
    .maxIterations(10)
    .verbose(true)
    .build();

const portfolioManager = agentBuilder()
    .name("portfolio_manager")
    .role("Portfolio Manager")
    .goal("Optimize portfolio allocation and rebalancing across strategies")
    .backstory(`CFA charterholder with expertise in modern portfolio theory, factor investing,
and dynamic asset allocation. Balances return targets with risk budgets.`)
    .tools(["csv_reader", "json_tool", "file_write"])
    .withAutonomousTools(pickTools("mean_variance_optimization", "portfolio_rebalancing"))
    .maxIterations(10)
    .verbose(true)
    .build();

const trader = agentBuilder()
    .name("trader")
    .role("Execution Trader")
    .goal("Execute orders with minimal market impact and optimal timing")
    .backstory(`Experienced execution trader skilled in algorithmic order routing,
TWAP/VWAP strategies, and dark pool access. Minimizes slippage on every trade.`)
    .tools(["http_api", "json_tool"])
    .withAutonomousTools(pickTools("twap_execution", "vwap_execution", "smart_order_routing"))
    .maxIterations(10)
    .verbose(true)
    .build();

const complianceOfficer = agentBuilder()
    .name("compliance_officer")
    .role("Compliance Officer")
    .goal("Ensure all trading activities comply with regulations and internal policies")
    .backstory(`Regulatory compliance expert covering MiFID II, Dodd-Frank, and internal
trading policies. Reviews every order for pre-trade and post-trade compliance.`)
    .tools(["json_tool", "file_write", "relational_database_query"])
    .maxIterations(10)
    .verbose(true)
    .build();

const collectMarketData = taskBuilder()
    .name("collect_market_data")
    .agent(dataOrchestrator)
    .description("Collect real-time market data from multiple sources including price feeds, order book data, and alternative data. Normalize into a unified format for analysis.")
    .expectedOutput("JSON object with normalized market data including OHLCV, order book depth, and alternative signals")
    .build();

const analyzeTechnicals = taskBuilder()
    .name("analyze_technicals")
    .agent(technicalAnalyst)
    .description("Run technical analysis on collected market data. Calculate indicators (RSI, MACD, Bollinger Bands, etc.) and identify trading signals across multiple timeframes.")
    .expectedOutput("JSON report with technical signals, confidence scores, and recommended trade direction per instrument")
    .withContext(collectMarketData)
    .build();

const runQuantModels = taskBuilder()
    .name("run_quant_models")
    .agent(quantForecaster)
    .description("Execute quantitative forecasting models on the market data. Generate price predictions, volatility forecasts, and statistical arbitrage signals.")
    .expectedOutput("JSON with model predictions, confidence intervals, and expected returns per strategy")
    .withContext(collectMarketData)
    .build();

const assessRisk = taskBuilder()
    .name("assess_risk")
    .agent(riskOfficer)
    .description("Calculate current portfolio risk metrics including VaR, maximum drawdown, correlation exposure, and position concentration. Flag any limit breaches.")
    .expectedOutput("Risk dashboard JSON with VaR, drawdown, concentration metrics, and any limit violations")
    .withContext(analyzeTechnicals)
    .withContext(runQuantModels)
    .build();

const optimizePortfolio = taskBuilder()
    .name("optimize_portfolio")
    .agent(portfolioManager)
    .description("Based on signals, forecasts, and risk assessment, determine optimal portfolio allocation and generate rebalancing orders.")
    .expectedOutput("Portfolio allocation plan with target weights, rebalancing trades, and expected risk/return profile")
    .withContext(assessRisk)
    .build();

const executeTrades = taskBuilder()
    .name("execute_trades")
    .agent(trader)
    .description("Execute the approved trading orders using optimal execution algorithms. Monitor fills and report execution quality.")
    .expectedOutput("Execution report with fill prices, slippage analysis, and market impact assessment")
    .withContext(optimizePortfolio)
    .build();

const complianceReview = taskBuilder()
    .name("compliance_review")
    .agent(complianceOfficer)
    .description("Perform post-trade compliance review. Verify all trades meet regulatory requirements and internal policies. Generate audit trail.")
    .expectedOutput("Compliance report with pass/fail status per trade, any violations flagged, and complete audit trail")
    .withContext(executeTrades)
    .build();

const crew = crewBuilder()
    .name("algo-trading")
    .goal("Coordinate multi-strategy algorithmic trading with real-time analysis, risk management, and compliance")
    .process("hierarchical")
    .manager(cio)
    .memory(true)
    .verbose(true)
    .withAgents([cio, dataOrchestrator, technicalAnalyst, quantForecaster, riskOfficer, portfolioManager, trader, complianceOfficer])
    .withTasks([collectMarketData, analyzeTechnicals, runQuantModels, assessRisk, optimizePortfolio, executeTrades, complianceReview])
    .build();

(globalThis as any).crew = crew;
