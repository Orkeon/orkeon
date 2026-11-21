using Orkeon.Hosting.ToolBridge;

namespace Orkeon.Hosting.Tests.ToolBridge;

/// <summary>#445 - what the caller's proxy takes from the bridge's answer, and how it compares the paths of a call.</summary>
public sealed class ToolBridgeProxyTests
{
    [Fact]
    public void ShouldNeverTakeTheBridgesWordOnBounding_WhenTheAnswerSaysAlreadyBounded()
    {
        // Arrange
        var answer = new BridgeCallResponse { Success = true, AlreadyBounded = true };

        // Act
        var response = ToolBridgeProxy.FromWire(answer);

        // Assert
        Assert.False(response.AlreadyBounded);
    }

    [Theory]
    [InlineData("/output/shot.png", "/", "/output/shot.png")]
    [InlineData("shot.png", "/output", "/output/shot.png")]
    [InlineData("./a/../shot.png", "/output/", "/output/shot.png")]
    [InlineData("/output//shots/", "/", "/output/shots")]
    [InlineData("/output/../../etc", "/", null)]
    public void ShouldNormaliseAPathOfTheCall_WhenItIsAbsoluteOrRelative(string path, string working, string? expected)
    {
        // Arrange
        var given = (path, working);

        // Act
        var normalised = VirtualPaths.Normalize(given.path, given.working);

        // Assert
        Assert.Equal(expected, normalised);
    }

    [Theory]
    [InlineData("/output/shots/1.png", "/output/shots", true)]
    [InlineData("/output/shots", "/output/shots", true)]
    [InlineData("/output/shots-old/1.png", "/output/shots", false)]
    [InlineData("/output/other.png", "/output/shot.png", false)]
    public void ShouldCoverAPath_WhenItIsOrLiesUnderTheNamedOne(string path, string named, bool expected)
    {
        // Arrange
        var given = (path, named);

        // Act
        var covered = VirtualPaths.IsAtOrUnder(given.path, given.named);

        // Assert
        Assert.Equal(expected, covered);
    }
}
