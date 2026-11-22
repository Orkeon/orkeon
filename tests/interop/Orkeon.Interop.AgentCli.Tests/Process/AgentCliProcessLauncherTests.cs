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

        // Act: this test's folder alone, since another test of the run may hold a live one.
        AgentCliProcessLauncher.LiveFolders.RemoveAll(candidate => candidate == folder);

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
        string folder;
        await using (launcher.Start(new ClaudeCodeEngine(options), marker, "haiku"))
        {
            folder = Assert.Single(AgentCliProcessLauncher.LiveFolders.Current, candidate =>
                File.Exists(Path.Join(candidate, "system-prompt.md"))
                && File.ReadAllText(Path.Join(candidate, "system-prompt.md")) == marker);
        }

        // Assert
        Assert.False(Directory.Exists(folder));
        Assert.DoesNotContain(folder, AgentCliProcessLauncher.LiveFolders.Current);
    }

    /// <summary>
    /// ADR-020: the CLI answers on its login, so no <c>ANTHROPIC_*</c> variable of the host reaches it (a proxy
    /// set for Orkeon's anthropic provider would otherwise receive the CLI's subscription login); the rest stays.
    /// </summary>
    [Fact]
    public void ShouldRemoveEveryAnthropicVariable_WhenStartInfoIsBuilt()
    {
        // Arrange
        var suffix = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var anthropic = "ANTHROPIC_ORKEON_TEST_" + suffix;
        var kept = "ORKEON_TEST_KEPT_" + suffix;
        Environment.SetEnvironmentVariable(anthropic, "https://proxy.invalid");
        Environment.SetEnvironmentVariable(kept, "stays");
        try
        {
            var line = new ClaudeCodeEngine(new AgentCliOptions()).BuildCommandLine(new AgentCliLaunch("/p", "{}", null, "/w"));

            // Act
            var startInfo = AgentCliProcessLauncher.StartInfo(line, "/w");

            // Assert
            Assert.DoesNotContain(startInfo.Environment.Keys, name => name.StartsWith("ANTHROPIC_", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("stays", startInfo.Environment[kept]);
            Assert.Equal("1", startInfo.Environment["CLAUDE_CODE_DISABLE_CLAUDE_MDS"]);
            Assert.False(startInfo.UseShellExecute);
        }
        finally
        {
            Environment.SetEnvironmentVariable(anthropic, null);
            Environment.SetEnvironmentVariable(kept, null);
        }
    }
}
