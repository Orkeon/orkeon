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

    private static FakeBridgeTool WithParameter(string name, ParameterSchema parameter) => new("firefox")
    {
        Schema = new ToolSchema("firefox", "a fake tool of the bridge", new Dictionary<string, ParameterSchema>
        {
            ["action"] = new("string", "what to do", Required: true, Enum: ["read", "write"]),
            [name] = parameter,
        }),
    };

    [Fact]
    public void ShouldChangeTheHash_WhenAParameterNamedDescriptionChangesItsType()
    {
        // Arrange
        var text = WithParameter("description", new ParameterSchema("string", "a text", Required: false));

        // Act
        var number = ToolBridgeContract.HashOf(WithParameter("description", new ParameterSchema("integer", "a text", Required: false)));

        // Assert
        Assert.NotEqual(ToolBridgeContract.HashOf(text), number);
    }

    [Fact]
    public void ShouldChangeTheHash_WhenOneSideCarriesFilesAndTheOtherDoesNot()
    {
        // Arrange
        var carries = new FakeBridgeTool("firefox") { UsesFileSystem = true };

        // Act
        var hash = ToolBridgeContract.HashOf(carries);

        // Assert
        Assert.NotEqual(ToolBridgeContract.HashOf(new FakeBridgeTool("firefox")), hash);
    }

    [Fact]
    public void ShouldChangeTheHash_WhenOneSideActsOnTheWorkspace()
    {
        // Arrange
        var workspace = new FakeWorkspaceBridgeTool();

        // Act
        var canonical = ToolBridgeContract.CanonicalOf(workspace);

        // Assert
        Assert.Contains("acts_on_workspace=true", canonical, StringComparison.Ordinal);
        Assert.Contains("acts_on_workspace=false", ToolBridgeContract.CanonicalOf(new FakeBridgeTool("workspace_tool")), StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldKeepTheHash_WhenOnlyTheOrderOfTheEnumValuesDiffers()
    {
        // Arrange
        var reversed = new FakeBridgeTool("firefox", "write", "read");

        // Act
        var hash = ToolBridgeContract.HashOf(reversed);

        // Assert
        Assert.Equal(ToolBridgeContract.HashOf(new FakeBridgeTool("firefox", "read", "write")), hash);
    }

    [Fact]
    public void ShouldSampleTheAccessOfAnEnumMap_WhenAParameterDeclaresOne()
    {
        // Arrange
        var tool = WithParameter("mode", new ParameterSchema("string", "a mode", Required: false, EnumMap: new() { ["fast"] = 0, ["safe"] = 1 }));

        // Act
        var table = ToolBridgeContract.AccessTable(tool);

        // Assert
        Assert.Contains(table, line => line.StartsWith("mode=fast:", StringComparison.Ordinal));
    }

    [Fact]
    public void ShouldChangeTheHash_WhenOnlyACombinationOfEnumValuesChangesItsAccess()
    {
        // Arrange
        var mode = new ParameterSchema("string", "a mode", Required: false, Enum: ["dry", "real"]);
        var plain = WithParameter("mode", mode);
        var joint = new FakeBridgeTool("firefox")
        {
            Schema = plain.Schema,
            AccessOf = a => a.GetValueOrDefault("action") as string == "write" && a.GetValueOrDefault("mode") as string == "real" ? ToolAccess.Execute : null,
        };

        // Act
        var hash = ToolBridgeContract.HashOf(joint);

        // Assert
        Assert.NotEqual(ToolBridgeContract.HashOf(plain), hash);
    }
}
