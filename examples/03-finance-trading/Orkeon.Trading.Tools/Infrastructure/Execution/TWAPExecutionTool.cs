using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Execution.Contracts;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Execution;

/// <summary>
/// Time-Weighted Average Price (TWAP) execution algorithm tool.
/// Breaks large orders into equal-sized slices executed at regular intervals.
/// Simplest execution algorithm, useful when volume patterns are unpredictable.
/// </summary>
public class TWAPExecutionTool(ILogger<TWAPExecutionTool>? logger = null)
    : TradingToolBase<TWAPExecutionRequest, TWAPExecutionResponse>(logger)
{
    protected override string ToolId => "twap_execution";

    protected override async Task<TWAPExecutionResponse> ExecuteTypedAsync(
        TWAPExecutionRequest request,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Generating TWAP execution schedule for {Symbol} {Side} {Qty} shares",
            request.Symbol, request.Side, request.Quantity);

        var execution = await Task.Run(() => GenerateTWAPSchedule(
            request.Symbol,
            request.Side,
            request.Quantity,
            request.StartTime,
            request.EndTime,
            request.SliceIntervalMinutes,
            request.RandomizeTiming), cancellationToken);

        _logger?.LogInformation("TWAP schedule generated with {Slices} slices", execution.TotalSlices);

        return execution;
    }

    private static TWAPExecutionResponse GenerateTWAPSchedule(
        string symbol,
        string side,
        decimal quantity,
        string startTime,
        string endTime,
        int sliceIntervalMinutes,
        bool randomizeTiming)
    {
        var start = TimeSpan.Parse(startTime);
        var end = TimeSpan.Parse(endTime);
        var duration = end - start;

        // Calculate number of slices
        var numSlices = (int)(duration.TotalMinutes / sliceIntervalMinutes);
        var sliceQuantity = Math.Round(quantity / numSlices, 0);

        var schedule = new List<Dictionary<string, object>>();
        var cumulativeQuantity = 0m;
        var random = randomizeTiming ? new Random(42) : null;

        for (int i = 0; i < numSlices; i++)
        {
            var baseTime = start + TimeSpan.FromMinutes(i * sliceIntervalMinutes);

            // Add random jitter if requested (±2 minutes)
            var jitter = random != null ? TimeSpan.FromSeconds(random.Next(-120, 120)) : TimeSpan.Zero;
            var sliceTime = baseTime + jitter;

            // Ensure time stays within bounds
            if (sliceTime < start) sliceTime = start;
            if (sliceTime > end) sliceTime = end;

            var currentSliceQty = sliceQuantity;

            // Last slice: allocate remaining quantity
            if (i == numSlices - 1)
            {
                currentSliceQty = quantity - cumulativeQuantity;
            }

            cumulativeQuantity += currentSliceQty;

            schedule.Add(new Dictionary<string, object>
            {
                ["slice_number"] = i + 1,
                ["execution_time"] = sliceTime.ToString(@"hh\:mm\:ss"),
                ["quantity"] = currentSliceQty,
                ["cumulative_quantity"] = cumulativeQuantity,
                ["percentage_of_order"] = Math.Round((currentSliceQty / quantity) * 100, 2),
                ["cumulative_percentage"] = Math.Round((cumulativeQuantity / quantity) * 100, 2)
            });
        }

        return new TWAPExecutionResponse
        {
            Timestamp = DateTime.UtcNow,
            Algorithm = "TWAP",
            Symbol = symbol,
            Side = side,
            TotalQuantity = quantity,
            StartTime = startTime,
            EndTime = endTime,
            TotalSlices = schedule.Count,
            SliceIntervalMinutes = sliceIntervalMinutes,
            QuantityPerSlice = sliceQuantity,
            RandomizedTiming = randomizeTiming,
            ExecutionSchedule = schedule,
            EstimatedCompletion = $"{duration.TotalHours:F1} hours",
            ExecutionStrategy = new Dictionary<string, string>
            {
                ["type"] = "TWAP",
                ["objective"] = "Achieve time-weighted average price",
                ["benefits"] = "Simple, predictable, low complexity",
                ["best_for"] = "Orders with unpredictable volume patterns or need for steady pace"
            }
        };
    }
}
