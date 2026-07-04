using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Domain.Services;

/// <summary>
/// Abstraction for fundamental financial data providers (Alpha Vantage, IEX Cloud, Financial Modeling Prep, etc.).
/// Tools depend on this interface instead of concrete API clients, following Clean Architecture.
/// </summary>
public interface IFundamentalDataProvider
{
    /// <summary>
    /// Human-readable provider name for logging and metadata (e.g., "mock_fundamental", "alpha_vantage").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Lightweight connectivity probe. Returns true if the provider is reachable.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches fundamental data (valuation, profitability, financial health, growth) for a symbol.
    /// </summary>
    /// <param name="symbol">Stock symbol (e.g., "AAPL")</param>
    /// <param name="period">Reporting period (e.g., "ttm", "annual", "quarterly")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<FundamentalData> GetFundamentalDataAsync(
        string symbol,
        string period = "ttm",
        CancellationToken cancellationToken = default);
}
