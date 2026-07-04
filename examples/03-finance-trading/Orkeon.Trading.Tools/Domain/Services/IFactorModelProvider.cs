using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Domain.Services;

/// <summary>
/// Abstraction for factor model / risk model providers (Barra, Axioma, internal risk models, etc.).
/// Tools depend on this interface instead of concrete API clients, following Clean Architecture.
/// </summary>
public interface IFactorModelProvider
{
    /// <summary>
    /// Human-readable provider name for logging and metadata (e.g., "mock_factor_model", "barra_one").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Lightweight connectivity probe. Returns true if the provider is reachable.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches factor loadings (exposures) for a portfolio position across specified factors.
    /// Returns a dictionary of factor name → loading value (e.g., {"MARKET": 1.05, "SIZE": -0.3}).
    /// </summary>
    /// <param name="position">Portfolio position with symbol and asset class</param>
    /// <param name="factors">List of factor names to retrieve (e.g., "MARKET", "SIZE", "VALUE")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<Dictionary<string, decimal>> GetFactorLoadingsAsync(
        PortfolioPosition position,
        List<string> factors,
        CancellationToken cancellationToken = default);
}
