using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Data.Contracts;
using Orkeon.Trading.Tools.Domain.Models;
using Orkeon.Trading.Tools.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Data;

/// <summary>
/// Real-time tick data streaming tool for high-frequency analysis.
/// Provides tick-by-tick trade data via an injected <see cref="ITickDataProvider"/>.
/// </summary>
public class RealTimeTickDataTool(
    ITickDataProvider tickDataProvider,
    ILogger<RealTimeTickDataTool>? logger = null)
    : TradingToolBase<RealTimeTickDataRequest, RealTimeTickDataResponse>(logger)
{
    private readonly ITickDataProvider _tickDataProvider = tickDataProvider;

    protected override string ToolId => "realtime_tick_data";

    protected override async Task<RealTimeTickDataResponse> ExecuteTypedAsync(
        RealTimeTickDataRequest request,
        CancellationToken cancellationToken)
    {
        var durationSeconds = Math.Min(300, request.DurationSeconds);
        var maxTicks = Math.Min(10000, request.MaxTicks);

        _logger?.LogInformation("Streaming tick data for {Symbol} for {Duration}s (max {MaxTicks} ticks) via {Provider}",
            request.Symbol, durationSeconds, maxTicks, _tickDataProvider.ProviderName);

        var startTime = DateTime.UtcNow;
        var ticks = await _tickDataProvider.GetTickDataAsync(request.Symbol, durationSeconds, maxTicks, cancellationToken);
        var endTime = DateTime.UtcNow;

        Dictionary<string, object>? summary = null;
        if (request.Aggregate && ticks.Count > 0)
            summary = CalculateTickSummary(ticks);

        _logger?.LogInformation("Collected {Count} ticks for {Symbol}", ticks.Count, request.Symbol);

        return new RealTimeTickDataResponse
        {
            Symbol = request.Symbol, Ticks = ticks, TotalTicks = ticks.Count,
            StartTime = startTime, EndTime = endTime,
            DurationMs = (endTime - startTime).TotalMilliseconds,
            AverageTps = ticks.Count / Math.Max(0.001, (endTime - startTime).TotalSeconds),
            Summary = summary ?? new Dictionary<string, object>()
        };
    }

    private static Dictionary<string, object> CalculateTickSummary(List<TickData> ticks)
    {
        var prices = ticks.Select(t => t.Price).ToList();
        var buyTicks = ticks.Where(t => t.Side == "BUY").ToList();
        var sellTicks = ticks.Where(t => t.Side == "SELL").ToList();

        return new Dictionary<string, object>
        {
            ["high_price"] = prices.Max(), ["low_price"] = prices.Min(),
            ["last_price"] = prices.Last(), ["price_range"] = prices.Max() - prices.Min(),
            ["total_volume"] = ticks.Sum(t => t.Volume),
            ["average_trade_size"] = ticks.Average(t => t.Volume),
            ["buy_volume"] = buyTicks.Sum(t => t.Volume),
            ["sell_volume"] = sellTicks.Sum(t => t.Volume),
            ["buy_count"] = buyTicks.Count, ["sell_count"] = sellTicks.Count,
            ["buy_sell_ratio"] = sellTicks.Count > 0 ? (decimal)buyTicks.Count / sellTicks.Count : 0,
            ["vwap"] = CalculateVWAP(ticks),
            ["price_volatility"] = CalculateVolatility(prices)
        };
    }

    private static decimal CalculateVWAP(List<TickData> ticks)
    {
        var totalValue = ticks.Sum(t => t.Price * t.Volume);
        var totalVolume = ticks.Sum(t => t.Volume);
        return totalVolume > 0 ? totalValue / totalVolume : 0;
    }

    private static decimal CalculateVolatility(List<decimal> prices)
    {
        if (prices.Count < 2) return 0;
        var returns = new List<decimal>();
        for (int i = 1; i < prices.Count; i++)
            returns.Add((prices[i] - prices[i - 1]) / prices[i - 1]);
        var mean = returns.Average();
        var variance = returns.Sum(r => (r - mean) * (r - mean)) / returns.Count;
        return (decimal)Math.Sqrt((double)variance);
    }
}
