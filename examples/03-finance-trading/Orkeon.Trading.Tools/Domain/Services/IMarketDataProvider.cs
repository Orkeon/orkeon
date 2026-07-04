using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Domain.Services;

/// <summary>
/// Abstraction for market data providers (Yahoo Finance, Alpha Vantage, IEX Cloud, etc.).
/// Tools depend on this interface instead of concrete API clients, following Clean Architecture.
/// </summary>
public interface IMarketDataProvider
{
    /// <summary>
    /// Human-readable provider name for logging and metadata (e.g., "yahoo_finance", "mock_data_generator").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Lightweight connectivity probe. Returns true if the provider is reachable.
    /// Implementations should cache the result for the session to avoid repeated probes.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches historical OHLCV data for a symbol between start and end dates.
    /// Returns a <see cref="MarketDataCollection"/> with the data points, source tracking, and metadata.
    /// </summary>
    /// <param name="symbol">Stock/ETF symbol (e.g., "AAPL", "SPY")</param>
    /// <param name="startDate">Start date (inclusive)</param>
    /// <param name="endDate">End date (inclusive)</param>
    /// <param name="timeframe">Data timeframe (e.g., "1d", "1h")</param>
    /// <param name="includeVwap">Whether to calculate VWAP for each data point</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<MarketDataCollection> GetHistoricalDataAsync(
        string symbol,
        DateTime startDate,
        DateTime endDate,
        string timeframe = "1d",
        bool includeVwap = true,
        CancellationToken cancellationToken = default);
}
