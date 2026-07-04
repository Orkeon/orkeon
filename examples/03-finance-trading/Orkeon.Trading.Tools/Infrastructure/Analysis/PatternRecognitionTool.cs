using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Analysis.Contracts;
using Orkeon.Trading.Tools.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Analysis;

/// <summary>
/// Pattern recognition tool for detecting common chart patterns.
/// Identifies candlestick patterns, chart patterns, and price action setups.
/// </summary>
public class PatternRecognitionTool(ILogger<PatternRecognitionTool>? logger = null)
    : TradingToolBase<PatternRecognitionRequest, PatternRecognitionResponse>(logger)
{
    protected override string ToolId => "pattern_recognition";

    protected override string? ValidateTypedRequest(PatternRecognitionRequest request)
    {
        if (request.PriceData.Count < 10)
            return "At least 10 data points required";
        return null;
    }

    protected override async Task<PatternRecognitionResponse> ExecuteTypedAsync(
        PatternRecognitionRequest request,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Detecting patterns for {Symbol} with {Count} data points", request.Symbol, request.PriceData.Count);

        var patterns = await Task.Run(() => DetectPatterns(request.PriceData, request.PatternTypes, request.MinConfidence), cancellationToken);

        var primaryPattern = patterns.OrderByDescending(p => p.Confidence).FirstOrDefault();

        Dictionary<string, object>? primaryPatternDict = null;
        if (primaryPattern != null)
        {
            primaryPatternDict = new Dictionary<string, object>
            {
                ["name"] = primaryPattern.PatternName,
                ["type"] = primaryPattern.Type,
                ["confidence"] = primaryPattern.Confidence,
                ["description"] = primaryPattern.Description ?? ""
            };
        }

        _logger?.LogInformation("Detected {Count} patterns for {Symbol}", patterns.Count, request.Symbol);

        return new PatternRecognitionResponse
        {
            Symbol = request.Symbol,
            Timestamp = DateTime.UtcNow,
            Patterns = patterns,
            TotalPatterns = patterns.Count,
            PatternSummary = new Dictionary<string, object>
            {
                ["bullish_patterns"] = patterns.Count(p => p.Type == "BULLISH"),
                ["bearish_patterns"] = patterns.Count(p => p.Type == "BEARISH"),
                ["neutral_patterns"] = patterns.Count(p => p.Type == "NEUTRAL")
            },
            PrimaryPattern = primaryPatternDict
        };
    }

    private static List<DetectedPattern> DetectPatterns(List<MarketData> priceData, List<string> patternTypes, double minConfidence)
    {
        var patterns = new List<DetectedPattern>();
        var includeAll = patternTypes.Contains("all");

        // Candlestick patterns
        if (includeAll || patternTypes.Contains("candlestick"))
        {
            patterns.AddRange(DetectCandlestickPatterns(priceData, minConfidence));
        }

        // Chart patterns
        if (includeAll || patternTypes.Contains("chart"))
        {
            patterns.AddRange(DetectChartPatterns(priceData, minConfidence));
        }

        return patterns.Where(p => p.Confidence >= (decimal)minConfidence).ToList();
    }

    private static List<DetectedPattern> DetectCandlestickPatterns(List<MarketData> priceData, double minConfidence)
    {
        var patterns = new List<DetectedPattern>();
        if (priceData.Count < 3) return patterns;

        // Check last few candles for patterns
        var recent = priceData.TakeLast(3).ToList();
        var last = recent.Last();
        var prev = recent.Count > 1 ? recent[recent.Count - 2] : last;

        // Doji pattern
        var bodySize = Math.Abs(last.Close - last.Open);
        var totalRange = last.High - last.Low;
        if (totalRange > 0 && bodySize / totalRange < 0.1m)
        {
            patterns.Add(new DetectedPattern
            {
                PatternName = "Doji",
                Type = "NEUTRAL",
                Confidence = 0.85m,
                DetectionTime = DateTime.UtcNow,
                Description = "Indecision in the market, potential reversal",
                Metadata = new Dictionary<string, object> { ["candle_count"] = 1 }
            });
        }

        // Hammer pattern (bullish)
        var lowerShadow = Math.Min(last.Open, last.Close) - last.Low;
        var upperShadow = last.High - Math.Max(last.Open, last.Close);
        if (lowerShadow > bodySize * 2 && upperShadow < bodySize * 0.5m)
        {
            patterns.Add(new DetectedPattern
            {
                PatternName = "Hammer",
                Type = "BULLISH",
                Confidence = 0.75m,
                DetectionTime = DateTime.UtcNow,
                Description = "Potential bullish reversal after downtrend",
                Metadata = new Dictionary<string, object> { ["candle_count"] = 1 }
            });
        }

        // Engulfing pattern
        if (recent.Count >= 2)
        {
            var prevBody = Math.Abs(prev.Close - prev.Open);
            var lastBody = Math.Abs(last.Close - last.Open);

            // Bullish engulfing
            if (prev.Close < prev.Open && last.Close > last.Open && lastBody > prevBody)
            {
                patterns.Add(new DetectedPattern
                {
                    PatternName = "Bullish Engulfing",
                    Type = "BULLISH",
                    Confidence = 0.80m,
                    DetectionTime = DateTime.UtcNow,
                    Description = "Strong bullish reversal signal",
                    Metadata = new Dictionary<string, object> { ["candle_count"] = 2 }
                });
            }

            // Bearish engulfing
            if (prev.Close > prev.Open && last.Close < last.Open && lastBody > prevBody)
            {
                patterns.Add(new DetectedPattern
                {
                    PatternName = "Bearish Engulfing",
                    Type = "BEARISH",
                    Confidence = 0.80m,
                    DetectionTime = DateTime.UtcNow,
                    Description = "Strong bearish reversal signal",
                    Metadata = new Dictionary<string, object> { ["candle_count"] = 2 }
                });
            }
        }

        return patterns;
    }

    private static List<DetectedPattern> DetectChartPatterns(List<MarketData> priceData, double minConfidence)
    {
        var patterns = new List<DetectedPattern>();
        if (priceData.Count < 20) return patterns;

        var closes = priceData.Select(p => p.Close).ToList();
        var highs = priceData.Select(p => p.High).ToList();
        var lows = priceData.Select(p => p.Low).ToList();

        // Simplified pattern detection using price action

        // Ascending triangle (bullish)
        var recentHighs = highs.TakeLast(10).ToList();
        var recentLows = lows.TakeLast(10).ToList();
        var isAscendingTriangle = IsAscendingTriangle(recentHighs, recentLows);
        if (isAscendingTriangle)
        {
            patterns.Add(new DetectedPattern
            {
                PatternName = "Ascending Triangle",
                Type = "BULLISH",
                Confidence = 0.70m,
                DetectionTime = DateTime.UtcNow,
                Description = "Bullish continuation pattern, potential breakout upward",
                Metadata = new Dictionary<string, object> { ["lookback"] = 10 }
            });
        }

        // Double bottom (bullish)
        var isDoubleBottom = DetectDoubleBottom(lows.TakeLast(20).ToList());
        if (isDoubleBottom)
        {
            patterns.Add(new DetectedPattern
            {
                PatternName = "Double Bottom",
                Type = "BULLISH",
                Confidence = 0.75m,
                DetectionTime = DateTime.UtcNow,
                Description = "Bullish reversal pattern, support level confirmed",
                Metadata = new Dictionary<string, object> { ["lookback"] = 20 }
            });
        }

        // Head and shoulders (bearish)
        var isHeadAndShoulders = DetectHeadAndShoulders(highs.TakeLast(30).ToList());
        if (isHeadAndShoulders)
        {
            patterns.Add(new DetectedPattern
            {
                PatternName = "Head and Shoulders",
                Type = "BEARISH",
                Confidence = 0.80m,
                DetectionTime = DateTime.UtcNow,
                Description = "Bearish reversal pattern, potential trend change",
                Metadata = new Dictionary<string, object> { ["lookback"] = 30 }
            });
        }

        return patterns;
    }

    private static bool IsAscendingTriangle(List<decimal> highs, List<decimal> lows)
    {
        // Simplified: flat top (resistance) + rising lows (support)
        var maxHigh = highs.Max();
        var recentHighsFlat = highs.Skip(5).All(h => Math.Abs(h - maxHigh) / maxHigh < 0.02m);

        var firstLows = lows.Take(5).Average();
        var lastLows = lows.Skip(5).Average();
        var lowsRising = lastLows > firstLows;

        return recentHighsFlat && lowsRising;
    }

    private static bool DetectDoubleBottom(List<decimal> lows)
    {
        if (lows.Count < 10) return false;

        // Find two similar lows
        var minLow = lows.Min();
        var lowIndices = lows.Select((l, i) => new { Low = l, Index = i })
                             .Where(x => Math.Abs(x.Low - minLow) / minLow < 0.02m)
                             .Select(x => x.Index)
                             .ToList();

        return lowIndices.Count >= 2 && (lowIndices.Last() - lowIndices.First()) > 5;
    }

    private static bool DetectHeadAndShoulders(List<decimal> highs)
    {
        if (highs.Count < 15) return false;

        // Simplified: find three peaks with middle one highest
        var peaks = new List<(int Index, decimal Value)>();
        for (int i = 1; i < highs.Count - 1; i++)
        {
            if (highs[i] > highs[i - 1] && highs[i] > highs[i + 1])
            {
                peaks.Add((i, highs[i]));
            }
        }

        if (peaks.Count >= 3)
        {
            var sorted = peaks.OrderByDescending(p => p.Value).Take(3).OrderBy(p => p.Index).ToList();
            return sorted[1].Value > sorted[0].Value && sorted[1].Value > sorted[2].Value;
        }

        return false;
    }
}
