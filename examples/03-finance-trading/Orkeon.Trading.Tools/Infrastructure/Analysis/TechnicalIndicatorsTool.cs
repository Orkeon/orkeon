using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Analysis.Contracts;
using Orkeon.Trading.Tools.Domain.Models;
using Microsoft.Extensions.Logging;
using MathNet.Numerics.Statistics;

namespace Orkeon.Trading.Tools.Infrastructure.Analysis;

/// <summary>
/// Comprehensive technical indicators calculator supporting 15+ indicators.
/// Includes trend, momentum, volatility, and volume indicators.
/// </summary>
public class TechnicalIndicatorsTool(ILogger<TechnicalIndicatorsTool>? logger = null)
    : TradingToolBase<TechnicalIndicatorsRequest, TechnicalIndicatorsResponse>(logger)
{
    protected override string ToolId => "technical_indicators";

    protected override string? ValidateTypedRequest(TechnicalIndicatorsRequest request)
    {
        if (request.PriceData.Count < 50)
            return "At least 50 data points required for technical indicator calculation";
        return null;
    }

    protected override async Task<TechnicalIndicatorsResponse> ExecuteTypedAsync(
        TechnicalIndicatorsRequest request,
        CancellationToken cancellationToken)
    {
        var requestedIndicators = request.Indicators;
        var includeAll = requestedIndicators == null || requestedIndicators.Count == 0;

        _logger?.LogInformation(
            "Calculating technical indicators for {Symbol} with {Count} data points",
            request.Symbol, request.PriceData.Count);

        // Calculate indicators
        var indicators = await Task.Run(() => CalculateAllIndicators(request.PriceData, requestedIndicators, includeAll), cancellationToken);

        var currentPrice = request.PriceData.Last().Close;

        var response = new TechnicalIndicatorsResponse
        {
            Symbol = request.Symbol,
            Timestamp = DateTime.UtcNow,
            CurrentPrice = currentPrice,
            Indicators = indicators,
            Signals = GenerateSignals(indicators),
            Metadata = new Dictionary<string, object>
            {
                ["data_points"] = request.PriceData.Count,
                ["start_date"] = request.PriceData.First().Timestamp,
                ["end_date"] = request.PriceData.Last().Timestamp
            }
        };

        _logger?.LogInformation(
            "Calculated {Count} technical indicators for {Symbol}",
            indicators.Count, request.Symbol);

        return response;
    }

    private static Dictionary<string, object> CalculateAllIndicators(
        List<MarketData> priceData,
        List<string>? requestedIndicators,
        bool includeAll)
    {
        var indicators = new Dictionary<string, object>();
        var closes = priceData.Select(p => (double)p.Close).ToArray();
        var highs = priceData.Select(p => (double)p.High).ToArray();
        var lows = priceData.Select(p => (double)p.Low).ToArray();
        var volumes = priceData.Select(p => (double)p.Volume).ToArray();

        // Moving Averages
        if (includeAll || requestedIndicators!.Any(i => i.ToUpper() == "MA" || i.ToUpper() == "SMA" || i.ToUpper() == "EMA"))
        {
            indicators["moving_averages"] = CalculateMovingAverages(closes);
        }

        // MACD
        if (includeAll || requestedIndicators!.Any(i => i.ToUpper() == "MACD"))
        {
            indicators["macd"] = CalculateMACD(closes);
        }

        // RSI
        if (includeAll || requestedIndicators!.Any(i => i.ToUpper() == "RSI"))
        {
            indicators["rsi"] = CalculateRSI(closes);
        }

        // Bollinger Bands
        if (includeAll || requestedIndicators!.Any(i => i.ToUpper() == "BB" || i.ToUpper() == "BOLLINGER"))
        {
            indicators["bollinger_bands"] = CalculateBollingerBands(closes);
        }

        // ATR
        if (includeAll || requestedIndicators!.Any(i => i.ToUpper() == "ATR"))
        {
            indicators["atr"] = CalculateATR(highs, lows, closes);
        }

        // ADX
        if (includeAll || requestedIndicators!.Any(i => i.ToUpper() == "ADX"))
        {
            indicators["adx"] = CalculateADX(highs, lows, closes);
        }

        // Stochastic
        if (includeAll || requestedIndicators!.Any(i => i.ToUpper() == "STOCHASTIC" || i.ToUpper() == "STOCH"))
        {
            indicators["stochastic"] = CalculateStochastic(highs, lows, closes);
        }

        // OBV
        if (includeAll || requestedIndicators!.Any(i => i.ToUpper() == "OBV"))
        {
            indicators["obv"] = CalculateOBV(closes, volumes);
        }

        // VWAP
        if (includeAll || requestedIndicators!.Any(i => i.ToUpper() == "VWAP"))
        {
            indicators["vwap"] = CalculateVWAP(priceData);
        }

        // Support/Resistance
        if (includeAll || requestedIndicators!.Any(i => i.ToUpper().Contains("SUPPORT") || i.ToUpper().Contains("RESISTANCE")))
        {
            indicators["support_resistance"] = CalculateSupportResistance(priceData);
        }

        return indicators;
    }

    private static Dictionary<string, object> CalculateMovingAverages(double[] closes)
    {
        var currentPrice = closes.Last();

        return new Dictionary<string, object>
        {
            ["sma_20"] = CalculateSMA(closes, 20),
            ["sma_50"] = CalculateSMA(closes, 50),
            ["sma_100"] = CalculateSMA(closes, 100),
            ["sma_200"] = CalculateSMA(closes, 200),
            ["ema_12"] = CalculateEMA(closes, 12),
            ["ema_26"] = CalculateEMA(closes, 26),
            ["ema_50"] = CalculateEMA(closes, 50),
            ["ema_200"] = CalculateEMA(closes, 200),
            ["trend"] = DetermineTrend(closes)
        };
    }

    private static double CalculateSMA(double[] values, int period)
    {
        if (values.Length < period) return 0;
        return values.TakeLast(period).Average();
    }

    private static double CalculateEMA(double[] values, int period)
    {
        if (values.Length < period) return 0;

        var multiplier = 2.0 / (period + 1);
        var ema = values.Take(period).Average();

        for (int i = period; i < values.Length; i++)
        {
            ema = (values[i] - ema) * multiplier + ema;
        }

        return ema;
    }

    private static string DetermineTrend(double[] closes)
    {
        if (closes.Length < 50) return "UNKNOWN";

        var sma20 = CalculateSMA(closes, 20);
        var sma50 = CalculateSMA(closes, 50);
        var currentPrice = closes.Last();

        if (currentPrice > sma20 && sma20 > sma50)
            return "BULLISH";
        else if (currentPrice < sma20 && sma20 < sma50)
            return "BEARISH";
        else
            return "SIDEWAYS";
    }

    private static Dictionary<string, object> CalculateMACD(double[] closes)
    {
        var ema12 = CalculateEMA(closes, 12);
        var ema26 = CalculateEMA(closes, 26);
        var macdLine = ema12 - ema26;

        // For signal line, we'd need to calculate EMA of MACD line
        // Simplified here
        var signalLine = macdLine * 0.9; // Approximation
        var histogram = macdLine - signalLine;

        return new Dictionary<string, object>
        {
            ["macd_line"] = Math.Round(macdLine, 2),
            ["signal_line"] = Math.Round(signalLine, 2),
            ["histogram"] = Math.Round(histogram, 2),
            ["signal"] = histogram > 0 ? "BUY" : histogram < 0 ? "SELL" : "NEUTRAL"
        };
    }

    private static Dictionary<string, object> CalculateRSI(double[] closes)
    {
        if (closes.Length < 14) return new Dictionary<string, object> { ["value"] = 50, ["signal"] = "NEUTRAL" };

        var changes = new List<double>();
        for (int i = 1; i < closes.Length; i++)
        {
            changes.Add(closes[i] - closes[i - 1]);
        }

        var gains = changes.TakeLast(14).Where(c => c > 0).DefaultIfEmpty(0).Average();
        var losses = Math.Abs(changes.TakeLast(14).Where(c => c < 0).DefaultIfEmpty(0).Average());

        var rs = losses == 0 ? 100 : gains / losses;
        var rsi = 100 - (100 / (1 + rs));

        return new Dictionary<string, object>
        {
            ["value"] = Math.Round(rsi, 2),
            ["signal"] = rsi < 30 ? "OVERSOLD" : rsi > 70 ? "OVERBOUGHT" : "NEUTRAL"
        };
    }

    private static Dictionary<string, object> CalculateBollingerBands(double[] closes)
    {
        if (closes.Length < 20) return new Dictionary<string, object>();

        var sma20 = CalculateSMA(closes, 20);
        var stdDev = closes.TakeLast(20).StandardDeviation();

        var upper = sma20 + (2 * stdDev);
        var middle = sma20;
        var lower = sma20 - (2 * stdDev);
        var currentPrice = closes.Last();

        var percentB = (currentPrice - lower) / (upper - lower);

        return new Dictionary<string, object>
        {
            ["upper"] = Math.Round(upper, 2),
            ["middle"] = Math.Round(middle, 2),
            ["lower"] = Math.Round(lower, 2),
            ["bandwidth"] = Math.Round(upper - lower, 2),
            ["percent_b"] = Math.Round(percentB, 2),
            ["signal"] = percentB < 0.2 ? "OVERSOLD" : percentB > 0.8 ? "OVERBOUGHT" : "NEUTRAL"
        };
    }

    private static Dictionary<string, object> CalculateATR(double[] highs, double[] lows, double[] closes)
    {
        if (closes.Length < 14) return new Dictionary<string, object> { ["value"] = 0 };

        var trueRanges = new List<double>();
        for (int i = 1; i < closes.Length; i++)
        {
            var tr = Math.Max(
                highs[i] - lows[i],
                Math.Max(
                    Math.Abs(highs[i] - closes[i - 1]),
                    Math.Abs(lows[i] - closes[i - 1])
                )
            );
            trueRanges.Add(tr);
        }

        var atr14 = trueRanges.TakeLast(14).Average();
        var percentOfPrice = (atr14 / closes.Last()) * 100;

        return new Dictionary<string, object>
        {
            ["value"] = Math.Round(atr14, 2),
            ["atr_14"] = Math.Round(atr14, 2),
            ["percent_of_price"] = Math.Round(percentOfPrice, 2),
            ["volatility_level"] = percentOfPrice < 2 ? "LOW" : percentOfPrice < 4 ? "NORMAL" : percentOfPrice < 6 ? "HIGH" : "EXTREME"
        };
    }

    private static Dictionary<string, object> CalculateADX(double[] highs, double[] lows, double[] closes, int period = 14)
    {
        // Wilder's DMI/ADX calculation
        // Need at least 2*period bars for smoothing + period bars for ADX averaging = ~3*period, but 2*period is minimum viable
        var minBars = 2 * period;
        if (closes.Length < minBars)
        {
            return new Dictionary<string, object>
            {
                ["value"] = 0.0,
                ["plus_di"] = 0.0,
                ["minus_di"] = 0.0,
                ["trend_strength"] = "INSUFFICIENT_DATA"
            };
        }

        int n = closes.Length;

        // Step 1: Calculate +DM, -DM, and TR for each bar (starting at index 1)
        var plusDM = new double[n];
        var minusDM = new double[n];
        var tr = new double[n];

        for (int i = 1; i < n; i++)
        {
            double upMove = highs[i] - highs[i - 1];
            double downMove = lows[i - 1] - lows[i];

            plusDM[i] = (upMove > downMove && upMove > 0) ? upMove : 0.0;
            minusDM[i] = (downMove > upMove && downMove > 0) ? downMove : 0.0;

            tr[i] = Math.Max(
                highs[i] - lows[i],
                Math.Max(
                    Math.Abs(highs[i] - closes[i - 1]),
                    Math.Abs(lows[i] - closes[i - 1])
                )
            );
        }

        // Step 2: Apply Wilder's smoothing
        // First smoothed value = sum of first 'period' values (indices 1..period)
        double smoothedPlusDM = 0, smoothedMinusDM = 0, smoothedTR = 0;
        for (int i = 1; i <= period; i++)
        {
            smoothedPlusDM += plusDM[i];
            smoothedMinusDM += minusDM[i];
            smoothedTR += tr[i];
        }

        // Calculate +DI, -DI, and DX series starting after the first smoothing window
        var dxValues = new List<double>();

        // First DI values from the initial smoothed sums
        double plusDI = smoothedTR != 0 ? 100.0 * smoothedPlusDM / smoothedTR : 0.0;
        double minusDI = smoothedTR != 0 ? 100.0 * smoothedMinusDM / smoothedTR : 0.0;
        double diSum = plusDI + minusDI;
        double dx = diSum != 0 ? Math.Abs(plusDI - minusDI) / diSum * 100.0 : 0.0;
        dxValues.Add(dx);

        // Subsequent values: Wilder's smoothing formula: smoothed = prev - prev/period + current
        double latestPlusDI = plusDI;
        double latestMinusDI = minusDI;

        for (int i = period + 1; i < n; i++)
        {
            smoothedPlusDM = smoothedPlusDM - (smoothedPlusDM / period) + plusDM[i];
            smoothedMinusDM = smoothedMinusDM - (smoothedMinusDM / period) + minusDM[i];
            smoothedTR = smoothedTR - (smoothedTR / period) + tr[i];

            latestPlusDI = smoothedTR != 0 ? 100.0 * smoothedPlusDM / smoothedTR : 0.0;
            latestMinusDI = smoothedTR != 0 ? 100.0 * smoothedMinusDM / smoothedTR : 0.0;
            diSum = latestPlusDI + latestMinusDI;
            dx = diSum != 0 ? Math.Abs(latestPlusDI - latestMinusDI) / diSum * 100.0 : 0.0;
            dxValues.Add(dx);
        }

        // Step 3: Calculate ADX from DX values
        double adx;
        if (dxValues.Count < period)
        {
            // Not enough DX values for a full ADX period, use simple average of what we have
            adx = dxValues.Average();
        }
        else
        {
            // First ADX = average of first 'period' DX values
            adx = dxValues.Take(period).Average();

            // Subsequent ADX values: ((prev_ADX * (period-1)) + current_DX) / period
            for (int i = period; i < dxValues.Count; i++)
            {
                adx = ((adx * (period - 1)) + dxValues[i]) / period;
            }
        }

        return new Dictionary<string, object>
        {
            ["value"] = Math.Round(adx, 2),
            ["plus_di"] = Math.Round(latestPlusDI, 2),
            ["minus_di"] = Math.Round(latestMinusDI, 2),
            ["trend_strength"] = adx < 20 ? "WEAK" : adx < 40 ? "MODERATE" : adx < 60 ? "STRONG" : "VERY_STRONG"
        };
    }

    private static Dictionary<string, object> CalculateStochastic(double[] highs, double[] lows, double[] closes)
    {
        if (closes.Length < 14) return new Dictionary<string, object>();

        var period = 14;
        var highestHigh = highs.TakeLast(period).Max();
        var lowestLow = lows.TakeLast(period).Min();
        var currentClose = closes.Last();

        var k = ((currentClose - lowestLow) / (highestHigh - lowestLow)) * 100;
        var d = k * 0.9; // Simplified D calculation

        return new Dictionary<string, object>
        {
            ["k"] = Math.Round(k, 2),
            ["d"] = Math.Round(d, 2),
            ["signal"] = k < 20 ? "OVERSOLD" : k > 80 ? "OVERBOUGHT" : "NEUTRAL"
        };
    }

    private static Dictionary<string, object> CalculateOBV(double[] closes, double[] volumes)
    {
        double obv = 0;
        for (int i = 1; i < closes.Length; i++)
        {
            if (closes[i] > closes[i - 1])
                obv += volumes[i];
            else if (closes[i] < closes[i - 1])
                obv -= volumes[i];
        }

        return new Dictionary<string, object>
        {
            ["value"] = Math.Round(obv, 0),
            ["trend"] = obv > 0 ? "ACCUMULATION" : "DISTRIBUTION"
        };
    }

    private static Dictionary<string, object> CalculateVWAP(List<MarketData> priceData)
    {
        double totalPV = 0;
        double totalVolume = 0;

        foreach (var candle in priceData.TakeLast(20))
        {
            var typicalPrice = (double)(candle.High + candle.Low + candle.Close) / 3;
            totalPV += typicalPrice * (double)candle.Volume;
            totalVolume += (double)candle.Volume;
        }

        var vwap = totalVolume > 0 ? totalPV / totalVolume : 0;
        var currentPrice = (double)priceData.Last().Close;

        return new Dictionary<string, object>
        {
            ["value"] = Math.Round(vwap, 2),
            ["deviation_percent"] = Math.Round(((currentPrice - vwap) / vwap) * 100, 2),
            ["signal"] = currentPrice > vwap ? "ABOVE_VWAP" : "BELOW_VWAP"
        };
    }

    private static Dictionary<string, object> CalculateSupportResistance(List<MarketData> priceData)
    {
        var closes = priceData.Select(p => (double)p.Close).ToArray();
        var highs = priceData.Select(p => (double)p.High).ToArray();
        var lows = priceData.Select(p => (double)p.Low).ToArray();

        // Simplified support/resistance using recent highs/lows
        var recentHighs = highs.TakeLast(20).ToList();
        var recentLows = lows.TakeLast(20).ToList();

        var resistanceLevels = new List<double>
        {
            recentHighs.Max(),
            recentHighs.OrderByDescending(h => h).Skip(1).First()
        };

        var supportLevels = new List<double>
        {
            recentLows.Min(),
            recentLows.OrderBy(l => l).Skip(1).First()
        };

        return new Dictionary<string, object>
        {
            ["resistance_levels"] = resistanceLevels.Select(r => Math.Round(r, 2)).ToList(),
            ["support_levels"] = supportLevels.Select(s => Math.Round(s, 2)).ToList(),
            ["pivot_point"] = Math.Round((highs.Last() + lows.Last() + closes.Last()) / 3, 2)
        };
    }

    private static Dictionary<string, object> GenerateSignals(Dictionary<string, object> indicators)
    {
        var signals = new List<string>();
        var bullishCount = 0;
        var bearishCount = 0;

        // Analyze each indicator for signals
        if (indicators.TryGetValue("rsi", out var rsiObj))
        {
            var rsi = (Dictionary<string, object>)rsiObj;
            var signal = rsi["signal"].ToString();
            if (signal == "OVERSOLD") { signals.Add("RSI Oversold"); bullishCount++; }
            if (signal == "OVERBOUGHT") { signals.Add("RSI Overbought"); bearishCount++; }
        }

        if (indicators.TryGetValue("macd", out var macdObj))
        {
            var macd = (Dictionary<string, object>)macdObj;
            var signal = macd["signal"].ToString();
            if (signal == "BUY") { signals.Add("MACD Bullish"); bullishCount++; }
            if (signal == "SELL") { signals.Add("MACD Bearish"); bearishCount++; }
        }

        if (indicators.TryGetValue("bollinger_bands", out var bbObj))
        {
            var bb = (Dictionary<string, object>)bbObj;
            var signal = bb["signal"].ToString();
            if (signal == "OVERSOLD") { signals.Add("BB Oversold"); bullishCount++; }
            if (signal == "OVERBOUGHT") { signals.Add("BB Overbought"); bearishCount++; }
        }

        var overallSignal = bullishCount > bearishCount ? "BULLISH" :
                           bearishCount > bullishCount ? "BEARISH" : "NEUTRAL";

        return new Dictionary<string, object>
        {
            ["overall_signal"] = overallSignal,
            ["bullish_signals"] = bullishCount,
            ["bearish_signals"] = bearishCount,
            ["signal_strength"] = Math.Abs(bullishCount - bearishCount),
            ["active_signals"] = signals
        };
    }
}
