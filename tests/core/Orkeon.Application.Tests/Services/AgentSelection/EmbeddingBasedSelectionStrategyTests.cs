using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Services.AgentSelection;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Application.Tests.Services.AgentSelection;

public class EmbeddingBasedSelectionStrategyTests
{
    /// <summary>
    /// Fake embedding service that returns a fixed vector per text snippet for deterministic tests.
    /// </summary>
    private sealed class StubEmbeddingService : IEmbeddingService
    {
        private readonly Dictionary<string, float[]> _vectors;
        private readonly float[] _defaultVector;

        public StubEmbeddingService(Dictionary<string, float[]> vectors, float[]? defaultVector = null)
        {
            _vectors = vectors;
            _defaultVector = defaultVector ?? [0f, 0f, 0f];
        }

        public System.Threading.Tasks.Task<float[]> GetEmbeddingAsync(string text)
        {
            foreach (var kv in _vectors)
            {
                if (text.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                    return System.Threading.Tasks.Task.FromResult(kv.Value);
            }
            return System.Threading.Tasks.Task.FromResult(_defaultVector);
        }
    }

    private static DomainAgent CreateAgent(string role, string goal = "Do work")
    {
        return DomainAgent.Create(
            AgentRole.From(role),
            AgentGoal.From(goal),
            allowDelegation: false,
            maxIterations: 5,
            maxRpm: 10,
            verbose: false,
            maxRetryLimit: 3);
    }

    private static DomainTask CreateTask(string description)
    {
        return DomainTask.Create(
            TaskDescription.From(description),
            expectedOutput: ExpectedOutput.From("Result"));
    }

    [Fact]
    public async System.Threading.Tasks.Task SelectBestAgent_AboveThreshold_ReturnsBestMatch()
    {
        // Arrange: task and agentA are very similar (cosine ~ 1.0); agentB is orthogonal
        var taskVector = new float[] { 1f, 0f, 0f };
        var agentAVector = new float[] { 1f, 0f, 0f };   // cosine = 1.0
        var agentBVector = new float[] { 0f, 1f, 0f };   // cosine = 0.0

        var embeddings = new Dictionary<string, float[]>
        {
            ["analysis task"] = taskVector,
            ["analyst"] = agentAVector,
            ["developer"] = agentBVector
        };

        var stub = new StubEmbeddingService(embeddings);
        var strategy = new EmbeddingBasedSelectionStrategy(stub);

        var agentA = CreateAgent("analyst");
        var agentB = CreateAgent("developer");
        var task = CreateTask("analysis task");

        // Act
        var result = await strategy.SelectBestAgentAsync(task, [agentA, agentB], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(agentA.Id, result.Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task SelectBestAgent_BelowThreshold_ReturnsNull()
    {
        // Arrange: orthogonal vectors → cosine = 0 which is below the 0.5 threshold
        var embeddings = new Dictionary<string, float[]>
        {
            ["quantum"] = [1f, 0f, 0f],
            ["chef"] = [0f, 1f, 0f]
        };

        var stub = new StubEmbeddingService(embeddings);
        var strategy = new EmbeddingBasedSelectionStrategy(stub);

        var agent = CreateAgent("chef");
        var task = CreateTask("quantum physics experiments");

        // Act
        var result = await strategy.SelectBestAgentAsync(task, [agent], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async System.Threading.Tasks.Task SelectBestAgent_NullTask_ThrowsArgumentNullException()
    {
        // Arrange
        var stub = new StubEmbeddingService([]);
        var strategy = new EmbeddingBasedSelectionStrategy(stub);
        var agent = CreateAgent("researcher");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            strategy.SelectBestAgentAsync(null!, [agent], cancellationToken: TestContext.Current.CancellationToken));
    }
}
