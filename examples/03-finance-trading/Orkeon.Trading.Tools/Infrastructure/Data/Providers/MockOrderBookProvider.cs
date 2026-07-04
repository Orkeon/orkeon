using Orkeon.Trading.Tools.Domain.Models;
using Orkeon.Trading.Tools.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Data.Providers;

/// <summary>
/// Mock order book provider for demo and testing purposes.
/// Generates deterministic order book data using the symbol hash as seed for reproducibility.
/// </summary>
public class MockOrderBookProvider(ILogger? logger = null) : IOrderBookProvider
{
    public string ProviderName => "mock_order_book";

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<OrderBookData> GetOrderBookAsync(
        string symbol, int depth = 10, CancellationToken cancellationToken = default)
    {
        var random = new Random(symbol.GetHashCode());
        var midPrice = 150.0m + (decimal)(random.NextDouble() * 50);
        var spread = midPrice * 0.0001m * (decimal)(random.NextDouble() * 5 + 1); // 1-5 bps

        var bids = new List<PriceLevel>();
        var asks = new List<PriceLevel>();

        // Generate bid levels
        var currentBid = midPrice - spread / 2;
        for (int i = 0; i < depth; i++)
        {
            var priceDecrement = midPrice * 0.0001m * (decimal)(random.NextDouble() * 2 + 0.5);
            currentBid -= priceDecrement;

            bids.Add(new PriceLevel
            {
                Price = Math.Round(currentBid, 2),
                Volume = (decimal)random.Next(100, 10000),
                OrderCount = random.Next(1, 50)
            });
        }

        // Generate ask levels
        var currentAsk = midPrice + spread / 2;
        for (int i = 0; i < depth; i++)
        {
            asks.Add(new PriceLevel
            {
                Price = Math.Round(currentAsk, 2),
                Volume = (decimal)random.Next(100, 10000),
                OrderCount = random.Next(1, 50)
            });

            var priceIncrement = midPrice * 0.0001m * (decimal)(random.NextDouble() * 2 + 0.5);
            currentAsk += priceIncrement;
        }

        var result = new OrderBookData
        {
            Symbol = symbol,
            Timestamp = DateTime.UtcNow,
            Bids = bids.OrderByDescending(b => b.Price).ToList(),
            Asks = asks.OrderBy(a => a.Price).ToList()
        };

        logger?.LogInformation("Generated mock order book for {Symbol} with {Depth} levels", symbol, depth);
        return Task.FromResult(result);
    }
}
