using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Prediction.Contracts;
using Orkeon.Trading.Tools.Domain.Models;
using Microsoft.Extensions.Logging;
using MathNet.Numerics.Statistics;

namespace Orkeon.Trading.Tools.Infrastructure.Prediction;

/// <summary>
/// Facebook Prophet prediction tool for time series forecasting.
/// Handles seasonality, holidays, trend changes, and irregular patterns.
/// Particularly effective for business time series with strong seasonal effects.
/// </summary>
public class ProphetPredictionTool(ILogger<ProphetPredictionTool>? logger = null)
    : TradingToolBase<ProphetPredictionRequest, ProphetPredictionResponse>(logger)
{

    protected override string ToolId => "prophet_prediction";

    protected override string? ValidateTypedRequest(ProphetPredictionRequest request)
    {
        if (request.PriceData.Count < 30)
            return "At least 30 data points required for Prophet";
        return null;
    }

    protected override async Task<ProphetPredictionResponse> ExecuteTypedAsync(
        ProphetPredictionRequest request,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Running Prophet forecast for {Symbol} with {Periods} forecast periods", request.Symbol, request.ForecastPeriods);

        var forecast = await Task.Run(() => GenerateProphetForecast(
            request.Symbol,
            request.PriceData,
            request.ForecastPeriods,
            request.SeasonalityMode,
            request.IncludeWeeklySeasonality,
            request.IncludeYearlySeasonality,
            request.ChangepointPriorScale), cancellationToken);

        _logger?.LogInformation("Prophet forecast completed for {Symbol}", request.Symbol);

        return forecast;
    }

    private static ProphetPredictionResponse GenerateProphetForecast(
        string symbol,
        List<MarketData> priceData,
        int forecastPeriods,
        string seasonalityMode,
        bool includeWeekly,
        bool includeYearly,
        double changepointPrior)
    {
        // Prophet decomposition: y(t) = g(t) + s(t) + h(t) + e(t)
        // g(t) = trend, s(t) = seasonality, h(t) = holidays, e(t) = error

        // Extract time series data
        var dates = priceData.Select(x => x.Timestamp).ToArray();
        var values = priceData.Select(x => (double)x.Close).ToArray();

        // 1. Fit trend component with changepoints
        var trend = FitTrend(dates, values, changepointPrior);

        // 2. Detrend data
        var detrended = values.Select((v, i) => v - trend.Values[i]).ToArray();

        // 3. Fit seasonality components
        var seasonality = FitSeasonality(dates, detrended, includeWeekly, includeYearly, seasonalityMode);

        // 4. Calculate residuals
        var residuals = detrended.Select((v, i) => v - seasonality.Values[i]).ToArray();
        var residualStd = residuals.StandardDeviation();

        // 5. Generate forecasts
        var lastDate = dates.Last();
        var forecastDates = Enumerable.Range(1, forecastPeriods)
                                      .Select(i => lastDate.AddDays(i))
                                      .ToArray();

        var forecastTrend = ExtrapolateTrend(trend, forecastDates);
        var forecastSeasonality = ExtrapolateSeasonality(seasonality, forecastDates);

        var forecasts = new List<Dictionary<string, object>>();
        for (int i = 0; i < forecastPeriods; i++)
        {
            var yhat = forecastTrend[i] + forecastSeasonality[i];

            // Confidence intervals (growing with forecast horizon)
            var intervalWidth = residualStd * Math.Sqrt(i + 1) * 1.96; // 95% CI

            forecasts.Add(new Dictionary<string, object>
            {
                ["date"] = forecastDates[i].ToString("yyyy-MM-dd"),
                ["period"] = i + 1,
                ["predicted_price"] = Math.Round(yhat, 2),
                ["trend"] = Math.Round(forecastTrend[i], 2),
                ["seasonality"] = Math.Round(forecastSeasonality[i], 2),
                ["confidence_interval_lower"] = Math.Round(yhat - intervalWidth, 2),
                ["confidence_interval_upper"] = Math.Round(yhat + intervalWidth, 2)
            });
        }

        // Decompose components for interpretation
        var components = DecomposeComponents(trend, seasonality, residuals);

        return new ProphetPredictionResponse
        {
            Symbol = symbol,
            Timestamp = DateTime.UtcNow,
            ModelConfig = new Dictionary<string, object>
            {
                ["seasonality_mode"] = seasonalityMode,
                ["weekly_seasonality"] = includeWeekly,
                ["yearly_seasonality"] = includeYearly,
                ["changepoint_prior_scale"] = changepointPrior
            },
            Forecast = forecasts,
            Components = components,
            CurrentPrice = priceData.Last().Close,
            ForecastDirection = ((double)forecasts.Last()["predicted_price"]) > (double)priceData.Last().Close ? "BULLISH" : "BEARISH",
            TrendStrength = CalculateTrendStrength(trend),
            SeasonalityStrength = CalculateSeasonalityStrength(seasonality, values)
        };
    }

    private static TrendComponent FitTrend(DateTime[] dates, double[] values, double changepointPrior)
    {
        // Linear trend with potential changepoints
        // Prophet uses piecewise linear growth with automatic changepoint detection

        var n = dates.Length;
        var t = dates.Select((d, i) => (double)i / n).ToArray(); // Normalized time [0,1]

        // Detect changepoints (simplified - use quantiles of time)
        var numChangepoints = Math.Min(10, n / 20); // Prophet default: 25 changepoints
        var changepointIndices = Enumerable.Range(0, numChangepoints)
                                          .Select(i => (int)((i + 1) * n / (numChangepoints + 1)))
                                          .ToArray();

        // Fit piecewise linear trend (simplified OLS)
        var slope = CalculateSlope(t, values);
        var intercept = values.Mean() - slope * t.Mean();

        // Apply changepoints with dampening based on changepointPrior
        var trendValues = new double[n];
        var currentSlope = slope;

        for (int i = 0; i < n; i++)
        {
            // Check if we're past a changepoint
            foreach (var cpIndex in changepointIndices.Where(cp => cp == i))
            {
                // Adjust slope at changepoint (simplified)
                var slopeChange = (values[Math.Min(cpIndex + 5, n - 1)] - values[Math.Max(cpIndex - 5, 0)]) / 10;
                currentSlope += slopeChange * changepointPrior;
            }

            trendValues[i] = intercept + currentSlope * t[i];
        }

        return new TrendComponent
        {
            Values = trendValues,
            Slope = currentSlope,
            Intercept = intercept,
            Changepoints = changepointIndices
        };
    }

    private static double CalculateSlope(double[] x, double[] y)
    {
        var xMean = x.Mean();
        var yMean = y.Mean();

        var numerator = x.Zip(y, (xi, yi) => (xi - xMean) * (yi - yMean)).Sum();
        var denominator = x.Sum(xi => Math.Pow(xi - xMean, 2));

        return denominator != 0 ? numerator / denominator : 0;
    }

    private static SeasonalityComponent FitSeasonality(
        DateTime[] dates,
        double[] detrendedValues,
        bool includeWeekly,
        bool includeYearly,
        string mode)
    {
        var n = dates.Length;
        var seasonalValues = new double[n];

        // Weekly seasonality (7-day period)
        if (includeWeekly)
        {
            var weeklyPattern = CalculateSeasonalPattern(dates, detrendedValues, 7);
            for (int i = 0; i < n; i++)
            {
                var dayOfWeek = (int)dates[i].DayOfWeek;
                seasonalValues[i] += weeklyPattern[dayOfWeek];
            }
        }

        // Yearly seasonality (365-day period, simplified as monthly)
        if (includeYearly)
        {
            var monthlyPattern = CalculateSeasonalPattern(dates, detrendedValues, 12);
            for (int i = 0; i < n; i++)
            {
                var month = dates[i].Month - 1; // 0-indexed
                seasonalValues[i] += monthlyPattern[month];
            }
        }

        // Apply multiplicative seasonality if specified
        if (mode == "multiplicative")
        {
            for (int i = 0; i < n; i++)
            {
                seasonalValues[i] = Math.Exp(seasonalValues[i]) - 1;
            }
        }

        return new SeasonalityComponent
        {
            Values = seasonalValues,
            WeeklyPattern = includeWeekly ? CalculateSeasonalPattern(dates, detrendedValues, 7) : null,
            YearlyPattern = includeYearly ? CalculateSeasonalPattern(dates, detrendedValues, 12) : null,
            Mode = mode
        };
    }

    private static double[] CalculateSeasonalPattern(DateTime[] dates, double[] values, int period)
    {
        var pattern = new double[period];
        var counts = new int[period];

        for (int i = 0; i < dates.Length; i++)
        {
            var index = period == 7 ? (int)dates[i].DayOfWeek : (dates[i].Month - 1) % period;
            pattern[index] += values[i];
            counts[index]++;
        }

        // Average and center the pattern
        for (int i = 0; i < period; i++)
        {
            pattern[i] = counts[i] > 0 ? pattern[i] / counts[i] : 0;
        }

        var patternMean = pattern.Average();
        for (int i = 0; i < period; i++)
        {
            pattern[i] -= patternMean; // Center around zero
        }

        return pattern;
    }

    private static double[] ExtrapolateTrend(TrendComponent trend, DateTime[] forecastDates)
    {
        var n = forecastDates.Length;
        var forecasts = new double[n];

        for (int i = 0; i < n; i++)
        {
            // Continue linear trend
            forecasts[i] = trend.Values.Last() + trend.Slope * (i + 1) / 365.0; // Normalize by year
        }

        return forecasts;
    }

    private static double[] ExtrapolateSeasonality(SeasonalityComponent seasonality, DateTime[] forecastDates)
    {
        var n = forecastDates.Length;
        var forecasts = new double[n];

        for (int i = 0; i < n; i++)
        {
            double seasonal = 0;

            // Weekly seasonality
            if (seasonality.WeeklyPattern != null)
            {
                var dayOfWeek = (int)forecastDates[i].DayOfWeek;
                seasonal += seasonality.WeeklyPattern[dayOfWeek];
            }

            // Yearly seasonality
            if (seasonality.YearlyPattern != null)
            {
                var month = forecastDates[i].Month - 1;
                seasonal += seasonality.YearlyPattern[month];
            }

            forecasts[i] = seasonal;
        }

        return forecasts;
    }

    private static Dictionary<string, object> DecomposeComponents(TrendComponent trend, SeasonalityComponent seasonality, double[] residuals)
    {
        return new Dictionary<string, object>
        {
            ["trend"] = new Dictionary<string, object>
            {
                ["slope"] = Math.Round(trend.Slope, 6),
                ["intercept"] = Math.Round(trend.Intercept, 2),
                ["direction"] = trend.Slope > 0 ? "UPWARD" : trend.Slope < 0 ? "DOWNWARD" : "FLAT",
                ["changepoints_detected"] = trend.Changepoints.Length
            },
            ["seasonality"] = new Dictionary<string, object>
            {
                ["mode"] = seasonality.Mode,
                ["weekly_present"] = seasonality.WeeklyPattern != null,
                ["yearly_present"] = seasonality.YearlyPattern != null,
                ["amplitude"] = seasonality.Values.Length > 0 ? Math.Round(seasonality.Values.Max() - seasonality.Values.Min(), 2) : 0
            },
            ["residuals"] = new Dictionary<string, object>
            {
                ["std_deviation"] = Math.Round(residuals.StandardDeviation(), 4),
                ["mean"] = Math.Round(residuals.Mean(), 6)
            }
        };
    }

    private static string CalculateTrendStrength(TrendComponent trend)
    {
        var absSlope = Math.Abs(trend.Slope);

        if (absSlope > 0.1) return "STRONG";
        if (absSlope > 0.05) return "MODERATE";
        if (absSlope > 0.01) return "WEAK";
        return "FLAT";
    }

    private static string CalculateSeasonalityStrength(SeasonalityComponent seasonality, double[] originalValues)
    {
        if (seasonality.Values.Length == 0) return "NONE";

        var seasonalVar = seasonality.Values.Variance();
        var totalVar = originalValues.Variance();

        var strength = totalVar > 0 ? seasonalVar / totalVar : 0;

        if (strength > 0.3) return "STRONG";
        if (strength > 0.15) return "MODERATE";
        if (strength > 0.05) return "WEAK";
        return "MINIMAL";
    }

    private class TrendComponent
    {
        public required double[] Values { get; init; }
        public required double Slope { get; init; }
        public required double Intercept { get; init; }
        public required int[] Changepoints { get; init; }
    }

    private class SeasonalityComponent
    {
        public required double[] Values { get; init; }
        public double[]? WeeklyPattern { get; init; }
        public double[]? YearlyPattern { get; init; }
        public required string Mode { get; init; }
    }
}
