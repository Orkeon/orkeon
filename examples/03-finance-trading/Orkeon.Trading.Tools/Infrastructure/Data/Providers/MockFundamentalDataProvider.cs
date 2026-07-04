using Orkeon.Trading.Tools.Domain.Models;
using Orkeon.Trading.Tools.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Data.Providers;

/// <summary>
/// Mock fundamental data provider for demo and testing purposes.
/// Generates deterministic fundamental data using the symbol hash as seed for reproducibility.
/// </summary>
public class MockFundamentalDataProvider(ILogger? logger = null) : IFundamentalDataProvider
{
    public string ProviderName => "mock_fundamental";

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<FundamentalData> GetFundamentalDataAsync(
        string symbol, string period = "ttm", CancellationToken cancellationToken = default)
    {
        var random = new Random(symbol.GetHashCode());

        var result = new FundamentalData
        {
            Symbol = symbol,
            ReportDate = DateTime.UtcNow.AddDays(-30),

            // Valuation Metrics
            MarketCap = 2500000000000m + (decimal)(random.NextDouble() * 500000000000),
            EnterpriseValue = 2400000000000m + (decimal)(random.NextDouble() * 500000000000),
            PERatio = 25m + (decimal)(random.NextDouble() * 15),
            PEGRatio = 1.5m + (decimal)(random.NextDouble() * 1),
            PriceToBook = 8m + (decimal)(random.NextDouble() * 10),
            PriceToSales = 6m + (decimal)(random.NextDouble() * 4),
            EVToEBITDA = 18m + (decimal)(random.NextDouble() * 8),

            // Profitability Metrics
            ROE = 25m + (decimal)(random.NextDouble() * 25),
            ROA = 15m + (decimal)(random.NextDouble() * 15),
            ROI = 18m + (decimal)(random.NextDouble() * 18),
            NetMargin = 20m + (decimal)(random.NextDouble() * 15),
            OperatingMargin = 25m + (decimal)(random.NextDouble() * 15),
            GrossMargin = 40m + (decimal)(random.NextDouble() * 20),

            // Financial Health
            CurrentRatio = 1.2m + (decimal)(random.NextDouble() * 1),
            QuickRatio = 1.0m + (decimal)(random.NextDouble() * 0.8),
            DebtToEquity = 0.5m + (decimal)(random.NextDouble() * 1),
            InterestCoverage = 10m + (decimal)(random.NextDouble() * 20),

            // Growth Metrics
            RevenueGrowthYoY = 10m + (decimal)(random.NextDouble() * 20),
            EarningsGrowthYoY = 15m + (decimal)(random.NextDouble() * 25),
            DividendYield = 0.5m + (decimal)(random.NextDouble() * 2),

            AdditionalMetrics = new Dictionary<string, object>
            {
                ["period"] = period,
                ["currency"] = "USD",
                ["simulated"] = true
            }
        };

        logger?.LogInformation("Generated mock fundamental data for {Symbol} ({Period})", symbol, period);
        return Task.FromResult(result);
    }
}
