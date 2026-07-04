using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Execution.Contracts;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Execution;

/// <summary>
/// Implementation Shortfall (IS) execution algorithm tool.
/// Optimizes trade-off between market impact and timing risk.
/// Front-loads execution when alpha is strong, spreads out when market impact is high.
/// </summary>
public class ImplementationShortfallTool(ILogger<ImplementationShortfallTool>? logger = null)
    : TradingToolBase<ImplementationShortfallRequest, ImplementationShortfallResponse>(logger)
{
    protected override string ToolId => "implementation_shortfall";

    protected override async Task<ImplementationShortfallResponse> ExecuteTypedAsync(
        ImplementationShortfallRequest request,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Generating IS execution schedule for {Symbol} {Side} {Qty} shares with urgency {Urgency}",
            request.Symbol, request.Side, request.Quantity, request.Urgency);

        var execution = await Task.Run(() => GenerateISSchedule(
            request.Symbol,
            request.Side,
            request.Quantity,
            request.DecisionPrice,
            request.Urgency,
            request.AlphaForecast,
            request.Volatility,
            request.RiskAversion), cancellationToken);

        _logger?.LogInformation("IS schedule generated with {Slices} slices", execution.TotalSlices);

        return execution;
    }

    private static ImplementationShortfallResponse GenerateISSchedule(
        string symbol,
        string side,
        decimal quantity,
        decimal decisionPrice,
        string urgency,
        double alphaForecast,
        double volatility,
        double riskAversion)
    {
        // Implementation Shortfall optimal trajectory
        // Front-load if: high alpha, low volatility, high urgency
        // Spread out if: low alpha, high volatility, low urgency

        // Determine execution parameters based on urgency
        var (durationMinutes, frontLoadFactor) = urgency.ToLower() switch
        {
            "very_high" => (30, 0.70),   // Execute 70% in first 30 min
            "high" => (60, 0.60),         // Execute 60% in first hour
            "medium" => (120, 0.50),      // Execute 50% in first 2 hours
            "low" => (240, 0.40),         // Execute 40% in first 4 hours
            _ => (120, 0.50)
        };

        // Adjust based on alpha (higher alpha = more front-loading)
        if (Math.Abs(alphaForecast) > 0.01)
        {
            frontLoadFactor += Math.Sign(alphaForecast) * 0.10; // +/- 10%
            frontLoadFactor = Math.Clamp(frontLoadFactor, 0.3, 0.8);
        }

        // Adjust based on volatility (higher vol = less front-loading due to timing risk)
        if (volatility > 0.03) // High volatility
        {
            frontLoadFactor -= 0.10;
            frontLoadFactor = Math.Max(0.3, frontLoadFactor);
        }

        // Generate execution trajectory
        var numSlices = (int)(durationMinutes / 5); // 5-minute slices
        var schedule = new List<Dictionary<string, object>>();
        var cumulativeQuantity = 0m;

        // Calculate optimal trajectory (exponential decay from front-load)
        var trajectoryWeights = CalculateOptimalTrajectory(numSlices, frontLoadFactor);

        for (int i = 0; i < numSlices; i++)
        {
            var sliceTime = TimeSpan.FromMinutes(i * 5);
            var sliceQuantity = Math.Round(quantity * (decimal)trajectoryWeights[i], 0);

            if (sliceQuantity > 0)
            {
                cumulativeQuantity += sliceQuantity;

                // Estimate market impact for this slice
                var marketImpact = EstimateMarketImpact(sliceQuantity, quantity, i);
                var timingRisk = EstimateTimingRisk(quantity - cumulativeQuantity, volatility, (numSlices - i) * 5);

                schedule.Add(new Dictionary<string, object>
                {
                    ["slice_number"] = schedule.Count + 1,
                    ["execution_time_offset_minutes"] = (int)sliceTime.TotalMinutes,
                    ["quantity"] = sliceQuantity,
                    ["cumulative_quantity"] = cumulativeQuantity,
                    ["percentage_of_order"] = Math.Round((sliceQuantity / quantity) * 100, 2),
                    ["cumulative_percentage"] = Math.Round((cumulativeQuantity / quantity) * 100, 2),
                    ["estimated_market_impact_bps"] = Math.Round(marketImpact, 2),
                    ["estimated_timing_risk_bps"] = Math.Round(timingRisk, 2)
                });
            }
        }

        // Adjust last slice to ensure total quantity
        if (cumulativeQuantity < quantity && schedule.Count > 0)
        {
            var adjustment = quantity - cumulativeQuantity;
            var lastSlice = schedule.Last();
            lastSlice["quantity"] = (decimal)lastSlice["quantity"] + adjustment;
            lastSlice["cumulative_quantity"] = quantity;
            lastSlice["cumulative_percentage"] = 100m;
        }

        // Calculate expected implementation shortfall
        var expectedImpactCost = schedule.Sum(s => (decimal)s["estimated_market_impact_bps"]) / 10000m * decisionPrice;
        var expectedTimingCost = schedule.Sum(s => (decimal)s["estimated_timing_risk_bps"]) / 10000m * decisionPrice;
        var totalIS = expectedImpactCost + expectedTimingCost;

        return new ImplementationShortfallResponse
        {
            Timestamp = DateTime.UtcNow,
            Algorithm = "IMPLEMENTATION_SHORTFALL",
            Symbol = symbol,
            Side = side,
            TotalQuantity = quantity,
            DecisionPrice = decisionPrice,
            Urgency = urgency,
            AlphaForecast = alphaForecast,
            FrontLoadFactor = Math.Round(frontLoadFactor, 2),
            TotalSlices = schedule.Count,
            ExecutionDurationMinutes = durationMinutes,
            ExecutionSchedule = schedule,
            ExpectedCosts = new Dictionary<string, object>
            {
                ["market_impact_cost"] = Math.Round(expectedImpactCost, 2),
                ["timing_risk_cost"] = Math.Round(expectedTimingCost, 2),
                ["total_implementation_shortfall"] = Math.Round(totalIS, 2),
                ["shortfall_bps"] = Math.Round((totalIS / decisionPrice) * 10000, 2)
            },
            ExecutionStrategy = new Dictionary<string, string>
            {
                ["type"] = "IMPLEMENTATION_SHORTFALL",
                ["objective"] = "Minimize total cost: market impact + timing risk",
                ["benefits"] = "Optimal trade-off based on alpha strength and urgency",
                ["best_for"] = "Orders with strong alpha signals or time-sensitive opportunities"
            }
        };
    }

    private static List<double> CalculateOptimalTrajectory(int numSlices, double frontLoadFactor)
    {
        // Generate exponential decay trajectory
        // Front-load more at the beginning, taper off

        var weights = new List<double>();
        var decay = -Math.Log(1 - frontLoadFactor) / numSlices;

        for (int i = 0; i < numSlices; i++)
        {
            // Exponential decay: higher weight early, lower weight late
            var weight = Math.Exp(-decay * i) / numSlices;
            weights.Add(weight);
        }

        // Normalize to sum to 1.0
        var sum = weights.Sum();
        weights = weights.Select(w => w / sum).ToList();

        return weights;
    }

    private static decimal EstimateMarketImpact(decimal sliceQuantity, decimal totalQuantity, int sliceIndex)
    {
        // Market impact increases with slice size and earlier execution
        // Simplified: impact = k * sqrt(quantity) where k depends on liquidity

        var participationRate = sliceQuantity / totalQuantity;
        var impactFactor = 5.0m; // basis points per sqrt(participation)

        // Earlier slices have more impact (market not yet absorbed previous trades)
        var earlyExecutionPenalty = 1.0m + (decimal)sliceIndex * 0.05m;

        return impactFactor * (decimal)Math.Sqrt((double)participationRate) * earlyExecutionPenalty;
    }

    private static decimal EstimateTimingRisk(decimal remainingQuantity, double volatility, int minutesRemaining)
    {
        // Timing risk = risk of adverse price move on unexecuted quantity
        // Risk increases with remaining quantity and volatility

        if (remainingQuantity <= 0) return 0;

        var timingRisk = (decimal)(volatility * Math.Sqrt(minutesRemaining / 390.0)); // 390 min trading day
        var basisPoints = timingRisk * 10000m;

        return basisPoints;
    }
}
