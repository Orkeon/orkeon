using Orkeon.Domain.Tools;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Hosting.ToolBridge;
using Orkeon.Tests.Shared.Doubles;
using ParameterSchema = Orkeon.Domain.Tools.Protocol.ParameterSchema;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;

namespace Orkeon.Hosting.Tests.ToolBridge;

/// <summary>#445 - the contract hash covers the schema's shape and the access table, through wrappers, not the prose.</summary>
public sealed class ToolBridgeContractTests
{
    [Fact]
    public void ShouldHashAToolAsItsWrapper_WhenAHostWrapsIt()
    {
        // Arrange
        var tool = new FakeBridgeTool("firefox");

        // Act
        var wrapped = ToolBridgeContract.HashOf(new StubWrapperTool(tool));

        // Assert
        Assert.Equal(ToolBridgeContract.HashOf(tool), wrapped);
    }

    [Fact]
    public void ShouldChangeTheHash_WhenTheAccessOfAnActionDiffers()
    {
        // Arrange
        var tool = new FakeBridgeTool("firefox") { WriteAccess = ToolAccess.Execute };

        // Act
        var hash = ToolBridgeContract.HashOf(tool);

        // Assert
        Assert.NotEqual(ToolBridgeContract.HashOf(new FakeBridgeTool("firefox")), hash);
    }

    [Fact]
    public void ShouldKeepTheHash_WhenOnlyTheDescriptionsDiffer()
    {
        // Arrange
        var tool = new FakeBridgeTool("firefox")
        {
            Schema = new ToolSchema("firefox", "another description", new Dictionary<string, ParameterSchema>
            {
                ["action"] = new("string", "another text", Required: true, Enum: ["read", "write"]),
                ["text"] = new("string", "yet another", Required: false),
            }),
        };

        // Act
        var hash = ToolBridgeContract.HashOf(tool);

        // Assert
        Assert.Equal(ToolBridgeContract.HashOf(new FakeBridgeTool("firefox")), hash);
    }
}
