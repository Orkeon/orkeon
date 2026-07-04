using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Portfolio.Contracts;
using Orkeon.Trading.Tools.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Portfolio;

/// <summary>
/// Black-Litterman portfolio optimization tool combining market equilibrium with investor views.
/// Blends market-implied returns (from market cap weights) with subjective views about asset returns.
/// Produces more stable and intuitive portfolios than pure Mean-Variance optimization.
/// </summary>
public class BlackLittermanTool(ILogger<BlackLittermanTool>? logger = null)
    : TradingToolBase<BlackLittermanRequest, BlackLittermanResponse>(logger)
{

    protected override string ToolId => "black_litterman";

    protected override async Task<BlackLittermanResponse> ExecuteTypedAsync(
        BlackLittermanRequest request,
        CancellationToken cancellationToken)
    {
        // Deduplicate symbols to prevent 'duplicate key' errors in dictionaries
        var symbols = request.Symbols.Distinct().ToList();

        _logger?.LogInformation("Running Black-Litterman optimization for {Count} symbols with {Views} views",
            symbols.Count, request.InvestorViews?.Count ?? 0);

        var optimization = await Task.Run(() => OptimizeBlackLitterman(
            symbols,
            request.ReturnsData,
            request.MarketCaps,
            request.InvestorViews,
            request.RiskAversion,
            request.Tau,
            request.RiskFreeRate), cancellationToken);

        _logger?.LogInformation("Black-Litterman optimization completed");

        return optimization;
    }

    private static BlackLittermanResponse OptimizeBlackLitterman(
        List<string> symbols,
        Dictionary<string, List<double>> returnsData,
        Dictionary<string, decimal> marketCaps,
        List<InvestorView>? investorViews,
        double riskAversion,
        double tau,
        double riskFreeRate)
    {
        // Step 1: Calculate market equilibrium weights from market caps
        var marketWeights = CalculateMarketWeights(symbols, marketCaps);

        // Step 2: Calculate covariance matrix
        var covarianceMatrix = FinancialMathHelper.CalculateCovarianceMatrix(symbols, returnsData);

        // Step 3: Calculate implied equilibrium returns (reverse optimization)
        var impliedReturns = CalculateImpliedReturns(marketWeights, covarianceMatrix, riskAversion);

        // Step 4: If no views provided, return market portfolio
        if (investorViews == null || investorViews.Count == 0)
        {
            return BuildResult(
                symbols,
                marketWeights,
                impliedReturns,
                impliedReturns,
                covarianceMatrix,
                riskFreeRate,
                "Market equilibrium portfolio (no views)",
                null
            );
        }

        // Step 5: Process investor views into P and Q matrices
        var (P, Q, Omega) = ProcessInvestorViews(investorViews, symbols);

        // Step 6: Calculate posterior (Black-Litterman) returns
        var posteriorReturns = CalculatePosteriorReturns(
            impliedReturns, covarianceMatrix, P, Q, Omega, tau);

        // Step 7: Calculate optimal weights using posterior returns
        var optimalWeights = CalculateOptimalWeights(posteriorReturns, covarianceMatrix, riskAversion);

        // Step 8: Build comprehensive result
        return BuildResult(
            symbols,
            optimalWeights,
            impliedReturns,
            posteriorReturns,
            covarianceMatrix,
            riskFreeRate,
            "Black-Litterman portfolio with investor views",
            investorViews
        );
    }

    private static Dictionary<string, double> CalculateMarketWeights(
        List<string> symbols,
        Dictionary<string, decimal> marketCaps)
    {
        var totalMarketCap = marketCaps.Values.Sum();
        return symbols.ToDictionary(
            s => s,
            s => (double)(marketCaps.GetValueOrDefault(s, 0) / totalMarketCap)
        );
    }

    private static Dictionary<string, double> CalculateImpliedReturns(
        Dictionary<string, double> marketWeights,
        Dictionary<string, Dictionary<string, double>> covarianceMatrix,
        double riskAversion)
    {
        // Implied returns: Π = δ * Σ * w_mkt
        // where δ = risk aversion, Σ = covariance matrix, w_mkt = market weights

        var impliedReturns = new Dictionary<string, double>();

        foreach (var symbol in marketWeights.Keys)
        {
            double impliedReturn = 0;

            // Matrix multiplication: (Σ * w_mkt)
            foreach (var symbol2 in marketWeights.Keys)
            {
                var cov = covarianceMatrix.GetValueOrDefault(symbol)?.GetValueOrDefault(symbol2) ?? 0;
                impliedReturn += cov * marketWeights[symbol2];
            }

            impliedReturns[symbol] = riskAversion * impliedReturn;
        }

        return impliedReturns;
    }

    private static (Dictionary<string, Dictionary<string, double>> P, Dictionary<string, double> Q, Dictionary<string, double> Omega)
        ProcessInvestorViews(List<InvestorView> views, List<string> symbols)
    {
        // P matrix: Pick matrix (links views to assets)
        // Q vector: View returns
        // Omega matrix: View uncertainty (diagonal)

        var P = new Dictionary<string, Dictionary<string, double>>();
        var Q = new Dictionary<string, double>();
        var Omega = new Dictionary<string, double>();

        for (int i = 0; i < views.Count; i++)
        {
            var view = views[i];
            var viewKey = $"view_{i}";

            P[viewKey] = new Dictionary<string, double>();

            // Initialize all assets to 0 in this view
            foreach (var symbol in symbols)
            {
                P[viewKey][symbol] = 0;
            }

            if (view.Type == "absolute")
            {
                // Absolute view: Asset X will return Y%
                foreach (var asset in view.Assets)
                {
                    P[viewKey][asset] = 1.0;
                }
                Q[viewKey] = view.Return;
            }
            else if (view.Type == "relative")
            {
                // Relative view: Asset X will outperform Asset Y by Z%
                if (view.Assets.Count >= 2)
                {
                    P[viewKey][view.Assets[0]] = 1.0;
                    P[viewKey][view.Assets[1]] = -1.0;
                    Q[viewKey] = view.Return;
                }
            }

            // Omega: View uncertainty (higher confidence = lower uncertainty)
            // Omega[i,i] = tau * (P * Σ * P') for view i
            // Simplified: use inverse of confidence as uncertainty
            Omega[viewKey] = (1.0 - view.Confidence) * 0.01; // Scale uncertainty
        }

        return (P, Q, Omega);
    }

    private static Dictionary<string, double> CalculatePosteriorReturns(
        Dictionary<string, double> priorReturns,
        Dictionary<string, Dictionary<string, double>> covarianceMatrix,
        Dictionary<string, Dictionary<string, double>> P,
        Dictionary<string, double> Q,
        Dictionary<string, double> Omega,
        double tau)
    {
        // Black-Litterman formula (simplified):
        // E[R] = [(τΣ)^-1 + P'Ω^-1P]^-1 [(τΣ)^-1 Π + P'Ω^-1 Q]

        // For simplicity, use a weighted average approach
        // Posterior = w_prior * Prior + w_views * Views

        var posteriorReturns = new Dictionary<string, double>(priorReturns);

        // Apply each view to adjust returns
        foreach (var viewKey in P.Keys)
        {
            var viewReturn = Q[viewKey];
            var viewConfidence = 1.0 / (Omega[viewKey] + 0.001); // Higher confidence = higher weight

            // Apply view to each asset in the view
            foreach (var symbol in P[viewKey].Keys)
            {
                var pickWeight = P[viewKey][symbol];
                if (Math.Abs(pickWeight) > 0.001)
                {
                    // Blend prior and view
                    var viewAdjustment = pickWeight * viewReturn * viewConfidence * tau;
                    posteriorReturns[symbol] = posteriorReturns[symbol] * (1 - tau) + viewAdjustment;
                }
            }
        }

        return posteriorReturns;
    }

    private static Dictionary<string, double> CalculateOptimalWeights(
        Dictionary<string, double> expectedReturns,
        Dictionary<string, Dictionary<string, double>> covarianceMatrix,
        double riskAversion)
    {
        // Optimal weights: w = (δ * Σ)^-1 * E[R]
        // Simplified: w_i ∝ E[R_i] / (δ * σ_i^2)

        var weights = new Dictionary<string, double>();

        foreach (var symbol in expectedReturns.Keys)
        {
            var expectedReturn = expectedReturns[symbol];
            var variance = covarianceMatrix.GetValueOrDefault(symbol)?.GetValueOrDefault(symbol) ?? 0.01;

            // Simple proportional allocation
            weights[symbol] = Math.Max(0, expectedReturn / (riskAversion * variance));
        }

        // Normalize weights to sum to 1
        var totalWeight = weights.Values.Sum();
        if (totalWeight > 0)
        {
            foreach (var symbol in weights.Keys.ToList())
            {
                weights[symbol] /= totalWeight;
            }
        }

        return weights;
    }

    private static BlackLittermanResponse BuildResult(
        List<string> symbols,
        Dictionary<string, double> optimalWeights,
        Dictionary<string, double> impliedReturns,
        Dictionary<string, double> posteriorReturns,
        Dictionary<string, Dictionary<string, double>> covarianceMatrix,
        double riskFreeRate,
        string description,
        List<InvestorView>? views)
    {
        var portfolioReturn = optimalWeights.Sum(w => w.Value * posteriorReturns.GetValueOrDefault(w.Key, 0));
        var portfolioVolatility = FinancialMathHelper.CalculatePortfolioVolatility(optimalWeights, covarianceMatrix);
        var sharpeRatio = portfolioVolatility > 0 ? (portfolioReturn - riskFreeRate) / portfolioVolatility : 0;

        List<Dictionary<string, object>>? viewsList = null;
        if (views != null && views.Count > 0)
        {
            viewsList = views.Select(v => new Dictionary<string, object>
            {
                ["type"] = v.Type,
                ["assets"] = v.Assets,
                ["expected_return"] = Math.Round((decimal)(v.Return * 100), 2),
                ["confidence"] = Math.Round((decimal)(v.Confidence * 100), 0)
            }).ToList();
        }

        return new BlackLittermanResponse
        {
            Timestamp = DateTime.UtcNow,
            Method = "BLACK_LITTERMAN",
            Description = description,
            Symbols = symbols,
            OptimalWeights = optimalWeights.ToDictionary(kvp => kvp.Key, kvp => Math.Round((decimal)kvp.Value, 4)),
            ExpectedReturn = Math.Round((decimal)(portfolioReturn * 100), 2),
            ExpectedVolatility = Math.Round((decimal)(portfolioVolatility * 100), 2),
            SharpeRatio = Math.Round((decimal)sharpeRatio, 2),
            ImpliedReturns = impliedReturns.ToDictionary(kvp => kvp.Key, kvp => Math.Round((decimal)(kvp.Value * 100), 2)),
            PosteriorReturns = posteriorReturns.ToDictionary(kvp => kvp.Key, kvp => Math.Round((decimal)(kvp.Value * 100), 2)),
            ReturnAdjustments = posteriorReturns.ToDictionary(
                kvp => kvp.Key,
                kvp => Math.Round((decimal)((kvp.Value - impliedReturns.GetValueOrDefault(kvp.Key, 0)) * 100), 2)
            ),
            InvestorViews = viewsList
        };
    }

    public record InvestorView
    {
        public required string Type { get; init; } // "absolute" or "relative"
        public required List<string> Assets { get; init; }
        public required double Return { get; init; } // Expected return for the view
        public required double Confidence { get; init; } // 0.0 to 1.0
    }
}
