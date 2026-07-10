using Orkeon.Domain.Tools;
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
        services.AddSingleton<IBaseTool, CorrelationAnalysisTool>();
        services.AddSingleton<IBaseTool, MarketRegimeClassificationTool>();
        services.AddSingleton<IBaseTool, MultiAssetComparisonTool>();
        services.AddSingleton<IBaseTool, PatternRecognitionTool>();
        services.AddSingleton<IBaseTool, TechnicalIndicatorsTool>();

        // Data tools
        services.AddSingleton<IBaseTool, AlternativeDataTool>();
        services.AddSingleton<IBaseTool, FundamentalDataTool>();
        services.AddSingleton<IBaseTool, HistoricalDataFetchTool>();
        services.AddSingleton<IBaseTool, OrderBookDepthTool>();
        services.AddSingleton<IBaseTool, RealTimeTickDataTool>();
        services.AddSingleton<IBaseTool, UnifiedMarketDataTool>();

        // Execution tools
        services.AddSingleton<IBaseTool, ImplementationShortfallTool>();
        services.AddSingleton<IBaseTool, POVExecutionTool>();
        services.AddSingleton<IBaseTool, SmartOrderRoutingTool>();
        services.AddSingleton<IBaseTool, TWAPExecutionTool>();
        services.AddSingleton<IBaseTool, VWAPExecutionTool>();

        // Governance tools
        services.AddSingleton<IBaseTool, AlertManagementTool>();
        services.AddSingleton<IBaseTool, AuditTrailTool>();
        services.AddSingleton<IBaseTool, CircuitBreakerTool>();
        services.AddSingleton<IBaseTool, ComplianceCheckTool>();
        services.AddSingleton<IBaseTool, ConcentrationRiskTool>();
        services.AddSingleton<IBaseTool, DashboardMetricsTool>();
        services.AddSingleton<IBaseTool, DrawdownMonitoringTool>();
        services.AddSingleton<IBaseTool, LiquidityAnalysisTool>();
        services.AddSingleton<IBaseTool, PerformanceAttributionTool>();
        services.AddSingleton<IBaseTool, PositionLimitMonitoringTool>();
        services.AddSingleton<IBaseTool, RegulatoryReportingTool>();
        services.AddSingleton<IBaseTool, TransactionCostAnalysisTool>();

        // Portfolio tools
        services.AddSingleton<IBaseTool, BlackLittermanTool>();
        services.AddSingleton<IBaseTool, ConstraintOptimizationTool>();
        services.AddSingleton<IBaseTool, HierarchicalRiskParityTool>();
        services.AddSingleton<IBaseTool, MeanVarianceOptimizationTool>();
        services.AddSingleton<IBaseTool, PortfolioRebalancingTool>();
        services.AddSingleton<IBaseTool, RiskParityTool>();

        // Prediction tools
        services.AddSingleton<IBaseTool, ARIMAPredictionTool>();
        services.AddSingleton<IBaseTool, BacktestingTool>();
        services.AddSingleton<IBaseTool, EnsemblePredictionTool>();
        services.AddSingleton<IBaseTool, ProphetPredictionTool>();
        services.AddSingleton<IBaseTool, RandomForestPredictionTool>();
        services.AddSingleton<IBaseTool, XGBoostPredictionTool>();

        // Risk tools
        services.AddSingleton<IBaseTool, CVaRCalculationTool>();
        services.AddSingleton<IBaseTool, FactorExposureTool>();
        services.AddSingleton<IBaseTool, StressTestingTool>();
        services.AddSingleton<IBaseTool, VaRCalculationTool>();

        return services;
    }
}
