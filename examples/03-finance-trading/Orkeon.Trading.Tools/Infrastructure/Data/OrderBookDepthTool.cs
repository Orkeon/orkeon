using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Data.Contracts;
using Orkeon.Trading.Tools.Domain.Models;
using Orkeon.Trading.Tools.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Data;

/// <summary>
/// Order book depth tool for analyzing market microstructure and liquidity.
/// Provides Level 2 market data via an injected <see cref="IOrderBookProvider"/>.
/// </summary>
public class OrderBookDepthTool(
    IOrderBookProvider orderBookProvider,
    ILogger<OrderBookDepthTool>? logger = null)
    : TradingToolBase<OrderBookDepthRequest, OrderBookDepthResponse>(logger)
{
    private readonly IOrderBookProvider _orderBookProvider = orderBookProvider;

    protected override string ToolId => "orderbook_depth";

    protected override string? ValidateTypedRequest(OrderBookDepthRequest request)
    {
        if (request.Depth > 100)
            return "depth must not exceed 100";
        return null;
    }

    protected override async Task<OrderBookDepthResponse> ExecuteTypedAsync(
        OrderBookDepthRequest request,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Fetching order book for {Symbol} with depth {Depth} via {Provider}",
            request.Symbol, request.Depth, _orderBookProvider.ProviderName);

        var orderBook = await _orderBookProvider.GetOrderBookAsync(request.Symbol, request.Depth, cancellationToken);

        Dictionary<string, object>? imbalance = null;
        if (request.CalculateImbalance)
            imbalance = CalculateImbalanceMetrics(orderBook);

        _logger?.LogInformation("Fetched order book for {Symbol}: spread={Spread:F4}, mid={MidPrice:F2}",
            request.Symbol, orderBook.Spread, orderBook.MidPrice);

        return new OrderBookDepthResponse
        {
            Symbol = request.Symbol, Timestamp = orderBook.Timestamp,
            Bids = orderBook.Bids, Asks = orderBook.Asks,
            Spread = orderBook.Spread,
            SpreadBps = orderBook.MidPrice > 0 ? (orderBook.Spread / orderBook.MidPrice) * 10000 : 0,
            MidPrice = orderBook.MidPrice,
            TotalBidVolume = orderBook.Bids.Sum(b => b.Volume),
            TotalAskVolume = orderBook.Asks.Sum(a => a.Volume),
            BidLevels = orderBook.Bids.Count, AskLevels = orderBook.Asks.Count,
            Imbalance = imbalance ?? new Dictionary<string, object>()
        };
    }

    private static Dictionary<string, object> CalculateImbalanceMetrics(OrderBookData orderBook)
    {
        var topBidVolume = orderBook.Bids.Take(5).Sum(b => b.Volume);
        var topAskVolume = orderBook.Asks.Take(5).Sum(a => a.Volume);
        var totalBidVolume = orderBook.Bids.Sum(b => b.Volume);
        var totalAskVolume = orderBook.Asks.Sum(a => a.Volume);

        var imbalanceRatio = (totalBidVolume - totalAskVolume) / (totalBidVolume + totalAskVolume);
        var tobImbalance = (topBidVolume - topAskVolume) / (topBidVolume + topAskVolume);
        var weightedMid = (orderBook.Bids[0].Price * topAskVolume + orderBook.Asks[0].Price * topBidVolume)
                          / (topBidVolume + topAskVolume);

        return new Dictionary<string, object>
        {
            ["imbalance_ratio"] = Math.Round(imbalanceRatio, 4),
            ["tob_imbalance"] = Math.Round(tobImbalance, 4),
            ["weighted_mid_price"] = Math.Round(weightedMid, 2),
            ["bid_pressure"] = totalBidVolume / (totalBidVolume + totalAskVolume),
            ["ask_pressure"] = totalAskVolume / (totalBidVolume + totalAskVolume),
            ["depth_imbalance_signal"] = Math.Abs(imbalanceRatio) > 0.1m
                ? (imbalanceRatio > 0 ? "BUY_PRESSURE" : "SELL_PRESSURE") : "BALANCED"
        };
    }
}
