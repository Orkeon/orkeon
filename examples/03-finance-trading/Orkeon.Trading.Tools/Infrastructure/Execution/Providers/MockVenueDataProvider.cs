using Orkeon.Trading.Tools.Domain.Models;
using Orkeon.Trading.Tools.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Execution.Providers;

/// <summary>
/// Mock venue data provider for demo and testing purposes.
/// Generates deterministic venue market data using symbol+venue hash as seed for reproducibility.
/// </summary>
public class MockVenueDataProvider(ILogger? logger = null) : IVenueDataProvider
{
    public string ProviderName => "mock_venue_data";

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<List<VenueMarketData>> GetVenueDataAsync(
        string symbol, List<string> venues, CancellationToken cancellationToken = default)
    {
        var venueDataList = new List<VenueMarketData>();

        foreach (var venue in venues)
        {
            var random = new Random((symbol + venue).GetHashCode());

            venueDataList.Add(new VenueMarketData
            {
                Venue = venue,
                BidPrice = 100.00m + (decimal)(random.NextDouble() * 0.10 - 0.05),
                AskPrice = 100.10m + (decimal)(random.NextDouble() * 0.10 - 0.05),
                BidSize = 1000 + random.Next(0, 10000),
                AskSize = 1000 + random.Next(0, 10000),
                TakerFee = GetTakerFee(venue),
                MakerRebate = GetMakerRebate(venue),
                AverageLatencyMs = GetAverageLatency(venue),
                HistoricalFillRate = 0.85 + random.NextDouble() * 0.15
            });
        }

        logger?.LogInformation("Generated mock venue data for {Symbol} across {Count} venues", symbol, venues.Count);
        return Task.FromResult(venueDataList);
    }

    private static decimal GetTakerFee(string venue) => venue switch
    {
        "NYSE" => 0.003m,
        "NASDAQ" => 0.003m,
        "IEX" => 0.0009m,
        "BATS" => 0.003m,
        _ => 0.003m
    };

    private static decimal GetMakerRebate(string venue) => venue switch
    {
        "NYSE" => 0.0020m,
        "NASDAQ" => 0.0020m,
        "IEX" => 0m,
        "BATS" => 0.0020m,
        _ => 0.0020m
    };

    private static int GetAverageLatency(string venue) => venue switch
    {
        "IEX" => 2,
        "NASDAQ" => 1,
        "NYSE" => 1,
        _ => 1
    };
}
