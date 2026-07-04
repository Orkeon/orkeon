using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Risk.Contracts;
using Microsoft.Extensions.Logging;
using MathNet.Numerics.Statistics;

namespace Orkeon.Trading.Tools.Infrastructure.Risk;

/// <summary>
/// Conditional Value at Risk (CVaR) calculation tool, also known as Expected Shortfall.
/// CVaR measures the expected loss given that the loss exceeds the VaR threshold.
/// More conservative than VaR and considers tail risk.
/// </summary>
public class CVaRCalculationTool(ILogger<CVaRCalculationTool>? logger = null)
    : TradingToolBase<CVaRCalculationRequest, CVaRCalculationResponse>(logger)
{

    protected override string ToolId => "cvar_calculation";

    protected override string? ValidateTypedRequest(CVaRCalculationRequest request)
    {
        if (request.ReturnsData.Count < 20)
            return "At least 20 returns data points required";
        return null;
    }

    protected override async Task<CVaRCalculationResponse> ExecuteTypedAsync(
        CVaRCalculationRequest request,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Calculating CVaR for portfolio value ${Value} with confidence {Confidence}%",
            request.PortfolioValue, request.ConfidenceLevel * 100);

        var cvarResponse = await Task.Run(() => CalculateCVaR(
            request.PortfolioValue,
            request.ReturnsData,
            request.ConfidenceLevel,
            request.TimeHorizon,
            request.Method), cancellationToken);

        _logger?.LogInformation("CVaR calculation completed");

        return cvarResponse;
    }

    private static CVaRCalculationResponse CalculateCVaR(
        decimal portfolioValue,
        List<double> returnsData,
        double confidenceLevel,
        int timeHorizon,
        string method)
    {
        // Convert returns to proper format
        var returns = returnsData.Select(r => r > 1 || r < -1 ? r / 100.0 : r).ToList();

        Dictionary<string, object> methodResults;

        if (method == "historical")
        {
            methodResults = CalculateHistoricalCVaR(portfolioValue, returns, confidenceLevel, timeHorizon);
        }
        else if (method == "parametric")
        {
            methodResults = CalculateParametricCVaR(portfolioValue, returns, confidenceLevel, timeHorizon);
        }
        else
        {
            methodResults = CalculateHistoricalCVaR(portfolioValue, returns, confidenceLevel, timeHorizon);
        }

        return new CVaRCalculationResponse
        {
            PortfolioValue = portfolioValue,
            ConfidenceLevel = confidenceLevel,
            TimeHorizonDays = timeHorizon,
            Method = method,
            Timestamp = DateTime.UtcNow,
            CvarAmount = (decimal)methodResults["cvar_amount"],
            CvarPercentage = (decimal)methodResults["cvar_percentage"],
            VarAmount = (decimal)methodResults["var_amount"],
            VarPercentage = (decimal)methodResults["var_percentage"],
            TailRiskPremium = (decimal)methodResults["tail_risk_premium"],
            TailRiskPremiumPct = methodResults.TryGetValue("tail_risk_premium_pct", out var tailRiskPremiumPctValue)
                ? (decimal)tailRiskPremiumPctValue : 0m,
            TailObservations = methodResults.TryGetValue("tail_observations", out var tailObservationsValue)
                ? (int)tailObservationsValue : 0,
            WorstLosses = methodResults.TryGetValue("worst_losses", out var worstLossesValue)
                ? (List<double>)worstLossesValue : [],
            CvarToVarRatio = (double)methodResults["cvar_to_var_ratio"],
            Interpretation = (string)methodResults["interpretation"],
            DistributionStats = new Dictionary<string, object>
            {
                ["mean_return"] = Math.Round(returns.Mean(), 6),
                ["std_deviation"] = Math.Round(returns.StandardDeviation(), 6),
                ["skewness"] = Math.Round(CalculateSkewness(returns), 4),
                ["min_return"] = Math.Round(returns.Min(), 6),
                ["max_return"] = Math.Round(returns.Max(), 6)
            }
        };
    }

    private static Dictionary<string, object> CalculateHistoricalCVaR(
        decimal portfolioValue,
        List<double> returns,
        double confidenceLevel,
        int timeHorizon)
    {
        // Sort returns in ascending order (worst to best)
        var sortedReturns = returns.OrderBy(r => r).ToList();

        // Calculate VaR threshold
        var varPercentileIndex = (int)Math.Ceiling((1 - confidenceLevel) * sortedReturns.Count) - 1;
        varPercentileIndex = Math.Max(0, Math.Min(varPercentileIndex, sortedReturns.Count - 1));
        var varReturn = sortedReturns[varPercentileIndex];

        // CVaR is the average of all losses beyond VaR
        var tailLosses = sortedReturns.Take(varPercentileIndex + 1).ToList();
        var cvarReturn = tailLosses.Count > 0 ? tailLosses.Average() : varReturn;

        // Scale for time horizon
        var scaledVarReturn = varReturn * Math.Sqrt(timeHorizon);
        var scaledCVarReturn = cvarReturn * Math.Sqrt(timeHorizon);

        var varAmount = (decimal)(portfolioValue * (decimal)Math.Abs(scaledVarReturn));
        var cvarAmount = (decimal)(portfolioValue * (decimal)Math.Abs(scaledCVarReturn));

        // Tail risk premium: difference between CVaR and VaR
        var tailRiskPremium = cvarAmount - varAmount;
        var tailRiskPremiumPct = ((decimal)Math.Abs(scaledCVarReturn) - (decimal)Math.Abs(scaledVarReturn)) * 100;

        return new Dictionary<string, object>
        {
            ["cvar_amount"] = Math.Round(cvarAmount, 2),
            ["cvar_percentage"] = Math.Round((decimal)Math.Abs(scaledCVarReturn) * 100, 2),
            ["var_amount"] = Math.Round(varAmount, 2),
            ["var_percentage"] = Math.Round((decimal)Math.Abs(scaledVarReturn) * 100, 2),
            ["tail_risk_premium"] = Math.Round(tailRiskPremium, 2),
            ["tail_risk_premium_pct"] = Math.Round(tailRiskPremiumPct, 2),
            ["tail_observations"] = tailLosses.Count,
            ["worst_losses"] = tailLosses.Take(5).Select(r => Math.Round(r * 100, 2)).ToList(),
            ["cvar_to_var_ratio"] = Math.Round((double)(cvarAmount / varAmount), 2),
            ["interpretation"] = $"Given a loss exceeding VaR (${Math.Round(varAmount, 2):N2}), " +
                               $"the expected loss is ${Math.Round(cvarAmount, 2):N2} over {timeHorizon} day(s). " +
                               $"Tail risk premium: ${Math.Round(tailRiskPremium, 2):N2}"
        };
    }

    private static Dictionary<string, object> CalculateParametricCVaR(
        decimal portfolioValue,
        List<double> returns,
        double confidenceLevel,
        int timeHorizon)
    {
        // Assume normal distribution
        var meanReturn = returns.Mean();
        var stdDev = returns.StandardDeviation();

        // Scale for time horizon
        var scaledMean = meanReturn * timeHorizon;
        var scaledStdDev = stdDev * Math.Sqrt(timeHorizon);

        // For normal distribution: CVaR = μ - σ * φ(Φ^-1(α)) / α
        // where α = 1 - confidence level
        var alpha = 1 - confidenceLevel;

        // Calculate z-score for VaR
        var zVaR = -2.326; // Approximation for 99% confidence
        if (confidenceLevel == 0.95)
            zVaR = -1.645;
        else if (confidenceLevel == 0.99)
            zVaR = -2.326;
        else if (confidenceLevel == 0.999)
            zVaR = -3.090;

        // CVaR for normal distribution
        var phi_z = Math.Exp(-0.5 * zVaR * zVaR) / Math.Sqrt(2 * Math.PI); // Standard normal PDF
        var cvarReturn = scaledMean + scaledStdDev * phi_z / alpha;

        var varReturn = scaledMean + zVaR * scaledStdDev;

        var varAmount = (decimal)(portfolioValue * (decimal)Math.Abs(varReturn));
        var cvarAmount = (decimal)(portfolioValue * (decimal)Math.Abs(cvarReturn));
        var tailRiskPremium = cvarAmount - varAmount;

        return new Dictionary<string, object>
        {
            ["cvar_amount"] = Math.Round(cvarAmount, 2),
            ["cvar_percentage"] = Math.Round((decimal)Math.Abs(cvarReturn) * 100, 2),
            ["var_amount"] = Math.Round(varAmount, 2),
            ["var_percentage"] = Math.Round((decimal)Math.Abs(varReturn) * 100, 2),
            ["tail_risk_premium"] = Math.Round(tailRiskPremium, 2),
            ["mean_return"] = Math.Round(meanReturn, 6),
            ["std_deviation"] = Math.Round(stdDev, 6),
            ["z_score"] = Math.Round(zVaR, 4),
            ["cvar_to_var_ratio"] = Math.Round((double)(cvarAmount / varAmount), 2),
            ["interpretation"] = $"Under normal distribution assumption, given a loss exceeding VaR (${Math.Round(varAmount, 2):N2}), " +
                               $"the expected loss is ${Math.Round(cvarAmount, 2):N2} over {timeHorizon} day(s)"
        };
    }

    private static double CalculateSkewness(List<double> returns)
    {
        var mean = returns.Mean();
        var stdDev = returns.StandardDeviation();
        var n = returns.Count;

        if (n < 3) return 0;

        var skewness = returns.Sum(r => Math.Pow((r - mean) / stdDev, 3)) * n / ((n - 1) * (n - 2));
        return skewness;
    }
}
