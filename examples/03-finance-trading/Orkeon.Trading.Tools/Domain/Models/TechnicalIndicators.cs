namespace Orkeon.Trading.Tools.Domain.Models;

/// <summary>
/// Collection of technical indicators for a symbol
/// </summary>
public record TechnicalIndicators
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public required decimal CurrentPrice { get; init; }

    // Trend Indicators
    public MovingAverages? MA { get; init; }
    public MACD? MacdIndicator { get; init; }
    public ADX? AdxIndicator { get; init; }
    public Ichimoku? IchimokuCloud { get; init; }

    // Momentum Indicators
    public RSI? RsiIndicator { get; init; }
    public Stochastic? StochasticOscillator { get; init; }
    public decimal? MomentumPercent { get; init; }
    public decimal? ROC { get; init; } // Rate of Change

    // Volatility Indicators
    public BollingerBands? Bollinger { get; init; }
    public ATR? AtrIndicator { get; init; }
    public decimal? HistoricalVolatility { get; init; }

    // Volume Indicators
    public decimal? OBV { get; init; } // On-Balance Volume
    public decimal? VolumeMA { get; init; }
    public decimal? VWAP { get; init; }
    public decimal? MoneyFlowIndex { get; init; }

    // Support/Resistance
    public List<decimal>? SupportLevels { get; init; }
    public List<decimal>? ResistanceLevels { get; init; }
    public decimal? PivotPoint { get; init; }

    public Dictionary<string, object>? CustomIndicators { get; init; }
}

public record MovingAverages
{
    public decimal SMA20 { get; init; }
    public decimal SMA50 { get; init; }
    public decimal SMA100 { get; init; }
    public decimal SMA200 { get; init; }
    public decimal EMA12 { get; init; }
    public decimal EMA26 { get; init; }
    public decimal EMA50 { get; init; }
    public decimal EMA200 { get; init; }
    public string? Trend { get; init; } // "BULLISH", "BEARISH", "SIDEWAYS"
}

public record MACD
{
    public decimal MacdLine { get; init; }
    public decimal SignalLine { get; init; }
    public decimal Histogram { get; init; }
    public string Signal { get; init; } = "NEUTRAL"; // "BUY", "SELL", "NEUTRAL"
    public decimal Divergence => MacdLine - SignalLine;
}

public record RSI
{
    public decimal Value { get; init; }
    public string Signal { get; init; } = "NEUTRAL"; // "OVERSOLD", "OVERBOUGHT", "NEUTRAL"
    public bool IsOversold => Value < 30;
    public bool IsOverbought => Value > 70;
    public decimal? RSI14 { get; init; }
}

public record BollingerBands
{
    public decimal Upper { get; init; }
    public decimal Middle { get; init; }
    public decimal Lower { get; init; }
    public decimal BandWidth { get; init; }
    public decimal PercentB { get; init; }
    public string Signal { get; init; } = "NEUTRAL"; // "SQUEEZE", "BREAKOUT", "NEUTRAL"
}

public record ATR
{
    public decimal Value { get; init; }
    public decimal ATR14 { get; init; }
    public decimal PercentOfPrice { get; init; }
    public string VolatilityLevel { get; init; } = "NORMAL"; // "LOW", "NORMAL", "HIGH", "EXTREME"
}

public record ADX
{
    public decimal Value { get; init; }
    public decimal PlusDI { get; init; }
    public decimal MinusDI { get; init; }
    public string TrendStrength { get; init; } = "WEAK"; // "WEAK", "MODERATE", "STRONG", "VERY_STRONG"
    public string Direction { get; init; } = "NEUTRAL"; // "BULLISH", "BEARISH", "NEUTRAL"
}

public record Stochastic
{
    public decimal K { get; init; }
    public decimal D { get; init; }
    public string Signal { get; init; } = "NEUTRAL"; // "OVERSOLD", "OVERBOUGHT", "NEUTRAL"
    public bool IsOversold => K < 20;
    public bool IsOverbought => K > 80;
}

public record Ichimoku
{
    public decimal TenkanSen { get; init; }
    public decimal KijunSen { get; init; }
    public decimal SenkouSpanA { get; init; }
    public decimal SenkouSpanB { get; init; }
    public decimal ChikouSpan { get; init; }
    public string Signal { get; init; } = "NEUTRAL"; // "BULLISH", "BEARISH", "NEUTRAL"
    public bool IsAboveCloud { get; init; }
    public bool IsBelowCloud { get; init; }
}

/// <summary>
/// Pattern recognition results
/// </summary>
public record PatternRecognition
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public List<DetectedPattern> Patterns { get; init; } = new();
    public string? PrimaryPattern { get; init; }
    public decimal? ConfidenceScore { get; init; }
}

public record DetectedPattern
{
    public required string PatternName { get; init; }
    public required string Type { get; init; } // "BULLISH", "BEARISH", "NEUTRAL"
    public required decimal Confidence { get; init; }
    public DateTime DetectionTime { get; init; }
    public string? Description { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Market regime classification
/// </summary>
public record MarketRegime
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Regime { get; init; } // "TRENDING_BULL", "TRENDING_BEAR", "RANGING", "VOLATILE"
    public decimal Confidence { get; init; }
    public decimal Volatility { get; init; }
    public decimal Trend { get; init; }
    public Dictionary<string, decimal>? RegimeProbabilities { get; init; }
}
