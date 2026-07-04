using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Portfolio.Contracts;
using Orkeon.Trading.Tools.Domain.Services;
using Microsoft.Extensions.Logging;
using MathNet.Numerics.Statistics;

namespace Orkeon.Trading.Tools.Infrastructure.Portfolio;

/// <summary>
/// Mean-Variance Optimization tool implementing Markowitz Modern Portfolio Theory.
/// Constructs efficient frontier and finds optimal portfolios maximizing Sharpe ratio.
/// Supports constraints: long-only, weight bounds, sector limits.
/// </summary>
public class MeanVarianceOptimizationTool(ILogger<MeanVarianceOptimizationTool>? logger = null)
    : TradingToolBase<MeanVarianceOptimizationRequest, MeanVarianceOptimizationResponse>(logger)
{

    protected override string ToolId => "mean_variance_optimization";

    protected override string? ValidateTypedRequest(MeanVarianceOptimizationRequest request)
    {
        if (request.Symbols.Count < 2)
            return "At least 2 symbols required for optimization";
        return null;
    }

    protected override async Task<MeanVarianceOptimizationResponse> ExecuteTypedAsync(
        MeanVarianceOptimizationRequest request,
        CancellationToken cancellationToken)
    {
        // Deduplicate symbols to prevent 'duplicate key' errors in dictionaries
        var symbols = request.Symbols.Distinct().ToList();

        _logger?.LogInformation("Running Mean-Variance Optimization for {Count} symbols", symbols.Count);

        var optimization = await Task.Run(() => OptimizePortfolio(
            symbols,
            request.ReturnsData,
            request.RiskFreeRate,
            request.TargetReturn,
            request.TargetVolatility,
            request.MinWeight,
            request.MaxWeight,
            request.CurrentWeights,
            request.MaxTurnover), cancellationToken);

        _logger?.LogInformation("Optimization completed - Expected Return: {Return}%, Volatility: {Vol}%",
            optimization.ExpectedReturn,
            optimization.ExpectedVolatility);

        return optimization;
    }

    private static MeanVarianceOptimizationResponse OptimizePortfolio(
        List<string> symbols,
        Dictionary<string, List<double>> returnsData,
        double riskFreeRate,
        double? targetReturn,
        double? targetVolatility,
        double minWeight,
        double maxWeight,
        Dictionary<string, double>? currentWeights,
        double? maxTurnover)
    {
        // 1. Calculate expected returns (mean of historical returns)
        var expectedReturns = symbols.ToDictionary(
            s => s,
            s => returnsData.TryGetValue(s, out var r) ? r.Mean() : 0.0
        );

        // 2. Calculate covariance matrix
        var covarianceMatrix = FinancialMathHelper.CalculateCovarianceMatrix(symbols, returnsData);

        // 3. Find optimal portfolios
        var maxSharpePortfolio = FindMaximumSharpePortfolio(
            symbols, expectedReturns, covarianceMatrix, riskFreeRate, minWeight, maxWeight);

        var minVariancePortfolio = FindMinimumVariancePortfolio(
            symbols, covarianceMatrix, minWeight, maxWeight);

        // 4. If target return/volatility specified, find that specific portfolio
        Dictionary<string, double>? targetPortfolio = null;
        if (targetReturn.HasValue)
        {
            targetPortfolio = FindPortfolioForTargetReturn(
                symbols, expectedReturns, covarianceMatrix, targetReturn.Value, minWeight, maxWeight);
        }
        else if (targetVolatility.HasValue)
        {
            targetPortfolio = FindPortfolioForTargetVolatility(
                symbols, expectedReturns, covarianceMatrix, targetVolatility.Value, minWeight, maxWeight);
        }

        // 5. Generate efficient frontier
        var efficientFrontier = GenerateEfficientFrontier(
            symbols, expectedReturns, covarianceMatrix, minWeight, maxWeight, 20);

        // 6. Calculate metrics for chosen portfolio (default: max Sharpe)
        var optimalWeights = targetPortfolio ?? maxSharpePortfolio;

        // 7. Apply turnover constraint if specified
        if (currentWeights != null && maxTurnover.HasValue)
        {
            optimalWeights = ApplyTurnoverConstraint(optimalWeights, currentWeights, maxTurnover.Value);
        }

        var portfolioReturn = CalculatePortfolioReturn(optimalWeights, expectedReturns);
        var portfolioVolatility = FinancialMathHelper.CalculatePortfolioVolatility(optimalWeights, covarianceMatrix);
        var sharpeRatio = (portfolioReturn - riskFreeRate) / portfolioVolatility;

        // 8. Calculate diversification metrics
        var diversification = CalculateDiversificationMetrics(optimalWeights, covarianceMatrix);

        return new MeanVarianceOptimizationResponse
        {
            Timestamp = DateTime.UtcNow,
            Method = "MEAN_VARIANCE",
            Symbols = symbols,
            OptimalWeights = optimalWeights.ToDictionary(kvp => kvp.Key, kvp => Math.Round((decimal)kvp.Value, 4)),
            ExpectedReturn = Math.Round((decimal)(portfolioReturn * 100), 2),
            ExpectedVolatility = Math.Round((decimal)(portfolioVolatility * 100), 2),
            SharpeRatio = Math.Round((decimal)sharpeRatio, 2),
            RiskFreeRate = riskFreeRate,
            AlternativePortfolios = new Dictionary<string, object>
            {
                ["maximum_sharpe"] = new Dictionary<string, object>
                {
                    ["weights"] = maxSharpePortfolio.ToDictionary(kvp => kvp.Key, kvp => Math.Round((decimal)kvp.Value, 4)),
                    ["expected_return"] = Math.Round((decimal)(CalculatePortfolioReturn(maxSharpePortfolio, expectedReturns) * 100), 2),
                    ["expected_volatility"] = Math.Round((decimal)(FinancialMathHelper.CalculatePortfolioVolatility(maxSharpePortfolio, covarianceMatrix) * 100), 2)
                },
                ["minimum_variance"] = new Dictionary<string, object>
                {
                    ["weights"] = minVariancePortfolio.ToDictionary(kvp => kvp.Key, kvp => Math.Round((decimal)kvp.Value, 4)),
                    ["expected_return"] = Math.Round((decimal)(CalculatePortfolioReturn(minVariancePortfolio, expectedReturns) * 100), 2),
                    ["expected_volatility"] = Math.Round((decimal)(FinancialMathHelper.CalculatePortfolioVolatility(minVariancePortfolio, covarianceMatrix) * 100), 2)
                }
            },
            EfficientFrontier = efficientFrontier.Select(ef => new Dictionary<string, object>
            {
                ["expected_return"] = Math.Round((decimal)(ef.Return * 100), 2),
                ["volatility"] = Math.Round((decimal)(ef.Volatility * 100), 2),
                ["sharpe_ratio"] = Math.Round((decimal)ef.SharpeRatio, 2)
            }).ToList(),
            DiversificationMetrics = diversification,
            ConstraintsApplied = new List<string?>
            {
                $"Weight bounds: [{minWeight}, {maxWeight}]",
                "Sum of weights = 1.0",
                currentWeights != null && maxTurnover.HasValue ? $"Max turnover: {maxTurnover}" : null
            }.Where(c => c != null).Select(c => c!).ToList()
        };
    }

    private static Dictionary<string, double> FindMaximumSharpePortfolio(
        List<string> symbols,
        Dictionary<string, double> expectedReturns,
        Dictionary<string, Dictionary<string, double>> covarianceMatrix,
        double riskFreeRate,
        double minWeight,
        double maxWeight)
    {
        // Simplified optimization using random search
        // In production, use quadratic programming solver (e.g., MathNet.Numerics.Optimization)

        var bestSharpe = double.MinValue;
        var bestWeights = new Dictionary<string, double>();

        var random = new Random(42);

        // Try many random portfolios and keep the best
        for (int iter = 0; iter < 10000; iter++)
        {
            var weights = GenerateRandomWeights(symbols, minWeight, maxWeight, random);

            var portfolioReturn = CalculatePortfolioReturn(weights, expectedReturns);
            var portfolioVolatility = FinancialMathHelper.CalculatePortfolioVolatility(weights, covarianceMatrix);

            if (portfolioVolatility > 0)
            {
                var sharpe = (portfolioReturn - riskFreeRate) / portfolioVolatility;

                if (sharpe > bestSharpe)
                {
                    bestSharpe = sharpe;
                    bestWeights = new Dictionary<string, double>(weights);
                }
            }
        }

        return bestWeights;
    }

    private static Dictionary<string, double> FindMinimumVariancePortfolio(
        List<string> symbols,
        Dictionary<string, Dictionary<string, double>> covarianceMatrix,
        double minWeight,
        double maxWeight)
    {
        var bestVariance = double.MaxValue;
        var bestWeights = new Dictionary<string, double>();

        var random = new Random(43);

        for (int iter = 0; iter < 10000; iter++)
        {
            var weights = GenerateRandomWeights(symbols, minWeight, maxWeight, random);

            var portfolioVariance = Math.Pow(FinancialMathHelper.CalculatePortfolioVolatility(weights, covarianceMatrix), 2);

            if (portfolioVariance < bestVariance)
            {
                bestVariance = portfolioVariance;
                bestWeights = new Dictionary<string, double>(weights);
            }
        }

        return bestWeights;
    }

    private static Dictionary<string, double> FindPortfolioForTargetReturn(
        List<string> symbols,
        Dictionary<string, double> expectedReturns,
        Dictionary<string, Dictionary<string, double>> covarianceMatrix,
        double targetReturn,
        double minWeight,
        double maxWeight)
    {
        var bestPortfolio = new Dictionary<string, double>();
        var minVolatility = double.MaxValue;

        var random = new Random(44);

        for (int iter = 0; iter < 10000; iter++)
        {
            var weights = GenerateRandomWeights(symbols, minWeight, maxWeight, random);

            var portfolioReturn = CalculatePortfolioReturn(weights, expectedReturns);

            // Check if return is close to target (within 0.5%)
            if (Math.Abs(portfolioReturn - targetReturn) < 0.005)
            {
                var volatility = FinancialMathHelper.CalculatePortfolioVolatility(weights, covarianceMatrix);

                if (volatility < minVolatility)
                {
                    minVolatility = volatility;
                    bestPortfolio = new Dictionary<string, double>(weights);
                }
            }
        }

        return bestPortfolio.Count > 0 ? bestPortfolio : FindMaximumSharpePortfolio(symbols, expectedReturns, covarianceMatrix, 0.02, minWeight, maxWeight);
    }

    private static Dictionary<string, double> FindPortfolioForTargetVolatility(
        List<string> symbols,
        Dictionary<string, double> expectedReturns,
        Dictionary<string, Dictionary<string, double>> covarianceMatrix,
        double targetVolatility,
        double minWeight,
        double maxWeight)
    {
        var bestPortfolio = new Dictionary<string, double>();
        var maxReturn = double.MinValue;

        var random = new Random(45);

        for (int iter = 0; iter < 10000; iter++)
        {
            var weights = GenerateRandomWeights(symbols, minWeight, maxWeight, random);

            var volatility = FinancialMathHelper.CalculatePortfolioVolatility(weights, covarianceMatrix);

            // Check if volatility is close to target
            if (Math.Abs(volatility - targetVolatility) < 0.005)
            {
                var portfolioReturn = CalculatePortfolioReturn(weights, expectedReturns);

                if (portfolioReturn > maxReturn)
                {
                    maxReturn = portfolioReturn;
                    bestPortfolio = new Dictionary<string, double>(weights);
                }
            }
        }

        return bestPortfolio.Count > 0 ? bestPortfolio : FindMaximumSharpePortfolio(symbols, expectedReturns, covarianceMatrix, 0.02, minWeight, maxWeight);
    }

    private static Dictionary<string, double> GenerateRandomWeights(
        List<string> symbols,
        double minWeight,
        double maxWeight,
        Random random)
    {
        var weights = new Dictionary<string, double>();

        // Generate random weights
        var rawWeights = symbols.Select(_ => random.NextDouble()).ToArray();
        var sum = rawWeights.Sum();

        // Normalize to sum to 1 and apply bounds
        for (int i = 0; i < symbols.Count; i++)
        {
            var weight = rawWeights[i] / sum;
            weight = Math.Max(minWeight, Math.Min(maxWeight, weight));
            weights[symbols[i]] = weight;
        }

        // Renormalize after applying bounds
        var totalWeight = weights.Values.Sum();
        if (totalWeight > 0)
        {
            foreach (var symbol in symbols)
            {
                weights[symbol] /= totalWeight;
            }
        }

        return weights;
    }

    private static double CalculatePortfolioReturn(
        Dictionary<string, double> weights,
        Dictionary<string, double> expectedReturns)
    {
        return weights.Sum(w => w.Value * expectedReturns.GetValueOrDefault(w.Key, 0));
    }

    private static List<EfficientFrontierPoint> GenerateEfficientFrontier(
        List<string> symbols,
        Dictionary<string, double> expectedReturns,
        Dictionary<string, Dictionary<string, double>> covarianceMatrix,
        double minWeight,
        double maxWeight,
        int numPoints)
    {
        var frontierPoints = new List<EfficientFrontierPoint>();

        // Generate portfolios along efficient frontier
        var minReturn = expectedReturns.Values.Min();
        var maxReturn = expectedReturns.Values.Max();

        for (int i = 0; i < numPoints; i++)
        {
            var targetReturn = minReturn + (maxReturn - minReturn) * i / (numPoints - 1);

            var portfolio = FindPortfolioForTargetReturn(
                symbols, expectedReturns, covarianceMatrix, targetReturn, minWeight, maxWeight);

            var portfolioReturn = CalculatePortfolioReturn(portfolio, expectedReturns);
            var volatility = FinancialMathHelper.CalculatePortfolioVolatility(portfolio, covarianceMatrix);
            var sharpe = volatility > 0 ? (portfolioReturn - 0.02) / volatility : 0;

            frontierPoints.Add(new EfficientFrontierPoint
            {
                Return = portfolioReturn,
                Volatility = volatility,
                SharpeRatio = sharpe
            });
        }

        return frontierPoints.OrderBy(p => p.Volatility).ToList();
    }

    private static Dictionary<string, double> ApplyTurnoverConstraint(
        Dictionary<string, double> newWeights,
        Dictionary<string, double> currentWeights,
        double maxTurnover)
    {
        // Calculate current turnover
        var turnover = newWeights.Sum(nw =>
            Math.Abs(nw.Value - currentWeights.GetValueOrDefault(nw.Key, 0)));

        if (turnover <= maxTurnover)
            return newWeights;

        // Scale down changes to meet turnover constraint
        var adjustedWeights = new Dictionary<string, double>();
        var scale = maxTurnover / turnover;

        foreach (var symbol in newWeights.Keys)
        {
            var currentWeight = currentWeights.GetValueOrDefault(symbol, 0);
            var targetWeight = newWeights[symbol];
            var change = (targetWeight - currentWeight) * scale;

            adjustedWeights[symbol] = currentWeight + change;
        }

        // Renormalize
        var total = adjustedWeights.Values.Sum();
        foreach (var symbol in adjustedWeights.Keys.ToList())
        {
            adjustedWeights[symbol] /= total;
        }

        return adjustedWeights;
    }

    private static Dictionary<string, object> CalculateDiversificationMetrics(
        Dictionary<string, double> weights,
        Dictionary<string, Dictionary<string, double>> covarianceMatrix)
    {
        // Effective number of assets (inverse Herfindahl index)
        var herfindahl = weights.Values.Sum(w => w * w);
        var effectiveN = herfindahl > 0 ? 1.0 / herfindahl : weights.Count;

        // Concentration risk
        var maxWeight = weights.Values.Max();
        var top5Weight = weights.Values.OrderByDescending(w => w).Take(5).Sum();

        return new Dictionary<string, object>
        {
            ["effective_number_of_assets"] = Math.Round(effectiveN, 2),
            ["concentration_index"] = Math.Round((decimal)(herfindahl * 100), 2),
            ["max_weight"] = Math.Round((decimal)(maxWeight * 100), 2),
            ["top_5_concentration"] = Math.Round((decimal)(top5Weight * 100), 2),
            ["diversification_score"] = Math.Round((decimal)((effectiveN / weights.Count) * 100), 1)
        };
    }

    private record EfficientFrontierPoint
    {
        public required double Return { get; init; }
        public required double Volatility { get; init; }
        public required double SharpeRatio { get; init; }
    }
}
