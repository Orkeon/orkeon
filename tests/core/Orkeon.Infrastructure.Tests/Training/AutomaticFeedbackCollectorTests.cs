using Orkeon.Application.Evaluation;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;

namespace Orkeon.Infrastructure.Tests.Training;

public class AutomaticFeedbackCollectorTests
{
    [Fact]
    public async Task CollectFeedbackAsync_GeneratesSuggestionsForLowScores()
    {
        var collector = AutomaticFeedbackCollectorTestsFixture.CreateCollector();
        var scores = new List<EvaluationScore>
        {
            new("Accuracy", 0.3, "Low accuracy"),
            new("Fluency", 0.8, "Good fluency"),
            new("Coherence", 0.2, "Poor coherence")
        };

        var feedback = await collector.CollectFeedbackAsync(AgentId1, "some output", scores, TestContext.Current.CancellationToken);

        Assert.Equal(2, feedback.Suggestions.Count);
        Assert.Contains(feedback.Suggestions, s => s.Contains("Accuracy") && s.Contains("0.30"));
        Assert.Contains(feedback.Suggestions, s => s.Contains("Coherence") && s.Contains("0.20"));
    }

    [Fact]
    public async Task CollectFeedbackAsync_SummaryContainsAgentIdAndAverage()
    {
        var collector = AutomaticFeedbackCollectorTestsFixture.CreateCollector();
        var scores = new List<EvaluationScore>
        {
            new("Eval1", 0.6),
            new("Eval2", 0.8)
        };

        var feedback = await collector.CollectFeedbackAsync("agent-42", "output text", scores, TestContext.Current.CancellationToken);

        Assert.Contains("agent-42", feedback.Summary);
        Assert.Contains("0.70", feedback.Summary);
        Assert.Contains("2", feedback.Summary);
    }

    [Fact]
    public async Task CollectFeedbackAsync_NoSuggestionsWhenAllScoresAreHigh()
    {
        var collector = AutomaticFeedbackCollectorTestsFixture.CreateCollector();
        var scores = new List<EvaluationScore>
        {
            new("Quality", 0.9),
            new("Relevance", 0.85),
            new("Format", 0.7)
        };

        var feedback = await collector.CollectFeedbackAsync(AgentId1, "good output", scores, TestContext.Current.CancellationToken);

        Assert.Empty(feedback.Suggestions);
    }

    [Fact]
    public async Task CollectFeedbackAsync_AgentIdIsSetCorrectly()
    {
        var collector = AutomaticFeedbackCollectorTestsFixture.CreateCollector();
        var scores = new List<EvaluationScore> { new("Eval1", 0.5) };

        var feedback = await collector.CollectFeedbackAsync("my-agent", "output", scores, TestContext.Current.CancellationToken);

        Assert.Equal("my-agent", feedback.AgentId);
    }
}
