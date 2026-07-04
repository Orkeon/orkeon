using Orkeon.Application.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Agent.ValueObjects;
using Microsoft.Extensions.Logging;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Application.Tests.Agent;

/// <summary>
/// Tests for DynamicAgentRegistry.
/// </summary>
public class DynamicAgentRegistryTests
{
    private readonly CrewId _crewId = CrewId.Create();
    private readonly ILogger<DynamicAgentRegistry>? _logger = null;

    private static Domain.Agent.Agent CreateTestAgent()
    {
        return Domain.Agent.Agent.Create(
            AgentRole.From("Test Role"),
            AgentGoal.From(TestGoal));
    }

    [Fact]
    public void Constructor_CreatesInstance()
    {
        // Act
        var registry = new DynamicAgentRegistry(_crewId, _logger);

        // Assert
        Assert.NotNull(registry);
        Assert.Equal(_crewId, registry.CrewId);
        Assert.Equal(0, registry.DynamicAgentCount);
    }

    [Fact]
    public void Constructor_WithNullCrewId_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new DynamicAgentRegistry(null!, _logger));
    }

    [Fact]
    public void RegisterAgent_AddsAgentToRegistry()
    {
        // Arrange
        var registry = new DynamicAgentRegistry(_crewId, _logger);
        var agent = CreateTestAgent();

        // Act
        registry.RegisterAgent(agent, null, "test spawn");

        // Assert
        Assert.Equal(1, registry.DynamicAgentCount);
        Assert.NotNull(registry.GetAgent(agent.Id));
    }

    [Fact]
    public void RegisterAgent_WithNullAgent_ThrowsArgumentNullException()
    {
        // Arrange
        var registry = new DynamicAgentRegistry(_crewId, _logger);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            registry.RegisterAgent(null!, null, null));
    }

    [Fact]
    public void RegisterAgent_DuplicateAgent_ThrowsInvalidOperationException()
    {
        // Arrange
        var registry = new DynamicAgentRegistry(_crewId, _logger);
        var agent = CreateTestAgent();
        registry.RegisterAgent(agent);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterAgent(agent));
        Assert.Contains("already registered", ex.Message);
    }

    [Fact]
    public void TerminateAgent_MarkAgentAsTerminated()
    {
        // Arrange
        var registry = new DynamicAgentRegistry(_crewId, _logger);
        var agent = CreateTestAgent();
        registry.RegisterAgent(agent);

        // Act
        var result = registry.TerminateAgent(agent.Id, "test termination");

        // Assert
        Assert.True(result);
        Assert.False(registry.IsAgentActive(agent.Id));
        var metadata = registry.GetAgentMetadata(agent.Id);
        Assert.NotNull(metadata);
        Assert.True(metadata.IsTerminated);
        Assert.Equal("test termination", metadata.TerminationReason);
    }

    [Fact]
    public void TerminateAgent_NonExistentAgent_ReturnsFalse()
    {
        // Arrange
        var registry = new DynamicAgentRegistry(_crewId, _logger);
        var nonExistentAgentId = AgentId.Create();

        // Act
        var result = registry.TerminateAgent(nonExistentAgentId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetAgent_ReturnsRegisteredAgent()
    {
        // Arrange
        var registry = new DynamicAgentRegistry(_crewId, _logger);
        var agent = CreateTestAgent();
        registry.RegisterAgent(agent);

        // Act
        var retrieved = registry.GetAgent(agent.Id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(agent.Id, retrieved.Id);
    }

    [Fact]
    public void GetAgent_NonExistentAgent_ReturnsNull()
    {
        // Arrange
        var registry = new DynamicAgentRegistry(_crewId, _logger);
        var nonExistentAgentId = AgentId.Create();

        // Act
        var retrieved = registry.GetAgent(nonExistentAgentId);

        // Assert
        Assert.Null(retrieved);
    }

    [Fact]
    public void IsAgentActive_WithActiveAgent_ReturnsTrue()
    {
        // Arrange
        var registry = new DynamicAgentRegistry(_crewId, _logger);
        var agent = CreateTestAgent();
        registry.RegisterAgent(agent);

        // Act
        var isActive = registry.IsAgentActive(agent.Id);

        // Assert
        Assert.True(isActive);
    }

    [Fact]
    public void IsAgentActive_WithTerminatedAgent_ReturnsFalse()
    {
        // Arrange
        var registry = new DynamicAgentRegistry(_crewId, _logger);
        var agent = CreateTestAgent();
        registry.RegisterAgent(agent);
        registry.TerminateAgent(agent.Id);

        // Act
        var isActive = registry.IsAgentActive(agent.Id);

        // Assert
        Assert.False(isActive);
    }

    [Fact]
    public void DynamicAgents_ReturnsOnlyActiveAgents()
    {
        // Arrange
        var registry = new DynamicAgentRegistry(_crewId, _logger);
        var agent1 = CreateTestAgent();
        var agent2 = CreateTestAgent();
        registry.RegisterAgent(agent1);
        registry.RegisterAgent(agent2);
        registry.TerminateAgent(agent1.Id);

        // Act
        var active = registry.GetDynamicAgents().ToList();

        // Assert
        Assert.Single(active);
        Assert.Equal(agent2.Id, active[0].Id);
    }

    [Fact]
    public void TerminatedAgents_ReturnsOnlyTerminatedAgents()
    {
        // Arrange
        var registry = new DynamicAgentRegistry(_crewId, _logger);
        var agent1 = CreateTestAgent();
        var agent2 = CreateTestAgent();
        registry.RegisterAgent(agent1);
        registry.RegisterAgent(agent2);
        registry.TerminateAgent(agent1.Id);

        // Act
        var terminated = registry.GetTerminatedAgents().ToList();

        // Assert
        Assert.Single(terminated);
        Assert.Equal(agent1.Id, terminated[0].Id);
    }

    [Fact]
    public void GetChildrenOf_ReturnsOnlyAgentsRequestedByParent()
    {
        // Arrange
        var registry = new DynamicAgentRegistry(_crewId, _logger);
        var parentAgentId = AgentId.Create();
        var otherAgentId = AgentId.Create();

        var agent1 = CreateTestAgent();
        var agent2 = CreateTestAgent();
        var agent3 = CreateTestAgent();

        registry.RegisterAgent(agent1, parentAgentId, "spawned by parent");
        registry.RegisterAgent(agent2, parentAgentId, "spawned by parent");
        registry.RegisterAgent(agent3, otherAgentId, "spawned by other");

        // Act
        var children = registry.GetChildrenOf(parentAgentId).ToList();

        // Assert
        Assert.Equal(2, children.Count);
        Assert.Contains(agent1.Id, children.Select(a => a.Id));
        Assert.Contains(agent2.Id, children.Select(a => a.Id));
    }

    [Fact]
    public void GetChildrenOf_ExcludesTerminatedAgents()
    {
        // Arrange
        var registry = new DynamicAgentRegistry(_crewId, _logger);
        var parentAgentId = AgentId.Create();
        var agent1 = CreateTestAgent();
        var agent2 = CreateTestAgent();
        registry.RegisterAgent(agent1, parentAgentId);
        registry.RegisterAgent(agent2, parentAgentId);
        registry.TerminateAgent(agent1.Id);

        // Act
        var children = registry.GetChildrenOf(parentAgentId).ToList();

        // Assert
        Assert.Single(children);
        Assert.Equal(agent2.Id, children[0].Id);
    }

    [Fact]
    public void TerminateAllAgents_TerminatesAllActiveAgents()
    {
        // Arrange
        var registry = new DynamicAgentRegistry(_crewId, _logger);
        var agent1 = CreateTestAgent();
        var agent2 = CreateTestAgent();
        registry.RegisterAgent(agent1);
        registry.RegisterAgent(agent2);

        // Act
        registry.TerminateAllAgents("test termination");

        // Assert
        Assert.Empty(registry.GetDynamicAgents());
        Assert.Equal(2, registry.GetTerminatedAgents().Count());
    }

    [Fact]
    public void GetStats_ReturnsCorrectStatistics()
    {
        // Arrange
        var registry = new DynamicAgentRegistry(_crewId, _logger);
        var agent1 = CreateTestAgent();
        var agent2 = CreateTestAgent();
        registry.RegisterAgent(agent1);
        registry.RegisterAgent(agent2);
        registry.TerminateAgent(agent1.Id);

        // Act
        var stats = registry.GetStats();

        // Assert
        Assert.Equal(2, stats.TotalAgents);
        Assert.Equal(1, stats.ActiveAgents);
        Assert.Equal(1, stats.TerminatedAgents);
    }

    [Fact]
    public void GetAgentMetadata_ReturnsMetadata()
    {
        // Arrange
        var registry = new DynamicAgentRegistry(_crewId, _logger);
        var agent = CreateTestAgent();
        var requestingAgentId = AgentId.Create();
        registry.RegisterAgent(agent, requestingAgentId, "test spawn");

        // Act
        var metadata = registry.GetAgentMetadata(agent.Id);

        // Assert
        Assert.NotNull(metadata);
        Assert.Equal(agent.Id, metadata.Agent.Id);
        Assert.Equal(requestingAgentId, metadata.RequestingAgentId);
        Assert.Equal("test spawn", metadata.SpawnReason);
        Assert.False(metadata.IsTerminated);
    }

    [Fact]
    public void Cleanup_ClearsRegistry()
    {
        // Arrange
        var registry = new DynamicAgentRegistry(_crewId, _logger);
        var agent = CreateTestAgent();
        registry.RegisterAgent(agent);

        // Act
        registry.Cleanup();

        // Assert
        Assert.Equal(0, registry.DynamicAgentCount);
    }

    [Fact]
    public void Cleanup_PreventsFurtherOperations()
    {
        // Arrange
        var registry = new DynamicAgentRegistry(_crewId, _logger);
        registry.Cleanup();

        // Act & Assert
        var agent = CreateTestAgent();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterAgent(agent));
        Assert.Contains("disposed", ex.Message);
    }
}
