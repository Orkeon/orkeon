using Orkeon.Domain.Common;
using Orkeon.Trading.Tools.Domain.Services;
using Orkeon.Trading.Tools.Infrastructure.Analysis;
using Orkeon.Trading.Tools.Infrastructure.Data;
using Orkeon.Trading.Tools.Infrastructure.Data.Providers;
using Orkeon.Trading.Tools.Infrastructure.Execution;
using Orkeon.Trading.Tools.Infrastructure.Execution.Providers;
using Orkeon.Trading.Tools.Infrastructure.Governance;
using Orkeon.Trading.Tools.Infrastructure.Portfolio;
using Orkeon.Trading.Tools.Infrastructure.Prediction;
using Orkeon.Trading.Tools.Infrastructure.Risk;
using Orkeon.Trading.Tools.Infrastructure.Risk.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Orkeon.Trading.Tools.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering all Trading Tools in the DI container.
/// </summary>
public static class TradingToolsServiceCollectionExtensions
{
    /// <summary>
    /// Registers all 44 trading tools as singleton ITool services.
    /// </summary>
    public static IServiceCollection AddTradingTools(this IServiceCollection services)
    {
        // Market data provider (HttpClient via IHttpClientFactory for connection pooling)
        services.AddHttpClient<YahooFinanceMarketDataProvider>();
        services.AddSingleton<IMarketDataProvider, YahooFinanceMarketDataProvider>();

        // Data providers (mock implementations — swap for real providers in production)
        services.AddSingleton<ISentimentDataProvider, MockSentimentDataProvider>();
        services.AddSingleton<IFundamentalDataProvider, MockFundamentalDataProvider>();
        services.AddSingleton<IOrderBookProvider, MockOrderBookProvider>();
        services.AddSingleton<ITickDataProvider, MockTickDataProvider>();
        services.AddSingleton<IFactorModelProvider, MockFactorModelProvider>();
        services.AddSingleton<IVenueDataProvider, MockVenueDataProvider>();

        // Analysis tools
        services.AddSingleton<ITool, CorrelationAnalysisTool>();
        services.AddSingleton<ITool, MarketRegimeClassificationTool>();
        services.AddSingleton<ITool, MultiAssetComparisonTool>();
        services.AddSingleton<ITool, PatternRecognitionTool>();
        services.AddSingleton<ITool, TechnicalIndicatorsTool>();

        // Data tools
        services.AddSingleton<ITool, AlternativeDataTool>();
        services.AddSingleton<ITool, FundamentalDataTool>();
        services.AddSingleton<ITool, HistoricalDataFetchTool>();
        services.AddSingleton<ITool, OrderBookDepthTool>();
        services.AddSingleton<ITool, RealTimeTickDataTool>();
        services.AddSingleton<ITool, UnifiedMarketDataTool>();

        // Execution tools
        services.AddSingleton<ITool, ImplementationShortfallTool>();
        services.AddSingleton<ITool, POVExecutionTool>();
        services.AddSingleton<ITool, SmartOrderRoutingTool>();
        services.AddSingleton<ITool, TWAPExecutionTool>();
        services.AddSingleton<ITool, VWAPExecutionTool>();

        // Governance tools
        services.AddSingleton<ITool, AlertManagementTool>();
        services.AddSingleton<ITool, AuditTrailTool>();
        services.AddSingleton<ITool, CircuitBreakerTool>();
        services.AddSingleton<ITool, ComplianceCheckTool>();
        services.AddSingleton<ITool, ConcentrationRiskTool>();
        services.AddSingleton<ITool, DashboardMetricsTool>();
        services.AddSingleton<ITool, DrawdownMonitoringTool>();
        services.AddSingleton<ITool, LiquidityAnalysisTool>();
        services.AddSingleton<ITool, PerformanceAttributionTool>();
        services.AddSingleton<ITool, PositionLimitMonitoringTool>();
        services.AddSingleton<ITool, RegulatoryReportingTool>();
        services.AddSingleton<ITool, TransactionCostAnalysisTool>();

        // Portfolio tools
        services.AddSingleton<ITool, BlackLittermanTool>();
        services.AddSingleton<ITool, ConstraintOptimizationTool>();
        services.AddSingleton<ITool, HierarchicalRiskParityTool>();
        services.AddSingleton<ITool, MeanVarianceOptimizationTool>();
        services.AddSingleton<ITool, PortfolioRebalancingTool>();
        services.AddSingleton<ITool, RiskParityTool>();

        // Prediction tools
        services.AddSingleton<ITool, ARIMAPredictionTool>();
        services.AddSingleton<ITool, BacktestingTool>();
        services.AddSingleton<ITool, EnsemblePredictionTool>();
        services.AddSingleton<ITool, ProphetPredictionTool>();
        services.AddSingleton<ITool, RandomForestPredictionTool>();
        services.AddSingleton<ITool, XGBoostPredictionTool>();

        // Risk tools
        services.AddSingleton<ITool, CVaRCalculationTool>();
        services.AddSingleton<ITool, FactorExposureTool>();
        services.AddSingleton<ITool, StressTestingTool>();
        services.AddSingleton<ITool, VaRCalculationTool>();

        return services;
    }
}
