namespace Orkeon.Trading.Tools.Domain.Models;

/// <summary>
/// Represents market data for a financial instrument
/// </summary>
public record MarketData
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Source { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
    public decimal? AdjustedClose { get; init; }
    public decimal? VWAP { get; init; }
    public int? TradeCount { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Represents a collection of market data points
/// </summary>
public record MarketDataCollection
{
    public required string Symbol { get; init; }
    public required List<MarketData> Data { get; init; }
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public required string TimeFrame { get; init; }
    public required List<string> DataSources { get; init; }
    public int TotalPoints => Data.Count;
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Represents real-time order book data
/// </summary>
public record OrderBookData
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public required List<PriceLevel> Bids { get; init; }
    public required List<PriceLevel> Asks { get; init; }
    public decimal Spread => Asks.Count > 0 && Bids.Count > 0 ? Asks[0].Price - Bids[0].Price : 0;
    public decimal MidPrice => Asks.Count > 0 && Bids.Count > 0 ? (Asks[0].Price + Bids[0].Price) / 2 : 0;
}

/// <summary>
/// Represents a single price level in the order book
/// </summary>
public record PriceLevel
{
    public required decimal Price { get; init; }
    public required decimal Volume { get; init; }
    public int? OrderCount { get; init; }
}

/// <summary>
/// Represents tick-level trade data
/// </summary>
public record TickData
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public required decimal Price { get; init; }
    public required decimal Volume { get; init; }
    public required string Side { get; init; } // "BUY" or "SELL"
    public string? ExchangeTradeId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Represents fundamental data for a company
/// </summary>
public record FundamentalData
{
    public required string Symbol { get; init; }
    public required DateTime ReportDate { get; init; }

    // Valuation Metrics
    public decimal? MarketCap { get; init; }
    public decimal? EnterpriseValue { get; init; }
    public decimal? PERatio { get; init; }
    public decimal? PEGRatio { get; init; }
    public decimal? PriceToBook { get; init; }
    public decimal? PriceToSales { get; init; }
    public decimal? EVToEBITDA { get; init; }

    // Profitability Metrics
    public decimal? ROE { get; init; }
    public decimal? ROA { get; init; }
    public decimal? ROI { get; init; }
    public decimal? NetMargin { get; init; }
    public decimal? OperatingMargin { get; init; }
    public decimal? GrossMargin { get; init; }

    // Financial Health
    public decimal? CurrentRatio { get; init; }
    public decimal? QuickRatio { get; init; }
    public decimal? DebtToEquity { get; init; }
    public decimal? InterestCoverage { get; init; }

    // Growth Metrics
    public decimal? RevenueGrowthYoY { get; init; }
    public decimal? EarningsGrowthYoY { get; init; }
    public decimal? DividendYield { get; init; }

    public Dictionary<string, object>? AdditionalMetrics { get; init; }
}

/// <summary>
/// Represents market data from a specific trading venue/exchange.
/// Used by SmartOrderRoutingTool and IVenueDataProvider.
/// </summary>
public record VenueMarketData
{
    public required string Venue { get; init; }
    public decimal BidPrice { get; init; }
    public decimal AskPrice { get; init; }
    public int BidSize { get; init; }
    public int AskSize { get; init; }
    public decimal TakerFee { get; init; }
    public decimal MakerRebate { get; init; }
    public int AverageLatencyMs { get; init; }
    public double HistoricalFillRate { get; init; }
}

/// <summary>
/// Represents alternative/sentiment data
/// </summary>
public record SentimentData
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Source { get; init; }

    // Sentiment Scores (-1 to 1)
    public decimal OverallSentiment { get; init; }
    public decimal? NewsSentiment { get; init; }
    public decimal? SocialMediaSentiment { get; init; }
    public decimal? AnalystSentiment { get; init; }

    // Volume Metrics
    public int? MentionCount { get; init; }
    public int? PositiveMentions { get; init; }
    public int? NegativeMentions { get; init; }
    public int? NeutralMentions { get; init; }

    // Confidence Metrics
    public decimal? ConfidenceScore { get; init; }
    public string? ConfidenceLevel { get; init; } // "HIGH", "MEDIUM", "LOW"

    public Dictionary<string, object>? Metadata { get; init; }
}
