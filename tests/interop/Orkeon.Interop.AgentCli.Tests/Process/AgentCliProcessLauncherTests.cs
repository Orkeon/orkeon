using Orkeon.Interop.AgentCli.Process;

namespace Orkeon.Interop.AgentCli.Tests.Process;

/// <summary>The scratch folders the launcher creates never outlive the host.</summary>
public sealed class AgentCliProcessLauncherTests
{
    [Fact]
    public void ShouldRemoveLiveFolders_WhenHostExitsWithoutEndingSessions()
    {
        // Arrange
        var folder = Directory.CreateTempSubdirectory("orkeon-agent-cli-test-").FullName;
        File.WriteAllText(Path.Join(folder, "system-prompt.md"), "terse");
        AgentCliProcessLauncher.LiveFolders.Track(folder);

        // Act
        AgentCliProcessLauncher.LiveFolders.RemoveAll();

        // Assert
        Assert.False(Directory.Exists(folder));
        Assert.DoesNotContain(folder, AgentCliProcessLauncher.LiveFolders.Current);
    }

    [Fact]
    public void ShouldForgetFolder_WhenSessionEndsItself()
    {
        // Arrange
        var folder = Directory.CreateTempSubdirectory("orkeon-agent-cli-test-").FullName;
        AgentCliProcessLauncher.LiveFolders.Track(folder);

        // Act
        AgentCliProcessLauncher.TryDelete(folder);

        // Assert
        Assert.False(Directory.Exists(folder));
        Assert.DoesNotContain(folder, AgentCliProcessLauncher.LiveFolders.Current);
    }
}
