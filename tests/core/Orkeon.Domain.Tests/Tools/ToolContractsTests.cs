using Orkeon.Domain.Tests.Doubles;
using Orkeon.Domain.Tools;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Domain.Tests.Tools;

/// <summary>#445 - the workspace marker of a tool's contract, read through the wrappers a host puts around it.</summary>
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
}
