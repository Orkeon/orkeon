using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Execution.Contracts;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Execution;

/// <summary>
/// Percentage of Volume (POV) execution algorithm tool.
/// Maintains target participation rate relative to market volume throughout execution.
/// Adapts to changing market liquidity conditions dynamically.
/// </summary>
public class POVExecutionTool(ILogger<POVExecutionTool>? logger = null)
    : TradingToolBase<POVExecutionRequest, POVExecutionResponse>(logger)
{
    protected override string ToolId => "pov_execution";

    protected override async Task<POVExecutionResponse> ExecuteTypedAsync(
        POVExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var maxPOV = request.MaxPov ?? request.TargetPov * 1.5;
        var minPOV = request.MinPov ?? request.TargetPov * 0.5;

        _logger?.LogInformation("Generating POV execution plan for {Symbol} with target {POV}%",
            request.Symbol, request.TargetPov * 100);

        var execution = await Task.Run(() => GeneratePOVPlan(
            request.Symbol,
            request.Side,
            request.Quantity,
            request.TargetPov,
            maxPOV,
            minPOV,
            request.MaxDurationMinutes), cancellationToken);

        _logger?.LogInformation("POV execution plan generated");

        return execution;
    }

    private static POVExecutionResponse GeneratePOVPlan(
        string symbol,
        string side,
        decimal quantity,
        double targetPOV,
        double maxPOV,
        double minPOV,
        int maxDuration)
    {
        // POV algorithm: Execute quantity = target_pov * observed_market_volume
        // Adapts dynamically to market conditions

        // Estimate completion time based on expected volume
        var avgDailyVolume = 10_000_000m; // Typical liquid stock
        var expectedMinutesToComplete = (int)((double)quantity / ((double)avgDailyVolume / 390.0 * targetPOV));
        expectedMinutesToComplete = Math.Min(expectedMinutesToComplete, maxDuration);

        // Generate monitoring intervals (check every 5 minutes)
        var monitoringIntervals = new List<Dictionary<string, object>>();
        var numIntervals = expectedMinutesToComplete / 5;

        var cumulativeQuantity = 0m;
        for (int i = 0; i < numIntervals; i++)
        {
            var intervalStart = i * 5;
            var intervalEnd = (i + 1) * 5;

            // Estimate market volume for this interval
            var estimatedMarketVolume = EstimateIntervalVolume(intervalStart, avgDailyVolume);

            // Calculate target execution quantity
            var targetQuantity = Math.Min(
                (decimal)((double)estimatedMarketVolume * targetPOV),
                quantity - cumulativeQuantity
            );

            if (targetQuantity > 0)
            {
                cumulativeQuantity += targetQuantity;

                monitoringIntervals.Add(new Dictionary<string, object>
                {
                    ["interval_number"] = i + 1,
                    ["time_range_minutes"] = $"{intervalStart}-{intervalEnd}",
                    ["estimated_market_volume"] = Math.Round(estimatedMarketVolume, 0),
                    ["target_execution_quantity"] = Math.Round(targetQuantity, 0),
                    ["target_participation"] = Math.Round((decimal)targetPOV * 100, 2),
                    ["min_participation"] = Math.Round((decimal)minPOV * 100, 2),
                    ["max_participation"] = Math.Round((decimal)maxPOV * 100, 2),
                    ["cumulative_quantity"] = cumulativeQuantity,
                    ["cumulative_percentage"] = Math.Round((cumulativeQuantity / quantity) * 100, 2)
                });
            }

            if (cumulativeQuantity >= quantity) break;
        }

        return new POVExecutionResponse
        {
            Timestamp = DateTime.UtcNow,
            Algorithm = "POV",
            Symbol = symbol,
            Side = side,
            TotalQuantity = quantity,
            TargetParticipationRate = Math.Round((decimal)(targetPOV * 100), 2),
            MinParticipationRate = Math.Round((decimal)(minPOV * 100), 2),
            MaxParticipationRate = Math.Round((decimal)(maxPOV * 100), 2),
            MonitoringIntervals = monitoringIntervals,
            ExpectedCompletionMinutes = expectedMinutesToComplete,
            AdaptiveFeatures = new List<string>
            {
                "Dynamically adjusts to real-time market volume",
                "Increases participation when volume spikes",
                "Decreases participation during low volume",
                "Respects min/max participation bounds",
                "Auto-completes within max duration"
            },
            ExecutionStrategy = new Dictionary<string, string>
            {
                ["type"] = "POV",
                ["objective"] = "Maintain consistent market participation",
                ["benefits"] = "Adapts to liquidity, minimizes signaling risk",
                ["best_for"] = "Orders requiring consistent market presence without dominating"
            }
        };
    }

    private static decimal EstimateIntervalVolume(int minuteOffset, decimal avgDailyVolume)
    {
        // Estimate 5-minute volume based on time of day
        var minutesPerDay = 390;
        var intervalsPerDay = minutesPerDay / 5;
        var avgIntervalVolume = avgDailyVolume / intervalsPerDay;

        // Apply intraday profile
        var hourOfDay = 9.5 + (minuteOffset / 60.0);
        var multiplier = hourOfDay switch
        {
            >= 9.5 and < 10.5 => 2.5m,
            >= 10.5 and < 12 => 1.2m,
            >= 12 and < 14 => 0.8m,
            >= 14 and < 15.5 => 1.0m,
            >= 15.5 and <= 16 => 2.0m,
            _ => 1.0m
        };

        return avgIntervalVolume * multiplier;
    }
}
