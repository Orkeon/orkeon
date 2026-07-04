namespace Orkeon.Trading.Tools.Domain.Models;

/// <summary>
/// Execution order details and state
/// </summary>
public record ExecutionOrder
{
    public required string OrderId { get; init; }
    public required string Symbol { get; init; }
    public required string Side { get; init; } // "BUY" or "SELL"
    public required decimal Quantity { get; init; }
    public required string OrderType { get; init; } // "MARKET", "LIMIT", "STOP", "STOP_LIMIT"
    public required string Status { get; init; } // "PENDING", "SUBMITTED", "PARTIAL", "FILLED", "CANCELLED", "REJECTED"

    // Pricing
    public decimal? LimitPrice { get; init; }
    public decimal? StopPrice { get; init; }
    public decimal? AverageFilledPrice { get; init; }

    // Execution Details
    public decimal FilledQuantity { get; init; }
    public decimal RemainingQuantity { get; init; }
    public decimal FillPercent => Quantity > 0 ? (FilledQuantity / Quantity) * 100 : 0;

    // Timestamps
    public DateTime CreatedAt { get; init; }
    public DateTime? SubmittedAt { get; init; }
    public DateTime? FirstFillAt { get; init; }
    public DateTime? LastFillAt { get; init; }
    public DateTime? CompletedAt { get; init; }

    // Time in Force
    public string TimeInForce { get; init; } = "DAY"; // "DAY", "GTC", "IOC", "FOK"

    // Execution Algorithm
    public string? Algorithm { get; init; } // "VWAP", "TWAP", "POV", "IS"
    public Dictionary<string, object>? AlgorithmParams { get; init; }

    // Fills
    public List<OrderFill>? Fills { get; init; }

    // Cost Analysis
    public ExecutionCostAnalysis? CostAnalysis { get; init; }

    // Metadata
    public string? StrategyId { get; init; }
    public string? AgentId { get; init; }
    public string? Notes { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record OrderFill
{
    public required string FillId { get; init; }
    public required DateTime Timestamp { get; init; }
    public required decimal Price { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal Commission { get; init; }
    public decimal Value => Price * Quantity;
    public string? Exchange { get; init; }
    public string? ExecutionVenue { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record ExecutionCostAnalysis
{
    // Arrival Price Analysis
    public decimal ArrivalPrice { get; init; }
    public decimal AverageExecutionPrice { get; init; }
    public decimal Slippage { get; init; }
    public decimal SlippageBasisPoints { get; init; }

    // Benchmark Analysis
    public decimal VWAPPrice { get; init; }
    public decimal VWAPSlippage { get; init; }
    public decimal TWAPPrice { get; init; }
    public decimal TWAPSlippage { get; init; }

    // Costs
    public decimal TotalCommission { get; init; }
    public decimal MarketImpact { get; init; }
    public decimal TimingCost { get; init; }
    public decimal TotalCost { get; init; }
    public decimal TotalCostBasisPoints { get; init; }

    // Performance vs Benchmark
    public decimal ImplementationShortfall { get; init; }
    public string PerformanceRating { get; init; } = "NEUTRAL"; // "EXCELLENT", "GOOD", "NEUTRAL", "POOR"

    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// VWAP execution algorithm state
/// </summary>
public record VWAPExecution
{
    public required string OrderId { get; init; }
    public required string Symbol { get; init; }
    public required DateTime StartTime { get; init; }
    public required DateTime EndTime { get; init; }
    public required decimal TotalQuantity { get; init; }

    // Execution State
    public decimal ExecutedQuantity { get; init; }
    public decimal RemainingQuantity { get; init; }
    public decimal AveragePrice { get; init; }
    public string Status { get; init; } = "IN_PROGRESS"; // "IN_PROGRESS", "COMPLETED", "CANCELLED"

    // VWAP Tracking
    public decimal MarketVWAP { get; init; }
    public decimal ExecutionVWAP { get; init; }
    public decimal VWAPDeviation { get; init; }

    // Slice Details
    public List<VWAPSlice>? Slices { get; init; }
    public int CompletedSlices { get; init; }
    public int TotalSlices { get; init; }

    // Performance
    public decimal SlippageFromVWAP { get; init; }
    public string PerformanceRating { get; init; } = "ON_TARGET"; // "AHEAD", "ON_TARGET", "BEHIND"

    public Dictionary<string, object>? Metadata { get; init; }
}

public record VWAPSlice
{
    public int SliceNumber { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public decimal TargetQuantity { get; init; }
    public decimal ExecutedQuantity { get; init; }
    public decimal AveragePrice { get; init; }
    public decimal MarketVolume { get; init; }
    public decimal ParticipationRate { get; init; }
    public string Status { get; init; } = "PENDING"; // "PENDING", "IN_PROGRESS", "COMPLETED"
}

/// <summary>
/// TWAP execution algorithm state
/// </summary>
public record TWAPExecution
{
    public required string OrderId { get; init; }
    public required string Symbol { get; init; }
    public required DateTime StartTime { get; init; }
    public required DateTime EndTime { get; init; }
    public required decimal TotalQuantity { get; init; }
    public required int NumberOfSlices { get; init; }

    // Execution State
    public decimal ExecutedQuantity { get; init; }
    public decimal RemainingQuantity { get; init; }
    public decimal AveragePrice { get; init; }
    public string Status { get; init; } = "IN_PROGRESS";

    // TWAP Tracking
    public decimal MarketTWAP { get; init; }
    public decimal ExecutionTWAP { get; init; }
    public decimal TWAPDeviation { get; init; }

    // Slice Details
    public List<TWAPSlice>? Slices { get; init; }
    public int CompletedSlices { get; init; }

    // Performance
    public decimal SlippageFromTWAP { get; init; }
    public decimal ExecutionRate { get; init; }
    public string PerformanceRating { get; init; } = "ON_SCHEDULE";

    public Dictionary<string, object>? Metadata { get; init; }
}

public record TWAPSlice
{
    public int SliceNumber { get; init; }
    public DateTime ScheduledTime { get; init; }
    public DateTime? ExecutionTime { get; init; }
    public decimal TargetQuantity { get; init; }
    public decimal ExecutedQuantity { get; init; }
    public decimal? ExecutionPrice { get; init; }
    public string Status { get; init; } = "PENDING";
}

/// <summary>
/// Implementation Shortfall execution algorithm state
/// </summary>
public record ImplementationShortfallExecution
{
    public required string OrderId { get; init; }
    public required string Symbol { get; init; }
    public required decimal TotalQuantity { get; init; }
    public required decimal DecisionPrice { get; init; }
    public required DateTime DecisionTime { get; init; }

    // Execution State
    public decimal ExecutedQuantity { get; init; }
    public decimal RemainingQuantity { get; init; }
    public decimal AverageExecutionPrice { get; init; }
    public string Status { get; init; } = "IN_PROGRESS";

    // Cost Components
    public decimal DelayComponent { get; init; }
    public decimal ImpactComponent { get; init; }
    public decimal TimingComponent { get; init; }
    public decimal OpportunityComponent { get; init; }
    public decimal TotalShortfall { get; init; }
    public decimal TotalShortfallBasisPoints { get; init; }

    // Adaptive Parameters
    public decimal UrgencyLevel { get; init; }
    public decimal CurrentParticipationRate { get; init; }
    public decimal TargetParticipationRate { get; init; }

    // Market Conditions
    public decimal CurrentVolatility { get; init; }
    public decimal CurrentLiquidity { get; init; }
    public string MarketCondition { get; init; } = "NORMAL"; // "FAVORABLE", "NORMAL", "UNFAVORABLE"

    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Percentage of Volume (POV) execution algorithm state
/// </summary>
public record POVExecution
{
    public required string OrderId { get; init; }
    public required string Symbol { get; init; }
    public required decimal TotalQuantity { get; init; }
    public required decimal TargetParticipationRate { get; init; }

    // Execution State
    public decimal ExecutedQuantity { get; init; }
    public decimal RemainingQuantity { get; init; }
    public decimal AveragePrice { get; init; }
    public string Status { get; init; } = "IN_PROGRESS";

    // Participation Tracking
    public decimal ActualParticipationRate { get; init; }
    public decimal MarketVolumeTraded { get; init; }
    public decimal OurVolumeTraded { get; init; }

    // Adaptive Controls
    public decimal MinParticipationRate { get; init; }
    public decimal MaxParticipationRate { get; init; }
    public bool IsAggressive { get; init; }

    // Performance
    public decimal VWAPDeviation { get; init; }
    public decimal MarketImpact { get; init; }
    public string PerformanceRating { get; init; } = "ON_TARGET";

    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Real-time execution monitoring
/// </summary>
public record ExecutionMonitoring
{
    public required DateTime Timestamp { get; init; }
    public required string OrderId { get; init; }
    public required string Symbol { get; init; }

    // Real-time Metrics
    public decimal CurrentPrice { get; init; }
    public decimal ArrivalPrice { get; init; }
    public decimal CurrentSlippage { get; init; }
    public decimal EstimatedFinalSlippage { get; init; }

    // Market Conditions
    public decimal CurrentVolume { get; init; }
    public decimal AverageVolume { get; init; }
    public decimal CurrentSpread { get; init; }
    public decimal AverageSpread { get; init; }
    public decimal CurrentVolatility { get; init; }

    // Execution Progress
    public decimal PercentComplete { get; init; }
    public decimal ExecutionRate { get; init; }
    public TimeSpan ElapsedTime { get; init; }
    public TimeSpan EstimatedTimeToComplete { get; init; }

    // Alerts
    public List<ExecutionAlert>? Alerts { get; init; }

    // Recommendations
    public List<string>? Recommendations { get; init; }

    public Dictionary<string, object>? Metadata { get; init; }
}

public record ExecutionAlert
{
    public required string AlertId { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Severity { get; init; } // "INFO", "WARNING", "CRITICAL"
    public required string Type { get; init; } // "SLIPPAGE", "LIQUIDITY", "TIMING", "MARKET_IMPACT"
    public required string Message { get; init; }
    public Dictionary<string, object>? Details { get; init; }
}

/// <summary>
/// Trade blotter entry
/// </summary>
public record TradeBlotterEntry
{
    public required string TradeId { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Symbol { get; init; }
    public required string Side { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal Price { get; init; }
    public required decimal Value { get; init; }
    public required string Status { get; init; }

    public string? OrderId { get; init; }
    public string? StrategyId { get; init; }
    public string? AgentId { get; init; }
    public decimal? Commission { get; init; }
    public string? Exchange { get; init; }
    public string? CounterpartyId { get; init; }

    public Dictionary<string, object>? Metadata { get; init; }
}
