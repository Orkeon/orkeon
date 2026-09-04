/// <reference orkeon-script="1.0" />
// orkeon-example: {"process":"hierarchical","agents":4,"tasks":3,"tools":["http_api","csv_reader","json_tool","web_scrape","file_write","market_regime_classification","alternative_data","dashboard_metrics"]}
//
// 42. Pricing Dynamique Arbitrage Hierarchique — a Manager Agent arbitrates
// between three conflicting perspectives on the optimal price (demand,
// competition, margin). Source: project/marketing/content-strategy/101-USE-CASES.md #42
// Built-in tools are referenced by name; the trading tools come as TypeScript
// instances from the shared _tools module (EX-01).

import { pickTools } from "../_tools/index.ts";

const pricingArbiter = agentBuilder()
    .name("pricing_arbiter")
    .role("Pricing Arbitrator")
    .goal("Arbitrate between conflicting pricing recommendations to set the optimal price")
    .backstory(`Chief Revenue Officer with deep expertise in pricing strategy.
Arbitrates between demand-based, competition-based, and margin-based pricing
recommendations. Ensures final price respects min/max guardrails and
balances short-term revenue with long-term market positioning.`)
    .allowDelegation(true)
    .maxIterations(15)
    .verbose(true)
    .build();

const demandAnalyst = agentBuilder()
    .name("demand_analyst")
    .role("Demand Analysis Specialist")
    .goal("Determine optimal price based on demand elasticity and customer willingness to pay")
    .backstory(`Pricing economist specializing in demand curves and price elasticity.
Analyzes historical sales data, customer segments, seasonal patterns,
and willingness-to-pay signals to recommend demand-optimal pricing.`)
    .tools(["http_api", "csv_reader", "json_tool"])
    .withAutonomousTools(pickTools("market_regime_classification", "alternative_data"))
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const competitionMonitor = agentBuilder()
    .name("competition_monitor")
    .role("Competitive Intelligence Monitor")
    .goal("Track competitor pricing and recommend competitive positioning")
    .backstory(`Competitive intelligence analyst who monitors competitor prices in real-time.
Analyzes pricing strategies, promotional patterns, and market share dynamics
to recommend optimal competitive positioning.`)
    .tools(["web_scrape", "http_api", "json_tool"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const marginOptimizer = agentBuilder()
    .name("margin_optimizer")
    .role("Margin Optimization Analyst")
    .goal("Maximize profit margins while respecting volume targets")
    .backstory(`Financial analyst focusing on contribution margin optimization.
Models the impact of different price points on unit economics,
considering variable costs, fixed cost absorption, and volume incentives.`)
    .tools(["csv_reader", "json_tool", "file_write"])
    .withAutonomousTools(pickTools("dashboard_metrics"))
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const analyzeDemand = taskBuilder()
    .name("analyze_demand")
    .agent(demandAnalyst)
    .description("Analyze demand elasticity for target products. Calculate price-volume curves, identify optimal demand price points, and segment recommendations by customer tier and channel.")
    .expectedOutput("JSON with demand curves, elasticity coefficients, recommended price by segment, and expected volume at each price point")
    .build();

const monitorCompetition = taskBuilder()
    .name("monitor_competition")
    .agent(competitionMonitor)
    .description("Scrape and analyze competitor pricing for comparable products. Map competitive landscape, identify pricing gaps, and recommend competitive positioning (premium, parity, or undercut).")
    .expectedOutput("JSON competitive analysis with competitor price map, positioning recommendation, and price band for each product")
    .build();

const optimizeMargin = taskBuilder()
    .name("optimize_margin")
    .agent(marginOptimizer)
    .description("Model profit margin at different price points considering variable costs, volume effects, and fixed cost absorption. Identify margin-optimal price range and break-even volumes.")
    .expectedOutput("JSON margin analysis with contribution margin curves, optimal margin price range, break-even analysis, and volume sensitivity")
    .build();

const crew = crewBuilder()
    .name("dynamic-pricing")
    .goal("Determine optimal dynamic pricing through hierarchical arbitration of demand, competition, and margin perspectives")
    .process("hierarchical")
    .manager(pricingArbiter)
    .memory(true)
    .verbose(true)
    .withAgents([pricingArbiter, demandAnalyst, competitionMonitor, marginOptimizer])
    .withTasks([analyzeDemand, monitorCompetition, optimizeMargin])
    .build();

(globalThis as any).crew = crew;
