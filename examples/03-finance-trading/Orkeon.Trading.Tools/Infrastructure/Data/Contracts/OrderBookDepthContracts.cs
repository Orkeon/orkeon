using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Infrastructure.Data.Contracts;

/// <summary>
/// Typed request for the OrderBookDepthTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record OrderBookDepthRequest
{
    public string Symbol { get; init; } = "";
    public int Depth { get; init; } = 10;
    public bool CalculateImbalance { get; init; } = true;
}

/// <summary>
/// Typed response from the OrderBookDepthTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record OrderBookDepthResponse
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public required List<PriceLevel> Bids { get; init; }
    public required List<PriceLevel> Asks { get; init; }
    public required decimal Spread { get; init; }
    public required decimal SpreadBps { get; init; }
    public required decimal MidPrice { get; init; }
    public required decimal TotalBidVolume { get; init; }
    public required decimal TotalAskVolume { get; init; }
    public required int BidLevels { get; init; }
    public required int AskLevels { get; init; }
    public required Dictionary<string, object> Imbalance { get; init; }
}
