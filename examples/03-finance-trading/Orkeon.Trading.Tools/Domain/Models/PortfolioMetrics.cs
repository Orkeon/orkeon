namespace Orkeon.Trading.Tools.Domain.Models;

/// <summary>
/// Portfolio position for risk and optimization analysis
/// </summary>
public record PortfolioPosition
{
    public required string Symbol { get; init; }
    public required decimal Weight { get; init; } // Portfolio weight (0.0 to 1.0)
    public required string AssetClass { get; init; } // "equity", "bond", "commodity", "cash", etc.
    public decimal? Quantity { get; init; }
    public decimal? CurrentPrice { get; init; }
    public decimal? MarketValue { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Comprehensive portfolio metrics and state
/// </summary>
public record PortfolioMetrics
{
    public required string PortfolioId { get; init; }
    public required DateTime Timestamp { get; init; }

    // Portfolio Value
    public decimal TotalValue { get; init; }
    public decimal CashBalance { get; init; }
    public decimal EquityValue { get; init; }
    public decimal LongValue { get; init; }
    public decimal ShortValue { get; init; }

    // Performance
    public PerformanceMetrics? Performance { get; init; }

    // Risk Metrics
    public PortfolioRiskMetrics? Risk { get; init; }

    // Position Details
    public List<PositionMetrics>? Positions { get; init; }
    public int TotalPositions { get; init; }

    // Allocation
    public AllocationMetrics? Allocation { get; init; }

    // Rebalancing Recommendations
    public List<RebalancingAction>? RebalancingActions { get; init; }

    public Dictionary<string, object>? Metadata { get; init; }
}

public record PerformanceMetrics
{
    // Returns
    public decimal DailyReturn { get; init; }
    public decimal WeeklyReturn { get; init; }
    public decimal MonthlyReturn { get; init; }
    public decimal QuarterlyReturn { get; init; }
    public decimal YearlyReturn { get; init; }
    public decimal InceptionReturn { get; init; }

    // Cumulative Returns
    public decimal CumulativeReturn { get; init; }
    public decimal CAGR { get; init; }

    // Risk-Adjusted Returns
    public decimal SharpeRatio { get; init; }
    public decimal SortinoRatio { get; init; }
    public decimal CalmarRatio { get; init; }
    public decimal InformationRatio { get; init; }

    // Benchmark Comparison
    public decimal Alpha { get; init; }
    public decimal Beta { get; init; }
    public decimal TrackingError { get; init; }
    public decimal ActiveReturn { get; init; }

    // Win/Loss Statistics
    public int WinningDays { get; init; }
    public int LosingDays { get; init; }
    public decimal WinRate { get; init; }
    public decimal AverageWin { get; init; }
    public decimal AverageLoss { get; init; }
    public decimal ProfitFactor { get; init; }

    public DateTime InceptionDate { get; init; }
}

public record PositionMetrics
{
    public required string Symbol { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal CurrentPrice { get; init; }
    public required decimal MarketValue { get; init; }

    // Cost Basis
    public decimal AverageCost { get; init; }
    public decimal TotalCost { get; init; }

    // P&L
    public decimal UnrealizedPnL { get; init; }
    public decimal UnrealizedPnLPercent { get; init; }
    public decimal RealizedPnL { get; init; }
    public decimal TotalPnL { get; init; }

    // Position Details
    public decimal PortfolioWeight { get; init; }
    public DateTime OpenDate { get; init; }
    public int HoldingDays { get; init; }

    // Risk Metrics
    public RiskMetrics? Risk { get; init; }

    // Performance
    public decimal PositionReturn { get; init; }
    public decimal ContributionToReturn { get; init; }

    public Dictionary<string, object>? Metadata { get; init; }
}

public record AllocationMetrics
{
    // By Asset Class
    public Dictionary<string, decimal>? AssetClassAllocation { get; init; }

    // By Sector
    public Dictionary<string, decimal>? SectorAllocation { get; init; }

    // By Geography
    public Dictionary<string, decimal>? GeographicAllocation { get; init; }

    // By Strategy
    public Dictionary<string, decimal>? StrategyAllocation { get; init; }

    // By Market Cap
    public Dictionary<string, decimal>? MarketCapAllocation { get; init; }

    // Diversification Metrics
    public decimal ConcentrationIndex { get; init; }
    public decimal EffectiveNumberOfPositions { get; init; }
    public decimal DiversificationRatio { get; init; }

    // Top Holdings
    public List<TopHolding>? TopHoldings { get; init; }
}

public record TopHolding
{
    public required string Symbol { get; init; }
    public required decimal Weight { get; init; }
    public required decimal Value { get; init; }
    public string? Sector { get; init; }
    public string? AssetClass { get; init; }
}

public record RebalancingAction
{
    public required string Symbol { get; init; }
    public required string Action { get; init; } // "BUY", "SELL", "HOLD"
    public decimal CurrentWeight { get; init; }
    public decimal TargetWeight { get; init; }
    public decimal WeightDifference { get; init; }
    public decimal TargetQuantity { get; init; }
    public decimal QuantityChange { get; init; }
    public decimal EstimatedValue { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string Priority { get; init; } = "NORMAL"; // "LOW", "NORMAL", "HIGH", "URGENT"
}

/// <summary>
/// Portfolio optimization result
/// </summary>
public record PortfolioOptimization
{
    public required DateTime Timestamp { get; init; }
    public required string Method { get; init; } // "MEAN_VARIANCE", "BLACK_LITTERMAN", "RISK_PARITY", "HIERARCHICAL"

    // Optimal Weights
    public required Dictionary<string, decimal> OptimalWeights { get; init; }

    // Expected Metrics
    public decimal ExpectedReturn { get; init; }
    public decimal ExpectedVolatility { get; init; }
    public decimal ExpectedSharpeRatio { get; init; }

    // Constraints Applied
    public List<string>? AppliedConstraints { get; init; }

    // Efficient Frontier
    public List<EfficientFrontierPoint>? EfficientFrontier { get; init; }

    // Changes from Current
    public Dictionary<string, WeightChange>? WeightChanges { get; init; }
    public decimal TotalTurnover { get; init; }

    public Dictionary<string, object>? Metadata { get; init; }
}

public record EfficientFrontierPoint
{
    public decimal ExpectedReturn { get; init; }
    public decimal Volatility { get; init; }
    public decimal SharpeRatio { get; init; }
    public Dictionary<string, decimal>? Weights { get; init; }
}

public record WeightChange
{
    public required string Symbol { get; init; }
    public decimal CurrentWeight { get; init; }
    public decimal OptimalWeight { get; init; }
    public decimal Change { get; init; }
    public string Action { get; init; } = "HOLD"; // "BUY", "SELL", "HOLD"
}

/// <summary>
/// Transaction cost analysis
/// </summary>
public record TransactionCostAnalysis
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public required decimal Quantity { get; init; }
    public required string Side { get; init; } // "BUY" or "SELL"

    // Explicit Costs
    public decimal CommissionCost { get; init; }
    public decimal ExchangeFees { get; init; }
    public decimal TaxesAndDuties { get; init; }
    public decimal TotalExplicitCost { get; init; }

    // Implicit Costs
    public decimal BidAskSpread { get; init; }
    public decimal MarketImpact { get; init; }
    public decimal OpportunityCost { get; init; }
    public decimal TotalImplicitCost { get; init; }

    // Total Cost
    public decimal TotalCost { get; init; }
    public decimal TotalCostBasisPoints { get; init; }
    public decimal TotalCostPercent { get; init; }

    // Recommendations
    public string? Algorithm { get; init; } // "VWAP", "TWAP", "POV", "IS"
    public int? RecommendedSlices { get; init; }
    public TimeSpan? RecommendedDuration { get; init; }

    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Portfolio attribution analysis
/// </summary>
public record AttributionAnalysis
{
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public decimal TotalReturn { get; init; }

    // Brinson Attribution
    public decimal AllocationEffect { get; init; }
    public decimal SelectionEffect { get; init; }
    public decimal InteractionEffect { get; init; }

    // Factor Attribution
    public Dictionary<string, decimal>? FactorContributions { get; init; }

    // Sector Attribution
    public Dictionary<string, SectorAttribution>? SectorAttributions { get; init; }

    // Security Attribution
    public List<SecurityAttribution>? SecurityAttributions { get; init; }

    public Dictionary<string, object>? Metadata { get; init; }
}

public record SectorAttribution
{
    public required string Sector { get; init; }
    public decimal AllocationEffect { get; init; }
    public decimal SelectionEffect { get; init; }
    public decimal TotalEffect { get; init; }
    public decimal PortfolioWeight { get; init; }
    public decimal BenchmarkWeight { get; init; }
}

public record SecurityAttribution
{
    public required string Symbol { get; init; }
    public decimal Contribution { get; init; }
    public decimal Return { get; init; }
    public decimal AverageWeight { get; init; }
}
