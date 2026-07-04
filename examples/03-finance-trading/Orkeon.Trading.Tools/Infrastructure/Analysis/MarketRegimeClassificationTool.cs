using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Analysis.Contracts;
using Orkeon.Trading.Tools.Domain.Models;
using Microsoft.Extensions.Logging;
using MathNet.Numerics.Statistics;

namespace Orkeon.Trading.Tools.Infrastructure.Analysis;

/// <summary>
/// Market regime classification tool for identifying market states.
/// Classifies markets into regimes: trending bull/bear, ranging, volatile.
/// </summary>
public class MarketRegimeClassificationTool(ILogger<MarketRegimeClassificationTool>? logger = null)
    : TradingToolBase<MarketRegimeClassificationRequest, MarketRegimeClassificationResponse>(logger)
{
    protected override string ToolId => "market_regime_classification";

    protected override string? ValidateTypedRequest(MarketRegimeClassificationRequest request)
    {
        if (request.PriceData.Count < 20)
            return "At least 20 data points required";
        return null;
    }

    protected override async Task<MarketRegimeClassificationResponse> ExecuteTypedAsync(
        MarketRegimeClassificationRequest request,
        CancellationToken cancellationToken)
    {
        var lookbackDays = Math.Min(request.PriceData.Count, request.LookbackDays);

        _logger?.LogInformation("Classifying market regime for {Symbol} over {Days} days", request.Symbol, lookbackDays);

        var classification = await Task.Run(() => ClassifyRegime(request.Symbol, request.PriceData.TakeLast(lookbackDays).ToList()), cancellationToken);

        _logger?.LogInformation("Classified {Symbol} as {Regime}", request.Symbol, classification.CurrentRegime);

        return classification;
    }

    private static MarketRegimeClassificationResponse ClassifyRegime(string symbol, List<MarketData> priceData)
    {
        var closes = priceData.Select(p => (double)p.Close).ToArray();
        var highs = priceData.Select(p => (double)p.High).ToArray();
        var lows = priceData.Select(p => (double)p.Low).ToArray();

        // Calculate metrics for regime classification
        var volatility = CalculateVolatility(closes);
        var trendStrength = CalculateTrendStrength(closes);
        var trendDirection = CalculateTrendDirection(closes);
        var rangeCompression = CalculateRangeCompression(highs, lows);

        // Classify regime based on metrics
        var (regime, confidence, probabilities) = DetermineRegime(volatility, trendStrength, trendDirection, rangeCompression);

        return new MarketRegimeClassificationResponse
        {
            Symbol = symbol,
            Timestamp = DateTime.UtcNow,
            CurrentRegime = regime,
            Confidence = Math.Round(confidence, 2),
            RegimeProbabilities = probabilities,
            Metrics = new Dictionary<string, object>
            {
                ["volatility"] = Math.Round(volatility, 4),
                ["trend_strength"] = Math.Round(trendStrength, 2),
                ["trend_direction"] = Math.Round(trendDirection, 2),
                ["range_compression"] = Math.Round(rangeCompression, 2)
            },
            Characteristics = GetRegimeCharacteristics(regime),
            TradingImplications = GetTradingImplications(regime)
        };
    }

    private static double CalculateVolatility(double[] closes)
    {
        var returns = new List<double>();
        for (int i = 1; i < closes.Length; i++)
        {
            returns.Add((closes[i] - closes[i - 1]) / closes[i - 1]);
        }
        return Math.Sqrt(returns.Variance() * 252); // Annualized
    }

    private static double CalculateTrendStrength(double[] closes)
    {
        // Use ADX-like calculation (simplified)
        var sma20 = closes.TakeLast(20).Average();
        var sma50 = closes.TakeLast(Math.Min(50, closes.Length)).Average();

        var deviation = Math.Abs(sma20 - sma50) / sma50 * 100;
        return Math.Min(100, deviation * 10); // 0-100 scale
    }

    private static double CalculateTrendDirection(double[] closes)
    {
        // Linear regression slope
        var n = closes.Length;
        var x = Enumerable.Range(0, n).Select(i => (double)i).ToArray();
        var y = closes;

        var xMean = x.Average();
        var yMean = y.Average();

        var numerator = x.Zip(y, (xi, yi) => (xi - xMean) * (yi - yMean)).Sum();
        var denominator = x.Sum(xi => Math.Pow(xi - xMean, 2));

        var slope = denominator != 0 ? numerator / denominator : 0;

        // Normalize to -100 to +100
        return Math.Clamp(slope * 1000, -100, 100);
    }

    private static double CalculateRangeCompression(double[] highs, double[] lows)
    {
        var ranges = highs.Zip(lows, (h, l) => h - l).ToArray();
        var avgRange = ranges.Average();
        var recentRange = ranges.TakeLast(10).Average();

        return avgRange > 0 ? (recentRange / avgRange) * 100 : 100;
    }

    private static (string regime, double confidence, Dictionary<string, decimal> probabilities) DetermineRegime(
        double volatility,
        double trendStrength,
        double trendDirection,
        double rangeCompression)
    {
        var scores = new Dictionary<string, double>
        {
            ["TRENDING_BULL"] = 0,
            ["TRENDING_BEAR"] = 0,
            ["RANGING"] = 0,
            ["VOLATILE"] = 0
        };

        // High volatility favors VOLATILE regime
        if (volatility > 0.3)
        {
            scores["VOLATILE"] += 40;
        }

        // Strong trend favors trending regimes
        if (trendStrength > 50)
        {
            if (trendDirection > 10)
            {
                scores["TRENDING_BULL"] += 50;
            }
            else if (trendDirection < -10)
            {
                scores["TRENDING_BEAR"] += 50;
            }
        }

        // Low trend strength and low volatility favors RANGING
        if (trendStrength < 30 && volatility < 0.2)
        {
            scores["RANGING"] += 50;
        }

        // Range compression check
        if (rangeCompression < 70)
        {
            scores["RANGING"] += 20;
        }

        // Determine regime
        var maxScore = scores.MaxBy(kvp => kvp.Value);
        var totalScore = scores.Values.Sum();

        var probabilities = scores.ToDictionary(
            kvp => kvp.Key,
            kvp => (decimal)(totalScore > 0 ? kvp.Value / totalScore : 0.25)
        );

        var confidence = totalScore > 0 ? maxScore.Value / totalScore : 0.5;

        return (maxScore.Key, confidence, probabilities);
    }

    private static List<string> GetRegimeCharacteristics(string regime)
    {
        return regime switch
        {
            "TRENDING_BULL" => new List<string>
            {
                "Strong upward momentum",
                "Higher highs and higher lows",
                "Above key moving averages",
                "Positive breadth"
            },
            "TRENDING_BEAR" => new List<string>
            {
                "Strong downward momentum",
                "Lower highs and lower lows",
                "Below key moving averages",
                "Negative breadth"
            },
            "RANGING" => new List<string>
            {
                "Trading within defined range",
                "Low directional movement",
                "Mean reversion behavior",
                "Consolidation phase"
            },
            "VOLATILE" => new List<string>
            {
                "High price swings",
                "Increased uncertainty",
                "Wider bid-ask spreads",
                "Unstable support/resistance"
            },
            _ => new List<string> { "Unknown regime" }
        };
    }

    private static List<string> GetTradingImplications(string regime)
    {
        return regime switch
        {
            "TRENDING_BULL" => new List<string>
            {
                "Favor long positions",
                "Use pullbacks to add exposure",
                "Trailing stops recommended",
                "Momentum strategies effective"
            },
            "TRENDING_BEAR" => new List<string>
            {
                "Reduce long exposure",
                "Consider short positions",
                "Capital preservation priority",
                "Wait for reversal signals"
            },
            "RANGING" => new List<string>
            {
                "Mean reversion strategies",
                "Trade range extremes",
                "Tight stops recommended",
                "Avoid trend following"
            },
            "VOLATILE" => new List<string>
            {
                "Reduce position sizes",
                "Widen stops",
                "Increase cash allocation",
                "Consider options strategies"
            },
            _ => new List<string> { "Standard risk management" }
        };
    }
}
