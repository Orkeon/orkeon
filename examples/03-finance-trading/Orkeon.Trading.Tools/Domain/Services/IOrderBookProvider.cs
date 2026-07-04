using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Domain.Services;

/// <summary>
/// Abstraction for order book / Level 2 data providers (exchange APIs, broker APIs, etc.).
/// Tools depend on this interface instead of concrete API clients, following Clean Architecture.
/// </summary>
public interface IOrderBookProvider
{
    /// <summary>
    /// Human-readable provider name for logging and metadata (e.g., "mock_order_book", "polygon_io").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Lightweight connectivity probe. Returns true if the provider is reachable.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches order book (bid/ask levels) for a symbol with specified depth.
    /// </summary>
    /// <param name="symbol">Stock symbol (e.g., "AAPL")</param>
    /// <param name="depth">Number of price levels to retrieve per side</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<OrderBookData> GetOrderBookAsync(
        string symbol,
        int depth = 10,
        CancellationToken cancellationToken = default);
}
