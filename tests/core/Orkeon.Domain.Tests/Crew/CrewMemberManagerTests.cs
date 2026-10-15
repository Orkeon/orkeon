using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;

namespace Orkeon.Domain.Tests.Crew;

/// <summary>
/// Tests for CrewMemberManager internal domain helper.
/// Validates agent membership operations: add, remove, query.
/// </summary>
public class CrewMemberManagerTests
{
    #region Constructor

    [Fact]
    public void ShouldInitialize_WhenConstructingWithEmptyList()
    {
        // Arrange
        var agents = new List<AgentId>();

        // Act
        var manager = new CrewMemberManager(agents);

        // Assert
        Assert.Empty(manager.Agents);
        Assert.False(manager.Any());
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullList()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CrewMemberManager(null!));
    }

    #endregion

    #region AddAgent

    [Fact]
    public void ShouldAddAgent_WhenStatusIsIdle()
    {
        // Arrange
        var agents = new List<AgentId>();
        var manager = new CrewMemberManager(agents);
        var agentId = AgentId.Create();

        // Act
        manager.AddAgent(agentId, CrewStatus.Idle);

        // Assert
        Assert.Single(manager.Agents);
        Assert.Equal(agentId, manager.Agents[0]);
    }

    [Fact]
    public void ShouldAddMultipleAgents_WhenAddingSequentially()
    {
        // Arrange
        var agents = new List<AgentId>();
        var manager = new CrewMemberManager(agents);
        var agent1 = AgentId.Create();
        var agent2 = AgentId.Create();
        var agent3 = AgentId.Create();

        // Act
        manager.AddAgent(agent1, CrewStatus.Idle);
        manager.AddAgent(agent2, CrewStatus.Idle);
        manager.AddAgent(agent3, CrewStatus.Idle);

        // Assert
        Assert.Equal(3, manager.Agents.Count);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenAddingNullAgentId()
    {
        // Arrange
        var manager = new CrewMemberManager([]);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => manager.AddAgent(null!, CrewStatus.Idle));
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenAddingDuplicateAgent()
    {
        // Arrange
        var agents = new List<AgentId>();
        var manager = new CrewMemberManager(agents);
        var agentId = AgentId.Create();
        manager.AddAgent(agentId, CrewStatus.Idle);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => manager.AddAgent(agentId, CrewStatus.Idle));
        Assert.Contains("already in this crew", ex.Message);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenAddingAgentDuringExecution()
    {
        // Arrange
        var manager = new CrewMemberManager([]);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => manager.AddAgent(AgentId.Create(), CrewStatus.Executing));
        Assert.Contains("Cannot add agents while crew is executing", ex.Message);
    }

    [Theory]
    [InlineData("Created")]
    [InlineData("Idle")]
    [InlineData(Failed)]
    [InlineData(Completed)]
    [InlineData("Paused")]
    public void ShouldAddAgent_WhenStatusIsNotExecuting(string statusStr)
    {
        // Arrange
        var status = CrewStatus.From(statusStr);
        var manager = new CrewMemberManager([]);
        var agentId = AgentId.Create();

        // Act
        manager.AddAgent(agentId, status);

        // Assert
        Assert.Single(manager.Agents);
    }

    #endregion

    #region Contains

    [Fact]
    public void ShouldReturnTrue_WhenAgentIsInCrew()
    {
        // Arrange
        var manager = new CrewMemberManager([]);
        var agentId = AgentId.Create();
        manager.AddAgent(agentId, CrewStatus.Idle);

        // Act & Assert
        Assert.True(manager.Contains(agentId));
    }

    [Fact]
    public void ShouldReturnFalse_WhenAgentIsNotInCrew()
    {
        // Arrange
        var manager = new CrewMemberManager([]);

        // Act & Assert
        Assert.False(manager.Contains(AgentId.Create()));
    }

    #endregion

    #region Any

    [Fact]
    public void ShouldReturnTrue_WhenCrewHasAgents()
    {
        // Arrange
        var manager = new CrewMemberManager([]);
        manager.AddAgent(AgentId.Create(), CrewStatus.Idle);

        // Act & Assert
        Assert.True(manager.Any());
    }

    [Fact]
    public void ShouldReturnFalse_WhenCrewHasNoAgents()
    {
        // Arrange
        var manager = new CrewMemberManager([]);

        // Act & Assert
        Assert.False(manager.Any());
    }

    #endregion
}
