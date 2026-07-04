namespace Orkeon.Trading.Tools.Domain.Models;

/// <summary>
/// Comprehensive risk metrics for a portfolio or position
/// </summary>
public record RiskMetrics
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public required decimal PositionSize { get; init; }
    public required decimal CurrentPrice { get; init; }
    public required decimal PortfolioValue { get; init; }

    // Value at Risk (VaR)
    public VaRMetrics? VaR { get; init; }

    // Conditional Value at Risk (CVaR/ES)
    public CVaRMetrics? CVaR { get; init; }

    // Volatility Metrics
    public VolatilityMetrics? Volatility { get; init; }

    // Correlation & Beta
    public CorrelationMetrics? Correlation { get; init; }

    // Drawdown Metrics
    public DrawdownMetrics? Drawdown { get; init; }

    // Stress Test Results
    public List<StressTestResult>? StressTests { get; init; }

    // Risk-Adjusted Returns
    public RiskAdjustedReturns? RiskAdjustedMetrics { get; init; }

    // Position Limits
    public PositionLimits? Limits { get; init; }

    // Factor Exposures
    public Dictionary<string, decimal>? FactorExposures { get; init; }

    // Overall Risk Score
    public decimal OverallRiskScore { get; init; }
    public string RiskLevel { get; init; } = "MEDIUM"; // "LOW", "MEDIUM", "HIGH", "CRITICAL"

    public Dictionary<string, object>? Metadata { get; init; }
}

public record VaRMetrics
{
    public decimal VaR95_1Day { get; init; }
    public decimal VaR99_1Day { get; init; }
    public decimal VaR95_10Day { get; init; }
    public decimal VaR99_10Day { get; init; }
    public decimal VaRDollarAmount { get; init; }
    public decimal VaRPercentage { get; init; }
    public string Method { get; init; } = "PARAMETRIC"; // "HISTORICAL", "PARAMETRIC", "MONTE_CARLO"
    public int SampleSize { get; init; }
    public DateTime CalculationDate { get; init; }
}

public record CVaRMetrics
{
    public decimal CVaR95_1Day { get; init; }
    public decimal CVaR99_1Day { get; init; }
    public decimal CVaR95_10Day { get; init; }
    public decimal CVaR99_10Day { get; init; }
    public decimal ExpectedShortfall { get; init; }
    public decimal CVaRDollarAmount { get; init; }
    public decimal CVaRPercentage { get; init; }
    public string Method { get; init; } = "PARAMETRIC";
}

public record VolatilityMetrics
{
    public decimal Historical1Day { get; init; }
    public decimal Historical5Day { get; init; }
    public decimal Historical20Day { get; init; }
    public decimal Historical60Day { get; init; }
    public decimal Historical252Day { get; init; }
    public decimal Implied { get; init; }
    public decimal EWMA { get; init; } // Exponentially Weighted Moving Average
    public decimal GARCH { get; init; }
    public decimal Parkinson { get; init; }
    public decimal GarmanKlass { get; init; }
    public decimal YangZhang { get; init; }
    public string VolatilityRegime { get; init; } = "NORMAL"; // "LOW", "NORMAL", "HIGH", "EXTREME"
}

public record CorrelationMetrics
{
    public required string BenchmarkSymbol { get; init; }
    public decimal Correlation { get; init; }
    public decimal Beta { get; init; }
    public decimal Alpha { get; init; }
    public decimal RSquared { get; init; }
    public decimal TrackingError { get; init; }
    public decimal InformationRatio { get; init; }
    public int LookbackDays { get; init; }
}

public record DrawdownMetrics
{
    public decimal CurrentDrawdown { get; init; }
    public decimal MaxDrawdown { get; init; }
    public decimal AverageDrawdown { get; init; }
    public int MaxDrawdownDuration { get; init; }
    public int CurrentDrawdownDuration { get; init; }
    public DateTime MaxDrawdownDate { get; init; }
    public decimal RecoveryFactor { get; init; }
    public List<DrawdownPeriod>? DrawdownPeriods { get; init; }
}

public record DrawdownPeriod
{
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public int DurationDays { get; init; }
    public decimal Depth { get; init; }
    public decimal Recovery { get; init; }
    public bool IsRecovered { get; init; }
}

public record StressTestResult
{
    public required string ScenarioName { get; init; }
    public required string Description { get; init; }
    public required decimal ImpactOnPortfolio { get; init; }
    public required decimal ImpactPercentage { get; init; }
    public decimal NewPortfolioValue { get; init; }
    public decimal VaRImpact { get; init; }
    public string Severity { get; init; } = "MODERATE"; // "MILD", "MODERATE", "SEVERE", "EXTREME"
    public Dictionary<string, decimal>? PositionImpacts { get; init; }
}

public record RiskAdjustedReturns
{
    public decimal SharpeRatio { get; init; }
    public decimal SortinoRatio { get; init; }
    public decimal CalmarRatio { get; init; }
    public decimal OmegaRatio { get; init; }
    public decimal InformationRatio { get; init; }
    public decimal TreynorRatio { get; init; }
    public decimal JensenAlpha { get; init; }
    public int LookbackDays { get; init; }
}

public record PositionLimits
{
    public decimal MaxPositionSize { get; init; }
    public decimal MaxPositionValue { get; init; }
    public decimal MaxPortfolioPercentage { get; init; }
    public decimal MaxSectorExposure { get; init; }
    public decimal MaxConcentration { get; init; }
    public decimal MaxLeverage { get; init; }
    public decimal CurrentLeverage { get; init; }
    public bool IsWithinLimits { get; init; }
    public List<string>? ViolatedLimits { get; init; }
}

/// <summary>
/// Portfolio-level risk metrics
/// </summary>
public record PortfolioRiskMetrics
{
    public required DateTime Timestamp { get; init; }
    public required decimal PortfolioValue { get; init; }
    public required int PositionCount { get; init; }

    public VaRMetrics? PortfolioVaR { get; init; }
    public CVaRMetrics? PortfolioCVaR { get; init; }
    public VolatilityMetrics? PortfolioVolatility { get; init; }
    public DrawdownMetrics? PortfolioDrawdown { get; init; }
    public RiskAdjustedReturns? PortfolioRiskAdjusted { get; init; }

    public decimal DiversificationRatio { get; init; }
    public decimal ConcentrationIndex { get; init; }
    public decimal EffectiveNumberOfPositions { get; init; }

    public Dictionary<string, decimal>? SectorExposures { get; init; }
    public Dictionary<string, decimal>? AssetClassExposures { get; init; }
    public Dictionary<string, decimal>? GeographicExposures { get; init; }
    public Dictionary<string, decimal>? FactorExposures { get; init; }

    public List<RiskMetrics>? PositionRisks { get; init; }

    public decimal OverallRiskScore { get; init; }
    public string RiskLevel { get; init; } = "MEDIUM";

    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Real-time risk alerts
/// </summary>
public record RiskAlert
{
    public required string AlertId { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Severity { get; init; } // "INFO", "WARNING", "CRITICAL"
    public required string Type { get; init; } // "VAR_BREACH", "LIMIT_BREACH", "CONCENTRATION", "CORRELATION_SPIKE", etc.
    public required string Message { get; init; }
    public required string Symbol { get; init; }
    public decimal? CurrentValue { get; init; }
    public decimal? ThresholdValue { get; init; }
    public decimal? Deviation { get; init; }
    public Dictionary<string, object>? Details { get; init; }
    public List<string>? RecommendedActions { get; init; }
}
