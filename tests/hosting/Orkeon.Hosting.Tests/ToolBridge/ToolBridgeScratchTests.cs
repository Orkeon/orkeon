using System.Globalization;
using Orkeon.Hosting.ToolBridge;

namespace Orkeon.Hosting.Tests.ToolBridge;

/// <summary>#445 - the folder the bridge makes its scratches in.</summary>
public sealed class ToolBridgeScratchTests
{
    [Fact]
    public void ShouldNameTheParentUnpredictably_WhenItIsCreated()
    {
        // Arrange
        var predictable = "orkeon-bridge-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture);

        // Act
        var parent = ToolBridgeScratch.Parent;

        // Assert
        Assert.NotEqual(predictable, Path.GetFileName(parent));
    }

    [Fact]
    public void ShouldKeepTheParentToItsOwner_WhenItIsCreated()
    {
        // Arrange
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no Unix mode; its temporary folder is the user's own.");

        // Act
        var mode = OperatingSystem.IsWindows() ? default : File.GetUnixFileMode(ToolBridgeScratch.Parent);

        // Assert
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, mode);
    }
}
