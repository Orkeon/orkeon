namespace Orkeon.Trading.Tools.Domain.Models;

/// <summary>
/// Prediction ensemble result combining multiple models
/// </summary>
public record PredictionEnsemble
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public required DateTime PredictionHorizon { get; init; }
    public required int HorizonDays { get; init; }

    // Ensemble Predictions
    public decimal EnsemblePrediction { get; init; }
    public decimal EnsembleConfidence { get; init; }
    public decimal EnsembleStdDev { get; init; }

    // Individual Model Predictions
    public ModelPrediction? ARIMAPrediction { get; init; }
    public ModelPrediction? ProphetPrediction { get; init; }
    public ModelPrediction? RandomForestPrediction { get; init; }
    public ModelPrediction? XGBoostPrediction { get; init; }
    public ModelPrediction? LSTMPrediction { get; init; }

    // Prediction Intervals
    public PredictionInterval? Interval95 { get; init; }
    public PredictionInterval? Interval99 { get; init; }

    // Directional Prediction
    public string Direction { get; init; } = "NEUTRAL"; // "UP", "DOWN", "NEUTRAL"
    public decimal DirectionProbability { get; init; }

    // Model Agreement Metrics
    public decimal ModelAgreement { get; init; }
    public int ModelsInAgreement { get; init; }
    public int TotalModels { get; init; }

    // Feature Importance
    public Dictionary<string, decimal>? FeatureImportance { get; init; }

    public Dictionary<string, object>? Metadata { get; init; }
}

public record ModelPrediction
{
    public required string ModelName { get; init; }
    public required decimal Prediction { get; init; }
    public decimal Confidence { get; init; }
    public decimal? StandardError { get; init; }
    public decimal? RSquared { get; init; }
    public decimal? RMSE { get; init; }
    public decimal? MAE { get; init; }
    public decimal? MAPE { get; init; }
    public string Status { get; init; } = "SUCCESS"; // "SUCCESS", "FAILED", "LOW_CONFIDENCE"
    public Dictionary<string, object>? Metadata { get; init; }
}

public record PredictionInterval
{
    public decimal Lower { get; init; }
    public decimal Upper { get; init; }
    public decimal Width => Upper - Lower;
    public decimal ConfidenceLevel { get; init; }
}

/// <summary>
/// Time series forecast with multiple horizons
/// </summary>
public record TimeSeriesForecast
{
    public required string Symbol { get; init; }
    public required DateTime ForecastDate { get; init; }
    public required string Model { get; init; }
    public List<ForecastPoint> Forecast { get; init; } = new();
    public ModelDiagnostics? Diagnostics { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record ForecastPoint
{
    public required DateTime Date { get; init; }
    public required decimal Value { get; init; }
    public decimal? Lower95 { get; init; }
    public decimal? Upper95 { get; init; }
    public decimal? Lower99 { get; init; }
    public decimal? Upper99 { get; init; }
    public decimal? Confidence { get; init; }
}

public record ModelDiagnostics
{
    public decimal RSquared { get; init; }
    public decimal RMSE { get; init; }
    public decimal MAE { get; init; }
    public decimal MAPE { get; init; }
    public decimal AIC { get; init; }
    public decimal BIC { get; init; }
    public int TrainingSize { get; init; }
    public int TestSize { get; init; }
    public DateTime TrainStartDate { get; init; }
    public DateTime TrainEndDate { get; init; }
    public Dictionary<string, object>? AdditionalMetrics { get; init; }
}

/// <summary>
/// Machine learning model training result
/// </summary>
public record ModelTrainingResult
{
    public required string ModelName { get; init; }
    public required DateTime TrainingDate { get; init; }
    public required string Status { get; init; } // "SUCCESS", "FAILED", "IN_PROGRESS"

    // Performance Metrics
    public decimal? Accuracy { get; init; }
    public decimal? Precision { get; init; }
    public decimal? Recall { get; init; }
    public decimal? F1Score { get; init; }
    public decimal? RSquared { get; init; }
    public decimal? RMSE { get; init; }
    public decimal? MAE { get; init; }

    // Training Details
    public int EpochsCompleted { get; init; }
    public int TotalEpochs { get; init; }
    public TimeSpan TrainingDuration { get; init; }
    public int TrainingSamples { get; init; }
    public int ValidationSamples { get; init; }

    // Hyperparameters
    public Dictionary<string, object>? Hyperparameters { get; init; }

    // Feature Importance
    public Dictionary<string, decimal>? FeatureImportance { get; init; }

    public string? ModelPath { get; init; }
    public string? ErrorMessage { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Backtesting result for predictions
/// </summary>
public record BacktestResult
{
    public required string Symbol { get; init; }
    public required string ModelName { get; init; }
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public required int TotalPredictions { get; init; }

    // Accuracy Metrics
    public decimal DirectionalAccuracy { get; init; }
    public decimal MeanAbsoluteError { get; init; }
    public decimal RootMeanSquaredError { get; init; }
    public decimal MeanAbsolutePercentageError { get; init; }
    public decimal RSquared { get; init; }

    // Prediction Quality
    public decimal CalibrationScore { get; init; }
    public decimal CoverageRatio95 { get; init; }
    public decimal CoverageRatio99 { get; init; }

    // Performance by Horizon
    public Dictionary<int, HorizonMetrics>? PerformanceByHorizon { get; init; }

    // Trading Simulation
    public decimal? SimulatedReturn { get; init; }
    public decimal? SimulatedSharpeRatio { get; init; }
    public int? WinningTrades { get; init; }
    public int? LosingTrades { get; init; }

    public Dictionary<string, object>? Metadata { get; init; }
}

public record HorizonMetrics
{
    public int HorizonDays { get; init; }
    public int PredictionCount { get; init; }
    public decimal DirectionalAccuracy { get; init; }
    public decimal MAE { get; init; }
    public decimal RMSE { get; init; }
    public decimal MAPE { get; init; }
}

/// <summary>
/// Anomaly detection result
/// </summary>
public record AnomalyDetection
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public required bool IsAnomaly { get; init; }
    public decimal AnomalyScore { get; init; }
    public string Severity { get; init; } = "NORMAL"; // "NORMAL", "MINOR", "MAJOR", "CRITICAL"
    public List<DetectedAnomaly>? Anomalies { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record DetectedAnomaly
{
    public required DateTime Timestamp { get; init; }
    public required string Type { get; init; } // "PRICE", "VOLUME", "VOLATILITY", "PATTERN"
    public required decimal Score { get; init; }
    public decimal? ExpectedValue { get; init; }
    public decimal? ActualValue { get; init; }
    public decimal? Deviation { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Market regime prediction
/// </summary>
public record RegimePrediction
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string CurrentRegime { get; init; }
    public required string PredictedRegime { get; init; }
    public decimal PredictionConfidence { get; init; }
    public int PredictionHorizonDays { get; init; }
    public Dictionary<string, decimal>? RegimeProbabilities { get; init; }
    public DateTime? ExpectedTransitionDate { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}
