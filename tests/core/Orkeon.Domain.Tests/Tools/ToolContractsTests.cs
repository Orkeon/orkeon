using Orkeon.Domain.Tests.Doubles;
using Orkeon.Domain.Tools;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Domain.Tests.Tools;

/// <summary>#445 - the markers of a tool's contract, read through the wrappers a host puts around it.</summary>
public sealed class ToolContractsTests
{
    [Fact]
    public void ShouldReadTheMarker_WhenTheToolIsWrapped()
    {
        // Arrange
        var wrapped = new StubWrapperTool(new StubWrapperTool(new StubWorkspaceTool()));

        // Act
        var acts = ToolContracts.ActsOnWorkspace(wrapped);

        // Assert
        Assert.True(acts);
    }

    [Fact]
    public void ShouldSayNo_WhenTheToolHasNoMarker()
    {
        // Arrange
        var tool = new StubBaseTool("firefox");

        // Act
        var acts = ToolContracts.ActsOnWorkspace(tool);

        // Assert
        Assert.False(acts);
    }

    [Fact]
    public void ShouldReadStaysWithCaller_WhenTheToolIsWrapped()
    {
        // Arrange
        var wrapped = new StubWrapperTool(new StubCallerTool());

        // Act
        var stays = ToolContracts.StaysWithCaller(wrapped);

        // Assert
        Assert.True(stays);
        Assert.False(ToolContracts.ActsOnWorkspace(wrapped));
    }

    [Fact]
    public void ShouldReadTheContractOfTheBaseClass_WhenTheClassDeclaresNone()
    {
        // Arrange
        var tool = new StubDerivedCallerTool();

        // Act
        var contract = ToolContracts.ContractOf(tool);

        // Assert
        Assert.Equal(("caller_stub", true), (contract?.UniqueName, ToolContracts.StaysWithCaller(tool)));
    }

    [Fact]
    public void ShouldFindNoContract_WhenNoClassOfTheToolDeclaresOne()
    {
        // Arrange
        var tool = new StubWrapperTool(new StubBaseTool("plugin_tool"));

        // Act
        var contract = ToolContracts.ContractOf(tool);

        // Assert
        Assert.Null(contract);
    }
}
