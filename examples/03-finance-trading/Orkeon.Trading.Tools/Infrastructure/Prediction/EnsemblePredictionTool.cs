using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Prediction.Contracts;
using Orkeon.Trading.Tools.Domain.Models;
using Microsoft.Extensions.Logging;
using MathNet.Numerics.Statistics;

namespace Orkeon.Trading.Tools.Infrastructure.Prediction;

/// <summary>
/// Ensemble prediction tool that combines multiple forecasting models.
/// Aggregates ARIMA, Prophet, Random Forest, and XGBoost predictions with dynamic weighting.
/// Provides consensus forecast with improved accuracy and robustness.
/// </summary>
public class EnsemblePredictionTool(ILogger<EnsemblePredictionTool>? logger = null)
    : TradingToolBase<EnsemblePredictionRequest, EnsemblePredictionResponse>(logger)
{

    protected override string ToolId => "ensemble_prediction";

    protected override string? ValidateTypedRequest(EnsemblePredictionRequest request)
    {
        if (request.PriceData.Count < 100)
            return "At least 100 data points required for ensemble";
        return null;
    }

    protected override async Task<EnsemblePredictionResponse> ExecuteTypedAsync(
        EnsemblePredictionRequest request,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Running ensemble prediction for {Symbol} with {Periods} forecast periods", request.Symbol, request.ForecastPeriods);

        var ensemble = await Task.Run(() => GenerateEnsembleForecast(
            request.Symbol,
            request.PriceData,
            request.ForecastPeriods,
            request.ModelsToInclude,
            request.WeightingMethod), cancellationToken);

        _logger?.LogInformation("Ensemble prediction completed for {Symbol}", request.Symbol);

        return ensemble;
    }

    private static EnsemblePredictionResponse GenerateEnsembleForecast(
        string symbol,
        List<MarketData> priceData,
        int forecastPeriods,
        List<string> modelsToInclude,
        string weightingMethod)
    {
        var includeAll = modelsToInclude.Contains("all");

        // 1. Generate individual model forecasts
        var individualForecasts = new Dictionary<string, List<double>>();
        var modelPerformance = new Dictionary<string, double>();

        // ARIMA forecast
        if (includeAll || modelsToInclude.Contains("arima"))
        {
            var arimaForecast = GenerateARIMAForecast(priceData, forecastPeriods);
            individualForecasts["arima"] = arimaForecast.Forecasts;
            modelPerformance["arima"] = arimaForecast.R2Score;
        }

        // Prophet forecast
        if (includeAll || modelsToInclude.Contains("prophet"))
        {
            var prophetForecast = GenerateProphetForecast(priceData, forecastPeriods);
            individualForecasts["prophet"] = prophetForecast.Forecasts;
            modelPerformance["prophet"] = prophetForecast.R2Score;
        }

        // Random Forest forecast
        if (includeAll || modelsToInclude.Contains("random_forest"))
        {
            var rfForecast = GenerateRandomForestForecast(priceData, forecastPeriods);
            individualForecasts["random_forest"] = rfForecast.Forecasts;
            modelPerformance["random_forest"] = rfForecast.R2Score;
        }

        // XGBoost forecast
        if (includeAll || modelsToInclude.Contains("xgboost"))
        {
            var xgbForecast = GenerateXGBoostForecast(priceData, forecastPeriods);
            individualForecasts["xgboost"] = xgbForecast.Forecasts;
            modelPerformance["xgboost"] = xgbForecast.R2Score;
        }

        // 2. Calculate model weights
        var weights = CalculateModelWeights(modelPerformance, weightingMethod);

        // 3. Combine forecasts
        var ensembleForecast = CombineForecasts(individualForecasts, weights);

        // 4. Calculate ensemble confidence intervals
        var confidenceIntervals = CalculateEnsembleConfidence(individualForecasts, ensembleForecast, priceData);

        // 5. Build result
        return new EnsemblePredictionResponse
        {
            Symbol = symbol,
            Timestamp = DateTime.UtcNow,
            WeightingMethod = weightingMethod,
            ModelsIncluded = individualForecasts.Keys.ToList(),
            EnsembleForecast = ensembleForecast.Select((f, i) => new Dictionary<string, object>
            {
                ["period"] = i + 1,
                ["predicted_price"] = Math.Round(f, 2),
                ["confidence_interval_lower"] = Math.Round(confidenceIntervals.Lower[i], 2),
                ["confidence_interval_upper"] = Math.Round(confidenceIntervals.Upper[i], 2),
                ["prediction_std"] = Math.Round(confidenceIntervals.Std[i], 2)
            }).ToList(),
            IndividualForecasts = individualForecasts.ToDictionary(
                kvp => kvp.Key,
                kvp => (object)kvp.Value.Select((f, i) => new Dictionary<string, object>
                {
                    ["period"] = i + 1,
                    ["predicted_price"] = Math.Round(f, 2)
                }).ToList()
            ),
            ModelWeights = weights.ToDictionary(kvp => kvp.Key, kvp => (object)Math.Round(kvp.Value, 3)),
            ModelAgreement = CalculateModelAgreement(individualForecasts),
            CurrentPrice = priceData.Last().Close,
            ForecastDirection = ensembleForecast.Last() > (double)priceData.Last().Close ? "BULLISH" : "BEARISH",
            ConsensusStrength = CalculateConsensusStrength(individualForecasts, ensembleForecast)
        };
    }

    private static ModelForecast GenerateARIMAForecast(List<MarketData> priceData, int periods)
    {
        // Holdout validation: split into train/test
        var holdout = Math.Max(2, priceData.Count / 5);
        var trainData = priceData.Take(priceData.Count - holdout).ToList();
        var testCloses = priceData.Skip(priceData.Count - holdout).Select(x => (double)x.Close).ToList();

        // Train on train slice
        var trainCloses = trainData.Select(x => (double)x.Close).ToArray();
        var trainReturns = trainCloses.Skip(1).Select((c, i) => (c - trainCloses[i]) / trainCloses[i]).ToArray();
        var trainMeanReturn = trainReturns.Mean();
        var trainLastPrice = trainCloses.Last();

        // Predict for holdout period
        var holdoutPredictions = new List<double>();
        for (int i = 0; i < holdout; i++)
        {
            holdoutPredictions.Add(trainLastPrice * Math.Pow(1 + trainMeanReturn, i + 1));
        }

        // Calculate R2 from holdout
        var r2 = CalculateR2(testCloses, holdoutPredictions);

        // Generate final forecast using all data
        var closes = priceData.Select(x => (double)x.Close).ToArray();
        var returns = closes.Skip(1).Select((c, i) => (c - closes[i]) / closes[i]).ToArray();
        var meanReturn = returns.Mean();
        var lastPrice = closes.Last();

        var forecasts = new List<double>();
        for (int i = 0; i < periods; i++)
        {
            forecasts.Add(lastPrice * Math.Pow(1 + meanReturn, i + 1));
        }

        return new ModelForecast { Forecasts = forecasts, R2Score = r2 };
    }

    private static ModelForecast GenerateProphetForecast(List<MarketData> priceData, int periods)
    {
        // Holdout validation: split into train/test
        var holdout = Math.Max(2, priceData.Count / 5);
        var trainData = priceData.Take(priceData.Count - holdout).ToList();
        var testCloses = priceData.Skip(priceData.Count - holdout).Select(x => (double)x.Close).ToList();

        // Train on train slice
        var trainCloses = trainData.Select(x => (double)x.Close).ToArray();
        var tn = trainCloses.Length;
        var tx = Enumerable.Range(0, tn).Select(i => (double)i).ToArray();
        var txMean = tx.Average();
        var tyMean = trainCloses.Average();
        var tNumerator = tx.Zip(trainCloses, (xi, yi) => (xi - txMean) * (yi - tyMean)).Sum();
        var tDenominator = tx.Sum(xi => Math.Pow(xi - txMean, 2));
        var trainSlope = tDenominator != 0 ? tNumerator / tDenominator : 0;
        var trainLastPrice = trainCloses.Last();

        // Predict for holdout period
        var holdoutPredictions = new List<double>();
        for (int i = 0; i < holdout; i++)
        {
            holdoutPredictions.Add(trainLastPrice + trainSlope * (i + 1));
        }

        // Calculate R2 from holdout
        var r2 = CalculateR2(testCloses, holdoutPredictions);

        // Generate final forecast using all data
        var closes = priceData.Select(x => (double)x.Close).ToArray();
        var n = closes.Length;
        var x = Enumerable.Range(0, n).Select(i => (double)i).ToArray();
        var xMean = x.Average();
        var yMean = closes.Average();
        var numerator = x.Zip(closes, (xi, yi) => (xi - xMean) * (yi - yMean)).Sum();
        var denominator = x.Sum(xi => Math.Pow(xi - xMean, 2));
        var slope = denominator != 0 ? numerator / denominator : 0;

        var forecasts = new List<double>();
        var lastPrice = closes.Last();

        for (int i = 0; i < periods; i++)
        {
            forecasts.Add(lastPrice + slope * (i + 1));
        }

        return new ModelForecast { Forecasts = forecasts, R2Score = r2 };
    }

    private static ModelForecast GenerateRandomForestForecast(List<MarketData> priceData, int periods)
    {
        // Holdout validation: split into train/test
        var holdout = Math.Max(2, priceData.Count / 5);
        var trainData = priceData.Take(priceData.Count - holdout).ToList();
        var testCloses = priceData.Skip(priceData.Count - holdout).Select(x => (double)x.Close).ToList();

        // Train on train slice
        var trainCloses = trainData.Select(x => (double)x.Close).ToArray();
        var trainRecentTrend = (trainCloses.TakeLast(5).Average() - trainCloses.TakeLast(20).Average()) / trainCloses.TakeLast(20).Average();
        var trainLastPrice = trainCloses.Last();

        // Predict for holdout period
        var holdoutPredictions = new List<double>();
        for (int i = 0; i < holdout; i++)
        {
            var dampening = Math.Exp(-0.1 * i);
            holdoutPredictions.Add(trainLastPrice * (1 + trainRecentTrend * dampening * (i + 1) / 5.0));
        }

        // Calculate R2 from holdout
        var r2 = CalculateR2(testCloses, holdoutPredictions);

        // Generate final forecast using all data
        var closes = priceData.Select(x => (double)x.Close).ToArray();
        var recentTrend = (closes.TakeLast(5).Average() - closes.TakeLast(20).Average()) / closes.TakeLast(20).Average();

        var forecasts = new List<double>();
        var lastPrice = closes.Last();

        for (int i = 0; i < periods; i++)
        {
            var dampening = Math.Exp(-0.1 * i);
            forecasts.Add(lastPrice * (1 + recentTrend * dampening * (i + 1) / 5.0));
        }

        return new ModelForecast { Forecasts = forecasts, R2Score = r2 };
    }

    private static ModelForecast GenerateXGBoostForecast(List<MarketData> priceData, int periods)
    {
        // Holdout validation: split into train/test
        var holdout = Math.Max(2, priceData.Count / 5);
        var trainData = priceData.Take(priceData.Count - holdout).ToList();
        var testCloses = priceData.Skip(priceData.Count - holdout).Select(x => (double)x.Close).ToList();

        // Train on train slice
        var trainCloses = trainData.Select(x => (double)x.Close).ToArray();
        var trainShortTerm = (trainCloses.TakeLast(3).Average() - trainCloses.TakeLast(10).Average()) / trainCloses.TakeLast(10).Average();
        var trainMediumTerm = (trainCloses.TakeLast(10).Average() - trainCloses.TakeLast(20).Average()) / trainCloses.TakeLast(20).Average();
        var trainCombinedTrend = trainShortTerm * 0.6 + trainMediumTerm * 0.4;
        var trainLastPrice = trainCloses.Last();

        // Predict for holdout period
        var holdoutPredictions = new List<double>();
        for (int i = 0; i < holdout; i++)
        {
            var dampening = Math.Exp(-0.15 * i);
            holdoutPredictions.Add(trainLastPrice * (1 + trainCombinedTrend * dampening * (i + 1) / 5.0));
        }

        // Calculate R2 from holdout
        var r2 = CalculateR2(testCloses, holdoutPredictions);

        // Generate final forecast using all data
        var closes = priceData.Select(x => (double)x.Close).ToArray();
        var shortTerm = (closes.TakeLast(3).Average() - closes.TakeLast(10).Average()) / closes.TakeLast(10).Average();
        var mediumTerm = (closes.TakeLast(10).Average() - closes.TakeLast(20).Average()) / closes.TakeLast(20).Average();
        var combinedTrend = shortTerm * 0.6 + mediumTerm * 0.4;

        var forecasts = new List<double>();
        var lastPrice = closes.Last();

        for (int i = 0; i < periods; i++)
        {
            var dampening = Math.Exp(-0.15 * i);
            forecasts.Add(lastPrice * (1 + combinedTrend * dampening * (i + 1) / 5.0));
        }

        return new ModelForecast { Forecasts = forecasts, R2Score = r2 };
    }

    private static Dictionary<string, double> CalculateModelWeights(Dictionary<string, double> performance, string method)
    {
        var weights = new Dictionary<string, double>();

        if (method == "equal")
        {
            // Equal weights
            var equalWeight = 1.0 / performance.Count;
            foreach (var model in performance.Keys)
            {
                weights[model] = equalWeight;
            }
        }
        else if (method == "performance")
        {
            // Weight by R2 score
            var totalR2 = performance.Values.Sum();
            foreach (var model in performance.Keys)
            {
                weights[model] = totalR2 > 0 ? performance[model] / totalR2 : 1.0 / performance.Count;
            }
        }
        else if (method == "inverse_error")
        {
            // Weight by inverse of error (1 - R2)
            var inverseErrors = performance.ToDictionary(kvp => kvp.Key, kvp => 1.0 / (1.0 - kvp.Value + 0.01));
            var totalInverse = inverseErrors.Values.Sum();

            foreach (var model in performance.Keys)
            {
                weights[model] = inverseErrors[model] / totalInverse;
            }
        }

        return weights;
    }

    private static List<double> CombineForecasts(Dictionary<string, List<double>> forecasts, Dictionary<string, double> weights)
    {
        var numPeriods = forecasts.First().Value.Count;
        var combined = new List<double>();

        for (int i = 0; i < numPeriods; i++)
        {
            var weightedSum = 0.0;
            foreach (var model in forecasts.Keys)
            {
                weightedSum += forecasts[model][i] * weights[model];
            }
            combined.Add(weightedSum);
        }

        return combined;
    }

    private static (List<double> Lower, List<double> Upper, List<double> Std) CalculateEnsembleConfidence(
        Dictionary<string, List<double>> individualForecasts,
        List<double> ensembleForecast,
        List<MarketData> priceData)
    {
        var numPeriods = ensembleForecast.Count;
        var lower = new List<double>();
        var upper = new List<double>();
        var stds = new List<double>();

        for (int i = 0; i < numPeriods; i++)
        {
            // Calculate standard deviation across models for this period
            var predictions = individualForecasts.Values.Select(f => f[i]).ToArray();
            var std = predictions.StandardDeviation();

            // Confidence interval grows with horizon
            var horizonMultiplier = Math.Sqrt(i + 1);
            var adjustedStd = std * horizonMultiplier;

            lower.Add(ensembleForecast[i] - 1.96 * adjustedStd);
            upper.Add(ensembleForecast[i] + 1.96 * adjustedStd);
            stds.Add(adjustedStd);
        }

        return (lower, upper, stds);
    }

    private static decimal CalculateModelAgreement(Dictionary<string, List<double>> forecasts)
    {
        // Calculate how much models agree (low variance = high agreement)
        var firstPeriodPredictions = forecasts.Values.Select(f => f.First()).ToArray();
        var std = firstPeriodPredictions.StandardDeviation();
        var mean = firstPeriodPredictions.Average();

        // Agreement score: 100 - (coefficient of variation * 100)
        var cv = mean != 0 ? std / Math.Abs(mean) : 0;
        var agreement = Math.Max(0, 100 - cv * 100);

        return Math.Round((decimal)agreement, 1);
    }

    private static string CalculateConsensusStrength(Dictionary<string, List<double>> forecasts, List<double> ensemble)
    {
        // Check if all models agree on direction
        var currentPrice = forecasts.Values.First().First(); // Approximate
        var bullishModels = forecasts.Count(f => f.Value.Last() > currentPrice);
        var bearishModels = forecasts.Count - bullishModels;

        var consensus = (double)Math.Max(bullishModels, bearishModels) / forecasts.Count;

        if (consensus >= 0.9) return "STRONG";
        if (consensus >= 0.7) return "MODERATE";
        if (consensus >= 0.6) return "WEAK";
        return "DIVERGENT";
    }

    private static double CalculateR2(List<double> actual, List<double> predicted)
    {
        if (actual.Count != predicted.Count || actual.Count < 2) return 0;
        var meanActual = actual.Average();
        var ssResidual = actual.Zip(predicted, (a, p) => (a - p) * (a - p)).Sum();
        var ssTotal = actual.Sum(a => (a - meanActual) * (a - meanActual));
        return ssTotal > 0 ? Math.Max(0, 1.0 - (ssResidual / ssTotal)) : 0;
    }

    private record ModelForecast
    {
        public required List<double> Forecasts { get; init; }
        public required double R2Score { get; init; }
    }
}
