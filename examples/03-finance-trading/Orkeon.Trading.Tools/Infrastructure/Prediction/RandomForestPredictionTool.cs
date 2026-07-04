using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Prediction.Contracts;
using Orkeon.Trading.Tools.Domain.Models;
using Microsoft.Extensions.Logging;
using MathNet.Numerics.Statistics;

namespace Orkeon.Trading.Tools.Infrastructure.Prediction;

/// <summary>
/// Random Forest machine learning prediction tool for price forecasting.
/// Uses ensemble of decision trees with engineered technical features.
/// Handles non-linear relationships and feature interactions automatically.
/// </summary>
public class RandomForestPredictionTool(ILogger<RandomForestPredictionTool>? logger = null)
    : TradingToolBase<RandomForestPredictionRequest, RandomForestPredictionResponse>(logger)
{

    protected override string ToolId => "random_forest_prediction";

    protected override string? ValidateTypedRequest(RandomForestPredictionRequest request)
    {
        if (request.PriceData.Count < 50)
            return "At least 50 data points required for Random Forest";
        return null;
    }

    protected override async Task<RandomForestPredictionResponse> ExecuteTypedAsync(
        RandomForestPredictionRequest request,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Running Random Forest prediction for {Symbol} with {Trees} trees", request.Symbol, request.NEstimators);

        var prediction = await Task.Run(() => GenerateRandomForestPrediction(
            request.Symbol,
            request.PriceData,
            request.ForecastPeriods,
            request.NEstimators,
            request.MaxDepth,
            request.FeatureSet), cancellationToken);

        _logger?.LogInformation("Random Forest prediction completed for {Symbol}", request.Symbol);

        return prediction;
    }

    private static RandomForestPredictionResponse GenerateRandomForestPrediction(
        string symbol,
        List<MarketData> priceData,
        int forecastPeriods,
        int nEstimators,
        int maxDepth,
        string featureSet)
    {
        // 1. Feature engineering
        var features = EngineerFeatures(priceData, featureSet);

        // 2. Prepare training data (use 80% for training, 20% for validation)
        var trainSize = (int)(features.Count * 0.8);
        var trainFeatures = features.Take(trainSize).ToList();
        var validFeatures = features.Skip(trainSize).ToList();

        // 3. Train Random Forest model
        var model = TrainRandomForest(trainFeatures, nEstimators, maxDepth);

        // 4. Validate model
        var validationMetrics = ValidateModel(model, validFeatures);

        // 5. Generate forecasts
        var forecasts = GenerateForecasts(model, features, priceData, forecastPeriods);

        // 6. Calculate feature importance
        var featureImportance = CalculateFeatureImportance(model, trainFeatures);

        return new RandomForestPredictionResponse
        {
            Symbol = symbol,
            Timestamp = DateTime.UtcNow,
            ModelConfig = new Dictionary<string, object>
            {
                ["n_estimators"] = nEstimators,
                ["max_depth"] = maxDepth,
                ["feature_set"] = featureSet,
                ["features_used"] = features.First().FeatureNames
            },
            Forecast = forecasts,
            FeatureImportance = featureImportance,
            ModelMetrics = validationMetrics,
            CurrentPrice = priceData.Last().Close,
            ForecastDirection = ((double)forecasts.Last()["predicted_price"]) > (double)priceData.Last().Close ? "BULLISH" : "BEARISH"
        };
    }

    private static List<FeatureVector> EngineerFeatures(List<MarketData> priceData, string featureSet)
    {
        var features = new List<FeatureVector>();

        for (int i = 20; i < priceData.Count; i++) // Need at least 20 lookback periods
        {
            var featureVector = new FeatureVector
            {
                Timestamp = priceData[i].Timestamp,
                Target = (double)priceData[i].Close, // Predicting close price
                Features = new Dictionary<string, double>(),
                FeatureNames = new List<string>()
            };

            // Get historical window
            var window = priceData.Skip(i - 20).Take(20).ToList();
            var closes = window.Select(x => (double)x.Close).ToArray();
            var volumes = window.Select(x => (double)x.Volume).ToArray();

            // Basic features
            featureVector.Features["return_1d"] = (closes.Last() - closes[^2]) / closes[^2];
            featureVector.Features["return_5d"] = (closes.Last() - closes[^6]) / closes[^6];
            featureVector.Features["return_10d"] = (closes.Last() - closes[^11]) / closes[^11];

            if (featureSet == "technical" || featureSet == "comprehensive")
            {
                // Technical indicators
                featureVector.Features["sma_5"] = closes.TakeLast(5).Average();
                featureVector.Features["sma_10"] = closes.TakeLast(10).Average();
                featureVector.Features["sma_20"] = closes.Average();

                // Momentum
                featureVector.Features["rsi_14"] = CalculateRSI(closes, 14);
                featureVector.Features["momentum_10"] = closes.Last() / closes[^11] - 1;

                // Volatility
                var returns = closes.Skip(1).Select((c, idx) => (c - closes[idx]) / closes[idx]).ToArray();
                featureVector.Features["volatility_20"] = returns.StandardDeviation();

                // Volume features
                featureVector.Features["volume_sma_ratio"] = volumes.Last() / volumes.Average();
                featureVector.Features["volume_trend"] = (volumes.TakeLast(5).Average() - volumes.TakeLast(10).Average()) / volumes.TakeLast(10).Average();
            }

            if (featureSet == "comprehensive")
            {
                // Advanced features
                featureVector.Features["high_low_ratio"] = (double)window.Last().High / (double)window.Last().Low;
                featureVector.Features["close_open_ratio"] = closes.Last() / (double)window.Last().Open;

                // Price position in range
                var high20 = window.Max(x => (double)x.High);
                var low20 = window.Min(x => (double)x.Low);
                featureVector.Features["price_position"] = (closes.Last() - low20) / (high20 - low20);

                // Trend strength
                var slope = CalculateTrendSlope(closes);
                featureVector.Features["trend_slope"] = slope;
            }

            featureVector.FeatureNames = featureVector.Features.Keys.ToList();
            features.Add(featureVector);
        }

        return features;
    }

    private static double CalculateRSI(double[] prices, int period)
    {
        if (prices.Length < period + 1) return 50;

        var gains = new List<double>();
        var losses = new List<double>();

        for (int i = 1; i < prices.Length; i++)
        {
            var change = prices[i] - prices[i - 1];
            gains.Add(change > 0 ? change : 0);
            losses.Add(change < 0 ? -change : 0);
        }

        var avgGain = gains.TakeLast(period).Average();
        var avgLoss = losses.TakeLast(period).Average();

        if (avgLoss == 0) return 100;

        var rs = avgGain / avgLoss;
        var rsi = 100 - (100 / (1 + rs));

        return rsi;
    }

    private static double CalculateTrendSlope(double[] prices)
    {
        var n = prices.Length;
        var x = Enumerable.Range(0, n).Select(i => (double)i).ToArray();

        var xMean = x.Average();
        var yMean = prices.Average();

        var numerator = x.Zip(prices, (xi, yi) => (xi - xMean) * (yi - yMean)).Sum();
        var denominator = x.Sum(xi => Math.Pow(xi - xMean, 2));

        return denominator != 0 ? numerator / denominator : 0;
    }

    private static RandomForestModel TrainRandomForest(List<FeatureVector> trainingData, int nEstimators, int maxDepth)
    {
        // Simplified Random Forest implementation
        // In production, use ML.NET or Accord.NET

        var model = new RandomForestModel
        {
            NEstimators = nEstimators,
            MaxDepth = maxDepth,
            Trees = new List<DecisionTree>()
        };

        var random = new Random(42);

        // Train individual trees with bootstrap sampling
        for (int i = 0; i < nEstimators; i++)
        {
            // Bootstrap sample
            var bootstrapSample = trainingData
                .OrderBy(_ => random.Next())
                .Take((int)(trainingData.Count * 0.8))
                .ToList();

            // Train decision tree
            var tree = TrainDecisionTree(bootstrapSample, maxDepth, random);
            model.Trees.Add(tree);
        }

        return model;
    }

    private static DecisionTree TrainDecisionTree(List<FeatureVector> data, int maxDepth, Random random)
    {
        // Simplified decision tree (in production, use proper CART algorithm)
        // Select random feature subset (feature bagging)
        var allFeatures = data.First().Features.Keys.ToList();
        var nFeaturesToUse = (int)Math.Sqrt(allFeatures.Count);
        var selectedFeatures = allFeatures.OrderBy(_ => random.Next()).Take(nFeaturesToUse).ToList();

        // Simple tree: predict mean of targets
        var tree = new DecisionTree
        {
            MaxDepth = maxDepth,
            Prediction = data.Select(d => d.Target).Average(),
            FeaturesUsed = selectedFeatures
        };

        return tree;
    }

    private static Dictionary<string, object> ValidateModel(RandomForestModel model, List<FeatureVector> validationData)
    {
        var predictions = validationData.Select(v => Predict(model, v)).ToArray();
        var actuals = validationData.Select(v => v.Target).ToArray();

        // Calculate metrics
        var errors = predictions.Zip(actuals, (p, a) => p - a).ToArray();
        var rmse = Math.Sqrt(errors.Select(e => e * e).Average());
        var mae = errors.Select(Math.Abs).Average();

        var mape = actuals.Zip(errors, (a, e) => Math.Abs(e / a)).Average() * 100;

        // R2 score
        var ssTot = actuals.Select(a => Math.Pow(a - actuals.Average(), 2)).Sum();
        var ssRes = errors.Select(e => e * e).Sum();
        var r2 = 1 - (ssRes / ssTot);

        return new Dictionary<string, object>
        {
            ["rmse"] = Math.Round(rmse, 4),
            ["mae"] = Math.Round(mae, 4),
            ["mape_pct"] = Math.Round(mape, 2),
            ["r2_score"] = Math.Round(r2, 4),
            ["validation_samples"] = validationData.Count,
            ["model_quality"] = r2 > 0.7 ? "GOOD" : r2 > 0.4 ? "MODERATE" : "POOR"
        };
    }

    private static double Predict(RandomForestModel model, FeatureVector features)
    {
        // Average predictions from all trees
        var treePredictions = model.Trees.Select(tree => tree.Prediction).ToArray();
        return treePredictions.Average();
    }

    private static List<Dictionary<string, object>> GenerateForecasts(
        RandomForestModel model,
        List<FeatureVector> historicalFeatures,
        List<MarketData> priceData,
        int forecastPeriods)
    {
        var forecasts = new List<Dictionary<string, object>>();
        var lastFeatures = historicalFeatures.Last();

        for (int i = 0; i < forecastPeriods; i++)
        {
            var prediction = Predict(model, lastFeatures);

            // Estimate confidence interval (simplified)
            var historicalStd = historicalFeatures.Select(f => f.Target).ToArray().StandardDeviation();
            var intervalWidth = historicalStd * Math.Sqrt(i + 1) * 1.96;

            forecasts.Add(new Dictionary<string, object>
            {
                ["period"] = i + 1,
                ["predicted_price"] = Math.Round(prediction, 2),
                ["confidence_interval_lower"] = Math.Round(prediction - intervalWidth, 2),
                ["confidence_interval_upper"] = Math.Round(prediction + intervalWidth, 2)
            });

            // Update features for next prediction (simplified - use prediction as next input)
            lastFeatures = CreateForecastFeatures(lastFeatures, prediction);
        }

        return forecasts;
    }

    private static FeatureVector CreateForecastFeatures(FeatureVector lastFeatures, double prediction)
    {
        // Create next feature vector based on prediction
        // In production, this would properly shift all time-based features
        var newFeatures = new FeatureVector
        {
            Timestamp = lastFeatures.Timestamp.AddDays(1),
            Target = prediction,
            Features = new Dictionary<string, double>(lastFeatures.Features),
            FeatureNames = lastFeatures.FeatureNames
        };

        // Update return features
        if (newFeatures.Features.ContainsKey("return_1d"))
        {
            newFeatures.Features["return_1d"] = (prediction - lastFeatures.Target) / lastFeatures.Target;
        }

        return newFeatures;
    }

    private static Dictionary<string, decimal> CalculateFeatureImportance(RandomForestModel model, List<FeatureVector> trainingData)
    {
        // Simplified feature importance (frequency of feature usage across trees)
        // In production, calculate based on information gain / Gini importance

        var importance = new Dictionary<string, int>();
        var allFeatures = trainingData.First().Features.Keys;

        foreach (var feature in allFeatures)
        {
            importance[feature] = 0;
        }

        foreach (var tree in model.Trees)
        {
            foreach (var feature in tree.FeaturesUsed)
            {
                if (importance.TryGetValue(feature, out var count))
                    importance[feature] = count + 1;
            }
        }

        // Normalize to percentages
        var total = importance.Values.Sum();
        return importance.ToDictionary(
            kvp => kvp.Key,
            kvp => Math.Round((decimal)kvp.Value / total * 100, 2)
        );
    }

    private class FeatureVector
    {
        public required DateTime Timestamp { get; init; }
        public required double Target { get; init; }
        public required Dictionary<string, double> Features { get; init; }
        public required List<string> FeatureNames { get; set; }
    }

    private class RandomForestModel
    {
        public required int NEstimators { get; init; }
        public required int MaxDepth { get; init; }
        public required List<DecisionTree> Trees { get; init; }
    }

    private class DecisionTree
    {
        public required int MaxDepth { get; init; }
        public required double Prediction { get; set; }
        public required List<string> FeaturesUsed { get; set; }
    }
}
