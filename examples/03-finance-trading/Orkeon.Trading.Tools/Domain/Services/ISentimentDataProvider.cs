using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Domain.Services;

/// <summary>
/// Abstraction for sentiment/alternative data providers (news APIs, social media APIs, analyst feeds, etc.).
/// Tools depend on this interface instead of concrete API clients, following Clean Architecture.
/// </summary>
public interface ISentimentDataProvider
{
    /// <summary>
    /// Human-readable provider name for logging and metadata (e.g., "mock_sentiment", "benzinga", "stocktwits").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Lightweight connectivity probe. Returns true if the provider is reachable.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches news sentiment data for a symbol over a lookback period.
    /// </summary>
    Task<List<SentimentData>> GetNewsSentimentAsync(
        string symbol,
        int lookbackDays,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches social media sentiment data for a symbol over a lookback period.
    /// </summary>
    Task<List<SentimentData>> GetSocialMediaSentimentAsync(
        string symbol,
        int lookbackDays,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches analyst sentiment/ratings data for a symbol.
    /// </summary>
    Task<List<SentimentData>> GetAnalystSentimentAsync(
        string symbol,
        int lookbackDays,
        CancellationToken cancellationToken = default);
}
