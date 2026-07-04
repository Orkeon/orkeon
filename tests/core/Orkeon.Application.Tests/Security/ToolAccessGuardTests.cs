using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Services.Security;
using Orkeon.Domain.Agent.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;

namespace Orkeon.Application.Tests.Security;

/// <summary>
/// Tests for ToolAccessGuard and related policy extension methods.
/// Validates tool access checks and policy enforcement through the guardian framework.
/// </summary>
public class ToolAccessGuardTests
{
    private readonly ToolAccessGuard _guard = new(new GuardianPolicyEngine(new GuardianPolicy()));

    #region CheckAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task CheckAsync_WithNonToolExecutionPhase_ShouldAllow()
    {
        // Arrange
        var context = new GuardContext
        {
            Phase = GuardPhase.Input,
            ToolName = ToolFileRead
        };

        // Act
        var result = await _guard.CheckAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsAllowed);
        Assert.Equal(GuardAction.Allow, result.Action);
    }

    [Fact]
    public async System.Threading.Tasks.Task CheckAsync_WithoutToolName_ShouldAllow()
    {
        // Arrange
        var context = new GuardContext
        {
            Phase = GuardPhase.ToolExecution,
            ToolName = null
        };

        // Act
        var result = await _guard.CheckAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async System.Threading.Tasks.Task CheckAsync_WithEmptyToolName_ShouldAllow()
    {
        // Arrange
        var context = new GuardContext
        {
            Phase = GuardPhase.ToolExecution,
            ToolName = ""
        };

        // Act
        var result = await _guard.CheckAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async System.Threading.Tasks.Task CheckAsync_WithToolExecutionPhaseAndNoPolicyRestriction_ShouldAllow()
    {
        // Arrange — global policy has neither allow- nor block-list, so access is unrestricted.
        var context = new GuardContext
        {
            Phase = GuardPhase.ToolExecution,
            ToolName = ToolFileRead
        };

        // Act
        var result = await _guard.CheckAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async System.Threading.Tasks.Task CheckAsync_WithBlacklistedTool_ShouldBlock()
    {
        // Arrange — global policy blocks the tool by name.
        var policy = new GuardianPolicy { BlockedTools = [ToolFileWrite] };
        var guard = new ToolAccessGuard(new GuardianPolicyEngine(policy));
        var context = new GuardContext
        {
            Phase = GuardPhase.ToolExecution,
            ToolName = ToolFileWrite
        };

        // Act
        var result = await guard.CheckAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Equal(GuardAction.Block, result.Action);
        Assert.Single(result.Violations);
        Assert.Equal(nameof(ToolAccessGuard), result.Violations[0].GuardName);
    }

    [Fact]
    public async System.Threading.Tasks.Task CheckAsync_WithWhitelistedToolNotInList_ShouldBlock()
    {
        // Arrange — only ToolFileRead is whitelisted; any other tool is denied.
        var policy = new GuardianPolicy { AllowedTools = [ToolFileRead] };
        var guard = new ToolAccessGuard(new GuardianPolicyEngine(policy));
        var context = new GuardContext
        {
            Phase = GuardPhase.ToolExecution,
            ToolName = "DatabaseDelete"
        };

        // Act
        var result = await guard.CheckAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Equal(GuardAction.Block, result.Action);
    }

    [Fact]
    public async System.Threading.Tasks.Task CheckAsync_WithPerCrewPolicy_ShouldResolveOverride()
    {
        // Arrange — crew-specific policy blocks a tool only for that crew.
        const string crewId = "crew-42";
        var engine = new GuardianPolicyEngine(new GuardianPolicy());
        engine.SetCrewPolicy(crewId, new GuardianPolicy { BlockedTools = [ToolFileWrite] });
        var guard = new ToolAccessGuard(engine);
        var context = new GuardContext
        {
            Phase = GuardPhase.ToolExecution,
            CrewId = crewId,
            ToolName = ToolFileWrite
        };

        // Act
        var result = await guard.CheckAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Equal(GuardAction.Block, result.Action);
    }

    #endregion

    #region CheckToolAccess Extension Tests

    [Fact]
    public void CheckToolAccess_WithNullPolicy_ShouldAllow()
    {
        // Arrange
        ToolAccessPolicy? policy = null;

        // Act
        var result = policy.CheckToolAccess(ToolFileRead);

        // Assert
        Assert.True(result.IsAllowed);
        Assert.Equal(GuardAction.Allow, result.Action);
    }

    [Fact]
    public void CheckToolAccess_WithUnrestrictedPolicy_ShouldAllow()
    {
        // Arrange
        var policy = ToolAccessPolicy.CreateUnrestricted();

        // Act
        var result = policy.CheckToolAccess("AnyTool");

        // Assert
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void CheckToolAccess_WithWhitelistPolicyAndAllowedTool_ShouldAllow()
    {
        // Arrange
        var policy = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolWebScrape);

        // Act
        var result = policy.CheckToolAccess(ToolFileRead);

        // Assert
        Assert.True(result.IsAllowed);
        Assert.Equal(GuardAction.Allow, result.Action);
    }

    [Fact]
    public void CheckToolAccess_WithWhitelistPolicyAndDeniedTool_ShouldBlock()
    {
        // Arrange
        var policy = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolWebScrape);

        // Act
        var result = policy.CheckToolAccess("DatabaseDelete");

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Equal(GuardAction.Block, result.Action);
        Assert.NotNull(result.Reason);
        Assert.Contains("not in the whitelist", result.Reason);
    }

    [Fact]
    public void CheckToolAccess_WithBlacklistPolicyAndAllowedTool_ShouldAllow()
    {
        // Arrange
        var policy = ToolAccessPolicy.CreateBlacklist(ToolFileWrite, "DatabaseDelete");

        // Act
        var result = policy.CheckToolAccess(ToolFileRead);

        // Assert
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void CheckToolAccess_WithBlacklistPolicyAndDeniedTool_ShouldBlock()
    {
        // Arrange
        var policy = ToolAccessPolicy.CreateBlacklist(ToolFileWrite, "DatabaseDelete");

        // Act
        var result = policy.CheckToolAccess(ToolFileWrite);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Equal(GuardAction.Block, result.Action);
        Assert.Contains("in the blacklist", result.Reason);
    }

    [Fact]
    public void CheckToolAccess_WithBlockedTool_ShouldReturnViolation()
    {
        // Arrange
        var policy = ToolAccessPolicy.CreateBlacklist("DangerousTool");

        // Act
        var result = policy.CheckToolAccess("DangerousTool");

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Single(result.Violations);
        var violation = result.Violations[0];
        Assert.Equal(nameof(ToolAccessGuard), violation.GuardName);
        Assert.Equal(GuardPhase.ToolExecution, violation.Phase);
        Assert.Equal(GuardThreatSeverity.High, violation.Severity);
    }

    [Fact]
    public void CheckToolAccess_WithNullToolName_ShouldThrow()
    {
        // Arrange
        var policy = ToolAccessPolicy.CreateUnrestricted();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => policy.CheckToolAccess(null!));
    }

    [Fact]
    public void CheckToolAccess_WithEmptyToolName_ShouldThrow()
    {
        // Arrange
        var policy = ToolAccessPolicy.CreateUnrestricted();

        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => policy.CheckToolAccess(""));
    }

    [Fact]
    public void CheckToolAccess_WithWildcardPattern_ShouldMatchCorrectly()
    {
        // Arrange
        var policy = ToolAccessPolicy.CreateBlacklist("File*", "*Delete*");

        // Act & Assert
        Assert.False(policy.CheckToolAccess(ToolFileRead).IsAllowed);
        Assert.False(policy.CheckToolAccess(ToolFileWrite).IsAllowed);
        Assert.False(policy.CheckToolAccess("DatabaseDelete").IsAllowed);
        Assert.True(policy.CheckToolAccess(ToolWebScrape).IsAllowed);
    }

    #endregion

    #region GetEffectivePolicy Extension Tests

    [Fact]
    public void GetEffectivePolicy_WithoutCrewOverride_ShouldReturnAgentPolicy()
    {
        // Arrange
        var agentPolicy = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolWebScrape);

        // Act
        var effective = agentPolicy.GetEffectivePolicy(null);

        // Assert
        Assert.Same(agentPolicy, effective);
    }

    [Fact]
    public void GetEffectivePolicy_WithCrewOverride_ShouldIntersectPolicies()
    {
        // Arrange — agent allows FileRead + WebScrape, crew blocks FileWrite
        var agentPolicy = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolWebScrape);
        var crewOverride = ToolAccessPolicy.CreateBlacklist(ToolFileWrite);

        // Act
        var effective = agentPolicy.GetEffectivePolicy(crewOverride);

        // Assert — intersection: tool must be allowed by both policies
        Assert.True(effective.IsToolAllowed(ToolFileRead));   // agent allows, crew allows
        Assert.True(effective.IsToolAllowed(ToolWebScrape));  // agent allows, crew allows
        Assert.False(effective.IsToolAllowed(ToolFileWrite)); // agent denies (not in whitelist)
        Assert.False(effective.IsToolAllowed(ToolDatabaseQuery)); // agent denies (not in whitelist)
    }

    [Fact]
    public void GetEffectivePolicy_CrewOverrideTakePrecedence()
    {
        // Arrange
        var agentPolicy = ToolAccessPolicy.CreateUnrestricted();
        var crewOverride = ToolAccessPolicy.CreateWhitelist("OnlyAllowedTool");

        // Act
        var effective = agentPolicy.GetEffectivePolicy(crewOverride);

        // Assert - Crew override should be used
        Assert.Equal(ToolAccessMode.Whitelist, effective.Mode);
        Assert.True(effective.IsToolAllowed("OnlyAllowedTool"));
        Assert.False(effective.IsToolAllowed("AnyOtherTool"));
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void CheckToolAccess_WithComplexWhitelistPatterns()
    {
        // Arrange
        var policy = ToolAccessPolicy.CreateWhitelist(
            "File*",
            "Web*",
            "*Query",
            "Email*"
        );

        // Act & Assert
        Assert.True(policy.CheckToolAccess(ToolFileRead).IsAllowed);
        Assert.True(policy.CheckToolAccess(ToolWebScrape).IsAllowed);
        Assert.True(policy.CheckToolAccess(ToolDatabaseQuery).IsAllowed);
        Assert.True(policy.CheckToolAccess("EmailSend").IsAllowed);
        Assert.False(policy.CheckToolAccess("ApiCall").IsAllowed);
    }

    [Fact]
    public void CheckToolAccess_WithComplexBlacklistPatterns()
    {
        // Arrange
        var policy = ToolAccessPolicy.CreateBlacklist(
            "*Delete*",
            "*Drop*",
            "System*",
            "*Admin*"
        );

        // Act & Assert
        Assert.False(policy.CheckToolAccess("DatabaseDelete").IsAllowed);
        Assert.False(policy.CheckToolAccess("DropTable").IsAllowed);
        Assert.False(policy.CheckToolAccess("SystemCommand").IsAllowed);
        Assert.False(policy.CheckToolAccess("AdminPanel").IsAllowed);
        Assert.True(policy.CheckToolAccess(ToolFileRead).IsAllowed);
        Assert.True(policy.CheckToolAccess(ToolWebScrape).IsAllowed);
    }

    [Fact]
    public void CombinedPolicies_AgentAndCrewLevelControl()
    {
        // Arrange - Agent has whitelist, crew has override
        var agentPolicy = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolFileWrite, ToolWebScrape);
        var crewOverride = ToolAccessPolicy.CreateBlacklist(ToolFileWrite);

        // Act
        var effective = agentPolicy.GetEffectivePolicy(crewOverride);

        // Assert - Use crew policy
        Assert.False(effective.CheckToolAccess(ToolFileWrite).IsAllowed);
        Assert.True(effective.CheckToolAccess(ToolFileRead).IsAllowed);
        Assert.True(effective.CheckToolAccess(ToolWebScrape).IsAllowed);
    }

    #endregion

    #region Concurrency and Thread Safety

    [Fact]
    public async System.Threading.Tasks.Task CheckAsync_ConcurrentCalls_ShouldBeThreadSafe()
    {
        // Arrange
        var tasks = new List<Task<GuardResult>>();
        var contexts = new[]
        {
            new GuardContext { Phase = GuardPhase.ToolExecution, ToolName = "Tool1" },
            new GuardContext { Phase = GuardPhase.ToolExecution, ToolName = "Tool2" },
            new GuardContext { Phase = GuardPhase.Input, ToolName = null }
        };

        // Act
        for (int i = 0; i < 100; i++)
        {
            var context = contexts[i % contexts.Length];
            tasks.Add(_guard.CheckAsync(context, TestContext.Current.CancellationToken));
        }

        var results = await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert - All should complete without errors
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.NotNull(r));
    }

    #endregion
}
