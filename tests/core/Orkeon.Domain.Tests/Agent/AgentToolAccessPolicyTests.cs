using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Common;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;

namespace Orkeon.Domain.Tests.Agent;

/// <summary>
/// Tests for Agent integration with ToolAccessPolicy.
/// Validates policy assignment, builder methods, and policy retrieval.
/// </summary>
public class AgentToolAccessPolicyTests
{
    #region Agent Creation with Policy

    [Fact]
    public void Create_WithoutPolicy_ShouldDefaultToUnrestricted()
    {
        // Arrange
        var options = new AgentCreateOptions
        {
            Role = AgentRole.From("TestRole"),
            Goal = AgentGoal.From("TestGoal")
        };

        // Act
        var agent = DomainAgent.Create(options);

        // Assert
        Assert.NotNull(agent.ToolAccessPolicy);
        Assert.True(agent.ToolAccessPolicy.IsUnrestricted);
    }

    [Fact]
    public void Create_WithWhitelistPolicy_ShouldAssignCorrectly()
    {
        // Arrange
        var policy = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolWebScrape);
        var options = new AgentCreateOptions
        {
            Role = AgentRole.From("TestRole"),
            Goal = AgentGoal.From("TestGoal"),
            ToolAccessPolicy = policy
        };

        // Act
        var agent = DomainAgent.Create(options);

        // Assert
        Assert.Equal(policy, agent.ToolAccessPolicy);
        Assert.False(agent.ToolAccessPolicy.IsUnrestricted);
        Assert.Equal(ToolAccessMode.Whitelist, agent.ToolAccessPolicy.Mode);
    }

    [Fact]
    public void Create_WithBlacklistPolicy_ShouldAssignCorrectly()
    {
        // Arrange
        var policy = ToolAccessPolicy.CreateBlacklist(ToolFileWrite, "DatabaseDelete");
        var options = new AgentCreateOptions
        {
            Role = AgentRole.From("TestRole"),
            Goal = AgentGoal.From("TestGoal"),
            ToolAccessPolicy = policy
        };

        // Act
        var agent = DomainAgent.Create(options);

        // Assert
        Assert.Equal(policy, agent.ToolAccessPolicy);
        Assert.Equal(ToolAccessMode.Blacklist, agent.ToolAccessPolicy.Mode);
    }

    #endregion

    #region AgentBuilder with Whitelist

    [Fact]
    public void Builder_WithToolWhitelist_ShouldCreateAgentWithWhitelistPolicy()
    {
        // Arrange & Act
        var agent = new AgentBuilder()
            .Role(RoleAnalyst)
            .Goal(GoalAnalyzeData)
            .WithToolWhitelist(ToolFileRead, ToolWebScrape)
            .Build();

        // Assert
        Assert.NotNull(agent.ToolAccessPolicy);
        Assert.Equal(ToolAccessMode.Whitelist, agent.ToolAccessPolicy.Mode);
        Assert.True(agent.ToolAccessPolicy.IsToolAllowed(ToolFileRead));
        Assert.True(agent.ToolAccessPolicy.IsToolAllowed(ToolWebScrape));
        Assert.False(agent.ToolAccessPolicy.IsToolAllowed(ToolFileWrite));
    }

    [Fact]
    public void Builder_WithToolWhitelistEnumerable_ShouldCreateAgentWithWhitelistPolicy()
    {
        // Arrange
        var allowedTools = new[] { ToolFileRead, ToolWebScrape, "EmailSend" };

        // Act
        var agent = new AgentBuilder()
            .Role("Assistant")
            .Goal("Assist users")
            .WithToolWhitelist(allowedTools)
            .Build();

        // Assert
        Assert.Equal(ToolAccessMode.Whitelist, agent.ToolAccessPolicy.Mode);
        Assert.Equal(3, agent.ToolAccessPolicy.ToolList.Count);
    }

    [Fact]
    public void Builder_WithToolWhitelist_ShouldBeChainable()
    {
        // Arrange & Act
        var agent = new AgentBuilder()
            .Role(RoleManager)
            .Goal("Manage team")
            .WithToolWhitelist("ApprovalTool", "ReportingTool")
            .Verbose()
            .MaxIterations(5)
            .Build();

        // Assert
        Assert.NotNull(agent);
        Assert.True(agent.Verbose);
        Assert.Equal(5, agent.MaxIterations);
    }

    #endregion

    #region AgentBuilder with Blacklist

    [Fact]
    public void Builder_WithToolBlacklist_ShouldCreateAgentWithBlacklistPolicy()
    {
        // Arrange & Act
        var agent = new AgentBuilder()
            .Role("User")
            .Goal("Execute tasks")
            .WithToolBlacklist(ToolFileWrite, "DatabaseDelete", "SystemCommand")
            .Build();

        // Assert
        Assert.NotNull(agent.ToolAccessPolicy);
        Assert.Equal(ToolAccessMode.Blacklist, agent.ToolAccessPolicy.Mode);
        Assert.False(agent.ToolAccessPolicy.IsToolAllowed(ToolFileWrite));
        Assert.False(agent.ToolAccessPolicy.IsToolAllowed("DatabaseDelete"));
        Assert.True(agent.ToolAccessPolicy.IsToolAllowed(ToolFileRead));
    }

    [Fact]
    public void Builder_WithToolBlacklistEnumerable_ShouldCreateAgentWithBlacklistPolicy()
    {
        // Arrange
        var blockedTools = new[] { "Delete*", "Drop*" };

        // Act
        var agent = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal("Develop features")
            .WithToolBlacklist(blockedTools)
            .Build();

        // Assert
        Assert.Equal(ToolAccessMode.Blacklist, agent.ToolAccessPolicy.Mode);
    }

    #endregion

    #region AgentBuilder with Unrestricted

    [Fact]
    public void Builder_WithUnrestrictedToolAccess_ShouldCreateUnrestrictedPolicy()
    {
        // Arrange & Act
        var agent = new AgentBuilder()
            .Role("Admin")
            .Goal("Administer system")
            .WithUnrestrictedToolAccess()
            .Build();

        // Assert
        Assert.True(agent.ToolAccessPolicy.IsUnrestricted);
    }

    #endregion

    #region AgentBuilder with Custom Policy

    [Fact]
    public void Builder_WithCustomPolicy_ShouldAssignDirectly()
    {
        // Arrange
        var customPolicy = ToolAccessPolicy.CreateWhitelist("Tool1", "Tool2");

        // Act
        var agent = new AgentBuilder()
            .Role("Custom")
            .Goal("Custom goal")
            .WithToolAccessPolicy(customPolicy)
            .Build();

        // Assert
        Assert.Equal(customPolicy, agent.ToolAccessPolicy);
    }

    [Fact]
    public void Builder_WithCustomPolicy_WithNullPolicy_ShouldThrow()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => new AgentBuilder()
                .Role("Custom")
                .Goal("Custom goal")
                .WithToolAccessPolicy(null!)
                .Build());
    }

    #endregion

    #region UpdateToolAccessPolicy

    [Fact]
    public void UpdateToolAccessPolicy_WhenAgentIdle_ShouldUpdatePolicy()
    {
        // Arrange
        var agent = DomainAgent.Create(new AgentCreateOptions
        {
            Role = AgentRole.From("TestRole"),
            Goal = AgentGoal.From("TestGoal"),
            ToolAccessPolicy = ToolAccessPolicy.CreateUnrestricted()
        });

        var newPolicy = ToolAccessPolicy.CreateWhitelist(ToolFileRead);

        // Act
        agent.UpdateToolAccessPolicy(newPolicy);

        // Assert
        Assert.Equal(newPolicy, agent.ToolAccessPolicy);
    }

    [Fact]
    public void UpdateToolAccessPolicy_WithNullPolicy_ShouldThrow()
    {
        // Arrange
        var agent = DomainAgent.Create(new AgentCreateOptions
        {
            Role = AgentRole.From("TestRole"),
            Goal = AgentGoal.From("TestGoal")
        });

        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => agent.UpdateToolAccessPolicy(null!));
    }

    #endregion

    #region Policy Wildcard Patterns

    [Fact]
    public void Builder_WithWildcardWhitelist_ShouldMatchPatterns()
    {
        // Arrange & Act
        var agent = new AgentBuilder()
            .Role("Reader")
            .Goal("Read files")
            .WithToolWhitelist("File*", "*Query")
            .Build();

        // Assert
        Assert.True(agent.ToolAccessPolicy.IsToolAllowed(ToolFileRead));
        Assert.True(agent.ToolAccessPolicy.IsToolAllowed(ToolFileWrite));
        Assert.True(agent.ToolAccessPolicy.IsToolAllowed(ToolDatabaseQuery));
        Assert.False(agent.ToolAccessPolicy.IsToolAllowed("EmailSend"));
    }

    [Fact]
    public void Builder_WithWildcardBlacklist_ShouldBlockPatterns()
    {
        // Arrange & Act
        var agent = new AgentBuilder()
            .Role("SafeExecutor")
            .Goal("Execute safely")
            .WithToolBlacklist("*Delete*", "*Drop*", "System*")
            .Build();

        // Assert
        Assert.False(agent.ToolAccessPolicy.IsToolAllowed("DatabaseDelete"));
        Assert.False(agent.ToolAccessPolicy.IsToolAllowed("DropTable"));
        Assert.False(agent.ToolAccessPolicy.IsToolAllowed("SystemCommand"));
        Assert.True(agent.ToolAccessPolicy.IsToolAllowed(ToolFileRead));
    }

    #endregion

    #region Policy Chaining Behavior

    [Fact]
    public void Builder_MultipleToolPolicyCalls_ShouldUseLast()
    {
        // Arrange & Act - Last call should override previous
        var agent = new AgentBuilder()
            .Role("Flexible")
            .Goal("Flexible execution")
            .WithToolWhitelist("Tool1", "Tool2")
            .WithToolBlacklist("Tool3", "Tool4")
            .Build();

        // Assert - Should use the last blacklist policy
        Assert.Equal(ToolAccessMode.Blacklist, agent.ToolAccessPolicy.Mode);
        Assert.False(agent.ToolAccessPolicy.IsToolAllowed("Tool3"));
    }

    [Fact]
    public void Builder_WhitelistThenUnrestricted_ShouldUseUnrestricted()
    {
        // Arrange & Act
        var agent = new AgentBuilder()
            .Role("Admin")
            .Goal("Admin tasks")
            .WithToolWhitelist("LimitedTool")
            .WithUnrestrictedToolAccess()
            .Build();

        // Assert
        Assert.True(agent.ToolAccessPolicy.IsUnrestricted);
        Assert.True(agent.ToolAccessPolicy.IsToolAllowed("AnyTool"));
    }

    #endregion

    #region Agent Restore with Policy

    [Fact]
    public void Restore_WithPolicy_ShouldRestoreCorrectly()
    {
        // Arrange
        var policy = ToolAccessPolicy.CreateWhitelist(ToolFileRead);
        var agentId = new AgentId();
        var role = AgentRole.From("RestoreRole");
        var goal = AgentGoal.From("RestoreGoal");

        // Act
        var agent = DomainAgent.Restore(
            agentId,
            role,
            goal,
            null,
            false,
            3,
            100,
            false,
            AgentStatus.Idle,
            null,
            true,
            null,
            null,
            null,
            3,
            null,
            null,
            policy
        );

        // Assert
        Assert.Equal(policy, agent.ToolAccessPolicy);
    }

    [Fact]
    public void Restore_WithoutPolicy_ShouldDefaultToUnrestricted()
    {
        // Arrange
        var agentId = new AgentId();
        var role = AgentRole.From("DefaultRole");
        var goal = AgentGoal.From("DefaultGoal");

        // Act
        var agent = DomainAgent.Restore(
            agentId,
            role,
            goal,
            null,
            false,
            3,
            100,
            false,
            AgentStatus.Idle,
            null,
            true,
            null,
            null,
            null,
            3,
            null,
            null,
            null  // No policy
        );

        // Assert
        Assert.NotNull(agent.ToolAccessPolicy);
        Assert.True(agent.ToolAccessPolicy.IsUnrestricted);
    }

    #endregion
}
