using Orkeon.Trading.Tools.Domain.Models;
using Orkeon.Trading.Tools.Domain.Services;
using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Data.Contracts;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Data;

/// <summary>
/// Alternative data tool for sentiment analysis and non-traditional market signals.
/// Aggregates news sentiment, social media sentiment, and analyst ratings
/// via an injected <see cref="ISentimentDataProvider"/>.
/// </summary>
public class AlternativeDataTool(
    ISentimentDataProvider sentimentProvider,
    ILogger<AlternativeDataTool>? logger = null)
    : TradingToolBase<AlternativeDataRequest, AlternativeDataResponse>(logger)
{
    private readonly ISentimentDataProvider _sentimentProvider = sentimentProvider;

    protected override string ToolId => "alternative_data";

    protected override string? ValidateTypedRequest(AlternativeDataRequest request)
    {
        if (request.LookbackDays > 90)
            return "lookback_days must not exceed 90";
        return null;
    }

    protected override async Task<AlternativeDataResponse> ExecuteTypedAsync(
        AlternativeDataRequest request,
        CancellationToken cancellationToken)
    {
        var lookbackDays = request.LookbackDays;

        _logger?.LogInformation(
            "Fetching alternative data for {Symbol} ({LookbackDays} days, types: {Types}) via {Provider}",
            request.Symbol, lookbackDays, string.Join(", ", request.DataTypes), _sentimentProvider.ProviderName);

        var sentimentDataList = new List<SentimentData>();
        var includeAll = request.DataTypes.Contains("all");

        if (includeAll || request.DataTypes.Contains("news"))
        {
            var newsData = await _sentimentProvider.GetNewsSentimentAsync(request.Symbol, lookbackDays, cancellationToken);
            sentimentDataList.AddRange(newsData);
        }

        if (includeAll || request.DataTypes.Contains("social"))
        {
            var socialData = await _sentimentProvider.GetSocialMediaSentimentAsync(request.Symbol, lookbackDays, cancellationToken);
            sentimentDataList.AddRange(socialData);
        }

        if (includeAll || request.DataTypes.Contains("analyst"))
        {
            var analystData = await _sentimentProvider.GetAnalystSentimentAsync(request.Symbol, lookbackDays, cancellationToken);
            sentimentDataList.AddRange(analystData);
        }

        Dictionary<string, object>? aggregatedSentiment = null;
        if (request.Aggregate && sentimentDataList.Count > 0)
        {
            aggregatedSentiment = AggregateSentiment(sentimentDataList);
        }

        _logger?.LogInformation("Fetched {Count} alternative data signals for {Symbol}",
            sentimentDataList.Count, request.Symbol);

        return new AlternativeDataResponse
        {
            Symbol = request.Symbol,
            Timestamp = DateTime.UtcNow,
            LookbackDays = lookbackDays,
            DataSources = request.DataTypes,
            SentimentData = sentimentDataList,
            TotalSignals = sentimentDataList.Count,
            AggregatedSentiment = aggregatedSentiment ?? new Dictionary<string, object>(),
            Metadata = new Dictionary<string, object>
            {
                ["news_count"] = sentimentDataList.Count(s => s.Source == "news"),
                ["social_count"] = sentimentDataList.Count(s => s.Source == "social_media"),
                ["analyst_count"] = sentimentDataList.Count(s => s.Source == "analyst")
            }
        };
    }

    private static Dictionary<string, object> AggregateSentiment(List<SentimentData> sentimentDataList)
    {
        if (sentimentDataList.Count == 0) return new Dictionary<string, object>();

        var now = DateTime.UtcNow;
        var weightedSentiments = sentimentDataList.Select(s =>
        {
            var ageHours = (now - s.Timestamp).TotalHours;
            var recencyWeight = Math.Exp(-ageHours / 48);
            var weight = (s.ConfidenceScore ?? 0m) * (decimal)recencyWeight;
            weight *= s.Source switch { "analyst" => 2.0m, "news" => 1.5m, _ => 1.0m };
            return new { s.OverallSentiment, Weight = weight };
        }).ToList();

        var totalWeight = weightedSentiments.Sum(w => w.Weight);
        var weightedAvgSentiment = totalWeight > 0
            ? weightedSentiments.Sum(w => w.OverallSentiment * w.Weight) / totalWeight : 0;

        var positive = sentimentDataList.Sum(s => s.PositiveMentions ?? 0);
        var negative = sentimentDataList.Sum(s => s.NegativeMentions ?? 0);
        var neutral = sentimentDataList.Sum(s => s.NeutralMentions ?? 0);
        var total = positive + negative + neutral;

        var sentimentStdDev = CalculateStandardDeviation(sentimentDataList.Select(s => (double)s.OverallSentiment).ToList());
        var agreement = Math.Max(0, 1 - sentimentStdDev);
        var overallConfidence = Math.Min(1.0m, (decimal)Math.Log10(sentimentDataList.Count + 1) * (decimal)agreement);

        return new Dictionary<string, object>
        {
            ["overall_sentiment"] = Math.Round(weightedAvgSentiment, 3),
            ["sentiment_direction"] = weightedAvgSentiment switch
            {
                > 0.3m => "BULLISH", > 0.1m => "SLIGHTLY_BULLISH",
                > -0.1m => "NEUTRAL", > -0.3m => "SLIGHTLY_BEARISH", _ => "BEARISH"
            },
            ["confidence"] = Math.Round(overallConfidence, 2),
            ["total_signals"] = sentimentDataList.Count,
            ["positive_ratio"] = total > 0 ? Math.Round((decimal)positive / total, 2) : 0,
            ["negative_ratio"] = total > 0 ? Math.Round((decimal)negative / total, 2) : 0,
            ["neutral_ratio"] = total > 0 ? Math.Round((decimal)neutral / total, 2) : 0,
            ["sentiment_by_source"] = new Dictionary<string, object>
            {
                ["news"] = CalculateSourceSentiment(sentimentDataList.Where(s => s.Source == "news").ToList()),
                ["social_media"] = CalculateSourceSentiment(sentimentDataList.Where(s => s.Source == "social_media").ToList()),
                ["analyst"] = CalculateSourceSentiment(sentimentDataList.Where(s => s.Source == "analyst").ToList())
            },
            ["sentiment_std_dev"] = Math.Round((decimal)sentimentStdDev, 3),
            ["agreement_score"] = Math.Round((decimal)agreement, 2)
        };
    }

    private static Dictionary<string, object> CalculateSourceSentiment(List<SentimentData> sourceData)
    {
        if (sourceData.Count == 0)
            return new Dictionary<string, object> { ["count"] = 0, ["sentiment"] = 0, ["confidence"] = 0 };

        return new Dictionary<string, object>
        {
            ["count"] = sourceData.Count,
            ["sentiment"] = Math.Round(sourceData.Average(s => s.OverallSentiment), 2),
            ["confidence"] = Math.Round(sourceData.Average(s => s.ConfidenceScore ?? 0m), 2)
        };
    }

    private static double CalculateStandardDeviation(List<double> values)
    {
        if (values.Count < 2) return 0;
        var avg = values.Average();
        var sumOfSquares = values.Sum(v => Math.Pow(v - avg, 2));
        return Math.Sqrt(sumOfSquares / values.Count);
    }
}
