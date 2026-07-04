using Orkeon.Trading.Tools.Domain.Models;
using Orkeon.Trading.Tools.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Data.Providers;

/// <summary>
/// Mock tick data provider for demo and testing purposes.
/// Generates deterministic tick data using the symbol hash as seed for reproducibility.
/// </summary>
public class MockTickDataProvider(ILogger? logger = null) : ITickDataProvider
{
    public string ProviderName => "mock_tick_data";

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<List<TickData>> GetTickDataAsync(
        string symbol, int durationSeconds = 60, int maxTicks = 1000,
        CancellationToken cancellationToken = default)
    {
        var random = new Random(symbol.GetHashCode());
        var ticks = new List<TickData>();
        var basePrice = 150.0m + (decimal)(random.NextDouble() * 50);
        var startTime = DateTime.UtcNow;
        var tickCount = Math.Min(maxTicks, durationSeconds * 10);

        for (int i = 0; i < tickCount; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var priceChange = (decimal)(random.NextDouble() - 0.5) * 0.1m;
            var price = basePrice + priceChange;
            var volume = (decimal)random.Next(1, 1000);
            var side = random.NextDouble() > 0.5 ? "BUY" : "SELL";

            ticks.Add(new TickData
            {
                Symbol = symbol,
                Timestamp = startTime.AddMilliseconds(i * 100),
                Price = Math.Round(price, 2),
                Volume = volume,
                Side = side,
                ExchangeTradeId = $"MOCK{i:D8}",
                Metadata = new Dictionary<string, object>
                {
                    ["simulated"] = true
                }
            });

            basePrice = price;
        }

        logger?.LogInformation("Generated {Count} mock ticks for {Symbol}", ticks.Count, symbol);
        return Task.FromResult(ticks);
    }
}
