using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Logging;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Hosting;

namespace Orkeon.ConsoleApp.Tests.DependencyInjection;

/// <summary>
/// <c>--llm-log</c> and <c>--llm-log-path</c> on <c>orkeon-repl</c>, as on <c>orkeon run</c>: the
/// folder is mounted at <c>/llm-logs</c> and the exchange log is registered on it; a folder outside
/// the working directory is refused before any host without <c>--allow-external-mounts</c>, with
/// the runner's line. The REPL used to accept neither flag and wrote no exchange log at all.
/// <para>
/// Serial, in the collection of <see cref="ReplSettingsSourcesTests"/>: the global settings path is
/// process-wide state, pointed at an absent file of the test.
/// </para>
/// </summary>
[Collection(ReplWorkingDirectoryCollection.Name)]
public sealed class ReplLlmLogTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("orkeon-repl-llm-log-").FullName;
    private readonly string? _globalOverride = RunnerSettings.GlobalSettingsPathOverride;
    private readonly List<string> _reported = [];

    public ReplLlmLogTests() =>
        RunnerSettings.GlobalSettingsPathOverride = Path.Combine(_root, "global", "appsettings.json");

    public void Dispose()
    {
        RunnerSettings.GlobalSettingsPathOverride = _globalOverride;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private string Settings()
    {
        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, "{ \"RaggableTree\": { \"Enabled\": false } }");
        return path;
    }

    private ReplLaunch Start(params string[] args) =>
        ReplStartup.Prepare(args, UiMode.Plain, replWordWrap: false, _reported.Add);

    [Fact]
    public void ShouldMountTheFolderAndRegisterTheExchangeLog_WhenLlmLogPathIsGiven()
    {
        var logs = Path.Combine(_root, "logs");

        var launch = Start("--settings", Settings(), "--llm-log-path", logs, "--allow-external-mounts");

        using var host = launch.Host;
        Assert.NotNull(host);
        Assert.True(Directory.Exists(logs));
        var mount = host.Services.GetRequiredService<FileSystemRegistry>().GetMounts()
            .Single(m => m.VirtualPath == RunnerVirtualRoots.LlmLogs);
        Assert.Equal(logs, mount.BasePath);
        Assert.NotNull(host.Services.GetService<ILlmExchangeLogger>());
    }

    [Fact]
    public void ShouldRegisterNoExchangeLog_WhenNeitherFlagIsGiven()
    {
        // The REPL needs one mount to start: a workspace, without which it refuses the session.
        var workspace = Directory.CreateDirectory(Path.Combine(_root, "workspace")).FullName;

        var launch = Start("--settings", Settings(), "--mount", $"{workspace}:/workspace:rw", "--allow-external-mounts");

        using var host = launch.Host;
        Assert.NotNull(host);
        Assert.DoesNotContain(
            host.Services.GetRequiredService<FileSystemRegistry>().GetMounts(),
            m => m.VirtualPath == RunnerVirtualRoots.LlmLogs);
        Assert.Null(host.Services.GetService<ILlmExchangeLogger>());
    }

    [Fact]
    public void ShouldRefuseAtStart_WhenTheLogFolderIsOutsideTheWorkingDirectory()
    {
        var logs = Path.Combine(_root, "logs");
        Assert.False(PhysicalPathContainment.IsUnder(logs, Directory.GetCurrentDirectory()));

        var launch = Start("--settings", Settings(), "--llm-log-path", logs);

        using var host = launch.Host;
        Assert.Null(launch.Host);
        Assert.Equal(ReplStartup.RefusedExitCode, launch.ExitCode);
        Assert.Contains("--llm-log-path", _reported[0], StringComparison.Ordinal);
        Assert.Contains("--allow-external-mounts", _reported[0], StringComparison.Ordinal);
        Assert.False(Directory.Exists(logs));
    }
}
