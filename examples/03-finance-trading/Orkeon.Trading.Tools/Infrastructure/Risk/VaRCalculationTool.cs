using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Risk.Contracts;
using Microsoft.Extensions.Logging;
using MathNet.Numerics.Statistics;
using MathNet.Numerics.Distributions;

namespace Orkeon.Trading.Tools.Infrastructure.Risk;

/// <summary>
/// Value at Risk (VaR) calculation tool with multiple methodologies.
/// Supports parametric (assumes normal distribution), historical, and Monte Carlo simulation methods.
/// </summary>
public class VaRCalculationTool(ILogger<VaRCalculationTool>? logger = null)
    : TradingToolBase<VaRCalculationRequest, VaRCalculationResponse>(logger)
{
    private readonly Random _random = new Random();

    protected override string ToolId => "var_calculation";

    protected override string? ValidateTypedRequest(VaRCalculationRequest request)
    {
        if (request.ReturnsData.Count < 20)
            return "At least 20 returns data points required";
        return null;
    }

    protected override async Task<VaRCalculationResponse> ExecuteTypedAsync(
        VaRCalculationRequest request,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Calculating VaR for portfolio value ${Value} with confidence {Confidence}%",
            request.PortfolioValue, request.ConfidenceLevel * 100);

        var varResults = await Task.Run(() => CalculateVaR(
            request.PortfolioValue,
            request.ReturnsData,
            request.ConfidenceLevel,
            request.TimeHorizon,
            request.Method,
            request.MonteCarloSimulations), cancellationToken);

        _logger?.LogInformation("VaR calculation completed");

        return varResults;
    }

    private static VaRCalculationResponse CalculateVaR(
        decimal portfolioValue,
        List<double> returnsData,
        double confidenceLevel,
        int timeHorizon,
        string method,
        int monteCarloSims)
    {
        var includeAll = method == "all";

        // Convert returns to proper format (assume returns are in percentage or decimal)
        var returns = returnsData.Select(r => r > 1 || r < -1 ? r / 100.0 : r).ToList();

        Dictionary<string, object>? parametricVar = null;
        Dictionary<string, object>? historicalVar = null;
        Dictionary<string, object>? monteCarloVar = null;

        // Parametric VaR (assumes normal distribution)
        if (includeAll || method == "parametric")
        {
            parametricVar = CalculateParametricVaR(portfolioValue, returns, confidenceLevel, timeHorizon);
        }

        // Historical VaR (empirical distribution)
        if (includeAll || method == "historical")
        {
            historicalVar = CalculateHistoricalVaR(portfolioValue, returns, confidenceLevel, timeHorizon);
        }

        // Monte Carlo VaR (simulation-based)
        if (includeAll || method == "monte_carlo")
        {
            monteCarloVar = CalculateMonteCarloVaR(portfolioValue, returns, confidenceLevel, timeHorizon, monteCarloSims);
        }

        return new VaRCalculationResponse
        {
            PortfolioValue = portfolioValue,
            ConfidenceLevel = confidenceLevel,
            TimeHorizonDays = timeHorizon,
            Timestamp = DateTime.UtcNow,
            ParametricVar = parametricVar,
            HistoricalVar = historicalVar,
            MonteCarloVar = monteCarloVar,
            Summary = new Dictionary<string, object>
            {
                ["mean_return"] = Math.Round(returns.Mean(), 6),
                ["std_deviation"] = Math.Round(returns.StandardDeviation(), 6),
                ["skewness"] = Math.Round(CalculateSkewness(returns), 4),
                ["kurtosis"] = Math.Round(CalculateKurtosis(returns), 4),
                ["data_points"] = returns.Count
            }
        };
    }

    private static Dictionary<string, object> CalculateParametricVaR(
        decimal portfolioValue,
        List<double> returns,
        double confidenceLevel,
        int timeHorizon)
    {
        // Assume returns are normally distributed
        var meanReturn = returns.Mean();
        var stdDev = returns.StandardDeviation();

        // Get z-score for confidence level
        var normal = new Normal(0, 1);
        var zScore = normal.InverseCumulativeDistribution(1 - confidenceLevel);

        // Scale for time horizon (square root of time rule)
        var scaledStdDev = stdDev * Math.Sqrt(timeHorizon);

        // Calculate VaR
        var varReturn = meanReturn * timeHorizon + zScore * scaledStdDev;
        var varAmount = (decimal)(portfolioValue * (decimal)Math.Abs(varReturn));

        return new Dictionary<string, object>
        {
            ["var_amount"] = Math.Round(varAmount, 2),
            ["var_percentage"] = Math.Round((decimal)Math.Abs(varReturn) * 100, 2),
            ["mean_return"] = Math.Round(meanReturn, 6),
            ["std_deviation"] = Math.Round(stdDev, 6),
            ["z_score"] = Math.Round(zScore, 4),
            ["interpretation"] = $"At {confidenceLevel * 100}% confidence, potential loss should not exceed ${Math.Round(varAmount, 2):N2} over {timeHorizon} day(s)"
        };
    }

    private static Dictionary<string, object> CalculateHistoricalVaR(
        decimal portfolioValue,
        List<double> returns,
        double confidenceLevel,
        int timeHorizon)
    {
        // Sort returns in ascending order
        var sortedReturns = returns.OrderBy(r => r).ToList();

        // Find the percentile corresponding to (1 - confidence level)
        var percentileIndex = (int)Math.Ceiling((1 - confidenceLevel) * sortedReturns.Count) - 1;
        percentileIndex = Math.Max(0, Math.Min(percentileIndex, sortedReturns.Count - 1));

        var varReturn = sortedReturns[percentileIndex];

        // Scale for time horizon
        var scaledVarReturn = varReturn * Math.Sqrt(timeHorizon);
        var varAmount = (decimal)(portfolioValue * (decimal)Math.Abs(scaledVarReturn));

        // Calculate worst historical losses
        var worstLosses = sortedReturns.Take(5).Select(r => Math.Round(r * 100, 2)).ToList();

        return new Dictionary<string, object>
        {
            ["var_amount"] = Math.Round(varAmount, 2),
            ["var_percentage"] = Math.Round((decimal)Math.Abs(scaledVarReturn) * 100, 2),
            ["percentile_return"] = Math.Round(varReturn, 6),
            ["worst_5_losses_pct"] = worstLosses,
            ["interpretation"] = $"Based on historical data, at {confidenceLevel * 100}% confidence, potential loss should not exceed ${Math.Round(varAmount, 2):N2} over {timeHorizon} day(s)"
        };
    }

    private static Dictionary<string, object> CalculateMonteCarloVaR(
        decimal portfolioValue,
        List<double> returns,
        double confidenceLevel,
        int timeHorizon,
        int numSimulations)
    {
        var meanReturn = returns.Mean();
        var stdDev = returns.StandardDeviation();

        // Run Monte Carlo simulations
        var simulatedReturns = new List<double>();
        var normal = new Normal(meanReturn, stdDev);

        for (int i = 0; i < numSimulations; i++)
        {
            // Simulate returns for time horizon
            double cumulativeReturn = 0;
            for (int day = 0; day < timeHorizon; day++)
            {
                cumulativeReturn += normal.Sample();
            }
            simulatedReturns.Add(cumulativeReturn);
        }

        // Sort simulated returns
        var sortedSims = simulatedReturns.OrderBy(r => r).ToList();

        // Find VaR at confidence level
        var percentileIndex = (int)Math.Ceiling((1 - confidenceLevel) * sortedSims.Count) - 1;
        percentileIndex = Math.Max(0, Math.Min(percentileIndex, sortedSims.Count - 1));

        var varReturn = sortedSims[percentileIndex];
        var varAmount = (decimal)(portfolioValue * (decimal)Math.Abs(varReturn));

        // Get worst simulated losses
        var worstSimulations = sortedSims.Take(5).Select(r => Math.Round(r * 100, 2)).ToList();

        return new Dictionary<string, object>
        {
            ["var_amount"] = Math.Round(varAmount, 2),
            ["var_percentage"] = Math.Round((decimal)Math.Abs(varReturn) * 100, 2),
            ["num_simulations"] = numSimulations,
            ["worst_5_simulations_pct"] = worstSimulations,
            ["mean_simulated_return"] = Math.Round(simulatedReturns.Mean(), 6),
            ["std_simulated_return"] = Math.Round(simulatedReturns.StandardDeviation(), 6),
            ["interpretation"] = $"Based on {numSimulations:N0} Monte Carlo simulations, at {confidenceLevel * 100}% confidence, potential loss should not exceed ${Math.Round(varAmount, 2):N2} over {timeHorizon} day(s)"
        };
    }

    private static double CalculateSkewness(List<double> returns)
    {
        var mean = returns.Mean();
        var stdDev = returns.StandardDeviation();
        var n = returns.Count;

        var skewness = returns.Sum(r => Math.Pow((r - mean) / stdDev, 3)) * n / ((n - 1) * (n - 2));
        return skewness;
    }

    private static double CalculateKurtosis(List<double> returns)
    {
        var mean = returns.Mean();
        var stdDev = returns.StandardDeviation();
        var n = returns.Count;

        var kurtosis = returns.Sum(r => Math.Pow((r - mean) / stdDev, 4)) * n * (n + 1) / ((n - 1) * (n - 2) * (n - 3))
                      - 3 * Math.Pow(n - 1, 2) / ((n - 2) * (n - 3));
        return kurtosis;
    }
}
