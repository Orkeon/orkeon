using Orkeon.Domain.Tools;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Hosting.ToolBridge;

namespace Orkeon.Hosting.Tests.ToolBridge;

/// <summary>#445 - the selection both sides apply: no Allow selects nothing, '*' everything but the workspace, Deny wins.</summary>
public sealed class ToolBridgeSelectionTests
{
    private const string Side = "Orkeon:Bridge";

    private static readonly IReadOnlyList<IBaseTool> Tools =
        [new FakeBridgeTool("firefox"), new FakeBridgeTool("teams_chat"), new FakeWorkspaceBridgeTool()];

    private static string[] Names(ToolBridgeSelectionResult result) => [.. result.Tools.Select(t => t.Name)];

    [Fact]
    public void ShouldSelectNothing_WhenAllowIsEmpty()
    {
        // Arrange
        string[] allow = [];

        // Act
        var result = ToolBridgeSelection.Resolve(Side, Tools, allow, []);

        // Assert
        Assert.Empty(result.Tools);
    }

    [Fact]
    public void ShouldSelectEveryToolButTheWorkspaceOnes_WhenAllowIsTheLoneStar()
    {
        // Arrange
        string[] allow = ["*"];

        // Act
        var result = ToolBridgeSelection.Resolve(Side, Tools, allow, []);

        // Assert
        Assert.Equal(["firefox", "teams_chat"], Names(result));
    }

    [Fact]
    public void ShouldLetDenyWin_WhenAToolIsAllowedAndDenied()
    {
        // Arrange
        string[] allow = ["*", "firefox"];

        // Act
        var result = ToolBridgeSelection.Resolve(Side, Tools, allow, ["firefox"]);

        // Assert
        Assert.Equal(["teams_chat"], Names(result));
    }

    [Fact]
    public void ShouldSelectAWorkspaceToolWithAWarning_WhenItIsNamed()
    {
        // Arrange
        string[] allow = ["workspace_tool"];

        // Act
        var result = ToolBridgeSelection.Resolve(Side, Tools, allow, []);

        // Assert
        Assert.Equal(["workspace_tool"], Names(result));
        Assert.Contains("workspace_tool", Assert.Single(result.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldRefuseAName_WhenItIsNoToolOfTheRegistry()
    {
        // Arrange
        string[] allow = ["firefx"];

        // Act
        var result = ToolBridgeSelection.Resolve(Side, Tools, allow, []);

        // Assert
        Assert.Contains("'firefx'", Assert.Single(result.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldRefuseAPattern_WhenAnEntryIsNotTheLoneStar()
    {
        // Arrange
        string[] allow = ["teams_*"];

        // Act
        var problems = ToolBridgeSelection.SyntaxProblems(Side, allow, []).ToList();

        // Assert
        Assert.Contains("'teams_*'", Assert.Single(problems), StringComparison.Ordinal);
    }
}
