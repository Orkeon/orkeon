# Trading Tools Porting Inventory

44 trading tools ported from `examples/_legacy/classictrading/Orkeon.Trading.Tools/`
(commit `c5f776fc^`) into this project on 2026-05-04.

Source of truth for each tool is the YAML definition under `Domain/ToolDefinitions/{Category}/`,
loaded at runtime by `ToolDefinitionLoader` and used by `TradingToolBase` to expose the
schema (Name, Description, Parameters, Returns) to the agent.

## Ported categories

| Category | Count | Tools |
|---|---|---|
| Analysis | 5 | correlation_analysis, market_regime_classification, multi_asset_comparison, pattern_recognition, technical_indicators |
| Data | 6 | alternative_data, fundamental_data, historical_data_fetch, orderbook_depth, realtime_tick_data, unified_market_data |
| Execution | 5 | implementation_shortfall, pov_execution, smart_order_routing, twap_execution, vwap_execution |
| Governance | 12 | alert_management, audit_trail, circuit_breaker, compliance_check, concentration_risk, dashboard_metrics, drawdown_monitoring, liquidity_analysis, performance_attribution, position_limit_monitoring, regulatory_reporting, transaction_cost_analysis |
| Portfolio | 6 | black_litterman, constraint_optimization, hierarchical_risk_parity, mean_variance_optimization, portfolio_rebalancing, risk_parity |
| Prediction | 6 | arima_prediction, backtesting, ensemble_prediction, prophet_prediction, random_forest_prediction, xgboost_prediction |
| Risk | 4 | cvar_calculation, factor_exposure, stress_testing, var_calculation |
| **Total** | **44** | |

## Shared infrastructure ported

- `Domain/Models/` — ToolDefinition, MarketData, TechnicalIndicators, Predictions, RiskMetrics, PortfolioMetrics, ExecutionMetrics
- `Domain/Serialization/InvariantConverters.cs` — invariant-culture decimal/double converters
- `Domain/Services/` — provider interfaces (IMarketDataProvider, ITickDataProvider, IOrderBookProvider, IFundamentalDataProvider, ISentimentDataProvider, IFactorModelProvider, IVenueDataProvider) + FinancialMathHelper
- `Infrastructure/Base/` — TradingToolBase (legacy protocol), TradingToolBase&lt;TReq,TRes&gt; (typed pipeline), ToolDefinitionLoader (YAML reader)
- `Infrastructure/Data/Providers/` — Mock + YahooFinanceMarketDataProvider
- `Infrastructure/Execution/Providers/MockVenueDataProvider`
- `Infrastructure/Risk/Providers/MockFactorModelProvider`
- `Infrastructure/DependencyInjection/TradingToolsServiceCollectionExtensions.AddTradingTools()`

## DI registration

All 44 tools are registered as `ITool` singletons by `services.AddTradingTools()`,
which is wired into `examples/runners/trading/Program.cs` (RUN-01).

## Mock vs real providers

For the ported state, the following providers ship as **mocks** so the tools work
out of the box without external credentials. Swap them for real implementations
in production by registering an alternate provider before `AddTradingTools()`:

| Interface | Default mock | Real alternative |
|---|---|---|
| IMarketDataProvider | YahooFinanceMarketDataProvider (live) | (already real) |
| ISentimentDataProvider | MockSentimentDataProvider | (TBD — Polygon, Refinitiv, etc.) |
| IFundamentalDataProvider | MockFundamentalDataProvider | (TBD) |
| IOrderBookProvider | MockOrderBookProvider | (TBD) |
| ITickDataProvider | MockTickDataProvider | (TBD) |
| IFactorModelProvider | MockFactorModelProvider | (TBD) |
| IVenueDataProvider | MockVenueDataProvider | (TBD) |

## YAML lookup

`ToolDefinitionLoader` searches `AppContext.BaseDirectory/Domain/ToolDefinitions/` recursively
for `{tool_id}.yaml`. The csproj includes:

```xml
<Content Include="Domain\ToolDefinitions\**\*.yaml">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
```

so the YAMLs land next to the binaries at build time.
