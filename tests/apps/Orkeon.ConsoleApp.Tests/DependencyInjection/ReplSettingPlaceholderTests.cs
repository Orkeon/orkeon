using System.Text.Json;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Constants.Configuration;

namespace Orkeon.ConsoleApp.Tests.DependencyInjection;

/// <summary>CH-14: the REPL refuses a variable reference in a setting before any MCP server starts.</summary>
[Collection(ReplWorkingDirectoryCollection.Name)]
public sealed class ReplSettingPlaceholderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-repl-placeholder-" + Guid.NewGuid().ToString("N"));

    public ReplSettingPlaceholderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    [Trait("BR", "CH-14")]
    public void ShouldRefuseToStartBeforeAnyServer_WhenTheReplReadsAReference()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The marker server is a POSIX shell command.");
        const string path = "MCP:Servers:s:Env:GITHUB_TOKEN";
        var marker = Path.Combine(_root, "started-" + Guid.NewGuid().ToString("N"));
        var settings = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(settings, JsonSerializer.Serialize(new
        {
            RaggableTree = new { Enabled = false },
            MCP = new
            {
                Servers = new
                {
                    s = new
                    {
                        Command = "sh",
                        Args = new[] { "-c", $"touch '{marker}'; exec cat" },
                        Env = new Dictionary<string, string> { ["GITHUB_TOKEN"] = "${GITHUB_TOKEN}" },
                    },
                },
            },
        }));
        var reported = new List<string>();

        var launch = ReplStartup.Prepare(["--settings", settings], UiMode.Plain, replWordWrap: false, reported.Add);
        launch.Host?.Dispose();

        Assert.Equal(1, launch.ExitCode);
        Assert.Null(launch.Host);
        var all = string.Join('\n', reported);
        Assert.Contains(path, all, StringComparison.Ordinal);
        Assert.Contains(SettingPlaceholders.CorrectionFor(path)!, all, StringComparison.Ordinal);
        Assert.False(File.Exists(marker), "the MCP server was started");
    }
}
