using Orkeon.Application.Interfaces.Training;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;

namespace Orkeon.Infrastructure.Tests.Training;

public class TrainingOrchestratorTests
{
    [Fact]
    public async Task TrainAgentAsync_ReturnsResultWithScores()
    {
        var orchestrator = TrainingOrchestratorTestsFixture.CreateOrchestrator(evaluatorScore: 0.8);
        var plan = TrainingOrchestratorTestsFixture.CreateSingleTaskPlan("Plan1", "Describe something", "Expected output");

        var result = await orchestrator.TrainAgentAsync(AgentId1, plan, ct: TestContext.Current.CancellationToken);

        Assert.Equal(AgentId1, result.AgentId);
        Assert.Equal("Plan1", result.PlanName);
        Assert.Equal(1, result.Iteration);
        Assert.NotEmpty(result.Scores);
        Assert.Equal(0.8, result.AverageScore, 4);
    }

    [Fact]
    public async Task TrainAgentAsync_WithMultipleIterations_ReturnsLastIteration()
    {
        var orchestrator = TrainingOrchestratorTestsFixture.CreateOrchestrator(evaluatorScore: 0.75);
        var plan = new TrainingPlan(
            "MultiPlan",
            [new TrainingTask("t1", "Task one")],
            Iterations: 3);

        var result = await orchestrator.TrainAgentAsync(AgentId2, plan, ct: TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Iteration);
        Assert.Equal("MultiPlan", result.PlanName);
    }

    [Fact]
    public async Task GetTrainingHistoryAsync_ReturnsStoredResults()
    {
        var orchestrator = TrainingOrchestratorTestsFixture.CreateOrchestrator(evaluatorScore: 0.9);
        var plan = new TrainingPlan(
            "HistoryPlan",
            [new TrainingTask("t1", "Task one")],
            Iterations: 2);

        await orchestrator.TrainAgentAsync(AgentId3, plan, ct: TestContext.Current.CancellationToken);

        var history = await orchestrator.GetTrainingHistoryAsync(AgentId3, TestContext.Current.CancellationToken);

        Assert.Equal(2, history.Count);
        Assert.Equal(1, history[0].Iteration);
        Assert.Equal(2, history[1].Iteration);
    }

    [Fact]
    public async Task TrainAgentAsync_WithFeedbackDisabled_ReturnsNullFeedback()
    {
        var orchestrator = TrainingOrchestratorTestsFixture.CreateOrchestrator(evaluatorScore: 0.6);
        var plan = TrainingOrchestratorTestsFixture.CreateSingleTaskPlan("NoFeedback", "Task");
        var options = new TrainingOptions { CollectFeedback = false };

        var result = await orchestrator.TrainAgentAsync("agent-4", plan, options, TestContext.Current.CancellationToken);

        Assert.Null(result.Feedback);
    }

    [Fact]
    public async Task TrainAgentAsync_ResultPassedReflectsThreshold()
    {
        var orchestrator = TrainingOrchestratorTestsFixture.CreateOrchestrator(evaluatorScore: 0.5);
        var plan = TrainingOrchestratorTestsFixture.CreateSingleTaskPlan("ThresholdPlan", "Task");

        // Default threshold is 0.7 — score 0.5 should not pass
        var result = await orchestrator.TrainAgentAsync("agent-5", plan, ct: TestContext.Current.CancellationToken);

        Assert.False(result.Passed);

        // With threshold 0.4 — score 0.5 should pass
        var options = new TrainingOptions { PassingThreshold = 0.4 };
        var result2 = await orchestrator.TrainAgentAsync("agent-5", plan, options, TestContext.Current.CancellationToken);

        Assert.True(result2.Passed);
    }

    [Fact]
    public async Task GetTrainingHistoryAsync_UnknownAgent_ReturnsEmpty()
    {
        var orchestrator = TrainingOrchestratorTestsFixture.CreateOrchestrator(evaluatorScore: 0.8);

        var history = await orchestrator.GetTrainingHistoryAsync("unknown-agent", TestContext.Current.CancellationToken);

        Assert.Empty(history);
    }
}
