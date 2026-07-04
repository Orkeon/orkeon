using Orkeon.Trading.Tools.Domain.Models;
using Orkeon.Trading.Tools.Domain.Services;
using Microsoft.Extensions.Logging;
using YahooFinanceApi;

namespace Orkeon.Trading.Tools.Infrastructure.Data.Providers;

/// <summary>
/// Market data provider backed by Yahoo Finance API.
/// Handles connectivity probing, historical data fetching, and Candle → MarketData conversion.
/// Injected into tools via <see cref="IMarketDataProvider"/> — tools never reference YahooFinanceApi directly.
/// </summary>
public class YahooFinanceMarketDataProvider : IMarketDataProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger? _logger;

    /// <summary>
    /// Cached availability: null = not tested, true = available, false = unavailable.
    /// Tested once on first call and reused for all subsequent calls in the same session.
    /// </summary>
    private bool? _isAvailable;

    public string ProviderName => "yahoo_finance";

    public YahooFinanceMarketDataProvider(HttpClient httpClient, ILogger<YahooFinanceMarketDataProvider>? logger = null)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _logger = logger;
    }

    /// <summary>
    /// Lightweight connectivity probe using HttpClient.
    /// Result is cached for the session to avoid repeated probes.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (_isAvailable.HasValue)
            return _isAvailable.Value;

        try
        {
            using var probeClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var response = await probeClient.GetAsync(
                "https://query1.finance.yahoo.com/v8/finance/chart/SPY?range=1d&interval=1d",
                cancellationToken);

            _isAvailable = response.IsSuccessStatusCode;

            if (_isAvailable.Value)
                _logger?.LogInformation("Yahoo Finance API is reachable");
            else
                _logger?.LogInformation("Yahoo Finance API returned {StatusCode}", response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger?.LogInformation("Yahoo Finance API unreachable: {Error}", ex.Message);
            _isAvailable = false;
        }

        return _isAvailable.Value;
    }

    /// <summary>
    /// Fetches historical OHLCV data from Yahoo Finance and converts to domain model.
    /// </summary>
    public async Task<MarketDataCollection> GetHistoricalDataAsync(
        string symbol,
        DateTime startDate,
        DateTime endDate,
        string timeframe = "1d",
        bool includeVwap = true,
        CancellationToken cancellationToken = default)
    {
        var securities = await Yahoo.GetHistoricalAsync(symbol, startDate, endDate, token: cancellationToken);

        var dataPoints = securities.Select(candle => new MarketData
        {
            Symbol = symbol,
            Timestamp = candle.DateTime,
            Source = ProviderName,
            Open = candle.Open,
            High = candle.High,
            Low = candle.Low,
            Close = candle.Close,
            Volume = candle.Volume,
            AdjustedClose = candle.AdjustedClose,
            VWAP = includeVwap ? CalculateVWAP(candle) : null,
            TradeCount = null,
            Metadata = new Dictionary<string, object>
            {
                ["provider"] = ProviderName,
                ["timeframe"] = timeframe
            }
        }).ToList();

        return new MarketDataCollection
        {
            Symbol = symbol,
            Data = dataPoints,
            StartDate = startDate,
            EndDate = endDate,
            TimeFrame = timeframe,
            DataSources = [ProviderName],
            Metadata = new Dictionary<string, object>
            {
                ["fetch_timestamp"] = DateTime.UtcNow,
                ["provider"] = ProviderName
            }
        };
    }

    /// <summary>
    /// Simple VWAP approximation: (High + Low + Close) / 3.
    /// More accurate VWAP would require tick data.
    /// </summary>
    private static decimal? CalculateVWAP(Candle candle)
    {
        if (candle.Volume == 0) return null;
        return (candle.High + candle.Low + candle.Close) / 3;
    }
}
