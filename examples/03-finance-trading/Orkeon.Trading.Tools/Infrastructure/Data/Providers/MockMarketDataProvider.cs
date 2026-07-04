using Orkeon.Trading.Tools.Domain.Models;
using Orkeon.Trading.Tools.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Data.Providers;

/// <summary>
/// Mock market data provider for demo and testing purposes.
/// Generates deterministic OHLCV data using the symbol hash as seed for reproducibility.
/// Always available — used as fallback when real providers are unreachable.
/// </summary>
public class MockMarketDataProvider(ILogger? logger = null) : IMarketDataProvider
{
    public string ProviderName => "mock_data_generator";

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<MarketDataCollection> GetHistoricalDataAsync(
        string symbol,
        DateTime startDate,
        DateTime endDate,
        string timeframe = "1d",
        bool includeVwap = true,
        CancellationToken cancellationToken = default)
    {
        var dataPoints = new List<MarketData>();
        var currentDate = startDate;
        var basePrice = symbol switch
        {
            "SPY" => 450m,
            "QQQ" => 380m,
            "IWM" => 190m,
            "TLT" => 95m,
            "GLD" => 180m,
            _ => 100m
        };

        var random = new Random(symbol.GetHashCode()); // Deterministic for same symbol
        var currentPrice = basePrice;

        while (currentDate <= endDate)
        {
            // Skip weekends
            if (currentDate.DayOfWeek != DayOfWeek.Saturday &&
                currentDate.DayOfWeek != DayOfWeek.Sunday)
            {
                // Generate realistic daily movement
                var dailyReturn = (decimal)(random.NextDouble() * 0.04 - 0.02); // -2% to +2%
                var open = currentPrice * (1 + dailyReturn);
                var high = open * (1 + (decimal)random.NextDouble() * 0.015m);
                var low = open * (1 - (decimal)random.NextDouble() * 0.015m);
                var close = low + (high - low) * (decimal)random.NextDouble();

                dataPoints.Add(new MarketData
                {
                    Symbol = symbol,
                    Timestamp = currentDate.Date.AddHours(16),
                    Source = ProviderName,
                    Open = Math.Round(open, 2),
                    High = Math.Round(high, 2),
                    Low = Math.Round(low, 2),
                    Close = Math.Round(close, 2),
                    Volume = (decimal)(50_000_000 * (0.8 + random.NextDouble() * 0.4)),
                    AdjustedClose = Math.Round(close, 2),
                    VWAP = includeVwap ? Math.Round((high + low + close) / 3, 2) : null,
                    Metadata = new Dictionary<string, object>
                    {
                        ["is_mock"] = true,
                        ["note"] = "Generated for demo purposes - not real market data"
                    }
                });

                currentPrice = close;
            }

            currentDate = currentDate.AddDays(1);
        }

        logger?.LogInformation("Generated {Count} mock data points for {Symbol}", dataPoints.Count, symbol);

        var result = new MarketDataCollection
        {
            Symbol = symbol,
            Data = dataPoints,
            StartDate = startDate,
            EndDate = endDate,
            TimeFrame = timeframe,
            DataSources = [ProviderName],
            Metadata = new Dictionary<string, object>
            {
                ["is_mock"] = true,
                ["note"] = "This is simulated data for demonstration purposes"
            }
        };

        return Task.FromResult(result);
    }
}
