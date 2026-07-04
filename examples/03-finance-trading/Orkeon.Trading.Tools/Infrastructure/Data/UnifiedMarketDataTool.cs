using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Data.Contracts;
using Orkeon.Trading.Tools.Infrastructure.Data.Providers;
using Orkeon.Trading.Tools.Domain.Models;
using Orkeon.Trading.Tools.Domain.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Orkeon.Trading.Tools.Infrastructure.Data;

/// <summary>
/// Unified market data tool that uses an injected <see cref="IMarketDataProvider"/>
/// with automatic fallback to mock data when the provider is unavailable.
/// </summary>
public class UnifiedMarketDataTool(
    IMarketDataProvider marketDataProvider,
    ILogger<UnifiedMarketDataTool>? logger = null)
    : TradingToolBase<UnifiedMarketDataRequest, UnifiedMarketDataResponse>(logger)
{
    private readonly IMarketDataProvider _marketDataProvider = marketDataProvider;
    private readonly MockMarketDataProvider _mockFallback = new(logger);

    protected override string ToolId => "unified_market_data";

    protected override string? ValidateTypedRequest(UnifiedMarketDataRequest request)
    {
        if (!DateTime.TryParse(request.StartDate, out var startDate))
            return $"Invalid start_date format: {request.StartDate}. Use ISO 8601 format (YYYY-MM-DD).";

        if (!DateTime.TryParse(request.EndDate, out var endDate))
            return $"Invalid end_date format: {request.EndDate}. Use ISO 8601 format (YYYY-MM-DD).";

        if (endDate < startDate)
            return "end_date must be after start_date";

        return null;
    }

    protected override async Task<UnifiedMarketDataResponse> ExecuteTypedAsync(
        UnifiedMarketDataRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var startDate = DateTime.Parse(request.StartDate);
        var endDate = DateTime.Parse(request.EndDate);

        _logger?.LogInformation(
            "Fetching market data for {Symbol} from {StartDate} to {EndDate} with timeframe {Timeframe}",
            request.Symbol, startDate, endDate, request.Timeframe);

        // Try primary provider, then fall back to mock data
        MarketDataCollection? data = null;
        string dataSource = "";
        var sourcesTried = new List<string>();

        // Try injected provider
        if (await _marketDataProvider.IsAvailableAsync(cancellationToken))
        {
            try
            {
                data = await _marketDataProvider.GetHistoricalDataAsync(
                    request.Symbol, startDate, endDate,
                    request.Timeframe, request.IncludeVwap, cancellationToken);
                dataSource = _marketDataProvider.ProviderName;
                _logger?.LogInformation("Successfully fetched data from {Provider} for {Symbol}",
                    _marketDataProvider.ProviderName, request.Symbol);
            }
            catch (Exception ex)
            {
                sourcesTried.Add(_marketDataProvider.ProviderName);
                _logger?.LogDebug("{Provider} failed for {Symbol}: {Error}",
                    _marketDataProvider.ProviderName, request.Symbol, ex.Message);
            }
        }
        else
        {
            sourcesTried.Add($"{_marketDataProvider.ProviderName} (unavailable)");
        }

        // Fallback to mock data for demo purposes
        if (data == null)
        {
            _logger?.LogDebug("No data source available for {Symbol} (tried: {Sources}). Using mock data",
                request.Symbol, string.Join(", ", sourcesTried));
            data = await _mockFallback.GetHistoricalDataAsync(
                request.Symbol, startDate, endDate,
                request.Timeframe, request.IncludeVwap, cancellationToken);
            dataSource = _mockFallback.ProviderName;
        }

        stopwatch.Stop();

        // Calculate quality score based on completeness and consistency
        var qualityScore = CalculateQualityScore(data);

        _logger?.LogInformation(
            "Successfully fetched {Count} data points for {Symbol} from {Source} in {Latency}ms",
            data.TotalPoints, request.Symbol, dataSource, stopwatch.ElapsedMilliseconds);

        return new UnifiedMarketDataResponse
        {
            Symbol = request.Symbol,
            Data = data.Data,
            StartDate = data.StartDate,
            EndDate = data.EndDate,
            Timeframe = request.Timeframe,
            DataSource = dataSource,
            TotalPoints = data.TotalPoints,
            LatencyMs = stopwatch.ElapsedMilliseconds,
            QualityScore = qualityScore,
            Metadata = new Dictionary<string, object>
            {
                ["data_sources_tried"] = sourcesTried.Count > 0
                    ? sourcesTried.ToArray()
                    : new[] { dataSource },
                ["successful_source"] = dataSource,
                ["has_vwap"] = request.IncludeVwap
            }
        };
    }

    private static decimal CalculateQualityScore(MarketDataCollection data)
    {
        if (data.Data.Count == 0) return 0;

        decimal score = 100m;

        // Penalize for missing data points (assuming daily data)
        var expectedPoints = (data.EndDate - data.StartDate).Days + 1;
        var completenessRatio = (decimal)data.Data.Count / expectedPoints;
        score *= completenessRatio;

        // Penalize for missing VWAP
        var vwapCompleteness = data.Data.Count(d => d.VWAP.HasValue) / (decimal)data.Data.Count;
        score *= (0.8m + (0.2m * vwapCompleteness));

        // Penalize for data quality issues (zero volumes, invalid prices)
        var invalidCount = data.Data.Count(d =>
            d.Volume == 0 ||
            d.Open <= 0 || d.High <= 0 || d.Low <= 0 || d.Close <= 0 ||
            d.High < d.Low ||
            d.High < d.Open || d.High < d.Close ||
            d.Low > d.Open || d.Low > d.Close
        );

        var validityRatio = 1 - ((decimal)invalidCount / data.Data.Count);
        score *= validityRatio;

        return Math.Max(0, Math.Min(100, score));
    }
}
