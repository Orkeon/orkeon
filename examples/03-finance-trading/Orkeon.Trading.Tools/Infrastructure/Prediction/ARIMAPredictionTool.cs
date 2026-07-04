using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Prediction.Contracts;
using Orkeon.Trading.Tools.Domain.Models;
using Microsoft.Extensions.Logging;
using MathNet.Numerics.Statistics;

namespace Orkeon.Trading.Tools.Infrastructure.Prediction;

/// <summary>
/// ARIMA (AutoRegressive Integrated Moving Average) prediction tool for time series forecasting.
/// Automatically determines optimal ARIMA(p,d,q) parameters and generates forecasts with confidence intervals.
/// Suitable for stationary or trend-stationary time series data.
/// </summary>
public class ARIMAPredictionTool(ILogger<ARIMAPredictionTool>? logger = null)
    : TradingToolBase<ARIMAPredictionRequest, ARIMAPredictionResponse>(logger)
{

    protected override string ToolId => "arima_prediction";

    protected override string? ValidateTypedRequest(ARIMAPredictionRequest request)
    {
        if (request.PriceData.Count < 30)
            return "At least 30 data points required for ARIMA";
        return null;
    }

    protected override async Task<ARIMAPredictionResponse> ExecuteTypedAsync(
        ARIMAPredictionRequest request,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Running ARIMA forecast for {Symbol} with {Periods} forecast periods", request.Symbol, request.ForecastPeriods);

        var forecast = await Task.Run(() => GenerateARIMAForecast(
            request.Symbol,
            request.PriceData,
            request.ForecastPeriods,
            request.ConfidenceLevel,
            request.AutoParams,
            request.P,
            request.D,
            request.Q), cancellationToken);

        _logger?.LogInformation("ARIMA forecast completed for {Symbol}", request.Symbol);

        return forecast;
    }

    private ARIMAPredictionResponse GenerateARIMAForecast(
        string symbol,
        List<MarketData> priceData,
        int forecastPeriods,
        double confidenceLevel,
        bool autoParams,
        int p,
        int d,
        int q)
    {
        var closes = priceData.Select(x => (double)x.Close).ToArray();

        // Determine optimal parameters if auto_params is true
        if (autoParams)
        {
            (p, d, q) = DetermineOptimalParameters(closes);
        }

        // Apply differencing
        var differencedData = ApplyDifferencing(closes, d);

        // Fit ARIMA model (simplified implementation)
        var model = FitARIMAModel(differencedData, p, q);

        // Generate forecasts
        var forecasts = GenerateForecasts(closes, model, forecastPeriods, p, d, q);

        // Calculate confidence intervals
        var residuals = CalculateResiduals(differencedData, model, p, q);
        var residualStd = residuals.StandardDeviation();
        var confidenceIntervals = CalculateConfidenceIntervals(forecasts, residualStd, confidenceLevel);

        // Model diagnostics
        var diagnostics = CalculateModelDiagnostics(residuals, p, d, q, closes.Length);

        return new ARIMAPredictionResponse
        {
            Symbol = symbol,
            Timestamp = DateTime.UtcNow,
            ModelParameters = new Dictionary<string, object>
            {
                ["p"] = p,
                ["d"] = d,
                ["q"] = q,
                ["model_type"] = $"ARIMA({p},{d},{q})"
            },
            Forecast = forecasts.Select((f, i) => new Dictionary<string, object>
            {
                ["period"] = i + 1,
                ["predicted_price"] = Math.Round(f, 2),
                ["confidence_interval_lower"] = Math.Round(confidenceIntervals.Lower[i], 2),
                ["confidence_interval_upper"] = Math.Round(confidenceIntervals.Upper[i], 2)
            }).ToList(),
            ModelDiagnostics = diagnostics,
            CurrentPrice = priceData.Last().Close,
            ForecastDirection = forecasts.Last() > (double)priceData.Last().Close ? "BULLISH" : "BEARISH",
            ForecastChangePct = Math.Round((decimal)((forecasts.Last() - (double)priceData.Last().Close) / (double)priceData.Last().Close * 100), 2)
        };
    }

    private (int p, int d, int q) DetermineOptimalParameters(double[] data)
    {
        // Simplified parameter selection using AIC
        // In production, use grid search over (p,d,q) space with AIC/BIC minimization

        // Test for stationarity (simplified)
        var isStationary = TestStationarity(data);

        int bestP = 1, bestD = isStationary ? 0 : 1, bestQ = 1;
        double bestAIC = double.MaxValue;

        // Grid search over small parameter space
        for (int p = 0; p <= 3; p++)
        {
            for (int d = 0; d <= 2; d++)
            {
                for (int q = 0; q <= 3; q++)
                {
                    if (p == 0 && q == 0) continue; // At least one of p or q must be non-zero

                    var aic = CalculateAIC(data, p, d, q);
                    if (aic < bestAIC)
                    {
                        bestAIC = aic;
                        bestP = p;
                        bestD = d;
                        bestQ = q;
                    }
                }
            }
        }

        _logger?.LogInformation("Optimal ARIMA parameters: ({P},{D},{Q}) with AIC={AIC}", bestP, bestD, bestQ, Math.Round(bestAIC, 2));

        return (bestP, bestD, bestQ);
    }

    private static bool TestStationarity(double[] data)
    {
        // Simplified stationarity test
        // In production, use Augmented Dickey-Fuller test

        var firstHalf = data.Take(data.Length / 2).ToArray();
        var secondHalf = data.Skip(data.Length / 2).ToArray();

        var mean1 = firstHalf.Mean();
        var mean2 = secondHalf.Mean();
        var var1 = firstHalf.Variance();
        var var2 = secondHalf.Variance();

        // If means and variances are similar, series is more likely stationary
        var meanDiff = Math.Abs(mean1 - mean2) / mean1;
        var varDiff = Math.Abs(var1 - var2) / var1;

        return meanDiff < 0.1 && varDiff < 0.5;
    }

    private static double CalculateAIC(double[] data, int p, int d, int q)
    {
        // AIC = 2k - 2ln(L)
        // where k = number of parameters, L = likelihood

        var differencedData = ApplyDifferencing(data, d);
        var model = FitARIMAModel(differencedData, p, q);
        var residuals = CalculateResiduals(differencedData, model, p, q);

        var n = differencedData.Length;
        var k = p + q + 1; // +1 for intercept
        var sigma2 = residuals.Select(r => r * r).Sum() / n;

        // Log-likelihood for normal distribution
        var logLikelihood = -n / 2.0 * Math.Log(2 * Math.PI) - n / 2.0 * Math.Log(sigma2) - 1 / (2 * sigma2) * residuals.Select(r => r * r).Sum();

        var aic = 2 * k - 2 * logLikelihood;
        return aic;
    }

    private static double[] ApplyDifferencing(double[] data, int d)
    {
        var result = data.ToArray();

        for (int i = 0; i < d; i++)
        {
            var differenced = new double[result.Length - 1];
            for (int j = 0; j < differenced.Length; j++)
            {
                differenced[j] = result[j + 1] - result[j];
            }
            result = differenced;
        }

        return result;
    }

    private static ARIMAModel FitARIMAModel(double[] data, int p, int q)
    {
        // Simplified ARIMA fitting using least squares
        // In production, use maximum likelihood estimation or Kalman filter

        var model = new ARIMAModel { P = p, Q = q };

        // Fit AR coefficients (simplified)
        if (p > 0)
        {
            model.ARCoefficients = new double[p];
            for (int i = 0; i < p && i < data.Length - 1; i++)
            {
                // Simple autocorrelation-based estimation
                var lag = i + 1;
                var autocorr = CalculateAutocorrelation(data, lag);
                model.ARCoefficients[i] = autocorr * 0.8; // Dampened
            }
        }

        // Fit MA coefficients (simplified)
        if (q > 0)
        {
            model.MACoefficients = new double[q];
            var residuals = CalculateResiduals(data, model, p, 0);

            for (int i = 0; i < q && i < residuals.Count - 1; i++)
            {
                var lag = i + 1;
                var autocorr = CalculateAutocorrelation(residuals.ToArray(), lag);
                model.MACoefficients[i] = autocorr * 0.5; // Dampened
            }
        }

        model.Intercept = data.Mean();

        return model;
    }

    private static double CalculateAutocorrelation(double[] data, int lag)
    {
        if (lag >= data.Length) return 0;

        var mean = data.Mean();
        var variance = data.Variance();

        var autocov = 0.0;
        for (int i = 0; i < data.Length - lag; i++)
        {
            autocov += (data[i] - mean) * (data[i + lag] - mean);
        }
        autocov /= (data.Length - lag);

        return variance > 0 ? autocov / variance : 0;
    }

    private static List<double> GenerateForecasts(double[] originalData, ARIMAModel model, int periods, int p, int d, int q)
    {
        var forecasts = new List<double>();
        var lastValues = originalData.TakeLast(Math.Max(p, d) + 5).ToList();

        for (int t = 0; t < periods; t++)
        {
            double forecast = model.Intercept;

            // AR component
            if (p > 0 && model.ARCoefficients != null)
            {
                for (int i = 0; i < p && i < lastValues.Count; i++)
                {
                    forecast += model.ARCoefficients[i] * (lastValues[lastValues.Count - 1 - i] - model.Intercept);
                }
            }

            // MA component (simplified - assumes zero forecast errors)
            // In production, use Kalman filter for proper MA forecast

            forecasts.Add(forecast);
            lastValues.Add(forecast);
        }

        return forecasts;
    }

    private static List<double> CalculateResiduals(double[] data, ARIMAModel model, int p, int q)
    {
        var residuals = new List<double>();

        for (int t = Math.Max(p, q); t < data.Length; t++)
        {
            double fitted = model.Intercept;

            // AR component
            if (p > 0 && model.ARCoefficients != null)
            {
                for (int i = 0; i < p; i++)
                {
                    if (t - i - 1 >= 0)
                        fitted += model.ARCoefficients[i] * (data[t - i - 1] - model.Intercept);
                }
            }

            var residual = data[t] - fitted;
            residuals.Add(residual);
        }

        return residuals;
    }

    private static (List<double> Lower, List<double> Upper) CalculateConfidenceIntervals(
        List<double> forecasts,
        double residualStd,
        double confidenceLevel)
    {
        // Z-score for confidence level
        var zScore = confidenceLevel switch
        {
            >= 0.99 => 2.576,
            >= 0.95 => 1.96,
            >= 0.90 => 1.645,
            _ => 1.96
        };

        var lower = new List<double>();
        var upper = new List<double>();

        for (int i = 0; i < forecasts.Count; i++)
        {
            // Forecast error grows with forecast horizon
            var forecastStd = residualStd * Math.Sqrt(i + 1);
            var margin = zScore * forecastStd;

            lower.Add(forecasts[i] - margin);
            upper.Add(forecasts[i] + margin);
        }

        return (lower, upper);
    }

    private static Dictionary<string, object> CalculateModelDiagnostics(List<double> residuals, int p, int d, int q, int dataLength)
    {
        var residualMean = residuals.Mean();
        var residualStd = residuals.StandardDeviation();

        // Ljung-Box Q-statistic for residual autocorrelation
        var ljungBox = CalculateLjungBoxStatistic(residuals.ToArray(), 10);

        // RMSE
        var rmse = Math.Sqrt(residuals.Select(r => r * r).Average());

        // MAPE (simplified)
        var mape = Math.Abs(residualMean / residuals.Average()) * 100;

        return new Dictionary<string, object>
        {
            ["residual_mean"] = Math.Round(residualMean, 6),
            ["residual_std"] = Math.Round(residualStd, 4),
            ["rmse"] = Math.Round(rmse, 4),
            ["mape_pct"] = Math.Round(mape, 2),
            ["ljung_box_statistic"] = Math.Round(ljungBox, 2),
            ["ljung_box_interpretation"] = ljungBox < 20 ? "Residuals appear random (good)" : "Potential autocorrelation in residuals",
            ["aic"] = Math.Round(CalculateAIC(residuals.ToArray(), p, d, q), 2),
            ["parameters_used"] = $"ARIMA({p},{d},{q})",
            ["data_points"] = dataLength
        };
    }

    private static double CalculateLjungBoxStatistic(double[] residuals, int lag)
    {
        var n = residuals.Length;
        var qStat = 0.0;

        for (int k = 1; k <= lag; k++)
        {
            var rk = CalculateAutocorrelation(residuals, k);
            qStat += rk * rk / (n - k);
        }

        qStat *= n * (n + 2);
        return qStat;
    }

    private class ARIMAModel
    {
        public int P { get; set; }
        public int Q { get; set; }
        public double[]? ARCoefficients { get; set; }
        public double[]? MACoefficients { get; set; }
        public double Intercept { get; set; }
    }
}
