using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Analysis.Contracts;
using Orkeon.Trading.Tools.Domain.Models;
using Orkeon.Trading.Tools.Domain.Services;
using Microsoft.Extensions.Logging;
using MathNet.Numerics.Statistics;

namespace Orkeon.Trading.Tools.Infrastructure.Analysis;

/// <summary>
/// Multi-asset comparison tool for relative performance and cross-asset analysis.
/// Compares multiple instruments across performance, risk, and valuation metrics.
/// </summary>
public class MultiAssetComparisonTool(ILogger<MultiAssetComparisonTool>? logger = null)
    : TradingToolBase<MultiAssetComparisonRequest, MultiAssetComparisonResponse>(logger)
{
    protected override string ToolId => "multi_asset_comparison";

    protected override async Task<MultiAssetComparisonResponse> ExecuteTypedAsync(
        MultiAssetComparisonRequest request,
        CancellationToken cancellationToken)
    {
        // Deduplicate symbols to prevent 'duplicate key' errors in dictionaries
        var symbols = request.Symbols.Distinct().ToList();

        _logger?.LogInformation("Comparing {Count} assets over period {Period}", symbols.Count, request.ComparisonPeriod);

        var comparison = await Task.Run(() => CompareAssets(symbols, request.PriceData, request.ComparisonPeriod, request.Metrics), cancellationToken);

        _logger?.LogInformation("Completed comparison of {Count} assets", symbols.Count);

        return comparison;
    }

    private static MultiAssetComparisonResponse CompareAssets(
        List<string> symbols,
        Dictionary<string, List<MarketData>> priceData,
        string comparisonPeriod,
        List<string> requestedMetrics)
    {
        var includeAll = requestedMetrics.Contains("all");

        // Calculate metrics for each symbol
        var assetMetrics = new Dictionary<string, Dictionary<string, decimal>>();

        foreach (var symbol in symbols)
        {
            if (!priceData.TryGetValue(symbol, out var data) || data.Count == 0)
                continue;

            var metrics = new Dictionary<string, decimal>();

            // Total return
            if (includeAll || requestedMetrics.Contains("return"))
            {
                metrics["total_return"] = CalculateTotalReturn(data);
                metrics["annualized_return"] = CalculateAnnualizedReturn(data);
            }

            // Volatility
            if (includeAll || requestedMetrics.Contains("volatility"))
            {
                metrics["volatility"] = CalculateVolatility(data);
            }

            // Risk-adjusted metrics
            if (includeAll || requestedMetrics.Contains("sharpe"))
            {
                metrics["sharpe_ratio"] = CalculateSharpeRatio(data);
                metrics["sortino_ratio"] = CalculateSortinoRatio(data);
            }

            // Drawdown
            if (includeAll || requestedMetrics.Contains("max_dd"))
            {
                metrics["max_drawdown"] = CalculateMaxDrawdown(data);
            }

            // Current price and change
            metrics["current_price"] = data.Last().Close;
            metrics["price_change_pct"] = CalculatePriceChange(data);

            assetMetrics[symbol] = metrics;
        }

        // Create comparison table
        var comparisonTable = BuildComparisonTable(assetMetrics);

        // Rank assets by different criteria
        var rankings = CalculateRankings(assetMetrics);

        // Identify best performers
        var bestPerformers = IdentifyBestPerformers(assetMetrics);

        // Correlation matrix for diversification analysis
        var correlationMatrix = CalculateSimpleCorrelations(priceData, symbols);

        return new MultiAssetComparisonResponse
        {
            Timestamp = DateTime.UtcNow,
            Symbols = symbols,
            ComparisonPeriod = comparisonPeriod,
            ComparisonTable = comparisonTable,
            Rankings = rankings,
            BestPerformers = bestPerformers,
            CorrelationMatrix = correlationMatrix,
            SummaryStatistics = CalculateSummaryStatistics(assetMetrics)
        };
    }

    private static decimal CalculateTotalReturn(List<MarketData> data)
    {
        if (data.Count < 2) return 0;
        var startPrice = data.First().Close;
        var endPrice = data.Last().Close;
        return ((endPrice - startPrice) / startPrice) * 100;
    }

    private static decimal CalculateAnnualizedReturn(List<MarketData> data)
    {
        if (data.Count < 2) return 0;
        var totalReturn = CalculateTotalReturn(data) / 100;
        var days = (data.Last().Timestamp - data.First().Timestamp).TotalDays;
        if (days <= 0) return 0;
        return (decimal)(Math.Pow((double)(1 + totalReturn), 365.0 / days) - 1) * 100;
    }

    private static decimal CalculateVolatility(List<MarketData> data)
    {
        if (data.Count < 2) return 0;

        var returns = new List<double>();
        for (int i = 1; i < data.Count; i++)
        {
            var ret = (double)((data[i].Close - data[i - 1].Close) / data[i - 1].Close);
            returns.Add(ret);
        }

        var volatility = Math.Sqrt(returns.Variance() * 252); // Annualized
        return (decimal)volatility * 100;
    }

    private static decimal CalculateSharpeRatio(List<MarketData> data)
    {
        if (data.Count < 2) return 0;

        var annualizedReturn = CalculateAnnualizedReturn(data);
        var volatility = CalculateVolatility(data);
        var riskFreeRate = 2.0m; // Assume 2% risk-free rate

        return volatility > 0 ? (annualizedReturn - riskFreeRate) / volatility : 0;
    }

    private static decimal CalculateSortinoRatio(List<MarketData> data)
    {
        if (data.Count < 2) return 0;

        var returns = new List<double>();
        for (int i = 1; i < data.Count; i++)
        {
            var ret = (double)((data[i].Close - data[i - 1].Close) / data[i - 1].Close);
            returns.Add(ret);
        }

        var downsideReturns = returns.Where(r => r < 0).ToList();
        if (downsideReturns.Count == 0) return CalculateSharpeRatio(data);

        var downsideDeviation = Math.Sqrt(downsideReturns.Select(r => r * r).Average()) * Math.Sqrt(252);
        var annualizedReturn = CalculateAnnualizedReturn(data);
        var riskFreeRate = 2.0m;

        return downsideDeviation > 0 ? (annualizedReturn - riskFreeRate) / (decimal)(downsideDeviation * 100) : 0;
    }

    private static decimal CalculateMaxDrawdown(List<MarketData> data)
    {
        if (data.Count < 2) return 0;

        var peak = data[0].Close;
        var maxDrawdown = 0m;

        foreach (var candle in data)
        {
            if (candle.Close > peak)
            {
                peak = candle.Close;
            }

            var drawdown = ((peak - candle.Close) / peak) * 100;
            if (drawdown > maxDrawdown)
            {
                maxDrawdown = drawdown;
            }
        }

        return maxDrawdown;
    }

    private static decimal CalculatePriceChange(List<MarketData> data)
    {
        if (data.Count < 2) return 0;
        return CalculateTotalReturn(data);
    }

    private static Dictionary<string, object> BuildComparisonTable(Dictionary<string, Dictionary<string, decimal>> assetMetrics)
    {
        return assetMetrics.ToDictionary(
            kvp => kvp.Key,
            kvp => (object)kvp.Value.ToDictionary(
                m => m.Key,
                m => (object)Math.Round(m.Value, 2)
            )
        );
    }

    private static Dictionary<string, object> CalculateRankings(Dictionary<string, Dictionary<string, decimal>> assetMetrics)
    {
        var rankings = new Dictionary<string, object>();

        // Rank by total return
        var returnRanking = assetMetrics
            .Where(a => a.Value.ContainsKey("total_return"))
            .OrderByDescending(a => a.Value["total_return"])
            .Select((a, i) => new { Symbol = a.Key, Rank = i + 1, Value = a.Value["total_return"] })
            .ToList();

        rankings["by_return"] = returnRanking;

        // Rank by Sharpe ratio
        var sharpeRanking = assetMetrics
            .Where(a => a.Value.ContainsKey("sharpe_ratio"))
            .OrderByDescending(a => a.Value["sharpe_ratio"])
            .Select((a, i) => new { Symbol = a.Key, Rank = i + 1, Value = a.Value["sharpe_ratio"] })
            .ToList();

        rankings["by_sharpe"] = sharpeRanking;

        // Rank by lowest volatility
        var volatilityRanking = assetMetrics
            .Where(a => a.Value.ContainsKey("volatility"))
            .OrderBy(a => a.Value["volatility"])
            .Select((a, i) => new { Symbol = a.Key, Rank = i + 1, Value = a.Value["volatility"] })
            .ToList();

        rankings["by_low_volatility"] = volatilityRanking;

        return rankings;
    }

    private static Dictionary<string, object> IdentifyBestPerformers(Dictionary<string, Dictionary<string, decimal>> assetMetrics)
    {
        var bestReturnKvp = assetMetrics
            .Where(a => a.Value.ContainsKey("total_return"))
            .OrderByDescending(a => a.Value["total_return"])
            .FirstOrDefault();

        var bestSharpeKvp = assetMetrics
            .Where(a => a.Value.ContainsKey("sharpe_ratio"))
            .OrderByDescending(a => a.Value["sharpe_ratio"])
            .FirstOrDefault();

        var lowestDrawdownKvp = assetMetrics
            .Where(a => a.Value.ContainsKey("max_drawdown"))
            .OrderBy(a => a.Value["max_drawdown"])
            .FirstOrDefault();

        var result = new Dictionary<string, object>();

        if (bestReturnKvp.Key != null)
        {
            result["highest_return"] = new Dictionary<string, object>
            {
                ["symbol"] = bestReturnKvp.Key,
                ["value"] = Math.Round(bestReturnKvp.Value["total_return"], 2)
            };
        }

        if (bestSharpeKvp.Key != null)
        {
            result["best_sharpe"] = new Dictionary<string, object>
            {
                ["symbol"] = bestSharpeKvp.Key,
                ["value"] = Math.Round(bestSharpeKvp.Value["sharpe_ratio"], 2)
            };
        }

        if (lowestDrawdownKvp.Key != null)
        {
            result["lowest_drawdown"] = new Dictionary<string, object>
            {
                ["symbol"] = lowestDrawdownKvp.Key,
                ["value"] = Math.Round(lowestDrawdownKvp.Value["max_drawdown"], 2)
            };
        }

        return result;
    }

    private static Dictionary<string, object> CalculateSimpleCorrelations(
        Dictionary<string, List<MarketData>> priceData,
        List<string> symbols)
    {
        // Build daily returns from close prices for each symbol
        var returnsData = new Dictionary<string, List<double>>();

        foreach (var symbol in symbols)
        {
            if (!priceData.TryGetValue(symbol, out var data) || data.Count < 2)
                continue;

            var returns = new List<double>();

            for (int i = 1; i < data.Count; i++)
            {
                if (data[i - 1].Close != 0)
                {
                    returns.Add((double)((data[i].Close - data[i - 1].Close) / data[i - 1].Close));
                }
            }

            if (returns.Count > 0)
            {
                returnsData[symbol] = returns;
            }
        }

        // Delegate to FinancialMathHelper for proper Pearson correlation calculation
        var corrMatrix = FinancialMathHelper.CalculateCorrelationMatrix(symbols, returnsData);

        // Convert Dictionary<string, Dictionary<string, double>> to Dictionary<string, Dictionary<string, decimal>>
        var correlations = new Dictionary<string, Dictionary<string, decimal>>();

        foreach (var (symbol1, row) in corrMatrix)
        {
            correlations[symbol1] = new Dictionary<string, decimal>();

            foreach (var (symbol2, value) in row)
            {
                correlations[symbol1][symbol2] = Math.Round((decimal)value, 4);
            }
        }

        return new Dictionary<string, object>
        {
            ["matrix"] = correlations,
            ["note"] = "Pearson correlations calculated from daily returns"
        };
    }

    private static Dictionary<string, object> CalculateSummaryStatistics(Dictionary<string, Dictionary<string, decimal>> assetMetrics)
    {
        if (assetMetrics.Count == 0) return new Dictionary<string, object>();

        var allReturns = assetMetrics
            .Where(a => a.Value.ContainsKey("total_return"))
            .Select(a => a.Value["total_return"])
            .ToList();

        var allVolatilities = assetMetrics
            .Where(a => a.Value.ContainsKey("volatility"))
            .Select(a => a.Value["volatility"])
            .ToList();

        return new Dictionary<string, object>
        {
            ["average_return"] = allReturns.Count > 0 ? Math.Round(allReturns.Average(), 2) : 0,
            ["median_return"] = allReturns.Count > 0 ? Math.Round(allReturns.OrderBy(r => r).ElementAt(allReturns.Count / 2), 2) : 0,
            ["average_volatility"] = allVolatilities.Count > 0 ? Math.Round(allVolatilities.Average(), 2) : 0,
            ["return_spread"] = allReturns.Count > 0 ? Math.Round(allReturns.Max() - allReturns.Min(), 2) : 0,
            ["assets_analyzed"] = assetMetrics.Count
        };
    }
}
