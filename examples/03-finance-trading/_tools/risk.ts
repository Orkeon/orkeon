// Risk tools (EX-01) — the TypeScript port of Orkeon.Trading.Tools'
// Infrastructure/Risk. Same names, same schemas (translated verbatim from the
// YAML ToolDefinitions), same output shapes. The Monte Carlo path is
// DETERMINISTIC (seeded from the input) where the C# used a shared Random, and
// the mock factor model provider is folded in as a seeded generator keyed by
// symbol+factor. All four are pure computation — no disk, no network — hence
// access("read").

import {
    mean, stdDev, quantile, normalInvCdf, mulberry32, seedFrom, normalSampler
} from "./math.ts";

// ---------------------------------------------------------------------------
// Shared helpers
// ---------------------------------------------------------------------------

function isoNow(): string {
    return new Date().toISOString();
}

function round(x: number, digits: number): number {
    const f = Math.pow(10, digits);
    return Math.round((x + Number.EPSILON) * f) / f;
}

/** "N2"-style dollar amount: thousands separators, two decimals. */
function usd(n: number): string {
    const fixed = Math.abs(n).toFixed(2);
    const parts = fixed.split(".");
    const grouped = parts[0].replace(/\B(?=(\d{3})+(?!\d))/g, ",");
    return (n < 0 ? "-" : "") + grouped + "." + parts[1];
}

/** "N0"-style integer: thousands separators, no decimals. */
function groupInt(n: number): string {
    return String(Math.round(n)).replace(/\B(?=(\d{3})+(?!\d))/g, ",");
}

/** Percentage-or-decimal normalization, as in the C# tools: 2.5 -> 0.025. */
function toDecimalReturns(raw: unknown[]): number[] {
    return raw.map(v => Number(v)).map(r => r > 1 || r < -1 ? r / 100.0 : r);
}

/** Sample skewness (adjusted Fisher–Pearson), matching the C# helper. */
function skewness(returns: readonly number[]): number {
    const n = returns.length;
    if (n < 3) return 0;
    const m = mean(returns);
    const s = stdDev(returns);
    if (s === 0) return 0;
    let sum = 0;
    for (const r of returns) sum += Math.pow((r - m) / s, 3);
    return sum * n / ((n - 1) * (n - 2));
}

/** Sample excess kurtosis, matching the C# helper. */
function kurtosis(returns: readonly number[]): number {
    const n = returns.length;
    if (n < 4) return 0;
    const m = mean(returns);
    const s = stdDev(returns);
    if (s === 0) return 0;
    let sum = 0;
    for (const r of returns) sum += Math.pow((r - m) / s, 4);
    return sum * n * (n + 1) / ((n - 1) * (n - 2) * (n - 3))
        - 3 * Math.pow(n - 1, 2) / ((n - 2) * (n - 3));
}

function requireReturns(input: any): number[] {
    const raw: unknown[] = Array.isArray(input.returns_data) ? input.returns_data : [];
    if (raw.length < 20) throw new Error("At least 20 returns data points required");
    return toDecimalReturns(raw);
}

// ---------------------------------------------------------------------------
// var_calculation
// ---------------------------------------------------------------------------

function parametricVar(
    portfolioValue: number, returns: number[], confidenceLevel: number, timeHorizon: number) {
    const meanReturn = mean(returns);
    const sd = stdDev(returns);
    // z-score for the left tail of the standard normal
    const zScore = normalInvCdf(1 - confidenceLevel);
    // Square-root-of-time scaling
    const scaledStdDev = sd * Math.sqrt(timeHorizon);
    const varReturn = meanReturn * timeHorizon + zScore * scaledStdDev;
    const varAmount = portfolioValue * Math.abs(varReturn);
    const clPct = round(confidenceLevel * 100, 4);
    return {
        var_amount: round(varAmount, 2),
        var_percentage: round(Math.abs(varReturn) * 100, 2),
        mean_return: round(meanReturn, 6),
        std_deviation: round(sd, 6),
        z_score: round(zScore, 4),
        interpretation: `At ${clPct}% confidence, potential loss should not exceed $${usd(round(varAmount, 2))} over ${timeHorizon} day(s)`
    };
}

function historicalVar(
    portfolioValue: number, returns: number[], confidenceLevel: number, timeHorizon: number) {
    // Empirical left-tail quantile of the return distribution
    const varReturn = quantile(returns, 1 - confidenceLevel);
    const scaledVarReturn = varReturn * Math.sqrt(timeHorizon);
    const varAmount = portfolioValue * Math.abs(scaledVarReturn);
    const sorted = [...returns].sort((a, b) => a - b);
    const worstLosses = sorted.slice(0, 5).map(r => round(r * 100, 2));
    const clPct = round(confidenceLevel * 100, 4);
    return {
        var_amount: round(varAmount, 2),
        var_percentage: round(Math.abs(scaledVarReturn) * 100, 2),
        percentile_return: round(varReturn, 6),
        worst_5_losses_pct: worstLosses,
        interpretation: `Based on historical data, at ${clPct}% confidence, potential loss should not exceed $${usd(round(varAmount, 2))} over ${timeHorizon} day(s)`
    };
}

function monteCarloVar(
    portfolioValue: number, returns: number[], confidenceLevel: number,
    timeHorizon: number, numSimulations: number, seedText: string) {
    const meanReturn = mean(returns);
    const sd = stdDev(returns);
    // Seeded sampler: same input, same simulated paths, every run
    const sample = normalSampler(mulberry32(seedFrom(seedText)));
    const simulated: number[] = [];
    for (let i = 0; i < numSimulations; i++) {
        let cumulative = 0;
        for (let day = 0; day < timeHorizon; day++) {
            cumulative += meanReturn + sd * sample();
        }
        simulated.push(cumulative);
    }
    const varReturn = quantile(simulated, 1 - confidenceLevel);
    const varAmount = portfolioValue * Math.abs(varReturn);
    const sorted = [...simulated].sort((a, b) => a - b);
    const worstSims = sorted.slice(0, 5).map(r => round(r * 100, 2));
    const clPct = round(confidenceLevel * 100, 4);
    return {
        var_amount: round(varAmount, 2),
        var_percentage: round(Math.abs(varReturn) * 100, 2),
        num_simulations: numSimulations,
        worst_5_simulations_pct: worstSims,
        mean_simulated_return: round(mean(simulated), 6),
        std_simulated_return: round(stdDev(simulated), 6),
        interpretation: `Based on ${groupInt(numSimulations)} Monte Carlo simulations, at ${clPct}% confidence, potential loss should not exceed $${usd(round(varAmount, 2))} over ${timeHorizon} day(s)`
    };
}

export const varCalculation = toolBuilder()
    .name("var_calculation")
    .description("Calculates Value at Risk (VaR) using multiple methodologies: parametric (assumes normal distribution), historical (empirical distribution), and Monte Carlo simulation. VaR estimates the maximum potential loss over a given time horizon at a specified confidence level.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                portfolio_value: { type: "number", description: "Current portfolio value in USD" },
                returns_data: { type: "array", description: "Historical returns data (daily returns as percentages or decimals)" },
                confidence_level: { type: "number", description: "Confidence level for VaR calculation (e.g., 0.95, 0.99)", default: 0.95 },
                time_horizon: { type: "integer", description: "Time horizon in days", default: 1 },
                method: { type: "string", description: "Calculation method: 'parametric', 'historical', 'monte_carlo', 'all'", default: "all" },
                monte_carlo_simulations: { type: "integer", description: "Number of Monte Carlo simulations (if method is 'monte_carlo')", default: 10000 }
            },
            required: ["portfolio_value", "returns_data"]
        },
        output: {
            type: "object",
            properties: {
                portfolio_value: { type: "number" },
                confidence_level: { type: "number" },
                time_horizon_days: { type: "integer" },
                timestamp: { type: "string" },
                parametric_var: { type: "object" },
                historical_var: { type: "object" },
                monte_carlo_var: { type: "object" },
                summary: { type: "object" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const portfolioValue = Number(input.portfolio_value);
        const confidenceLevel = input.confidence_level ?? 0.95;
        const timeHorizon = input.time_horizon ?? 1;
        const method = input.method ?? "all";
        const simulations = input.monte_carlo_simulations ?? 10000;
        const returns = requireReturns(input);
        const includeAll = method === "all";
        return {
            portfolio_value: portfolioValue,
            confidence_level: confidenceLevel,
            time_horizon_days: timeHorizon,
            timestamp: isoNow(),
            parametric_var: includeAll || method === "parametric"
                ? parametricVar(portfolioValue, returns, confidenceLevel, timeHorizon)
                : null,
            historical_var: includeAll || method === "historical"
                ? historicalVar(portfolioValue, returns, confidenceLevel, timeHorizon)
                : null,
            monte_carlo_var: includeAll || method === "monte_carlo"
                ? monteCarloVar(portfolioValue, returns, confidenceLevel, timeHorizon,
                    simulations, JSON.stringify(input))
                : null,
            summary: {
                mean_return: round(mean(returns), 6),
                std_deviation: round(stdDev(returns), 6),
                skewness: round(skewness(returns), 4),
                kurtosis: round(kurtosis(returns), 4),
                data_points: returns.length
            }
        };
    })
    .build();

// ---------------------------------------------------------------------------
// cvar_calculation
// ---------------------------------------------------------------------------

function historicalCVar(
    portfolioValue: number, returns: number[], confidenceLevel: number, timeHorizon: number) {
    const sorted = [...returns].sort((a, b) => a - b);
    // VaR threshold: empirical left-tail quantile
    const varReturn = quantile(returns, 1 - confidenceLevel);
    // CVaR is the average of the tail at or beyond the VaR threshold
    const tailLosses = sorted.filter(r => r <= varReturn);
    const cvarReturn = tailLosses.length > 0 ? mean(tailLosses) : varReturn;

    const scaledVarReturn = varReturn * Math.sqrt(timeHorizon);
    const scaledCVarReturn = cvarReturn * Math.sqrt(timeHorizon);
    const varAmount = portfolioValue * Math.abs(scaledVarReturn);
    const cvarAmount = portfolioValue * Math.abs(scaledCVarReturn);
    const tailRiskPremium = cvarAmount - varAmount;
    const tailRiskPremiumPct = (Math.abs(scaledCVarReturn) - Math.abs(scaledVarReturn)) * 100;

    return {
        cvar_amount: round(cvarAmount, 2),
        cvar_percentage: round(Math.abs(scaledCVarReturn) * 100, 2),
        var_amount: round(varAmount, 2),
        var_percentage: round(Math.abs(scaledVarReturn) * 100, 2),
        tail_risk_premium: round(tailRiskPremium, 2),
        tail_risk_premium_pct: round(tailRiskPremiumPct, 2),
        tail_observations: tailLosses.length,
        worst_losses: tailLosses.slice(0, 5).map(r => round(r * 100, 2)),
        cvar_to_var_ratio: varAmount !== 0 ? round(cvarAmount / varAmount, 2) : 1,
        interpretation: `Given a loss exceeding VaR ($${usd(round(varAmount, 2))}), ` +
            `the expected loss is $${usd(round(cvarAmount, 2))} over ${timeHorizon} day(s). ` +
            `Tail risk premium: $${usd(round(tailRiskPremium, 2))}`
    };
}

function parametricCVar(
    portfolioValue: number, returns: number[], confidenceLevel: number, timeHorizon: number) {
    const meanReturn = mean(returns);
    const sd = stdDev(returns);
    const scaledMean = meanReturn * timeHorizon;
    const scaledStdDev = sd * Math.sqrt(timeHorizon);
    // For a normal distribution: CVaR = mu - sigma * phi(z) / alpha
    const alpha = 1 - confidenceLevel;
    // Same fixed z-score table as the C# original
    let zVaR = -2.326;
    if (confidenceLevel === 0.95) zVaR = -1.645;
    else if (confidenceLevel === 0.99) zVaR = -2.326;
    else if (confidenceLevel === 0.999) zVaR = -3.090;

    const phiZ = Math.exp(-0.5 * zVaR * zVaR) / Math.sqrt(2 * Math.PI);
    const cvarReturn = scaledMean + scaledStdDev * phiZ / alpha;
    const varReturn = scaledMean + zVaR * scaledStdDev;

    const varAmount = portfolioValue * Math.abs(varReturn);
    const cvarAmount = portfolioValue * Math.abs(cvarReturn);
    const tailRiskPremium = cvarAmount - varAmount;

    return {
        cvar_amount: round(cvarAmount, 2),
        cvar_percentage: round(Math.abs(cvarReturn) * 100, 2),
        var_amount: round(varAmount, 2),
        var_percentage: round(Math.abs(varReturn) * 100, 2),
        tail_risk_premium: round(tailRiskPremium, 2),
        tail_risk_premium_pct: 0,
        tail_observations: 0,
        worst_losses: [] as number[],
        cvar_to_var_ratio: varAmount !== 0 ? round(cvarAmount / varAmount, 2) : 1,
        interpretation: `Under normal distribution assumption, given a loss exceeding VaR ($${usd(round(varAmount, 2))}), ` +
            `the expected loss is $${usd(round(cvarAmount, 2))} over ${timeHorizon} day(s)`
    };
}

export const cvarCalculation = toolBuilder()
    .name("cvar_calculation")
    .description("Calculates Conditional Value at Risk (CVaR), also known as Expected Shortfall (ES). CVaR measures the expected loss given that the loss exceeds the VaR threshold, providing a more conservative risk measure that accounts for tail risk.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                portfolio_value: { type: "number", description: "Current portfolio value in USD" },
                returns_data: { type: "array", description: "Historical returns data (daily returns as percentages or decimals)" },
                confidence_level: { type: "number", description: "Confidence level for CVaR calculation (e.g., 0.95, 0.99)", default: 0.95 },
                time_horizon: { type: "integer", description: "Time horizon in days", default: 1 },
                method: { type: "string", description: "Calculation method: 'historical', 'parametric'", default: "historical" }
            },
            required: ["portfolio_value", "returns_data"]
        },
        output: {
            type: "object",
            properties: {
                portfolio_value: { type: "number" },
                confidence_level: { type: "number" },
                time_horizon_days: { type: "integer" },
                method: { type: "string" },
                timestamp: { type: "string" },
                cvar_amount: { type: "number" },
                cvar_percentage: { type: "number" },
                var_amount: { type: "number" },
                var_percentage: { type: "number" },
                tail_risk_premium: { type: "number" },
                tail_risk_premium_pct: { type: "number" },
                tail_observations: { type: "integer" },
                worst_losses: { type: "array" },
                cvar_to_var_ratio: { type: "number" },
                interpretation: { type: "string" },
                distribution_stats: { type: "object" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const portfolioValue = Number(input.portfolio_value);
        const confidenceLevel = input.confidence_level ?? 0.95;
        const timeHorizon = input.time_horizon ?? 1;
        const method = input.method ?? "historical";
        const returns = requireReturns(input);
        // Any unknown method falls back to historical, as in the C# original
        const m = method === "parametric"
            ? parametricCVar(portfolioValue, returns, confidenceLevel, timeHorizon)
            : historicalCVar(portfolioValue, returns, confidenceLevel, timeHorizon);
        return {
            portfolio_value: portfolioValue,
            confidence_level: confidenceLevel,
            time_horizon_days: timeHorizon,
            method,
            timestamp: isoNow(),
            cvar_amount: m.cvar_amount,
            cvar_percentage: m.cvar_percentage,
            var_amount: m.var_amount,
            var_percentage: m.var_percentage,
            tail_risk_premium: m.tail_risk_premium,
            tail_risk_premium_pct: m.tail_risk_premium_pct,
            tail_observations: m.tail_observations,
            worst_losses: m.worst_losses,
            cvar_to_var_ratio: m.cvar_to_var_ratio,
            interpretation: m.interpretation,
            distribution_stats: {
                mean_return: round(mean(returns), 6),
                std_deviation: round(stdDev(returns), 6),
                skewness: round(skewness(returns), 4),
                min_return: round(Math.min(...returns), 6),
                max_return: round(Math.max(...returns), 6)
            }
        };
    })
    .build();

// ---------------------------------------------------------------------------
// stress_testing
// ---------------------------------------------------------------------------

interface StressScenario {
    name: string;
    description: string;
    equityShock: number;
    bondShock: number;
    volatilityShock: number;
    correlationShock: number;
    creditSpreadShock: number;
    duration: string;
}

// Historical crisis scenarios — names and shocks carried over verbatim from C#
const historicalScenarios: Record<string, StressScenario> = {
    "2008_financial_crisis": {
        name: "2008 Financial Crisis",
        description: "Global financial crisis, Lehman Brothers collapse",
        equityShock: -0.37,        // S&P 500 down 37%
        bondShock: 0.08,           // Flight to quality
        volatilityShock: 2.5,      // VIX spike to 80+
        correlationShock: 0.25,    // Correlations increase in crisis
        creditSpreadShock: 0.06,   // Credit spreads widen 600 bps
        duration: "12 months"
    },
    "2020_covid_crash": {
        name: "2020 COVID-19 Crash",
        description: "Pandemic-driven market crash",
        equityShock: -0.34,        // S&P 500 down 34% (peak to trough)
        bondShock: 0.12,           // Treasuries rally
        volatilityShock: 3.0,      // VIX spike to 82
        correlationShock: 0.30,    // Everything sold off together
        creditSpreadShock: 0.04,   // Credit spreads widen
        duration: "1 month"
    },
    "2022_bear_market": {
        name: "2022 Bear Market",
        description: "Fed rate hikes, inflation concerns",
        equityShock: -0.25,        // S&P 500 down 25%
        bondShock: -0.13,          // Bonds down with stocks (unusual)
        volatilityShock: 1.8,      // Elevated VIX
        correlationShock: 0.15,    // 60/40 portfolio suffered
        creditSpreadShock: 0.02,   // Moderate widening
        duration: "10 months"
    },
    "black_monday_1987": {
        name: "Black Monday 1987",
        description: "Largest single-day stock market crash",
        equityShock: -0.23,        // Dow down 22.6% in one day
        bondShock: 0.05,           // Flight to quality
        volatilityShock: 4.0,      // Extreme volatility
        correlationShock: 0.20,    // Global contagion
        creditSpreadShock: 0.03,
        duration: "1 day"
    },
    "dot_com_bubble_2000": {
        name: "Dot-com Bubble Burst 2000-2002",
        description: "Technology stock crash",
        equityShock: -0.49,        // NASDAQ down 49%
        bondShock: 0.10,           // Safe haven demand
        volatilityShock: 2.0,      // High volatility
        correlationShock: 0.10,    // Sector-specific initially
        creditSpreadShock: 0.03,
        duration: "30 months"
    }
};

function shockForAssetClass(assetClass: string, scenario: StressScenario): number {
    switch (String(assetClass ?? "").toLowerCase()) {
        case "equity": case "stock": case "etf": return scenario.equityShock;
        case "bond": case "fixed_income": return scenario.bondShock;
        case "commodity": return scenario.equityShock * 0.8;   // Commodities correlate with risk-off
        case "real_estate": case "reit": return scenario.equityShock * 0.7;
        case "cash": return 0;
        case "crypto": case "cryptocurrency": return scenario.equityShock * 1.5; // Higher volatility
        case "alternative": return scenario.equityShock * 0.5;
        default: return scenario.equityShock * 0.6;            // Default
    }
}

function simulateScenario(positions: any[], portfolioValue: number, scenario: StressScenario) {
    let totalLoss = 0;
    const positionImpacts: Record<string, unknown>[] = [];

    for (const position of positions) {
        const weight = Number(position.weight ?? 0);
        const positionValue = portfolioValue * weight;
        const shock = shockForAssetClass(position.asset_class, scenario);
        const positionLoss = positionValue * shock;
        totalLoss += positionLoss;
        positionImpacts.push({
            symbol: position.symbol,
            asset_class: position.asset_class,
            weight: round(weight * 100, 2),
            value_before: round(positionValue, 2),
            shock_percentage: round(shock * 100, 2),
            loss_amount: round(Math.abs(positionLoss), 2),
            value_after: round(positionValue + positionLoss, 2)
        });
    }

    const portfolioValueAfter = portfolioValue + totalLoss;
    const lossPercentage = (totalLoss / portfolioValue) * 100;

    return {
        scenario_name: scenario.name,
        description: scenario.description,
        duration: scenario.duration,
        portfolio_value_before: round(portfolioValue, 2),
        portfolio_value_after: round(portfolioValueAfter, 2),
        loss_amount: round(Math.abs(totalLoss), 2),
        loss_percentage: round(lossPercentage, 2),
        position_impacts: positionImpacts,
        shocks_applied: {
            equity_shock: round(scenario.equityShock * 100, 1),
            bond_shock: round(scenario.bondShock * 100, 1),
            volatility_multiplier: round(scenario.volatilityShock, 2),
            correlation_increase: round(scenario.correlationShock * 100, 1)
        }
    };
}

function resilienceScore(results: any[]): number {
    // Score based on average loss and worst-case loss
    const avgLossPct = mean(results.map(r => Math.abs(r.loss_percentage)));
    const maxLossPct = Math.max(...results.map(r => Math.abs(r.loss_percentage)));
    const score = 100 - (avgLossPct * 0.6 + maxLossPct * 0.4);
    return Math.max(0, round(score, 1));
}

function stressRecommendations(results: any[], positions: any[]): string[] {
    const recommendations: string[] = [];
    const avgLossPct = mean(results.map(r => Math.abs(r.loss_percentage)));
    const maxLossPct = Math.max(...results.map(r => Math.abs(r.loss_percentage)));

    // Analyze vulnerability
    if (maxLossPct > 40) {
        recommendations.push("⚠️ CRITICAL: Portfolio could lose more than 40% in severe stress scenarios. Consider significant risk reduction.");
    } else if (maxLossPct > 30) {
        recommendations.push("⚠️ HIGH RISK: Maximum stress loss exceeds 30%. Recommend increasing defensive positions.");
    } else if (maxLossPct > 20) {
        recommendations.push("⚠️ MODERATE RISK: Maximum stress loss between 20-30%. Consider modest hedging strategies.");
    } else {
        recommendations.push("✓ RESILIENT: Portfolio shows good resilience to historical stress scenarios.");
    }

    // Analyze asset class concentration
    const equityWeight = positions
        .filter(p => {
            const ac = String(p.asset_class ?? "").toLowerCase();
            return ac === "equity" || ac === "stock";
        })
        .reduce((s, p) => s + Number(p.weight ?? 0), 0);
    if (equityWeight > 0.8) {
        recommendations.push(`Consider diversification: Portfolio is ${round(equityWeight * 100, 0)}% equities. Add bonds or alternative assets.`);
    }

    // Hedging recommendations
    if (avgLossPct > 25) {
        recommendations.push("Recommend implementing tail risk hedging strategies (put options, volatility hedges).");
    }
    if (maxLossPct > 35) {
        recommendations.push("Consider increasing cash allocation by 10-15% to provide dry powder during crises.");
    }

    // Position-specific recommendations: worst average losers across scenarios
    const bySymbol: Record<string, { sum: number; count: number }> = {};
    for (const r of results) {
        for (const impact of r.position_impacts) {
            const key = String(impact.symbol);
            bySymbol[key] ??= { sum: 0, count: 0 };
            bySymbol[key].sum += Math.abs(Number(impact.loss_amount));
            bySymbol[key].count += 1;
        }
    }
    const worstPositions = Object.entries(bySymbol)
        .map(([symbol, agg]) => ({ symbol, avgLoss: agg.sum / agg.count }))
        .sort((a, b) => b.avgLoss - a.avgLoss)
        .slice(0, 3);
    if (worstPositions.length > 0) {
        recommendations.push(`Most vulnerable positions: ${worstPositions.map(p => p.symbol).join(", ")}. Consider reducing exposure or hedging.`);
    }

    return recommendations;
}

export const stressTesting = toolBuilder()
    .name("stress_testing")
    .description("Performs stress testing on portfolio using historical crisis scenarios (2008, 2020, 2022, 1987, 2000) and custom shock scenarios. Assesses portfolio resilience, identifies vulnerabilities, and provides risk mitigation recommendations.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                portfolio_positions: { type: "array", description: "Portfolio positions with symbol, weight, asset_class" },
                portfolio_value: { type: "number", description: "Current portfolio value in USD" },
                scenarios: { type: "array", description: "Scenarios to test: ['2008_financial_crisis', '2020_covid_crash', '2022_bear_market', 'black_monday_1987', 'dot_com_bubble_2000', 'all']", default: ["all"] },
                custom_shocks: { type: "object", description: "Custom shock scenario: {equity_shock: -0.20, bond_shock: 0.05, ...}" }
            },
            required: ["portfolio_positions", "portfolio_value"]
        },
        output: {
            type: "object",
            properties: {
                portfolio_value: { type: "number" },
                timestamp: { type: "string" },
                scenarios_tested: { type: "integer" },
                scenario_results: { type: "array" },
                worst_case_scenario: { type: "object" },
                summary: { type: "object" },
                recommendations: { type: "array" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const positions: any[] = Array.isArray(input.portfolio_positions) ? input.portfolio_positions : [];
        const portfolioValue = Number(input.portfolio_value);
        const scenarios: string[] = Array.isArray(input.scenarios) && input.scenarios.length > 0
            ? input.scenarios : ["all"];
        const customShocks = input.custom_shocks ?? null;
        const includeAll = scenarios.includes("all");

        const scenarioResults: any[] = [];
        for (const key of Object.keys(historicalScenarios)) {
            if (includeAll || scenarios.includes(key)) {
                scenarioResults.push(simulateScenario(positions, portfolioValue, historicalScenarios[key]));
            }
        }

        // Custom scenario, if provided
        if (customShocks && Object.keys(customShocks).length > 0) {
            scenarioResults.push(simulateScenario(positions, portfolioValue, {
                name: "Custom Shock Scenario",
                description: "User-defined custom shock scenario",
                equityShock: Number(customShocks.equity_shock ?? 0),
                bondShock: Number(customShocks.bond_shock ?? 0),
                volatilityShock: Number(customShocks.volatility_shock ?? 1),
                correlationShock: Number(customShocks.correlation_shock ?? 0),
                creditSpreadShock: Number(customShocks.credit_spread_shock ?? 0),
                duration: "Custom"
            }));
        }

        // Worst case: lowest portfolio value after shock
        const worstCase = [...scenarioResults]
            .sort((a, b) => a.portfolio_value_after - b.portfolio_value_after)[0];

        return {
            portfolio_value: portfolioValue,
            timestamp: isoNow(),
            scenarios_tested: scenarioResults.length,
            scenario_results: scenarioResults,
            worst_case_scenario: worstCase,
            summary: {
                average_loss_pct: round(mean(scenarioResults.map(r => r.loss_percentage)), 2),
                max_loss_pct: round(Math.max(...scenarioResults.map(r => r.loss_percentage)), 2),
                min_loss_pct: round(Math.min(...scenarioResults.map(r => r.loss_percentage)), 2),
                portfolio_resilience_score: resilienceScore(scenarioResults)
            },
            recommendations: stressRecommendations(scenarioResults, positions)
        };
    })
    .build();

// ---------------------------------------------------------------------------
// factor_exposure (with the mock factor model provider folded in)
// ---------------------------------------------------------------------------

const standardFactors = ["MARKET", "SIZE", "VALUE", "MOMENTUM", "VOLATILITY", "QUALITY", "LIQUIDITY"];

function mockMarketBeta(position: any, rand: () => number): number {
    switch (String(position.asset_class ?? "").toLowerCase()) {
        case "equity": case "stock": return 1.0 + (rand() * 0.4 - 0.2); // 0.8 to 1.2
        case "bond": case "fixed_income": return 0.2;
        case "commodity": return 0.8;
        case "crypto": return 1.5;
        case "reit": return 0.9;
        default: return 0.5;
    }
}

function mockLiquidityLoading(position: any): number {
    switch (String(position.asset_class ?? "").toLowerCase()) {
        case "equity": case "stock": return -0.2;
        case "bond": return 0.3;
        case "crypto": return 0.4;
        default: return 0.1;
    }
}

/**
 * Mock factor model provider — the port of MockFactorModelProvider. Loadings
 * are DETERMINISTIC: the PRNG is seeded from symbol+factor, so the same
 * position always gets the same loadings, in the same plausible ranges.
 */
function mockFactorLoading(position: any, factor: string): number {
    const rand = mulberry32(seedFrom(String(position.symbol ?? "") + factor));
    switch (factor) {
        case "MARKET": return mockMarketBeta(position, rand);
        case "SIZE": return rand() * 2 - 1;            // -1.0 to +1.0
        case "VALUE": return rand() * 1.5 - 0.5;       // -0.5 to +1.0
        case "MOMENTUM": return rand() * 2 - 1;        // -1.0 to +1.0
        case "VOLATILITY": return rand() * 0.8 - 0.4;  // -0.4 to +0.4
        case "QUALITY": return rand();                 // 0 to +1.0
        case "LIQUIDITY": return mockLiquidityLoading(position);
        default: return 0;
    }
}

function factorVolatility(factor: string): number {
    switch (factor) {
        case "MARKET": return 0.16;
        case "SIZE": return 0.12;
        case "VALUE": return 0.10;
        case "MOMENTUM": return 0.15;
        case "VOLATILITY": return 0.08;
        case "QUALITY": return 0.07;
        case "LIQUIDITY": return 0.09;
        default: return 0.10;
    }
}

function decomposeRisk(exposures: Record<string, number>) {
    const factorVariances: Record<string, number> = {};
    let totalFactorVariance = 0;
    for (const factor of Object.keys(exposures)) {
        const vol = factorVolatility(factor);
        const variance = exposures[factor] * exposures[factor] * vol * vol;
        factorVariances[factor] = variance;
        totalFactorVariance += variance;
    }

    const residualVariance = 0.03 * 0.03;
    const totalVariance = totalFactorVariance + residualVariance;
    const totalVolatility = Math.sqrt(totalVariance);
    const systematicRisk = Math.sqrt(totalFactorVariance);
    const idiosyncraticRisk = Math.sqrt(residualVariance);
    const systematicRiskPct = totalVariance > 0 ? (totalFactorVariance / totalVariance) * 100 : 0;

    const factorRiskContributions: Record<string, unknown> = {};
    for (const [factor, variance] of Object.entries(factorVariances)) {
        factorRiskContributions[factor] = {
            variance: round(variance, 6),
            volatility: round(Math.sqrt(variance), 4),
            contribution_pct: round(totalVariance > 0 ? (variance / totalVariance) * 100 : 0, 2)
        };
    }

    return {
        total_volatility: round(totalVolatility * 100, 2),
        systematic_risk: round(systematicRisk * 100, 2),
        idiosyncratic_risk: round(idiosyncraticRisk * 100, 2),
        systematic_risk_percentage: round(systematicRiskPct, 2),
        idiosyncratic_risk_percentage: round(100 - systematicRiskPct, 2),
        factor_risk_contributions: factorRiskContributions,
        top_risk_factors: Object.entries(factorVariances)
            .sort((a, b) => b[1] - a[1]).slice(0, 3).map(([factor]) => factor)
    };
}

function factorConcentrations(exposures: Record<string, number>) {
    return Object.entries(exposures)
        .sort((a, b) => Math.abs(b[1]) - Math.abs(a[1]))
        .filter(([, e]) => Math.abs(e) > 0.5)
        .map(([factor, e]) => {
            const abs = Math.abs(e);
            return {
                factor,
                exposure: round(e, 3),
                abs_exposure: round(abs, 3),
                severity: abs > 1.0 ? "HIGH" : abs > 0.75 ? "MODERATE" : "LOW",
                direction: e > 0 ? "POSITIVE" : "NEGATIVE"
            };
        });
}

function factorRecommendations(exposures: Record<string, number>, riskDecomp: any): string[] {
    const recommendations: string[] = [];

    const highExposures = Object.entries(exposures).filter(([, e]) => Math.abs(e) > 0.75);
    if (highExposures.length > 0) {
        recommendations.push(`⚠️ High factor concentration in: ${highExposures.map(([f]) => f).join(", ")}. Consider diversifying across factors.`);
    }

    if (riskDecomp.systematic_risk_percentage < 70) {
        recommendations.push("⚠️ High idiosyncratic risk. Portfolio may not be well-diversified. Consider adding more positions.");
    }

    const marketExposure = exposures["MARKET"] ?? 0;
    if (marketExposure > 1.2) {
        recommendations.push(`⚠️ High market beta (${round(marketExposure, 2)}). Consider adding defensive positions.`);
    } else if (marketExposure < 0.8) {
        recommendations.push(`ℹ️ Low market beta (${round(marketExposure, 2)}). May underperform in bull markets.`);
    }

    const sizeExposure = exposures["SIZE"] ?? 0;
    if (Math.abs(sizeExposure) > 0.6) {
        recommendations.push(`ℹ️ Significant ${sizeExposure > 0 ? "small-cap" : "large-cap"} bias. Consider balancing.`);
    }

    const valueExposure = exposures["VALUE"] ?? 0;
    if (Math.abs(valueExposure) > 0.6) {
        recommendations.push(`ℹ️ Strong ${valueExposure > 0 ? "value" : "growth"} tilt. Consider adding ${valueExposure > 0 ? "growth" : "value"} stocks.`);
    }

    if (recommendations.length === 0) {
        recommendations.push("✓ Factor exposures are well-balanced. No major concentration risks identified.");
    }
    return recommendations;
}

export const factorExposure = toolBuilder()
    .name("factor_exposure")
    .description("Analyzes portfolio factor exposures using multi-factor risk model (Barra-style). Decomposes risk into systematic factors: market, size, value, momentum, volatility, quality, liquidity. Identifies factor concentrations, diversification, and residual risk.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                portfolio_positions: { type: "array", description: "Portfolio positions with symbol, weight, and optional factor loadings" },
                factor_returns: { type: "object", description: "Dictionary of factor name -> historical returns array (optional, will use defaults if not provided)" },
                benchmark: { type: "string", description: "Benchmark symbol for relative factor analysis (e.g., 'SPY')", default: "SPY" }
            },
            required: ["portfolio_positions"]
        },
        output: {
            type: "object",
            properties: {
                timestamp: { type: "string" },
                benchmark: { type: "string" },
                factor_exposures: { type: "object" },
                risk_decomposition: { type: "object" },
                factor_concentrations: { type: "array" },
                factor_correlation_matrix: { type: "object" },
                diversification_metrics: { type: "object" },
                recommendations: { type: "array" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const positions: any[] = Array.isArray(input.portfolio_positions) ? input.portfolio_positions : [];
        const benchmark = input.benchmark ?? "SPY";

        // Portfolio exposure = weight-averaged mock loadings per factor
        const exposures: Record<string, number> = {};
        for (const factor of standardFactors) exposures[factor] = 0;
        for (const position of positions) {
            const weight = Number(position.weight ?? 0);
            for (const factor of standardFactors) {
                exposures[factor] += weight * mockFactorLoading(position, factor);
            }
        }
        for (const factor of standardFactors) exposures[factor] = round(exposures[factor], 4);

        const riskDecomposition = decomposeRisk(exposures);

        return {
            timestamp: isoNow(),
            benchmark,
            factor_exposures: exposures,
            risk_decomposition: riskDecomposition,
            factor_concentrations: factorConcentrations(exposures),
            // Static Barra-style factor correlations, as in the C# original
            factor_correlation_matrix: {
                MARKET_VALUE: -0.2, MARKET_MOMENTUM: 0.3,
                SIZE_VALUE: 0.4, SIZE_VOLATILITY: 0.5,
                MOMENTUM_VOLATILITY: -0.3, QUALITY_VOLATILITY: -0.6
            },
            diversification_metrics: (() => {
                const absExposures = standardFactors.map(f => Math.abs(exposures[f]));
                const maxExposure = Math.max(...absExposures);
                const avgExposure = mean(absExposures);
                const concentrationRatio = avgExposure > 0 ? maxExposure / avgExposure : 1;
                return {
                    factor_diversification_score: round(Math.max(0, 100 - (concentrationRatio - 1) * 50), 1),
                    concentration_risk: round(standardFactors.reduce((s, f) => s + exposures[f] * exposures[f], 0) * 100, 2),
                    active_factor_risk: riskDecomposition.systematic_risk
                };
            })(),
            recommendations: factorRecommendations(exposures, riskDecomposition)
        };
    })
    .build();
