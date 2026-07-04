using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Risk.Contracts;
using Orkeon.Trading.Tools.Domain.Models;
using Orkeon.Trading.Tools.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Risk;

/// <summary>
/// Factor exposure analysis tool using Barra-style multi-factor risk model.
/// Decomposes portfolio risk into systematic factors via an injected <see cref="IFactorModelProvider"/>.
/// </summary>
public class FactorExposureTool(
    IFactorModelProvider factorModelProvider,
    ILogger<FactorExposureTool>? logger = null)
    : TradingToolBase<FactorExposureRequest, FactorExposureResponse>(logger)
{
    private readonly IFactorModelProvider _factorModelProvider = factorModelProvider;

    private readonly List<string> _standardFactors =
    [
        "MARKET", "SIZE", "VALUE", "MOMENTUM", "VOLATILITY", "QUALITY", "LIQUIDITY"
    ];

    protected override string ToolId => "factor_exposure";

    protected override async Task<FactorExposureResponse> ExecuteTypedAsync(
        FactorExposureRequest request,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Analyzing factor exposures for portfolio with {Count} positions via {Provider}",
            request.PortfolioPositions.Count, _factorModelProvider.ProviderName);

        var factorExposures = await CalculatePortfolioFactorExposuresAsync(
            request.PortfolioPositions, cancellationToken);

        var riskDecomposition = DecomposeRisk(request.PortfolioPositions, factorExposures);
        var concentrations = IdentifyFactorConcentrations(factorExposures);
        var recommendations = GenerateFactorRecommendations(factorExposures, riskDecomposition);

        _logger?.LogInformation("Factor exposure analysis completed");

        return new FactorExposureResponse
        {
            Timestamp = DateTime.UtcNow,
            Benchmark = request.Benchmark,
            FactorExposures = factorExposures,
            RiskDecomposition = riskDecomposition,
            FactorConcentrations = concentrations,
            FactorCorrelationMatrix = CalculateFactorCorrelations(factorExposures),
            DiversificationMetrics = new Dictionary<string, object>
            {
                ["factor_diversification_score"] = CalculateFactorDiversificationScore(factorExposures),
                ["concentration_risk"] = CalculateConcentrationRisk(factorExposures),
                ["active_factor_risk"] = riskDecomposition["systematic_risk"]
            },
            Recommendations = recommendations
        };
    }

    private async Task<Dictionary<string, decimal>> CalculatePortfolioFactorExposuresAsync(
        List<PortfolioPosition> positions,
        CancellationToken cancellationToken)
    {
        var exposures = new Dictionary<string, decimal>();
        foreach (var factor in _standardFactors)
            exposures[factor] = 0m;

        // Fetch factor loadings for each position from the provider
        foreach (var position in positions)
        {
            var loadings = await _factorModelProvider.GetFactorLoadingsAsync(
                position, _standardFactors, cancellationToken);

            foreach (var factor in _standardFactors)
            {
                if (loadings.TryGetValue(factor, out var loading))
                    exposures[factor] += position.Weight * loading;
            }
        }

        // Round results
        foreach (var factor in _standardFactors)
            exposures[factor] = Math.Round(exposures[factor], 4);

        return exposures;
    }

    private static Dictionary<string, object> DecomposeRisk(
        List<PortfolioPosition> positions,
        Dictionary<string, decimal> factorExposures)
    {
        var factorVariances = new Dictionary<string, decimal>();
        decimal totalFactorVariance = 0;

        foreach (var factor in factorExposures.Keys)
        {
            var factorVol = GetFactorVolatility(factor);
            var factorVariance = factorExposures[factor] * factorExposures[factor] * factorVol * factorVol;
            factorVariances[factor] = factorVariance;
            totalFactorVariance += factorVariance;
        }

        var residualVariance = 0.03m * 0.03m;
        var totalVariance = totalFactorVariance + residualVariance;
        var totalVolatility = (decimal)Math.Sqrt((double)totalVariance);
        var systematicRisk = (decimal)Math.Sqrt((double)totalFactorVariance);
        var idiosyncraticRisk = (decimal)Math.Sqrt((double)residualVariance);
        var systematicRiskPct = totalVariance > 0 ? (totalFactorVariance / totalVariance) * 100 : 0;

        var factorRiskContributions = factorVariances.ToDictionary(
            kvp => kvp.Key,
            kvp => new Dictionary<string, object>
            {
                ["variance"] = Math.Round(kvp.Value, 6),
                ["volatility"] = Math.Round((decimal)Math.Sqrt((double)kvp.Value), 4),
                ["contribution_pct"] = Math.Round(totalVariance > 0 ? (kvp.Value / totalVariance) * 100 : 0, 2)
            }
        );

        return new Dictionary<string, object>
        {
            ["total_volatility"] = Math.Round(totalVolatility * 100, 2),
            ["systematic_risk"] = Math.Round(systematicRisk * 100, 2),
            ["idiosyncratic_risk"] = Math.Round(idiosyncraticRisk * 100, 2),
            ["systematic_risk_percentage"] = Math.Round(systematicRiskPct, 2),
            ["idiosyncratic_risk_percentage"] = Math.Round(100 - systematicRiskPct, 2),
            ["factor_risk_contributions"] = factorRiskContributions,
            ["top_risk_factors"] = factorVariances.OrderByDescending(kvp => kvp.Value)
                .Take(3).Select(kvp => kvp.Key).ToList()
        };
    }

    private static decimal GetFactorVolatility(string factor) => factor switch
    {
        "MARKET" => 0.16m, "SIZE" => 0.12m, "VALUE" => 0.10m,
        "MOMENTUM" => 0.15m, "VOLATILITY" => 0.08m, "QUALITY" => 0.07m,
        "LIQUIDITY" => 0.09m, _ => 0.10m
    };

    private static List<Dictionary<string, object>> IdentifyFactorConcentrations(Dictionary<string, decimal> exposures)
    {
        var concentrations = new List<Dictionary<string, object>>();
        foreach (var exposure in exposures.OrderByDescending(e => Math.Abs(e.Value)))
        {
            var abs = Math.Abs(exposure.Value);
            if (abs > 0.5m)
                concentrations.Add(new Dictionary<string, object>
                {
                    ["factor"] = exposure.Key, ["exposure"] = Math.Round(exposure.Value, 3),
                    ["abs_exposure"] = Math.Round(abs, 3),
                    ["severity"] = abs > 1.0m ? "HIGH" : abs > 0.75m ? "MODERATE" : "LOW",
                    ["direction"] = exposure.Value > 0 ? "POSITIVE" : "NEGATIVE"
                });
        }
        return concentrations;
    }

    private static Dictionary<string, decimal> CalculateFactorCorrelations(Dictionary<string, decimal> exposures) => new()
    {
        ["MARKET_VALUE"] = -0.2m, ["MARKET_MOMENTUM"] = 0.3m,
        ["SIZE_VALUE"] = 0.4m, ["SIZE_VOLATILITY"] = 0.5m,
        ["MOMENTUM_VOLATILITY"] = -0.3m, ["QUALITY_VOLATILITY"] = -0.6m
    };

    private static decimal CalculateFactorDiversificationScore(Dictionary<string, decimal> exposures)
    {
        var absExposures = exposures.Values.Select(Math.Abs).ToList();
        var maxExposure = absExposures.Max();
        var avgExposure = absExposures.Average();
        var concentrationRatio = avgExposure > 0 ? maxExposure / avgExposure : 1;
        return Math.Round(Math.Max(0, 100 - (concentrationRatio - 1) * 50), 1);
    }

    private static decimal CalculateConcentrationRisk(Dictionary<string, decimal> exposures) =>
        Math.Round(exposures.Values.Sum(e => e * e) * 100, 2);

    private static List<string> GenerateFactorRecommendations(
        Dictionary<string, decimal> exposures, Dictionary<string, object> riskDecomp)
    {
        var recommendations = new List<string>();

        var highExposures = exposures.Where(e => Math.Abs(e.Value) > 0.75m).ToList();
        if (highExposures.Count > 0)
            recommendations.Add($"⚠️ High factor concentration in: {string.Join(", ", highExposures.Select(e => e.Key))}. Consider diversifying across factors.");

        var systematicPct = (decimal)riskDecomp["systematic_risk_percentage"];
        if (systematicPct < 70)
            recommendations.Add("⚠️ High idiosyncratic risk. Portfolio may not be well-diversified. Consider adding more positions.");

        var marketExposure = exposures.GetValueOrDefault("MARKET", 0);
        if (marketExposure > 1.2m)
            recommendations.Add($"⚠️ High market beta ({Math.Round(marketExposure, 2)}). Consider adding defensive positions.");
        else if (marketExposure < 0.8m)
            recommendations.Add($"ℹ️ Low market beta ({Math.Round(marketExposure, 2)}). May underperform in bull markets.");

        var sizeExposure = exposures.GetValueOrDefault("SIZE", 0);
        if (Math.Abs(sizeExposure) > 0.6m)
            recommendations.Add($"ℹ️ Significant {(sizeExposure > 0 ? "small-cap" : "large-cap")} bias. Consider balancing.");

        var valueExposure = exposures.GetValueOrDefault("VALUE", 0);
        if (Math.Abs(valueExposure) > 0.6m)
            recommendations.Add($"ℹ️ Strong {(valueExposure > 0 ? "value" : "growth")} tilt. Consider adding {(valueExposure > 0 ? "growth" : "value")} stocks.");

        if (recommendations.Count == 0)
            recommendations.Add("✓ Factor exposures are well-balanced. No major concentration risks identified.");

        return recommendations;
    }
}
