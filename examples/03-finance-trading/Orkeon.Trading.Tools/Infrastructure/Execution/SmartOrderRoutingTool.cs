using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Execution.Contracts;
using Orkeon.Trading.Tools.Domain.Models;
using Orkeon.Trading.Tools.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Execution;

/// <summary>
/// Smart Order Routing (SOR) tool for multi-venue execution optimization.
/// Routes orders across multiple exchanges to optimize price, speed, and fill probability
/// via an injected <see cref="IVenueDataProvider"/>.
/// </summary>
public class SmartOrderRoutingTool(
    IVenueDataProvider venueDataProvider,
    ILogger<SmartOrderRoutingTool>? logger = null)
    : TradingToolBase<SmartOrderRoutingRequest, SmartOrderRoutingResponse>(logger)
{
    private readonly IVenueDataProvider _venueDataProvider = venueDataProvider;

    private static readonly List<string> DefaultVenues =
        ["NYSE", "NASDAQ", "ARCA", "BATS", "IEX", "EDGX", "EDGA", "PSX"];

    protected override string ToolId => "smart_order_routing";

    protected override async Task<SmartOrderRoutingResponse> ExecuteTypedAsync(
        SmartOrderRoutingRequest request,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Generating smart routing plan for {Symbol} {Side} {Qty} via {Provider}",
            request.Symbol, request.Side, request.Quantity, _venueDataProvider.ProviderName);

        var venues = request.AvailableVenues ?? DefaultVenues;
        var venueDataList = await _venueDataProvider.GetVenueDataAsync(request.Symbol, venues, cancellationToken);

        // Build venue lookup dictionary
        var venueData = venueDataList.ToDictionary(v => v.Venue);

        var scoredVenues = ScoreVenues(venueData, request.OptimizationObjective, request.Side);
        var routing = AllocateQuantityAcrossVenues(request.Quantity, scoredVenues, request.OptimizationObjective);
        var totalCost = CalculateTotalCost(routing, request.Side);

        _logger?.LogInformation("Routing plan generated across {Venues} venues", routing.Count);

        return new SmartOrderRoutingResponse
        {
            Timestamp = DateTime.UtcNow,
            Algorithm = "SMART_ORDER_ROUTING",
            Symbol = request.Symbol,
            Side = request.Side,
            TotalQuantity = request.Quantity,
            OrderType = request.OrderType,
            OptimizationObjective = request.OptimizationObjective,
            TotalVenues = routing.Count,
            RoutingPlan = routing.Select(r => new Dictionary<string, object>
            {
                ["venue"] = r.Venue, ["quantity"] = r.Quantity,
                ["percentage"] = Math.Round((r.Quantity / request.Quantity) * 100, 2),
                ["priority"] = r.Priority,
                ["expected_fill_rate"] = Math.Round(r.ExpectedFillRate * 100, 1),
                ["estimated_fee"] = Math.Round(r.EstimatedFee, 4),
                ["estimated_latency_ms"] = r.EstimatedLatencyMs,
                ["venue_liquidity"] = r.VenueLiquidity,
                ["reason"] = r.Reason
            }).ToList(),
            ExpectedCosts = totalCost,
            ExecutionStrategy = new Dictionary<string, string>
            {
                ["type"] = "SMART_ORDER_ROUTING",
                ["objective"] = $"Optimize for {request.OptimizationObjective.Replace('_', ' ')}",
                ["benefits"] = "Best execution across fragmented markets",
                ["best_for"] = "Large orders requiring optimal price discovery"
            },
            LimitPrice = request.LimitPrice
        };
    }

    private static List<VenueScore> ScoreVenues(
        Dictionary<string, VenueMarketData> venueData, string objective, string side)
    {
        return venueData.Values
            .Select(venue => new VenueScore
            {
                Venue = venue.Venue,
                Score = objective.ToLower() switch
                {
                    "best_price" => ScoreBestPrice(venue, side),
                    "best_speed" => ScoreBestSpeed(venue),
                    "best_fill_rate" => ScoreBestFillRate(venue),
                    "lowest_cost" => ScoreLowestCost(venue, side),
                    _ => ScoreBestPrice(venue, side)
                },
                Data = venue
            })
            .OrderByDescending(v => v.Score)
            .ToList();
    }

    private static double ScoreBestPrice(VenueMarketData venue, string side)
    {
        var price = side == "BUY" ? venue.AskPrice : venue.BidPrice;
        var liquidity = side == "BUY" ? venue.AskSize : venue.BidSize;
        var priceScore = side == "BUY" ? 1.0 / (double)price : (double)price;
        var liquidityScore = Math.Log((double)liquidity + 1) / 10.0;
        return priceScore * 0.7 + liquidityScore * 0.3;
    }

    private static double ScoreBestSpeed(VenueMarketData venue) => 1.0 / (venue.AverageLatencyMs + 0.1);

    private static double ScoreBestFillRate(VenueMarketData venue) => venue.HistoricalFillRate * 100;

    private static double ScoreLowestCost(VenueMarketData venue, string side) =>
        1.0 / ((double)(venue.TakerFee - venue.MakerRebate) * 10000 + 0.1);

    private static List<RoutingDecision> AllocateQuantityAcrossVenues(
        decimal totalQuantity, List<VenueScore> scoredVenues, string objective)
    {
        var routing = new List<RoutingDecision>();
        var remainingQuantity = totalQuantity;
        var totalScore = scoredVenues.Sum(v => v.Score);

        for (int i = 0; i < scoredVenues.Count && remainingQuantity > 0; i++)
        {
            var venue = scoredVenues[i];
            var allocationPct = venue.Score / totalScore;
            if (allocationPct < 0.01 && i > 0) continue;

            var allocatedQty = Math.Min(
                Math.Round(totalQuantity * (decimal)allocationPct, 0),
                remainingQuantity);

            if (allocatedQty > 0)
            {
                routing.Add(new RoutingDecision
                {
                    Venue = venue.Venue, Quantity = allocatedQty, Priority = i + 1,
                    ExpectedFillRate = venue.Data.HistoricalFillRate,
                    EstimatedFee = venue.Data.TakerFee * allocatedQty,
                    EstimatedLatencyMs = venue.Data.AverageLatencyMs,
                    VenueLiquidity = venue.Data.AskSize + venue.Data.BidSize,
                    Reason = $"Score: {Math.Round(venue.Score, 2)}, {objective.Replace('_', ' ')}"
                });
                remainingQuantity -= allocatedQty;
            }
        }

        if (remainingQuantity > 0 && routing.Count > 0)
            routing[0].Quantity += remainingQuantity;

        return routing;
    }

    private static Dictionary<string, object> CalculateTotalCost(List<RoutingDecision> routing, string side)
    {
        return new Dictionary<string, object>
        {
            ["total_estimated_fees"] = Math.Round(routing.Sum(r => r.EstimatedFee), 4),
            ["average_latency_ms"] = Math.Round((decimal)routing.Average(r => r.EstimatedLatencyMs), 1),
            ["expected_fill_rate"] = Math.Round((decimal)(routing.Average(r => r.ExpectedFillRate) * 100), 1),
            ["venues_used"] = routing.Count
        };
    }

    private record VenueScore
    {
        public required string Venue { get; init; }
        public required double Score { get; init; }
        public required VenueMarketData Data { get; init; }
    }

    private record RoutingDecision
    {
        public required string Venue { get; init; }
        public decimal Quantity { get; set; }
        public required int Priority { get; init; }
        public required double ExpectedFillRate { get; init; }
        public required decimal EstimatedFee { get; init; }
        public required int EstimatedLatencyMs { get; init; }
        public required int VenueLiquidity { get; init; }
        public required string Reason { get; init; }
    }
}
