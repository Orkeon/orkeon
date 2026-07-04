using Orkeon.Trading.Tools.Domain.Models;
using Orkeon.Trading.Tools.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Data.Providers;

/// <summary>
/// Mock sentiment data provider for demo and testing purposes.
/// Generates deterministic sentiment data using the symbol hash as seed for reproducibility.
/// </summary>
public class MockSentimentDataProvider(ILogger? logger = null) : ISentimentDataProvider
{
    public string ProviderName => "mock_sentiment";

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<List<SentimentData>> GetNewsSentimentAsync(
        string symbol, int lookbackDays, CancellationToken cancellationToken = default)
    {
        var random = new Random(symbol.GetHashCode() ^ "news".GetHashCode());
        var sentimentData = new List<SentimentData>();

        for (int i = 0; i < lookbackDays * 3; i++)
        {
            var daysAgo = random.Next(0, lookbackDays);
            var sentiment = (decimal)(random.NextDouble() * 2 - 1);

            sentimentData.Add(new SentimentData
            {
                Symbol = symbol,
                Timestamp = DateTime.UtcNow.AddDays(-daysAgo).AddHours(-random.Next(0, 24)),
                Source = "news",
                OverallSentiment = Math.Round(sentiment, 2),
                NewsSentiment = Math.Round(sentiment, 2),
                MentionCount = random.Next(1, 20),
                PositiveMentions = sentiment > 0 ? random.Next(1, 15) : random.Next(0, 5),
                NegativeMentions = sentiment < 0 ? random.Next(1, 15) : random.Next(0, 5),
                NeutralMentions = random.Next(1, 10),
                ConfidenceScore = (decimal)(random.NextDouble() * 0.4 + 0.6),
                ConfidenceLevel = sentiment switch
                {
                    > 0.5m => "HIGH",
                    > 0.2m or < -0.2m => "MEDIUM",
                    _ => "LOW"
                },
                Metadata = new Dictionary<string, object>
                {
                    ["simulated"] = true,
                    ["article_count"] = random.Next(1, 10)
                }
            });
        }

        logger?.LogInformation("Generated {Count} mock news sentiment entries for {Symbol}", sentimentData.Count, symbol);
        return Task.FromResult(sentimentData);
    }

    public Task<List<SentimentData>> GetSocialMediaSentimentAsync(
        string symbol, int lookbackDays, CancellationToken cancellationToken = default)
    {
        var random = new Random(symbol.GetHashCode() ^ "social".GetHashCode());
        var sentimentData = new List<SentimentData>();

        for (int i = 0; i < lookbackDays * 5; i++)
        {
            var daysAgo = random.Next(0, lookbackDays);
            var sentiment = (decimal)(random.NextDouble() * 2 - 1);

            sentimentData.Add(new SentimentData
            {
                Symbol = symbol,
                Timestamp = DateTime.UtcNow.AddDays(-daysAgo).AddHours(-random.Next(0, 24)),
                Source = "social_media",
                OverallSentiment = Math.Round(sentiment, 2),
                SocialMediaSentiment = Math.Round(sentiment, 2),
                MentionCount = random.Next(10, 1000),
                PositiveMentions = sentiment > 0 ? random.Next(10, 500) : random.Next(5, 100),
                NegativeMentions = sentiment < 0 ? random.Next(10, 500) : random.Next(5, 100),
                NeutralMentions = random.Next(10, 300),
                ConfidenceScore = (decimal)(random.NextDouble() * 0.3 + 0.5),
                ConfidenceLevel = "MEDIUM",
                Metadata = new Dictionary<string, object>
                {
                    ["simulated"] = true,
                    ["platform"] = random.Next(0, 2) == 0 ? "twitter" : "reddit",
                    ["engagement_score"] = random.Next(100, 10000)
                }
            });
        }

        logger?.LogInformation("Generated {Count} mock social sentiment entries for {Symbol}", sentimentData.Count, symbol);
        return Task.FromResult(sentimentData);
    }

    public Task<List<SentimentData>> GetAnalystSentimentAsync(
        string symbol, int lookbackDays, CancellationToken cancellationToken = default)
    {
        var random = new Random(symbol.GetHashCode() ^ "analyst".GetHashCode());
        var sentimentData = new List<SentimentData>();
        var ratingCount = Math.Max(1, lookbackDays / 7);

        for (int i = 0; i < ratingCount; i++)
        {
            var daysAgo = random.Next(0, lookbackDays);
            var rating = random.Next(1, 6);
            var sentiment = (rating - 3) / 2m;

            sentimentData.Add(new SentimentData
            {
                Symbol = symbol,
                Timestamp = DateTime.UtcNow.AddDays(-daysAgo),
                Source = "analyst",
                OverallSentiment = Math.Round(sentiment, 2),
                AnalystSentiment = Math.Round(sentiment, 2),
                MentionCount = 1,
                PositiveMentions = rating >= 4 ? 1 : 0,
                NegativeMentions = rating <= 2 ? 1 : 0,
                NeutralMentions = rating == 3 ? 1 : 0,
                ConfidenceScore = (decimal)(random.NextDouble() * 0.2 + 0.8),
                ConfidenceLevel = "HIGH",
                Metadata = new Dictionary<string, object>
                {
                    ["simulated"] = true,
                    ["rating"] = rating,
                    ["rating_text"] = rating switch
                    {
                        5 => "Strong Buy",
                        4 => "Buy",
                        3 => "Hold",
                        2 => "Sell",
                        1 => "Strong Sell",
                        _ => "Unknown"
                    },
                    ["analyst_firm"] = $"Analyst Firm {random.Next(1, 20)}"
                }
            });
        }

        logger?.LogInformation("Generated {Count} mock analyst sentiment entries for {Symbol}", sentimentData.Count, symbol);
        return Task.FromResult(sentimentData);
    }
}
