using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Common;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;

namespace Orkeon.Domain.Tests.Agent;

/// <summary>
/// Tests for AgentToolManager internal domain helper.
/// Validates tool addition, removal, duplicate detection, and read-only access.
/// </summary>
public class AgentToolManagerTests
{
    #region AddTool Tests

    [Fact]
    public void ShouldAddTool_WhenToolIsValid()
    {
        // Arrange
        var tools = new List<ITool>();
        var manager = new AgentToolManager(tools);
        var tool = new StubTool(ToolSearch);

        // Act
        var addedName = manager.AddTool(tool, RoleDeveloper);

        // Assert
        Assert.Single(manager.Tools);
        Assert.Equal(ToolSearch, addedName);
        Assert.Equal(ToolSearch, manager.Tools[0].Name);
    }

    [Fact]
    public void ShouldThrow_WhenAddingDuplicateTool()
    {
        // Arrange
        var tools = new List<ITool>();
        var manager = new AgentToolManager(tools);
        manager.AddTool(new StubTool(ToolSearch), RoleDeveloper);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => manager.AddTool(new StubTool(ToolSearch), RoleDeveloper));
        Assert.Contains("already exists", ex.Message);
        Assert.Contains(ToolSearch, ex.Message);
    }

    [Fact]
    public void ShouldThrow_WhenAddingNullTool()
    {
        // Arrange
        var tools = new List<ITool>();
        var manager = new AgentToolManager(tools);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => manager.AddTool(null!, RoleDeveloper));
    }

    [Fact]
    public void ShouldThrow_WhenConstructedWithNullList()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => new AgentToolManager(null!));
    }

    [Fact]
    public void ShouldAddMultipleTools_WhenToolsHaveDifferentNames()
    {
        // Arrange
        var tools = new List<ITool>();
        var manager = new AgentToolManager(tools);

        // Act
        manager.AddTool(new StubTool("ToolA"), RoleDeveloper);
        manager.AddTool(new StubTool("ToolB"), RoleDeveloper);
        manager.AddTool(new StubTool("ToolC"), RoleDeveloper);

        // Assert
        Assert.Equal(3, manager.Tools.Count);
    }

    #endregion

    #region ValidateToolPermissions / Policy Tests

    [Fact]
    public void ShouldAddTool_WhenPolicyAllowsIt()
    {
        // Arrange — whitelist that permits the tool.
        var policy = ToolAccessPolicy.CreateWhitelist(ToolSearch);
        var manager = new AgentToolManager([], () => policy);

        // Act
        var addedName = manager.AddTool(new StubTool(ToolSearch), RoleDeveloper);

        // Assert
        Assert.Equal(ToolSearch, addedName);
        Assert.Single(manager.Tools);
    }

    [Fact]
    public void ShouldThrowUnauthorized_WhenPolicyDeniesTool()
    {
        // Arrange — whitelist that does NOT include the tool being added.
        var policy = ToolAccessPolicy.CreateWhitelist("SomeOtherTool");
        var manager = new AgentToolManager([], () => policy);

        // Act & Assert — the guard is now live: a denied tool cannot be attached.
        var ex = Assert.Throws<UnauthorizedAccessException>(
            () => manager.AddTool(new StubTool(ToolSearch), RoleDeveloper));
        Assert.Contains("lacks permissions", ex.Message);
        Assert.Empty(manager.Tools);
    }

    [Fact]
    public void ShouldBlockTool_WhenBlacklistPolicyDeniesIt()
    {
        // Arrange — blacklist that blocks the tool by name.
        var policy = ToolAccessPolicy.CreateBlacklist(ToolSearch);
        var manager = new AgentToolManager([], () => policy);

        // Act & Assert
        Assert.False(manager.ValidateToolPermissions(new StubTool(ToolSearch)));
        Assert.True(manager.ValidateToolPermissions(new StubTool("AllowedTool")));
    }

    [Fact]
    public void ShouldAllowAnyTool_WhenNoPolicyProviderSupplied()
    {
        // Arrange — legacy construction without a policy provider is unrestricted.
        var manager = new AgentToolManager([]);

        // Act & Assert
        Assert.True(manager.ValidateToolPermissions(new StubTool(ToolSearch)));
    }

    #endregion

    #region RemoveTool Tests

    [Fact]
    public void ShouldRemoveTool_WhenToolExists()
    {
        // Arrange
        var tools = new List<ITool>();
        var manager = new AgentToolManager(tools);
        manager.AddTool(new StubTool(ToolSearch), RoleDeveloper);

        // Act
        var removedName = manager.RemoveTool(ToolSearch, RoleDeveloper);

        // Assert
        Assert.Empty(manager.Tools);
        Assert.Equal(ToolSearch, removedName);
    }

    [Fact]
    public void ShouldThrow_WhenRemovingNonExistentTool()
    {
        // Arrange
        var tools = new List<ITool>();
        var manager = new AgentToolManager(tools);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => manager.RemoveTool("NonExistent", RoleDeveloper));
        Assert.Contains("not found", ex.Message);
        Assert.Contains("NonExistent", ex.Message);
    }

    [Fact]
    public void ShouldOnlyRemoveSpecifiedTool_WhenMultipleToolsExist()
    {
        // Arrange
        var tools = new List<ITool>();
        var manager = new AgentToolManager(tools);
        manager.AddTool(new StubTool("ToolA"), RoleDeveloper);
        manager.AddTool(new StubTool("ToolB"), RoleDeveloper);
        manager.AddTool(new StubTool("ToolC"), RoleDeveloper);

        // Act
        manager.RemoveTool("ToolB", RoleDeveloper);

        // Assert
        Assert.Equal(2, manager.Tools.Count);
        Assert.True(manager.HasTool("ToolA"));
        Assert.False(manager.HasTool("ToolB"));
        Assert.True(manager.HasTool("ToolC"));
    }

    #endregion

    #region HasTool Tests

    [Fact]
    public void ShouldReturnTrue_WhenToolExists()
    {
        // Arrange
        var tools = new List<ITool>();
        var manager = new AgentToolManager(tools);
        manager.AddTool(new StubTool(ToolSearch), RoleDeveloper);

        // Act
        var result = manager.HasTool(ToolSearch);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldReturnFalse_WhenToolDoesNotExist()
    {
        // Arrange
        var tools = new List<ITool>();
        var manager = new AgentToolManager(tools);

        // Act
        var result = manager.HasTool("NonExistent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldReturnTrue_WhenToolNameMatchesCaseInsensitively()
    {
        // Arrange
        var tools = new List<ITool>();
        var manager = new AgentToolManager(tools);
        manager.AddTool(new StubTool(ToolSearch), RoleDeveloper);

        // Act & Assert
        Assert.True(manager.HasTool("searchtool"));
        Assert.True(manager.HasTool("SEARCHTOOL"));
        Assert.True(manager.HasTool(ToolSearch));
    }

    #endregion

    #region Tools ReadOnly Tests

    [Fact]
    public void ShouldReturnReadOnlyList_WhenAccessingTools()
    {
        // Arrange
        var tools = new List<ITool>();
        var manager = new AgentToolManager(tools);
        manager.AddTool(new StubTool("ToolA"), RoleDeveloper);

        // Act
        var result = manager.Tools;

        // Assert
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyCollection<ITool>>(result);
        Assert.Single(result);
    }

    [Fact]
    public void ShouldReturnEmptyReadOnlyList_WhenNoToolsAdded()
    {
        // Arrange
        var tools = new List<ITool>();
        var manager = new AgentToolManager(tools);

        // Act
        var result = manager.Tools;

        // Assert
        Assert.Empty(result);
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyCollection<ITool>>(result);
    }

    #endregion

    #region Test Doubles

    private sealed class StubTool : ITool
    {
        public string Name { get; }
        public string Description => $"Stub tool {Name}";
        public Orkeon.Domain.Tools.Protocol.ToolSchema Schema => new(Name, Description, []);

        public StubTool(string name) => Name = name;

        public System.Threading.Tasks.Task<Orkeon.Domain.Tools.Protocol.ToolCallResponse> CallAsync(
            Orkeon.Domain.Tools.Protocol.ToolCallRequest request,
            CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(
                new Orkeon.Domain.Tools.Protocol.ToolCallResponse(true, "ok", null));

        public System.Threading.Tasks.Task<Orkeon.Domain.Tools.ToolResult> ExecuteAsync(
            string input,
            CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(
                Orkeon.Domain.Tools.ToolResult.CreateSuccess("ok"));

        public bool ValidateInput(string input) => true;
    }

    #endregion
}
