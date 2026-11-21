using Orkeon.Hosting.ToolBridge;

namespace Orkeon.Hosting.Tests.ToolBridge;

/// <summary>#445 - the settings of both sides, judged at start.</summary>
public sealed class ToolBridgeOptionsTests
{
    [Theory]
    [InlineData("http://192.168.1.20:7410/", "requires https")]
    [InlineData("https://bridge.example:7410", "must end with '/'")]
    [InlineData("bridge:7410", "not an absolute http or https address")]
    public void ShouldRefuseThePrefix_WhenItIsNotAnAcceptableAddress(string prefix, string expected)
    {
        // Arrange
        var options = new ToolBridgeServerOptions { Prefix = prefix };

        // Act
        var problems = options.Problems().ToList();

        // Assert
        Assert.Contains(expected, Assert.Single(problems), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://127.0.0.1:7410/")]
    [InlineData("http://localhost:7410/")]
    [InlineData("https://bridge.example:7410/")]
    public void ShouldAcceptTheAddress_WhenItIsLoopbackOrHttps(string address)
    {
        // Arrange
        var options = new ToolBridgeClientOptions { Address = address };

        // Act
        var problems = options.Problems().ToList();

        // Assert
        Assert.Empty(problems);
    }

    [Fact]
    public void ShouldBridgeNothing_WhenAllowIsEmpty()
    {
        // Arrange
        var options = new ToolBridgeClientOptions { Address = "http://127.0.0.1:7410/" };

        // Act
        var enabled = options.IsEnabled;

        // Assert
        Assert.False(enabled);
    }
}
