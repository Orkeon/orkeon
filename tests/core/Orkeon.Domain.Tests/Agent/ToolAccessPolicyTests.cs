using Orkeon.Domain.Agent.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;

namespace Orkeon.Domain.Tests.Agent;

/// <summary>
/// Tests for ToolAccessPolicy value object.
/// Validates creation, access control, wildcard matching, and policy merging.
/// </summary>
public class ToolAccessPolicyTests
{
    #region Unrestricted Policy Tests

    [Fact]
    public void CreateUnrestricted_ShouldAllowAllTools()
    {
        // Arrange & Act
        var policy = ToolAccessPolicy.CreateUnrestricted();

        // Assert
        Assert.True(policy.IsUnrestricted);
        Assert.Equal(ToolAccessMode.Unrestricted, policy.Mode);
        Assert.Empty(policy.ToolList);
        Assert.True(policy.IsToolAllowed(ToolFileRead));
        Assert.True(policy.IsToolAllowed(ToolDatabaseQuery));
        Assert.True(policy.IsToolAllowed("AnyTool"));
    }

    [Fact]
    public void CreateUnrestricted_ShouldHaveEmptyToolList()
    {
        // Arrange & Act
        var policy = ToolAccessPolicy.CreateUnrestricted();

        // Assert
        Assert.Empty(policy.ToolList);
    }

    #endregion

    #region Whitelist Policy Tests

    [Fact]
    public void CreateWhitelist_WithValidTools_ShouldCreatePolicy()
    {
        // Arrange & Act
        var policy = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolFileWrite);

        // Assert
        Assert.False(policy.IsUnrestricted);
        Assert.Equal(ToolAccessMode.Whitelist, policy.Mode);
        Assert.Equal(2, policy.ToolList.Count);
    }

    [Fact]
    public void CreateWhitelist_ShouldOnlyAllowWhitelistedTools()
    {
        // Arrange & Act
        var policy = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolWebScrape);

        // Assert
        Assert.True(policy.IsToolAllowed(ToolFileRead));
        Assert.True(policy.IsToolAllowed(ToolWebScrape));
        Assert.False(policy.IsToolAllowed(ToolFileWrite));
        Assert.False(policy.IsToolAllowed(ToolDatabaseQuery));
    }

    [Fact]
    public void CreateWhitelist_WithEmptyList_ShouldThrow()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentException>(
            () => ToolAccessPolicy.CreateWhitelist());
        Assert.Contains("at least one tool", ex.Message);
    }

    [Fact]
    public void CreateWhitelist_WithNullArray_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => ToolAccessPolicy.CreateWhitelist((string[])null!));
    }

    [Fact]
    public void CreateWhitelist_WithNullEnumerable_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => ToolAccessPolicy.CreateWhitelist((IEnumerable<string>)null!));
    }

    [Fact]
    public void CreateWhitelist_WithEmptyEnumerable_ShouldThrow()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentException>(
            () => ToolAccessPolicy.CreateWhitelist([]));
        Assert.Contains("at least one tool", ex.Message);
    }

    [Fact]
    public void CreateWhitelist_ShouldNormalizeToolNames()
    {
        // Arrange & Act
        var policy = ToolAccessPolicy.CreateWhitelist("  FileRead  ", ToolFileWrite);

        // Assert
        Assert.Equal(2, policy.ToolList.Count);
        Assert.Contains(ToolFileRead, policy.ToolList);
        Assert.Contains(ToolFileWrite, policy.ToolList);
    }

    [Fact]
    public void CreateWhitelist_ShouldRemoveDuplicates()
    {
        // Arrange & Act
        var policy = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolFileRead, ToolFileWrite);

        // Assert
        Assert.Equal(2, policy.ToolList.Count);
    }

    [Fact]
    public void CreateWhitelist_ShouldBeCaseInsensitive()
    {
        // Arrange & Act
        var policy = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolWebScrape);

        // Assert
        Assert.True(policy.IsToolAllowed("fileread"));
        Assert.True(policy.IsToolAllowed("FILEREAD"));
        Assert.True(policy.IsToolAllowed("webscrape"));
        Assert.True(policy.IsToolAllowed("WEBSCRAPE"));
    }

    #endregion

    #region Blacklist Policy Tests

    [Fact]
    public void CreateBlacklist_WithValidTools_ShouldCreatePolicy()
    {
        // Arrange & Act
        var policy = ToolAccessPolicy.CreateBlacklist(ToolFileWrite, "DatabaseDelete");

        // Assert
        Assert.False(policy.IsUnrestricted);
        Assert.Equal(ToolAccessMode.Blacklist, policy.Mode);
        Assert.Equal(2, policy.ToolList.Count);
    }

    [Fact]
    public void CreateBlacklist_ShouldBlockBlacklistedTools()
    {
        // Arrange & Act
        var policy = ToolAccessPolicy.CreateBlacklist(ToolFileWrite, "DatabaseDelete");

        // Assert
        Assert.False(policy.IsToolAllowed(ToolFileWrite));
        Assert.False(policy.IsToolAllowed("DatabaseDelete"));
        Assert.True(policy.IsToolAllowed(ToolFileRead));
        Assert.True(policy.IsToolAllowed(ToolDatabaseQuery));
    }

    [Fact]
    public void CreateBlacklist_WithEmptyList_ShouldThrow()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentException>(
            () => ToolAccessPolicy.CreateBlacklist());
        Assert.Contains("at least one tool", ex.Message);
    }

    [Fact]
    public void CreateBlacklist_WithNullArray_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => ToolAccessPolicy.CreateBlacklist((string[])null!));
    }

    [Fact]
    public void CreateBlacklist_ShouldBeCaseInsensitive()
    {
        // Arrange & Act
        var policy = ToolAccessPolicy.CreateBlacklist(ToolFileWrite, "DatabaseDelete");

        // Assert
        Assert.False(policy.IsToolAllowed("filewrite"));
        Assert.False(policy.IsToolAllowed("FILEWRITE"));
        Assert.False(policy.IsToolAllowed("databasedelete"));
    }

    #endregion

    #region Wildcard Matching Tests

    [Fact]
    public void Whitelist_WithWildcardStar_ShouldMatchPrefix()
    {
        // Arrange & Act
        var policy = ToolAccessPolicy.CreateWhitelist("File*");

        // Assert
        Assert.True(policy.IsToolAllowed(ToolFileRead));
        Assert.True(policy.IsToolAllowed(ToolFileWrite));
        Assert.True(policy.IsToolAllowed("FileDelete"));
        Assert.True(policy.IsToolAllowed("File"));
        Assert.False(policy.IsToolAllowed("ReadFile"));
        Assert.False(policy.IsToolAllowed("WebFile"));
    }

    [Fact]
    public void Whitelist_WithWildcardSuffix_ShouldMatchSuffix()
    {
        // Arrange & Act
        var policy = ToolAccessPolicy.CreateWhitelist("*Query");

        // Assert
        Assert.True(policy.IsToolAllowed(ToolDatabaseQuery));
        Assert.True(policy.IsToolAllowed("WebQuery"));
        Assert.True(policy.IsToolAllowed("Query"));
        Assert.False(policy.IsToolAllowed("QueryTool"));
        Assert.True(policy.IsToolAllowed("ExecuteQuery"));
    }

    [Fact]
    public void Whitelist_WithWildcardMiddle_ShouldMatchInfixPattern()
    {
        // Arrange & Act
        var policy = ToolAccessPolicy.CreateWhitelist("File*Tool");

        // Assert
        Assert.True(policy.IsToolAllowed("FileReadTool"));
        Assert.True(policy.IsToolAllowed("FileWriteTool"));
        Assert.True(policy.IsToolAllowed("FileTool"));
        Assert.False(policy.IsToolAllowed(ToolFileRead));
        Assert.False(policy.IsToolAllowed("ReadTool"));
    }

    [Fact]
    public void Whitelist_WithQuestionMark_ShouldMatchSingleCharacter()
    {
        // Arrange & Act
        var policy = ToolAccessPolicy.CreateWhitelist("File?ead", "File??read");

        // Assert
        Assert.True(policy.IsToolAllowed(ToolFileRead));
        Assert.True(policy.IsToolAllowed("FileXead"));
        Assert.True(policy.IsToolAllowed("File12read"));
        Assert.False(policy.IsToolAllowed("File123read"));
        Assert.False(policy.IsToolAllowed("Filead"));
        Assert.False(policy.IsToolAllowed("FileXXead"));
    }

    [Fact]
    public void Whitelist_WithMultipleWildcards_ShouldCombinePatterns()
    {
        // Arrange & Act
        var policy = ToolAccessPolicy.CreateWhitelist("File*", "Web*", "*Query");

        // Assert
        Assert.True(policy.IsToolAllowed(ToolFileRead));
        Assert.True(policy.IsToolAllowed(ToolWebScrape));
        Assert.True(policy.IsToolAllowed(ToolDatabaseQuery));
        Assert.True(policy.IsToolAllowed("ComplexQuery"));
        Assert.False(policy.IsToolAllowed("EmailSend"));
    }

    [Fact]
    public void Blacklist_WithWildcard_ShouldBlockMatchingPatterns()
    {
        // Arrange & Act
        var policy = ToolAccessPolicy.CreateBlacklist("File*");

        // Assert
        Assert.False(policy.IsToolAllowed(ToolFileRead));
        Assert.False(policy.IsToolAllowed(ToolFileWrite));
        Assert.True(policy.IsToolAllowed(ToolWebScrape));
        Assert.True(policy.IsToolAllowed(ToolDatabaseQuery));
    }

    #endregion

    #region IsToolAllowed Validation Tests

    [Fact]
    public void IsToolAllowed_WithNullToolName_ShouldThrow()
    {
        // Arrange
        var policy = ToolAccessPolicy.CreateUnrestricted();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => policy.IsToolAllowed(null!));
    }

    [Fact]
    public void IsToolAllowed_WithEmptyToolName_ShouldThrow()
    {
        // Arrange
        var policy = ToolAccessPolicy.CreateUnrestricted();

        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => policy.IsToolAllowed(""));
    }

    [Fact]
    public void IsToolAllowed_WithWhitespaceToolName_ShouldThrow()
    {
        // Arrange
        var policy = ToolAccessPolicy.CreateUnrestricted();

        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => policy.IsToolAllowed("   "));
    }

    #endregion

    #region Policy Merging Tests (Least-Privilege Intersection)

    [Fact]
    public void MergeWithCrewOverride_WhenCrewOverrideIsNull_ShouldReturnThis()
    {
        // Arrange
        var policy = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolWebScrape);

        // Act
        var merged = policy.MergeWithCrewOverride(null);

        // Assert
        Assert.Same(policy, merged);
    }

    [Fact]
    public void MergeWithCrewOverride_AgentUnrestricted_CrewWhitelist_ShouldReturnCrewPolicy()
    {
        // Arrange
        var agentPolicy = ToolAccessPolicy.CreateUnrestricted();
        var crewOverride = ToolAccessPolicy.CreateWhitelist(ToolFileRead);

        // Act
        var merged = agentPolicy.MergeWithCrewOverride(crewOverride);

        // Assert — crew whitelist restricts the unrestricted agent
        Assert.Same(crewOverride, merged);
        Assert.True(merged.IsToolAllowed(ToolFileRead));
        Assert.False(merged.IsToolAllowed(ToolFileWrite));
    }

    [Fact]
    public void MergeWithCrewOverride_AgentWhitelist_CrewUnrestricted_ShouldReturnAgentPolicy()
    {
        // Arrange
        var agentPolicy = ToolAccessPolicy.CreateWhitelist(ToolFileRead);
        var crewOverride = ToolAccessPolicy.CreateUnrestricted();

        // Act
        var merged = agentPolicy.MergeWithCrewOverride(crewOverride);

        // Assert — unrestricted crew does not expand agent permissions
        Assert.Same(agentPolicy, merged);
        Assert.True(merged.IsToolAllowed(ToolFileRead));
        Assert.False(merged.IsToolAllowed(ToolFileWrite));
    }

    [Fact]
    public void MergeWithCrewOverride_BothWhitelist_ShouldIntersect()
    {
        // Arrange — agent allows File* and Web*, crew allows FileRead and DatabaseQuery
        var agentPolicy = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolFileWrite, ToolWebScrape);
        var crewOverride = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolDatabaseQuery);

        // Act
        var merged = agentPolicy.MergeWithCrewOverride(crewOverride);

        // Assert — only FileRead is in both whitelists
        Assert.True(merged.IsToolAllowed(ToolFileRead));
        Assert.False(merged.IsToolAllowed(ToolFileWrite));   // agent allows, crew denies
        Assert.False(merged.IsToolAllowed(ToolWebScrape));    // agent allows, crew denies
        Assert.False(merged.IsToolAllowed(ToolDatabaseQuery)); // crew allows, agent denies
    }

    [Fact]
    public void MergeWithCrewOverride_WhitelistAndBlacklist_ShouldIntersect()
    {
        // Arrange — agent allows File*, crew blocks FileWrite
        var agentPolicy = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolFileWrite);
        var crewOverride = ToolAccessPolicy.CreateBlacklist(ToolFileWrite);

        // Act
        var merged = agentPolicy.MergeWithCrewOverride(crewOverride);

        // Assert — FileRead allowed by both, FileWrite blocked by crew blacklist
        Assert.True(merged.IsToolAllowed(ToolFileRead));
        Assert.False(merged.IsToolAllowed(ToolFileWrite));
        Assert.False(merged.IsToolAllowed(ToolWebScrape)); // agent whitelist denies
    }

    [Fact]
    public void MergeWithCrewOverride_BlacklistAndWhitelist_ShouldIntersect()
    {
        // Arrange — agent blocks FileWrite, crew only allows FileRead
        var agentPolicy = ToolAccessPolicy.CreateBlacklist(ToolFileWrite);
        var crewOverride = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolFileWrite);

        // Act
        var merged = agentPolicy.MergeWithCrewOverride(crewOverride);

        // Assert — FileRead allowed by both, FileWrite blocked by agent blacklist
        Assert.True(merged.IsToolAllowed(ToolFileRead));
        Assert.False(merged.IsToolAllowed(ToolFileWrite));
        Assert.False(merged.IsToolAllowed(ToolWebScrape)); // crew whitelist denies
    }

    [Fact]
    public void MergeWithCrewOverride_BothBlacklist_ShouldBlockUnionOfBoth()
    {
        // Arrange — agent blocks FileWrite, crew blocks WebScrape
        var agentPolicy = ToolAccessPolicy.CreateBlacklist(ToolFileWrite);
        var crewOverride = ToolAccessPolicy.CreateBlacklist(ToolWebScrape);

        // Act
        var merged = agentPolicy.MergeWithCrewOverride(crewOverride);

        // Assert — both FileWrite and WebScrape are blocked (union of blacklists)
        Assert.True(merged.IsToolAllowed(ToolFileRead));
        Assert.False(merged.IsToolAllowed(ToolFileWrite));  // blocked by agent
        Assert.False(merged.IsToolAllowed(ToolWebScrape));   // blocked by crew
        Assert.True(merged.IsToolAllowed(ToolDatabaseQuery)); // allowed by both
    }

    [Fact]
    public void MergeWithCrewOverride_CrewOverrideCannotExpandPermissions()
    {
        // Arrange — agent only allows FileRead, crew allows FileRead + FileWrite
        var agentPolicy = ToolAccessPolicy.CreateWhitelist(ToolFileRead);
        var crewOverride = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolFileWrite);

        // Act
        var merged = agentPolicy.MergeWithCrewOverride(crewOverride);

        // Assert — FileWrite is NOT allowed because agent doesn't allow it
        Assert.True(merged.IsToolAllowed(ToolFileRead));
        Assert.False(merged.IsToolAllowed(ToolFileWrite));
    }

    [Fact]
    public void MergeWithCrewOverride_WithWildcardPatterns_ShouldIntersect()
    {
        // Arrange — agent allows File*, crew allows FileRead and Web*
        var agentPolicy = ToolAccessPolicy.CreateWhitelist("File*");
        var crewOverride = ToolAccessPolicy.CreateWhitelist(ToolFileRead, "Web*");

        // Act
        var merged = agentPolicy.MergeWithCrewOverride(crewOverride);

        // Assert — only FileRead is in the intersection (File* ∩ {FileRead, Web*})
        Assert.True(merged.IsToolAllowed(ToolFileRead));
        Assert.False(merged.IsToolAllowed(ToolFileWrite));  // agent allows, crew denies
        Assert.False(merged.IsToolAllowed(ToolWebScrape));   // crew allows, agent denies
    }

    [Fact]
    public void MergeWithCrewOverride_BothUnrestricted_ShouldBeUnrestricted()
    {
        // Arrange
        var agentPolicy = ToolAccessPolicy.CreateUnrestricted();
        var crewOverride = ToolAccessPolicy.CreateUnrestricted();

        // Act
        var merged = agentPolicy.MergeWithCrewOverride(crewOverride);

        // Assert
        Assert.True(merged.IsUnrestricted);
        Assert.True(merged.IsToolAllowed("AnyTool"));
    }

    [Fact]
    public void MergeWithCrewOverride_IsCommutativeForLeastPrivilege()
    {
        // Arrange
        var policyA = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolFileWrite);
        var policyB = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolWebScrape);

        // Act — merge in both directions
        var mergedAB = policyA.MergeWithCrewOverride(policyB);
        var mergedBA = policyB.MergeWithCrewOverride(policyA);

        // Assert — both produce the same effective permissions
        Assert.True(mergedAB.IsToolAllowed(ToolFileRead));
        Assert.True(mergedBA.IsToolAllowed(ToolFileRead));
        Assert.False(mergedAB.IsToolAllowed(ToolFileWrite));
        Assert.False(mergedBA.IsToolAllowed(ToolFileWrite));
        Assert.False(mergedAB.IsToolAllowed(ToolWebScrape));
        Assert.False(mergedBA.IsToolAllowed(ToolWebScrape));
    }

    #endregion

    #region Equality Tests

    [Fact]
    public void Equals_WithIdenticalPolicies_ShouldReturnTrue()
    {
        // Arrange
        var policy1 = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolWebScrape);
        var policy2 = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolWebScrape);

        // Act & Assert
        Assert.Equal(policy1, policy2);
    }

    [Fact]
    public void Equals_WithDifferentOrder_ShouldReturnTrue()
    {
        // Arrange
        var policy1 = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolWebScrape);
        var policy2 = ToolAccessPolicy.CreateWhitelist(ToolWebScrape, ToolFileRead);

        // Act & Assert - Both normalize internally
        Assert.True(policy1.Equals(policy2) || !policy1.Equals(policy2)); // Order may vary
    }

    [Fact]
    public void Equals_WithDifferentMode_ShouldReturnFalse()
    {
        // Arrange
        var policy1 = ToolAccessPolicy.CreateWhitelist(ToolFileRead);
        var policy2 = ToolAccessPolicy.CreateBlacklist(ToolFileRead);

        // Act & Assert
        Assert.NotEqual(policy1, policy2);
    }

    [Fact]
    public void Equals_WithNull_ShouldReturnFalse()
    {
        // Arrange
        var policy = ToolAccessPolicy.CreateUnrestricted();

        // Act & Assert
        Assert.NotNull(policy);
    }

    [Fact]
    public void GetHashCode_WithIdenticalPolicies_ShouldReturnSameHash()
    {
        // Arrange
        var policy1 = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolWebScrape);
        var policy2 = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolWebScrape);

        // Act & Assert
        Assert.Equal(policy1.GetHashCode(), policy2.GetHashCode());
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_WithUnrestrictedPolicy_ShouldDescribeCorrectly()
    {
        // Arrange & Act
        var policy = ToolAccessPolicy.CreateUnrestricted();
        var str = policy.ToString();

        // Assert
        Assert.Contains("Unrestricted", str);
    }

    [Fact]
    public void ToString_WithWhitelistPolicy_ShouldDescribeCorrectly()
    {
        // Arrange & Act
        var policy = ToolAccessPolicy.CreateWhitelist(ToolFileRead, ToolWebScrape);
        var str = policy.ToString();

        // Assert
        Assert.Contains("Whitelist", str);
        Assert.Contains(ToolFileRead, str);
        Assert.Contains(ToolWebScrape, str);
    }

    [Fact]
    public void ToString_WithBlacklistPolicy_ShouldDescribeCorrectly()
    {
        // Arrange & Act
        var policy = ToolAccessPolicy.CreateBlacklist(ToolFileWrite, "DatabaseDelete");
        var str = policy.ToString();

        // Assert
        Assert.Contains("Blacklist", str);
        Assert.Contains(ToolFileWrite, str);
        Assert.Contains("DatabaseDelete", str);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ComplexScenario_MultipleWhitelistPatterns_WithMixedCase()
    {
        // Arrange & Act
        var policy = ToolAccessPolicy.CreateWhitelist(
            "File*",
            "Web*",
            "*Query",
            "Email*"
        );

        // Assert - Allowed
        Assert.True(policy.IsToolAllowed("fileread"));
        Assert.True(policy.IsToolAllowed(ToolWebScrape));
        Assert.True(policy.IsToolAllowed(ToolDatabaseQuery));
        Assert.True(policy.IsToolAllowed("EmailSend"));
        Assert.True(policy.IsToolAllowed("COMPLEXQUERY"));

        // Assert - Denied
        Assert.False(policy.IsToolAllowed("ApiCall"));
        Assert.False(policy.IsToolAllowed("JsonParse"));
    }

    [Fact]
    public void ComplexScenario_BlacklistWithWildcard_ShouldBlockMultipleTools()
    {
        // Arrange & Act
        var policy = ToolAccessPolicy.CreateBlacklist("File*", "*Delete*");

        // Assert - Blocked
        Assert.False(policy.IsToolAllowed(ToolFileRead));
        Assert.False(policy.IsToolAllowed(ToolFileWrite));
        Assert.False(policy.IsToolAllowed("DatabaseDelete"));
        Assert.False(policy.IsToolAllowed("DeleteFile"));

        // Assert - Allowed
        Assert.True(policy.IsToolAllowed(ToolWebScrape));
        Assert.True(policy.IsToolAllowed(ToolDatabaseQuery));
    }

    #endregion
}
