/// <reference orkeon-script="1.0" />
// orkeon-example: {"process":"sequential","agents":4,"tasks":4,"tools":["json_tool","human_input","http_api","csv_reader","file_write","mean_variance_optimization","risk_parity","portfolio_rebalancing"]}
//
// 39. Robo-Advisor Profilage Interactif — the risk profile is collected
// through typed interactive questions (human_input on the profiling task),
// client responses kept in encrypted episodic memory.
// Built-in tools are referenced by name; the trading tools come as TypeScript
// instances from the shared _tools module (EX-01).

import { pickTools } from "../_tools/index.ts";

const profiler = agentBuilder()
    .name("profiler")
    .role("Risk Profile Assessor")
    .goal("Collect client risk profile through interactive multi-modal questionnaire")
    .backstory(`Certified financial planner specializing in behavioral finance and risk assessment.
Conducts interactive profiling using multiple question types (multiple choice,
numeric scales, open text) to build a comprehensive risk tolerance profile.
Client responses are stored in encrypted episodic memory.`)
    .tools(["json_tool", "human_input"])
    .allowDelegation(false)
    .maxIterations(15)
    .verbose(true)
    .build();

const strategist = agentBuilder()
    .name("strategist")
    .role("Investment Strategy Allocator")
    .goal("Design optimal asset allocation strategy matching the client risk profile")
    .backstory(`Portfolio strategist with expertise in strategic and tactical asset allocation.
Maps risk profiles to optimal portfolio compositions using modern portfolio theory,
factor models, and behavioral considerations. Considers time horizon,
liquidity needs, and tax situation.`)
    .tools(["http_api", "csv_reader", "json_tool"])
    .withAutonomousTools(pickTools("mean_variance_optimization", "risk_parity"))
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const portfolioMonitor = agentBuilder()
    .name("portfolio_monitor")
    .role("Portfolio Monitor")
    .goal("Monitor portfolio performance and alert on significant deviations from targets")
    .backstory(`Portfolio monitoring specialist tracking real-time performance against benchmarks.
Monitors drift from target allocation, performance attribution,
and risk metric evolution. Triggers rebalancing alerts when thresholds are breached.`)
    .tools(["http_api", "csv_reader", "json_tool"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const rebalancer = agentBuilder()
    .name("rebalancer")
    .role("Portfolio Rebalancer")
    .goal("Generate rebalancing trades to bring portfolio back to target allocation")
    .backstory(`Execution specialist who calculates optimal rebalancing trades considering
transaction costs, tax-loss harvesting opportunities, and minimum trade sizes.
Produces actionable trade orders with cost-benefit analysis.`)
    .tools(["json_tool", "csv_reader", "file_write"])
    .withAutonomousTools(pickTools("portfolio_rebalancing"))
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const profileClient = taskBuilder()
    .name("profile_client")
    .agent(profiler)
    .description("Conduct interactive risk profiling session. Ask questions covering investment experience, time horizon, risk tolerance, income stability, financial goals, and loss aversion. Score and classify the risk profile.")
    .expectedOutput("JSON risk profile with scores across dimensions, overall risk category (Conservative/Moderate/Aggressive), and profile confidence level")
    .humanInput(true)
    .build();

const designStrategy = taskBuilder()
    .name("design_strategy")
    .agent(strategist)
    .description("Based on the risk profile, design an optimal asset allocation strategy. Determine target weights for each asset class, select appropriate investment vehicles, and set rebalancing thresholds.")
    .expectedOutput("JSON investment strategy with target allocation, recommended instruments, expected return/risk, and rebalancing rules")
    .withContext(profileClient)
    .build();

const monitorPortfolio = taskBuilder()
    .name("monitor_portfolio")
    .agent(portfolioMonitor)
    .description("Check current portfolio positions against target allocation. Calculate drift, performance attribution, and risk metrics. Identify any threshold breaches requiring rebalancing.")
    .expectedOutput("Portfolio monitoring report with current vs target allocation, drift analysis, performance attribution, and rebalancing triggers")
    .withContext(designStrategy)
    .build();

const rebalance = taskBuilder()
    .name("rebalance")
    .agent(rebalancer)
    .description("Generate rebalancing trade orders to correct allocation drift. Optimize for tax-loss harvesting, minimize transaction costs, and respect minimum trade sizes. Produce executable trade list.")
    .expectedOutput("Rebalancing trade list with buy/sell orders, estimated costs, tax-loss harvesting opportunities, and post-rebalance projected allocation")
    .withContext(monitorPortfolio)
    .build();

const crew = crewBuilder()
    .name("robo-advisor")
    .goal("Provide personalized investment advice through interactive risk profiling with encrypted client memory")
    .process("sequential")
    .memory(true)
    .verbose(true)
    .withAgents([profiler, strategist, portfolioMonitor, rebalancer])
    .withTasks([profileClient, designStrategy, monitorPortfolio, rebalance])
    .build();

(globalThis as any).crew = crew;
