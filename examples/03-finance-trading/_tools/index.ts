// The trading tool module (EX-01) — the TypeScript successor of the retired
// Orkeon.Trading.Tools C# assembly. Twenty-seven tools, one per name the finance
// examples actually reference; the seventeen the catalog never used were not
// ported. Every tool is deterministic (seeded PRNGs, no wall-clock in the math)
// and self-describing (schemas translated verbatim from the YAML definitions).
//
// Usage from an example's main.ork.ts:
//   import { pickTools } from "../_tools/index.ts";
//   agentBuilder().withAutonomousTools(pickTools("technical_indicators", "var_calculation"))

import { correlationAnalysis, marketRegimeClassification, patternRecognition, technicalIndicators } from "./analysis.ts";
import { alternativeData, fundamentalData } from "./data.ts";
import { twapExecution, vwapExecution, smartOrderRouting } from "./execution.ts";
import { auditTrail, alertManagement, complianceCheck, dashboardMetrics, regulatoryReporting } from "./governance.ts";
import { meanVarianceOptimization, blackLitterman, hierarchicalRiskParity, riskParity, portfolioRebalancing } from "./portfolio.ts";
import { arimaPrediction, prophetPrediction, ensemblePrediction, backtesting } from "./prediction.ts";
import { varCalculation, cvarCalculation, stressTesting, factorExposure } from "./risk.ts";

export {
    correlationAnalysis, marketRegimeClassification, patternRecognition, technicalIndicators,
    alternativeData, fundamentalData,
    twapExecution, vwapExecution, smartOrderRouting,
    auditTrail, alertManagement, complianceCheck, dashboardMetrics, regulatoryReporting,
    meanVarianceOptimization, blackLitterman, hierarchicalRiskParity, riskParity, portfolioRebalancing,
    arimaPrediction, prophetPrediction, ensemblePrediction, backtesting,
    varCalculation, cvarCalculation, stressTesting, factorExposure,
};

export const allTradingTools = [
    correlationAnalysis, marketRegimeClassification, patternRecognition, technicalIndicators,
    alternativeData, fundamentalData,
    twapExecution, vwapExecution, smartOrderRouting,
    auditTrail, alertManagement, complianceCheck, dashboardMetrics, regulatoryReporting,
    meanVarianceOptimization, blackLitterman, hierarchicalRiskParity, riskParity, portfolioRebalancing,
    arimaPrediction, prophetPrediction, ensemblePrediction, backtesting,
    varCalculation, cvarCalculation, stressTesting, factorExposure,
];

const byName: Record<string, unknown> = {};
for (const tool of allTradingTools) byName[(tool as any).name] = tool;

/**
 * The tools an example declares, by their snake_case names — a loud error on a
 * typo, so a migration cannot silently drop a tool the YAML used to carry.
 */
export function pickTools(...names: string[]) {
    return names.map(n => {
        const tool = byName[n];
        if (!tool) throw new Error(`Unknown trading tool '${n}'. Known: ${Object.keys(byName).sort().join(", ")}`);
        return tool;
    });
}
