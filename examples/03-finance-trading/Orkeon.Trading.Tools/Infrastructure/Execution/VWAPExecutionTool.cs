using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Execution.Contracts;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Execution;

/// <summary>
/// Volume-Weighted Average Price (VWAP) execution algorithm tool.
/// Breaks large orders into smaller slices matched to historical volume profile.
/// Minimizes market impact by trading proportionally to market volume throughout the day.
/// </summary>
public class VWAPExecutionTool(ILogger<VWAPExecutionTool>? logger = null)
    : TradingToolBase<VWAPExecutionRequest, VWAPExecutionResponse>(logger)
{
    protected override string ToolId => "vwap_execution";

    protected override async Task<VWAPExecutionResponse> ExecuteTypedAsync(
        VWAPExecutionRequest request,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Generating VWAP execution schedule for {Symbol} {Side} {Qty} shares",
            request.Symbol, request.Side, request.Quantity);

        var execution = await Task.Run(() => GenerateVWAPSchedule(
            request.Symbol,
            request.Side,
            request.Quantity,
            request.StartTime,
            request.EndTime,
            request.HistoricalVolumeProfile,
            request.ParticipationRate), cancellationToken);

        _logger?.LogInformation("VWAP schedule generated with {Slices} slices", execution.TotalSlices);

        return execution;
    }

    private static VWAPExecutionResponse GenerateVWAPSchedule(
        string symbol,
        string side,
        decimal quantity,
        string startTime,
        string endTime,
        List<double>? volumeProfile,
        double participationRate)
    {
        // Parse times
        var start = TimeSpan.Parse(startTime);
        var end = TimeSpan.Parse(endTime);
        var duration = end - start;

        // Use typical intraday volume profile if not provided (U-shaped)
        var profile = volumeProfile ?? GenerateTypicalVolumeProfile();

        // Calculate slice times (every 5 minutes)
        var sliceInterval = TimeSpan.FromMinutes(5);
        var numSlices = (int)(duration.TotalMinutes / sliceInterval.TotalMinutes);

        // Match slices to volume profile
        var schedule = new List<Dictionary<string, object>>();
        var cumulativeQuantity = 0m;

        for (int i = 0; i < numSlices; i++)
        {
            var sliceTime = start + TimeSpan.FromMinutes(i * sliceInterval.TotalMinutes);

            // Get volume weight for this time slice
            var profileIndex = (int)((double)i / numSlices * profile.Count);
            profileIndex = Math.Min(profileIndex, profile.Count - 1);
            var volumeWeight = profile[profileIndex];

            // Calculate slice quantity
            var sliceQuantity = Math.Round(quantity * (decimal)volumeWeight, 0);

            // Apply participation rate limit (estimate expected market volume)
            var estimatedMarketVolume = EstimateMarketVolume(symbol, sliceTime);
            var maxSliceQuantity = (decimal)(estimatedMarketVolume * participationRate);
            sliceQuantity = Math.Min(sliceQuantity, maxSliceQuantity);

            if (sliceQuantity > 0)
            {
                cumulativeQuantity += sliceQuantity;

                schedule.Add(new Dictionary<string, object>
                {
                    ["slice_number"] = schedule.Count + 1,
                    ["execution_time"] = sliceTime.ToString(@"hh\:mm"),
                    ["quantity"] = sliceQuantity,
                    ["cumulative_quantity"] = cumulativeQuantity,
                    ["percentage_of_order"] = Math.Round((sliceQuantity / quantity) * 100, 2),
                    ["cumulative_percentage"] = Math.Round((cumulativeQuantity / quantity) * 100, 2),
                    ["volume_weight"] = Math.Round((decimal)volumeWeight, 4),
                    ["estimated_market_volume"] = Math.Round((decimal)estimatedMarketVolume, 0),
                    ["participation_rate"] = Math.Round((decimal)(sliceQuantity / (decimal)estimatedMarketVolume), 4)
                });
            }
        }

        // Adjust last slice to ensure total quantity is met
        if (cumulativeQuantity < quantity && schedule.Count > 0)
        {
            var lastSlice = schedule.Last();
            var adjustment = quantity - cumulativeQuantity;
            lastSlice["quantity"] = (decimal)lastSlice["quantity"] + adjustment;
            lastSlice["cumulative_quantity"] = quantity;
            lastSlice["cumulative_percentage"] = 100m;
        }

        return new VWAPExecutionResponse
        {
            Timestamp = DateTime.UtcNow,
            Algorithm = "VWAP",
            Symbol = symbol,
            Side = side,
            TotalQuantity = quantity,
            StartTime = startTime,
            EndTime = endTime,
            TotalSlices = schedule.Count,
            SliceIntervalMinutes = sliceInterval.TotalMinutes,
            ExecutionSchedule = schedule,
            EstimatedCompletion = $"{duration.TotalHours:F1} hours",
            MaxParticipationRate = participationRate,
            ExecutionStrategy = new Dictionary<string, string>
            {
                ["type"] = "VWAP",
                ["objective"] = "Match volume-weighted average price",
                ["benefits"] = "Low market impact, tracks intraday volume",
                ["best_for"] = "Large orders requiring execution over several hours"
            }
        };
    }

    private static List<double> GenerateTypicalVolumeProfile()
    {
        // Typical U-shaped intraday volume profile (higher at open and close)
        // Represents percentage of daily volume in each time slice

        var profile = new List<double>();

        // Market open (9:30-10:30): High volume
        for (int i = 0; i < 12; i++) profile.Add(0.025); // 30% in first hour

        // Mid-morning (10:30-12:00): Moderate volume
        for (int i = 0; i < 18; i++) profile.Add(0.012); // ~22%

        // Lunch (12:00-14:00): Low volume
        for (int i = 0; i < 24; i++) profile.Add(0.008); // ~19%

        // Afternoon (14:00-15:30): Moderate volume
        for (int i = 0; i < 18; i++) profile.Add(0.010); // ~18%

        // Market close (15:30-16:00): High volume
        for (int i = 0; i < 6; i++) profile.Add(0.018); // ~11%

        // Normalize to ensure sum = 1.0
        var sum = profile.Sum();
        profile = profile.Select(v => v / sum).ToList();

        return profile;
    }

    private static double EstimateMarketVolume(string symbol, TimeSpan sliceTime)
    {
        // Estimate expected market volume for this time slice
        // In production, use historical volume data

        // Assume average daily volume and apply intraday profile
        var avgDailyVolume = 10_000_000.0; // 10M shares/day typical for liquid stocks

        // Get volume multiplier based on time of day
        var multiplier = sliceTime.TotalHours switch
        {
            >= 9.5 and < 10.5 => 2.5,  // High volume at open
            >= 10.5 and < 12 => 1.2,   // Moderate morning
            >= 12 and < 14 => 0.8,     // Lunch slowdown
            >= 14 and < 15.5 => 1.0,   // Afternoon pickup
            >= 15.5 and <= 16 => 2.0,  // High volume at close
            _ => 1.0
        };

        // Volume per 5-minute slice
        var slicesPerDay = 78; // 6.5 hours * 12 five-minute periods
        var avgVolumePerSlice = avgDailyVolume / slicesPerDay;

        return avgVolumePerSlice * multiplier;
    }
}
