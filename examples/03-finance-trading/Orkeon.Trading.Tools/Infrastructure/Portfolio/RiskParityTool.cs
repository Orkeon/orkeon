using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Portfolio.Contracts;
using Orkeon.Trading.Tools.Domain.Services;
using Microsoft.Extensions.Logging;
using MathNet.Numerics.Statistics;

namespace Orkeon.Trading.Tools.Infrastructure.Portfolio;

/// <summary>
/// Risk Parity portfolio optimization tool for equal risk contribution across assets.
/// Allocates capital so each asset contributes equally to portfolio risk.
/// Particularly effective for multi-asset class portfolios (stocks, bonds, commodities).
/// </summary>
public class RiskParityTool(ILogger<RiskParityTool>? logger = null)
    : TradingToolBase<RiskParityRequest, RiskParityResponse>(logger)
{

    protected override string ToolId => "risk_parity";

    protected override string? ValidateTypedRequest(RiskParityRequest request)
    {
        if (request.Symbols.Count < 2)
            return "Risk Parity requires at least 2 assets for diversification. Cannot optimize with single asset.";
        return null;
    }

    protected override async Task<RiskParityResponse> ExecuteTypedAsync(
        RiskParityRequest request,
        CancellationToken cancellationToken)
    {
        // Deduplicate symbols to prevent 'duplicate key' errors in dictionaries
        var symbols = request.Symbols.Distinct().ToList();

        _logger?.LogInformation("Running Risk Parity optimization for {Count} symbols", symbols.Count);

        var optimization = await Task.Run(() => OptimizeRiskParity(
            symbols,
            request.ReturnsData,
            request.TargetRiskContributions,
            request.RiskFreeRate), cancellationToken);

        _logger?.LogInformation("Risk Parity optimization completed");

        return optimization;
    }

    private static RiskParityResponse OptimizeRiskParity(
        List<string> symbols,
        Dictionary<string, List<double>> returnsData,
        Dictionary<string, double>? targetRiskContributions,
        double riskFreeRate)
    {
        // Step 1: Calculate covariance matrix
        var covarianceMatrix = FinancialMathHelper.CalculateCovarianceMatrix(symbols, returnsData);

        // Step 2: Calculate expected returns
        var expectedReturns = symbols.ToDictionary(
            s => s,
            s => returnsData.TryGetValue(s, out var r) ? r.Mean() : 0.0
        );

        // Step 3: Set target risk contributions (default: equal for all assets)
        var targetContributions = targetRiskContributions ?? symbols.ToDictionary(s => s, s => 1.0 / symbols.Count);

        // Step 4: Find Risk Parity weights
        var optimalWeights = FindRiskParityWeights(symbols, covarianceMatrix, targetContributions);

        // Step 5: Calculate actual risk contributions
        var riskContributions = CalculateRiskContributions(optimalWeights, covarianceMatrix);

        // Step 6: Calculate portfolio metrics
        var portfolioReturn = optimalWeights.Sum(w => w.Value * expectedReturns.GetValueOrDefault(w.Key, 0));
        var portfolioVolatility = FinancialMathHelper.CalculatePortfolioVolatility(optimalWeights, covarianceMatrix);
        var sharpeRatio = portfolioVolatility > 0 ? (portfolioReturn - riskFreeRate) / portfolioVolatility : 0;

        // Step 7: Calculate marginal risk contributions
        var marginalRiskContributions = CalculateMarginalRiskContributions(optimalWeights, covarianceMatrix);

        return new RiskParityResponse
        {
            Timestamp = DateTime.UtcNow,
            Method = "RISK_PARITY",
            Symbols = symbols,
            OptimalWeights = optimalWeights.ToDictionary(kvp => kvp.Key, kvp => Math.Round((decimal)kvp.Value, 4)),
            RiskContributions = riskContributions.ToDictionary(kvp => kvp.Key, kvp => Math.Round((decimal)(kvp.Value * 100), 2)),
            TargetRiskContributions = targetContributions.ToDictionary(kvp => kvp.Key, kvp => Math.Round((decimal)(kvp.Value * 100), 2)),
            MarginalRiskContributions = marginalRiskContributions.ToDictionary(kvp => kvp.Key, kvp => Math.Round((decimal)(kvp.Value * 100), 4)),
            ExpectedReturn = Math.Round((decimal)(portfolioReturn * 100), 2),
            ExpectedVolatility = Math.Round((decimal)(portfolioVolatility * 100), 2),
            SharpeRatio = Math.Round((decimal)sharpeRatio, 2),
            DiversificationRatio = CalculateDiversificationRatio(optimalWeights, covarianceMatrix),
            RiskBalanceScore = CalculateRiskBalanceScore(riskContributions, targetContributions)
        };
    }

    private static Dictionary<string, double> FindRiskParityWeights(
        List<string> symbols,
        Dictionary<string, Dictionary<string, double>> covarianceMatrix,
        Dictionary<string, double> targetContributions)
    {
        // Risk Parity: Find weights where risk contribution = target contribution
        // Risk Contribution_i = w_i * (Σ * w)_i / (w' * Σ * w)
        // Use iterative optimization to solve

        var weights = symbols.ToDictionary(s => s, s => 1.0 / symbols.Count); // Start with equal weights

        const int maxIterations = 1000;
        const double tolerance = 1e-6;

        for (int iter = 0; iter < maxIterations; iter++)
        {
            var riskContributions = CalculateRiskContributions(weights, covarianceMatrix);

            // Check convergence
            var maxError = riskContributions.Max(rc =>
                Math.Abs(rc.Value - targetContributions.GetValueOrDefault(rc.Key, 1.0 / symbols.Count)));

            if (maxError < tolerance)
                break;

            // Update weights using gradient descent
            var newWeights = new Dictionary<string, double>();

            foreach (var symbol in symbols)
            {
                var currentRC = riskContributions[symbol];
                var targetRC = targetContributions.GetValueOrDefault(symbol, 1.0 / symbols.Count);

                // Adjust weight proportionally to error
                var adjustment = 1.0 + 0.1 * (targetRC - currentRC); // Learning rate = 0.1
                newWeights[symbol] = Math.Max(0, weights[symbol] * adjustment);
            }

            // Normalize weights
            var totalWeight = newWeights.Values.Sum();
            foreach (var symbol in symbols)
            {
                weights[symbol] = newWeights[symbol] / totalWeight;
            }
        }

        return weights;
    }

    private static Dictionary<string, double> CalculateRiskContributions(
        Dictionary<string, double> weights,
        Dictionary<string, Dictionary<string, double>> covarianceMatrix)
    {
        // Risk Contribution_i = w_i * (Σ * w)_i / sqrt(w' * Σ * w)

        var portfolioVariance = 0.0;
        var marginalContributions = new Dictionary<string, double>();

        // Calculate (Σ * w) for each asset
        foreach (var symbol in weights.Keys)
        {
            double sumCovWeight = 0;
            foreach (var symbol2 in weights.Keys)
            {
                var cov = covarianceMatrix.GetValueOrDefault(symbol)?.GetValueOrDefault(symbol2) ?? 0;
                sumCovWeight += cov * weights[symbol2];
            }
            marginalContributions[symbol] = sumCovWeight;
        }

        // Calculate portfolio variance
        foreach (var symbol in weights.Keys)
        {
            portfolioVariance += weights[symbol] * marginalContributions[symbol];
        }

        var portfolioStd = Math.Sqrt(Math.Max(0, portfolioVariance));

        // Calculate risk contributions
        var riskContributions = new Dictionary<string, double>();
        foreach (var symbol in weights.Keys)
        {
            var riskContrib = portfolioStd > 0
                ? weights[symbol] * marginalContributions[symbol] / portfolioVariance
                : 1.0 / weights.Count;

            riskContributions[symbol] = riskContrib;
        }

        return riskContributions;
    }

    private static Dictionary<string, double> CalculateMarginalRiskContributions(
        Dictionary<string, double> weights,
        Dictionary<string, Dictionary<string, double>> covarianceMatrix)
    {
        // Marginal Risk Contribution_i = (Σ * w)_i / sqrt(w' * Σ * w)

        var portfolioVolatility = FinancialMathHelper.CalculatePortfolioVolatility(weights, covarianceMatrix);
        var marginalRiskContributions = new Dictionary<string, double>();

        foreach (var symbol in weights.Keys)
        {
            double sumCovWeight = 0;
            foreach (var symbol2 in weights.Keys)
            {
                var cov = covarianceMatrix.GetValueOrDefault(symbol)?.GetValueOrDefault(symbol2) ?? 0;
                sumCovWeight += cov * weights[symbol2];
            }

            marginalRiskContributions[symbol] = portfolioVolatility > 0 ? sumCovWeight / portfolioVolatility : 0;
        }

        return marginalRiskContributions;
    }

    private static decimal CalculateDiversificationRatio(
        Dictionary<string, double> weights,
        Dictionary<string, Dictionary<string, double>> covarianceMatrix)
    {
        // Diversification Ratio = (Σ w_i * σ_i) / σ_p
        // Measures how much diversification benefit the portfolio provides

        var weightedVolatilities = weights.Sum(w =>
        {
            var variance = covarianceMatrix.GetValueOrDefault(w.Key)?.GetValueOrDefault(w.Key) ?? 0;
            return w.Value * Math.Sqrt(variance);
        });

        var portfolioVolatility = FinancialMathHelper.CalculatePortfolioVolatility(weights, covarianceMatrix);

        return portfolioVolatility > 0 ? Math.Round((decimal)(weightedVolatilities / portfolioVolatility), 2) : 1;
    }

    private static decimal CalculateRiskBalanceScore(
        Dictionary<string, double> actualContributions,
        Dictionary<string, double> targetContributions)
    {
        // Score based on how close actual risk contributions are to targets
        // Score = 100 - average absolute deviation * 100

        var deviations = actualContributions.Select(ac =>
            Math.Abs(ac.Value - targetContributions.GetValueOrDefault(ac.Key, 1.0 / actualContributions.Count))
        ).ToList();

        var avgDeviation = deviations.Average();
        var score = Math.Max(0, 100 - avgDeviation * 100);

        return Math.Round((decimal)score, 1);
    }
}
