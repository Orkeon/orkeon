using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Analysis.Contracts;
using Orkeon.Trading.Tools.Domain.Services;
using Orkeon.Trading.Tools.Domain.Models;
using Microsoft.Extensions.Logging;
using MathNet.Numerics.Statistics;

namespace Orkeon.Trading.Tools.Infrastructure.Analysis;

/// <summary>
/// Correlation analysis tool for analyzing relationships between assets.
/// Calculates correlation matrices, beta, and portfolio diversification metrics.
/// </summary>
public class CorrelationAnalysisTool(ILogger<CorrelationAnalysisTool>? logger = null)
    : TradingToolBase<CorrelationAnalysisRequest, CorrelationAnalysisResponse>(logger)
{
    protected override string ToolId => "correlation_analysis";

    protected override async Task<CorrelationAnalysisResponse> ExecuteTypedAsync(
        CorrelationAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        // Deduplicate symbols to prevent 'duplicate key' errors in dictionaries
        var symbols = request.Symbols.Distinct().ToList();
        var lookbackDays = request.LookbackDays;

        _logger?.LogInformation("Analyzing correlations for {Count} symbols over {Days} days", symbols.Count, lookbackDays);

        var analysis = await Task.Run(() => AnalyzeCorrelations(symbols, request.PriceData, request.BenchmarkSymbol, lookbackDays), cancellationToken);

        _logger?.LogInformation("Completed correlation analysis for {Count} symbols", symbols.Count);

        return analysis;
    }

    private static CorrelationAnalysisResponse AnalyzeCorrelations(
        List<string> symbols,
        Dictionary<string, List<MarketData>> priceData,
        string benchmarkSymbol,
        int lookbackDays)
    {
        // Calculate returns for each symbol
        var returnsData = new Dictionary<string, List<double>>();
        foreach (var symbol in symbols)
        {
            if (!priceData.ContainsKey(symbol)) continue;

            var prices = priceData[symbol].TakeLast(lookbackDays).Select(p => (double)p.Close).ToArray();
            var returns = CalculateReturns(prices);
            returnsData[symbol] = returns;
        }

        // Calculate correlation matrix (helper returns double; convert to decimal with rounding)
        var rawCorrelationMatrix = FinancialMathHelper.CalculateCorrelationMatrix(symbols, returnsData);
        var correlationMatrix = rawCorrelationMatrix.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToDictionary(
                inner => inner.Key,
                inner => (decimal)Math.Round(inner.Value, 3)));

        // Calculate beta values if benchmark data exists
        var betaValues = new Dictionary<string, decimal>();
        if (priceData.TryGetValue(benchmarkSymbol, out var benchmarkSeries))
        {
            var benchmarkPrices = benchmarkSeries.TakeLast(lookbackDays).Select(p => (double)p.Close).ToArray();
            var benchmarkReturns = CalculateReturns(benchmarkPrices);

            foreach (var symbol in symbols)
            {
                if (symbol == benchmarkSymbol || !returnsData.ContainsKey(symbol)) continue;

                var beta = CalculateBeta(returnsData[symbol], benchmarkReturns);
                betaValues[symbol] = (decimal)beta;
            }
        }

        // Calculate diversification metrics
        var avgCorrelation = CalculateAverageCorrelation(correlationMatrix);
        var diversificationScore = (1 - avgCorrelation) * 100; // 0-100 scale

        return new CorrelationAnalysisResponse
        {
            Timestamp = DateTime.UtcNow,
            Symbols = symbols,
            LookbackDays = lookbackDays,
            CorrelationMatrix = correlationMatrix,
            BetaValues = betaValues,
            BenchmarkSymbol = benchmarkSymbol,
            DiversificationMetrics = new Dictionary<string, object>
            {
                ["average_correlation"] = Math.Round(avgCorrelation, 3),
                ["diversification_score"] = Math.Round(diversificationScore, 2),
                ["diversification_rating"] = diversificationScore > 70 ? "EXCELLENT" :
                                           diversificationScore > 50 ? "GOOD" :
                                           diversificationScore > 30 ? "MODERATE" : "POOR"
            },
            CorrelationClusters = IdentifyCorrelationClusters(correlationMatrix, 0.7)
        };
    }

    private static List<double> CalculateReturns(double[] prices)
    {
        var returns = new List<double>();
        for (int i = 1; i < prices.Length; i++)
        {
            var ret = (prices[i] - prices[i - 1]) / prices[i - 1];
            returns.Add(ret);
        }
        return returns;
    }

    private static double CalculateBeta(List<double> assetReturns, List<double> benchmarkReturns)
    {
        if (assetReturns.Count != benchmarkReturns.Count || assetReturns.Count < 2)
            return 1.0;

        var covariance = assetReturns.Zip(benchmarkReturns, (a, b) => a * b).Average() -
                        (assetReturns.Average() * benchmarkReturns.Average());

        var benchmarkVariance = benchmarkReturns.Variance();

        return benchmarkVariance > 0 ? covariance / benchmarkVariance : 1.0;
    }

    private static double CalculateAverageCorrelation(Dictionary<string, Dictionary<string, decimal>> matrix)
    {
        var correlations = new List<double>();

        foreach (var row in matrix)
        {
            foreach (var col in row.Value)
            {
                if (row.Key != col.Key) // Exclude diagonal (self-correlation)
                {
                    correlations.Add((double)col.Value);
                }
            }
        }

        return correlations.Count > 0 ? correlations.Average() : 0;
    }

    private static List<List<string>> IdentifyCorrelationClusters(
        Dictionary<string, Dictionary<string, decimal>> matrix,
        double threshold)
    {
        var clusters = new List<List<string>>();
        var processed = new HashSet<string>();

        foreach (var symbol in matrix.Keys)
        {
            if (processed.Contains(symbol)) continue;

            var cluster = new List<string> { symbol };
            processed.Add(symbol);

            foreach (var otherSymbol in matrix.Keys)
            {
                if (symbol == otherSymbol || processed.Contains(otherSymbol)) continue;

                if (matrix[symbol].TryGetValue(otherSymbol, out var corrValue) &&
                    Math.Abs(corrValue) >= (decimal)threshold)
                {
                    cluster.Add(otherSymbol);
                    processed.Add(otherSymbol);
                }
            }

            if (cluster.Count > 1)
            {
                clusters.Add(cluster);
            }
        }

        return clusters;
    }
}
