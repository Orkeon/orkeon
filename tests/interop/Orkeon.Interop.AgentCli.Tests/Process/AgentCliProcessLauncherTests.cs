using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Interop.AgentCli.Engine;
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

    [Fact]
    public async Task ShouldRemoveScratchFolder_WhenProcessIsDisposed()
    {
        // Arrange: a program that is on every machine the tests run on stands in for the CLI; it ends at
        // once on arguments it does not know, which the launcher does not judge.
        var options = new AgentCliOptions { Command = "dotnet" };
        var marker = Guid.NewGuid().ToString("N");
        var launcher = new AgentCliProcessLauncher(NullLogger.Instance);

        // Act
        var process = launcher.Start(new ClaudeCodeEngine(options), marker, "haiku");
        var folder = Assert.Single(AgentCliProcessLauncher.LiveFolders.Current, candidate =>
            File.Exists(Path.Join(candidate, "system-prompt.md"))
            && File.ReadAllText(Path.Join(candidate, "system-prompt.md")) == marker);
        await process.DisposeAsync();

        // Assert
        Assert.False(Directory.Exists(folder));
        Assert.DoesNotContain(folder, AgentCliProcessLauncher.LiveFolders.Current);
    }
}
