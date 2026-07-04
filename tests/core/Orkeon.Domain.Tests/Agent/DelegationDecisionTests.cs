using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Agent.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Domain.Tests.Agent;

public class DelegationDecisionTests
{
    [Fact]
    public void ShouldCreateNonDelegatingDecision_WhenUsingNoDelegationWithReason()
    {
        // Arrange
        var reason = "Agent has the required skills";

        // Act
        var decision = DelegationDecision.NoDelegation(reason);

        // Assert
        Assert.NotNull(decision);
        Assert.False(decision.ShouldDelegate);
        Assert.Null(decision.DelegateToAgentId);
        Assert.Equal(reason, decision.Reason);
    }

    [Fact]
    public void ShouldUseDefaultReason_WhenUsingNoDelegationWithoutReason()
    {
        // Act
        var decision = DelegationDecision.NoDelegation();

        // Assert
        Assert.NotNull(decision);
        Assert.False(decision.ShouldDelegate);
        Assert.Null(decision.DelegateToAgentId);
        Assert.Equal("No delegation needed", decision.Reason);
    }

    [Fact]
    public void ShouldCreateDelegatingDecision_WhenDelegatingToWithValidAgentId()
    {
        // Arrange
        var targetAgent = Orkeon.Domain.Agent.Agent.Create(
            AgentRole.From(RoleSeniorDeveloper),
            AgentGoal.From("Write high-quality code"));
        var reason = "Target agent has specialized expertise";

        // Act
        var decision = DelegationDecision.DelegateTo(targetAgent.Id, reason);

        // Assert
        Assert.NotNull(decision);
        Assert.True(decision.ShouldDelegate);
        Assert.NotNull(decision.DelegateToAgentId);
        Assert.Equal(targetAgent.Id, decision.DelegateToAgentId);
        Assert.Equal(reason, decision.Reason);
    }

    [Fact]
    public void ShouldGenerateDefaultReason_WhenDelegatingToWithoutReason()
    {
        // Arrange
        var targetAgent = Orkeon.Domain.Agent.Agent.Create(
            AgentRole.From(RoleDataAnalyst),
            AgentGoal.From("Analyze data effectively"));

        // Act
        var decision = DelegationDecision.DelegateTo(targetAgent.Id);

        // Assert
        Assert.NotNull(decision);
        Assert.True(decision.ShouldDelegate);
        Assert.Contains("Delegating to agent", decision.Reason);
        Assert.Contains("for optimal task execution", decision.Reason);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenDelegatingToWithNullAgentId()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => DelegationDecision.DelegateTo((AgentId)null!));
        Assert.Equal("agentId", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValues()
    {
        // Arrange
        var agent = Orkeon.Domain.Agent.Agent.Create(
            AgentRole.From(RoleDeveloper),
            AgentGoal.From("Code"));
        var reason = "Same reason";

        // Act
        var decision1 = DelegationDecision.DelegateTo(agent.Id, reason);
        var decision2 = DelegationDecision.DelegateTo(agent.Id, reason);

        // Assert
        Assert.Equal(decision1, decision2);
        Assert.True(decision1.Equals(decision2));
        Assert.Equal(decision1.GetHashCode(), decision2.GetHashCode());
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentValues()
    {
        // Arrange
        var agent1 = Orkeon.Domain.Agent.Agent.Create(
            AgentRole.From(RoleDeveloper),
            AgentGoal.From("Code"));
        var agent2 = Orkeon.Domain.Agent.Agent.Create(
            AgentRole.From("Tester"),
            AgentGoal.From("Test"));

        // Act
        var decision1 = DelegationDecision.DelegateTo(agent1.Id);
        var decision2 = DelegationDecision.DelegateTo(agent2.Id);
        var decision3 = DelegationDecision.NoDelegation();

        // Assert
        Assert.NotEqual(decision1, decision2); // Different agents
        Assert.NotEqual(decision1, decision3); // Different delegation status
        Assert.NotEqual(decision2, decision3);
    }

    [Fact]
    public void ShouldBeEqual_WhenComparingEqualityWithNoDelegationDecisionsWithSameReason()
    {
        // Arrange
        var reason = "Task is simple enough";

        // Act
        var decision1 = DelegationDecision.NoDelegation(reason);
        var decision2 = DelegationDecision.NoDelegation(reason);

        // Assert
        Assert.Equal(decision1, decision2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenComparingEqualityWithNoDelegationDecisionsWithDifferentReasons()
    {
        // Act
        var decision1 = DelegationDecision.NoDelegation("Reason 1");
        var decision2 = DelegationDecision.NoDelegation("Reason 2");

        // Assert
        Assert.NotEqual(decision1, decision2);
    }

    [Fact]
    public void ShouldBeConsistent_WhenCallingGetHashCode()
    {
        // Arrange
        var agent = Orkeon.Domain.Agent.Agent.Create(
            AgentRole.From("Expert"),
            AgentGoal.From("Solve complex problems"));
        var decision = DelegationDecision.DelegateTo(agent.Id, "Requires expertise");

        // Act
        var hash1 = decision.GetHashCode();
        var hash2 = decision.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShouldAcceptEmptyString_WhenDelegatingToWithEmptyReason()
    {
        // Arrange
        var agent = Orkeon.Domain.Agent.Agent.Create(
            AgentRole.From(RoleWorker),
            AgentGoal.From(GoalCompleteTasks));

        // Act
        var decision = DelegationDecision.DelegateTo(agent.Id, "");

        // Assert
        Assert.Equal("", decision.Reason);
    }

    [Fact]
    public void ShouldAcceptEmptyString_WhenUsingNoDelegationWithEmptyReason()
    {
        // Act
        var decision = DelegationDecision.NoDelegation("");

        // Assert
        Assert.Equal("", decision.Reason);
    }

    [Fact]
    public void ShouldConsiderAllComponents_WhenUsingValueObjectEquality()
    {
        // Arrange
        var agent = Orkeon.Domain.Agent.Agent.Create(
            AgentRole.From("Specialist"),
            AgentGoal.From("Specialized work"));

        // Create decisions with same agent but different reasons
        var decision1 = DelegationDecision.DelegateTo(agent.Id, "Reason A");
        var decision2 = DelegationDecision.DelegateTo(agent.Id, "Reason B");

        // Assert
        Assert.NotEqual(decision1, decision2); // Different reasons make them unequal
    }
}
