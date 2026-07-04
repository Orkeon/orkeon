using Orkeon.Trading.Tools.Domain.Models;
using Orkeon.Trading.Tools.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Risk.Providers;

/// <summary>
/// Mock factor model provider for demo and testing purposes.
/// Generates deterministic factor loadings using position symbol/asset class as seed for reproducibility.
/// </summary>
public class MockFactorModelProvider(ILogger? logger = null) : IFactorModelProvider
{
    public string ProviderName => "mock_factor_model";

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<Dictionary<string, decimal>> GetFactorLoadingsAsync(
        PortfolioPosition position, List<string> factors, CancellationToken cancellationToken = default)
    {
        var loadings = new Dictionary<string, decimal>();

        foreach (var factor in factors)
        {
            loadings[factor] = GetFactorLoading(position, factor);
        }

        logger?.LogInformation("Generated mock factor loadings for {Symbol} ({AssetClass}): {Factors}",
            position.Symbol, position.AssetClass, string.Join(", ", factors));

        return Task.FromResult(loadings);
    }

    private static decimal GetFactorLoading(PortfolioPosition position, string factor)
    {
        // Deterministic seed per symbol+factor combination
        var random = new Random((position.Symbol + factor).GetHashCode());

        return factor switch
        {
            "MARKET" => GetMarketBeta(position, random),
            "SIZE" => (decimal)(random.NextDouble() * 2 - 1),           // -1.0 to +1.0
            "VALUE" => (decimal)(random.NextDouble() * 1.5 - 0.5),      // -0.5 to +1.0
            "MOMENTUM" => (decimal)(random.NextDouble() * 2 - 1),       // -1.0 to +1.0
            "VOLATILITY" => (decimal)(random.NextDouble() * 0.8 - 0.4), // -0.4 to +0.4
            "QUALITY" => (decimal)(random.NextDouble() * 1.0),          // 0 to +1.0
            "LIQUIDITY" => GetLiquidityLoading(position),
            _ => 0m
        };
    }

    private static decimal GetMarketBeta(PortfolioPosition position, Random random)
    {
        return position.AssetClass.ToLower() switch
        {
            "equity" or "stock" => 1.0m + (decimal)(random.NextDouble() * 0.4 - 0.2), // 0.8 to 1.2
            "bond" or "fixed_income" => 0.2m,
            "commodity" => 0.8m,
            "crypto" => 1.5m,
            "reit" => 0.9m,
            _ => 0.5m
        };
    }

    private static decimal GetLiquidityLoading(PortfolioPosition position)
    {
        return position.AssetClass.ToLower() switch
        {
            "equity" or "stock" => -0.2m,
            "bond" => 0.3m,
            "crypto" => 0.4m,
            _ => 0.1m
        };
    }
}
