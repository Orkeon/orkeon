using Orkeon.Domain.Execution;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Code.Confinement;
using Orkeon.Tools.Code.Sessions;

namespace Orkeon.Tools.Code.Tests.Sessions;

/// <summary>
/// The bubblewrap session for real: one confined shell per run, its private <c>/tmp</c> kept from one
/// command to the next, and nothing left once the run ends. Integration: skipped with the probe's
/// reason where bubblewrap cannot confine a process.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BubblewrapShellSessionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public BubblewrapShellSessionTests()
    {
        var bubblewrap = BubblewrapProbe.Current();
        Assert.SkipUnless(bubblewrap.IsAvailable, $"bubblewrap cannot confine a process here: {bubblewrap.Reason}.");
    }

    private static ShellInvocation Command(string executable, params string[] arguments)
        => new(executable, arguments, null, TimeSpan.FromSeconds(30));

    [Fact]
    public async Task ShouldKeepTheSameConfinedShell_FromOneCommandToTheNext()
    {
        var sandbox = new BubblewrapShellSandbox();

        await using (var session = await sandbox.OpenAsync(ShellSessionView.Empty, Ct))
        {
            Assert.Equal(0, (await session.RunAsync(Command("touch", "/tmp/orkeon-marker"), Ct)).ExitCode);
            Assert.Equal(0, (await session.RunAsync(Command("test", "-e", "/tmp/orkeon-marker"), Ct)).ExitCode);
        }

        // A new session has a /tmp of its own.
        await using var fresh = await sandbox.OpenAsync(ShellSessionView.Empty, Ct);
        Assert.NotEqual(0, (await fresh.RunAsync(Command("test", "-e", "/tmp/orkeon-marker"), Ct)).ExitCode);
    }

    [Fact]
    public async Task ShouldSeeThePathsOfTheView()
    {
        var sandbox = new BubblewrapShellSandbox();
        var root = Directory.CreateTempSubdirectory("orkeon-bwrap-session-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "confined", Ct);
            var view = new ShellSessionView([new ConfinedPath(root, "/workspace", ConfinedAccess.ReadOnly)], [], false);
            await using var session = await sandbox.OpenAsync(view, Ct);

            var result = await session.RunAsync(new ShellInvocation("cat", ["a.txt"], "/workspace", TimeSpan.FromSeconds(30)), Ct);

            Assert.Equal("confined", result.Stdout);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("BR", "RT-11")]
    public async Task ShouldMapLogPathLikeRun_WhenBubblewrapStartsDetachedJob()
    {
        var sandbox = new BubblewrapShellSandbox();
        var root = Directory.CreateTempSubdirectory("orkeon-bwrap-job-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "jobs"));
            var log = Path.Combine(root, "jobs", "j1.log");
            var view = new ShellSessionView([new ConfinedPath(root, "/output", ConfinedAccess.ReadWrite)], [], false);
            await using var session = await sandbox.OpenAsync(view, Ct);

            var handle = await session.StartDetachedAsync(
                new ShellInvocation("echo", ["detached"], "/output", TimeSpan.FromSeconds(30)), log,
                _ => Task.FromResult<Stream>(new FileStream(log, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)), Ct);

            Assert.Equal(0, await handle.Exit.WaitAsync(TimeSpan.FromSeconds(30), Ct));
            Assert.Equal("detached", (await File.ReadAllTextAsync(log, Ct)).Trim());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [Trait("BR", "GH-188")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShouldCommitOnWorktreeBranch_WhenAgentRootIsLinkedWorktree(bool relativePaths)
    {
        // --relative-paths came with git 2.48; a plain worktree names the host path of its git dir.
        Assert.SkipUnless(!relativePaths || GitVersion() >= new Version(2, 48), "git worktree add --relative-paths needs git 2.48 or later.");
        var repo = Directory.CreateTempSubdirectory("orkeon-bwrap-worktree-").FullName;
        try
        {
            Git(repo, "init", "-q", "-b", "main");
            Git(repo, "config", "user.email", "agent@example.com");
            Git(repo, "config", "user.name", "Agent");
            await File.WriteAllTextAsync(Path.Combine(repo, "README.md"), "main tree", Ct);
            Git(repo, "add", "README.md");
            Git(repo, "commit", "-q", "-m", "init");
            Git(repo, [ "worktree", "add", "-q", .. relativePaths ? ["--relative-paths"] : Array.Empty<string>(),
                "-b", "probe-p1", ".orkeon/worktrees/p1", "main" ]);
            await File.WriteAllTextAsync(Path.Combine(repo, ".orkeon", "worktrees", "p1", "f.txt"), "from the agent", Ct);

            const string root = "/workspace/.orkeon/worktrees/p1";
            using var tool = new ShellCommandTool(new DiskBackedFileSystemService(repo, "/workspace"), allowInterpreters: true)
            {
                Sandbox = new BubblewrapShellSandbox(),
                Sessions = new ShellSessionManager(),
            };
            Dictionary<string, object?> status, add, commit, mainTree;
            await using (AgentRunScope.Begin("agent-1", "dev", root))
            {
                status = await RunAsync(tool, "git status --porcelain=v1 -b", root);
                add = await RunAsync(tool, "git add f.txt", root);
                commit = await RunAsync(tool, "git commit -q -m add-f", root);
                mainTree = await RunAsync(tool, "cat ../../../README.md", root);
            }

            Assert.Equal(0, status["exit_code"]);
            Assert.StartsWith("## probe-p1", status["stdout"]!.ToString(), StringComparison.Ordinal);
            Assert.Equal(0, add["exit_code"]);
            Assert.Equal(0, commit["exit_code"]);
            Assert.Equal("add-f", Git(repo, "log", "-1", "--format=%s", "probe-p1").Trim());
            Assert.Equal("init", Git(repo, "log", "-1", "--format=%s", "main").Trim());
            // The main working tree is not in the view.
            Assert.NotEqual(0, mainTree["exit_code"]);
            Assert.DoesNotContain("main tree", mainTree["stdout"]!.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(repo, recursive: true);
        }
    }

    private static async Task<Dictionary<string, object?>> RunAsync(ShellCommandTool tool, string command, string workingDirectory)
    {
        var response = await tool.CallAsync(new ToolCallRequest(ToolName: "shell_command", Parameters: new Dictionary<string, object?>
        {
            ["command"] = command,
            ["working_directory"] = workingDirectory,
        }), Ct);
        Assert.True(response.Success, response.Error);
        return Assert.IsType<Dictionary<string, object?>>(response.Result);
    }

    private static Version GitVersion()
    {
        var words = Git(Path.GetTempPath(), "--version").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var numbers = words.Length > 2 ? words[2].Split('.').Take(2).ToArray() : [];
        return numbers.Length == 2 && int.TryParse(numbers[0], out var major) && int.TryParse(numbers[1], out var minor)
            ? new Version(major, minor)
            : new Version(0, 0);
    }

    /// <summary>Runs git on the host in <paramref name="directory"/>; its standard output, or a failure with its error.</summary>
    private static string Git(string directory, params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)}: {stderr}");
        return stdout;
    }

    [Fact]
    public async Task ShouldKillTheConfinedTree_WhenTheRunEnds()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The test reads /proc.");
        var duration = LinuxProcesses.UniqueSleepDuration();
        int? sleep = null;
        try
        {
            var scope = AgentRunScope.Begin("agent-1");
            var running = new ShellSessionManager().RunAsync(new BubblewrapShellSandbox(), ShellSessionView.Empty,
                new ShellInvocation("sleep", [duration], null, TimeSpan.FromHours(1)), Ct);
            // The host's /proc shows the processes of the sandbox's PID namespace too.
            await Polling.WaitUntilAsync(() => (sleep = LinuxProcesses.Find("sleep", duration)) is not null);

            await scope.DisposeAsync();

            await Assert.ThrowsAsync<ShellSessionException>(() => running);
            await Polling.WaitUntilAsync(() => !LinuxProcesses.IsAlive(sleep!.Value));
        }
        finally
        {
            LinuxProcesses.KillIfAlive(sleep);
        }
    }
}
