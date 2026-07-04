using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Domain.Services;

/// <summary>
/// Abstraction for real-time tick/trade data providers (Bloomberg, Reuters, Polygon, etc.).
/// Tools depend on this interface instead of concrete streaming connections, following Clean Architecture.
/// </summary>
public interface ITickDataProvider
{
    /// <summary>
    /// Human-readable provider name for logging and metadata (e.g., "mock_tick_data", "polygon_ws").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Lightweight connectivity probe. Returns true if the provider is reachable.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches tick-level trade data for a symbol.
    /// </summary>
    /// <param name="symbol">Stock symbol (e.g., "AAPL")</param>
    /// <param name="durationSeconds">Duration of tick data to capture</param>
    /// <param name="maxTicks">Maximum number of ticks to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<List<TickData>> GetTickDataAsync(
        string symbol,
        int durationSeconds = 60,
        int maxTicks = 1000,
        CancellationToken cancellationToken = default);
}
