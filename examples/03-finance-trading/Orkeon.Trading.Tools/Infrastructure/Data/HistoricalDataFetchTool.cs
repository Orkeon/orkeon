using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Data.Contracts;
using Orkeon.Trading.Tools.Domain.Models;
using Orkeon.Trading.Tools.Domain.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Orkeon.Trading.Tools.Infrastructure.Data;

/// <summary>
/// Efficient historical data fetching tool with bulk parallel retrieval.
/// Delegates actual data fetching to an injected <see cref="IMarketDataProvider"/>.
/// </summary>
public class HistoricalDataFetchTool(
    IMarketDataProvider marketDataProvider,
    ILogger<HistoricalDataFetchTool>? logger = null)
    : TradingToolBase<HistoricalDataFetchRequest, HistoricalDataFetchResponse>(logger)
{
    private readonly IMarketDataProvider _marketDataProvider = marketDataProvider;

    protected override string ToolId => "historical_data_fetch";

    protected override string? ValidateTypedRequest(HistoricalDataFetchRequest request)
    {
        if (request.Symbols.Count == 0)
            return "At least one symbol is required";

        if (!DateTime.TryParse(request.StartDate, out _))
            return $"Invalid start_date format: {request.StartDate}";

        if (!DateTime.TryParse(request.EndDate, out _))
            return $"Invalid end_date format: {request.EndDate}";

        return null;
    }

    protected override async Task<HistoricalDataFetchResponse> ExecuteTypedAsync(
        HistoricalDataFetchRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var startDate = DateTime.Parse(request.StartDate);
        var endDate = DateTime.Parse(request.EndDate);

        // Deduplicate symbols to prevent 'duplicate key' errors in dictionaries
        var symbols = request.Symbols.Distinct().ToList();

        _logger?.LogInformation(
            "Fetching historical data for {Count} symbols from {StartDate} to {EndDate} via {Provider}",
            symbols.Count, startDate, endDate, _marketDataProvider.ProviderName);

        // Fetch data for all symbols in parallel
        var dataDict = new Dictionary<string, MarketDataCollection>();
        int cacheHits = 0;
        int cacheMisses = 0;
        var errors = new List<string>();

        var tasks = symbols.Select(async symbol =>
        {
            try
            {
                // TODO: Check Redis cache first if useCache is true
                var data = await _marketDataProvider.GetHistoricalDataAsync(
                    symbol, startDate, endDate, request.Timeframe, cancellationToken: cancellationToken);

                lock (dataDict)
                {
                    dataDict[symbol] = data;
                    cacheMisses++;
                }

                // TODO: Store in Redis cache if useCache is true
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("Failed to fetch data for {Symbol}: {Error}", symbol, ex.Message);
                lock (errors)
                {
                    errors.Add($"{symbol}: {ex.Message}");
                }
            }
        });

        await Task.WhenAll(tasks);

        stopwatch.Stop();

        if (dataDict.Count == 0)
        {
            throw new InvalidOperationException(
                $"Failed to fetch data for all symbols. Errors: {string.Join("; ", errors)}");
        }

        _logger?.LogInformation(
            "Successfully fetched historical data for {Successful}/{Total} symbols in {Latency}ms",
            dataDict.Count, symbols.Count, stopwatch.ElapsedMilliseconds);

        return new HistoricalDataFetchResponse
        {
            Data = dataDict,
            TotalSymbols = symbols.Count,
            SuccessfulSymbols = dataDict.Count,
            FailedSymbols = symbols.Count - dataDict.Count,
            CacheHits = cacheHits,
            CacheMisses = cacheMisses,
            TotalLatencyMs = stopwatch.ElapsedMilliseconds,
            AverageLatencyPerSymbolMs = symbols.Count > 0 ? stopwatch.ElapsedMilliseconds / (double)symbols.Count : 0,
            Timeframe = request.Timeframe,
            Errors = errors
        };
    }

    private static int CalculateAdaptiveTTL(DateTime dataDate)
    {
        var age = (DateTime.UtcNow - dataDate).TotalDays;

        // Adaptive TTL based on data age:
        // - Recent data (< 7 days): 1 hour
        // - Recent data (< 30 days): 4 hours
        // - Older data (< 90 days): 24 hours
        // - Historical data (> 90 days): 7 days

        return age switch
        {
            < 7 => 1,
            < 30 => 4,
            < 90 => 24,
            _ => 168 // 7 days
        };
    }
}
