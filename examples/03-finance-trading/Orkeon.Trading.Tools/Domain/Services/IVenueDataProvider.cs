using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Domain.Services;

/// <summary>
/// Abstraction for trading venue / exchange data providers (FIX gateways, exchange APIs, etc.).
/// Tools depend on this interface instead of concrete venue connections, following Clean Architecture.
/// </summary>
public interface IVenueDataProvider
{
    /// <summary>
    /// Human-readable provider name for logging and metadata (e.g., "mock_venue_data", "fix_gateway").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Lightweight connectivity probe. Returns true if the provider is reachable.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches market data (bid/ask, fees, latency) from specified trading venues for a symbol.
    /// </summary>
    /// <param name="symbol">Stock symbol (e.g., "AAPL")</param>
    /// <param name="venues">List of venue identifiers (e.g., "NYSE", "NASDAQ", "IEX")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<List<VenueMarketData>> GetVenueDataAsync(
        string symbol,
        List<string> venues,
        CancellationToken cancellationToken = default);
}
