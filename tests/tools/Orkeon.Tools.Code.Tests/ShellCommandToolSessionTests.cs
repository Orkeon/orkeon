using Orkeon.Domain.Execution;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Code.Sessions;
using Orkeon.Tools.Code.Tests.Doubles;

namespace Orkeon.Tools.Code.Tests;

/// <summary>
/// <see cref="ShellCommandTool"/> over its shell sessions: the validated command handed to the run's
/// session, its paths virtual or physical as the sandbox sees them, and what a timeout or a dead
/// session adds to the result.
/// </summary>
public sealed class ShellCommandToolSessionTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("orkeon-shell-tool-session-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ToolCallRequest Request(string command, string? workingDirectory = null)
    {
        var parameters = new Dictionary<string, object?> { ["command"] = command };
        if (workingDirectory is not null)
            parameters["working_directory"] = workingDirectory;
        return new ToolCallRequest(ToolName: "shell_command", Parameters: parameters);
    }

    private static Dictionary<string, object?> Unpack(ToolCallResponse result)
    {
        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        return dict;
    }

    [Fact]
    public async Task ShouldRunEveryCommandOfARunInOneSession()
    {
        var sandbox = new FakeShellSandbox();
        using var tool = new ShellCommandTool(new FakeFileSystemService()) { Sandbox = sandbox };

        await using (AgentRunScope.Begin("agent-1"))
        {
            Unpack(await tool.CallAsync(Request("echo one"), Ct));
            Unpack(await tool.CallAsync(Request("echo two"), Ct));
        }

        var session = Assert.Single(sandbox.Sessions);
        Assert.Equal(["one", "two"], session.Invocations.Select(i => Assert.Single(i.Arguments)));
        Assert.All(session.Invocations, i => Assert.Equal("echo", i.Executable));
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task ShouldReturnTheSessionsAnswer_AsTheResponse()
    {
        var sandbox = new FakeShellSandbox
        {
            Configure = session => session.DefaultResult = new ShellRunResult(2, "out", "err"),
        };
        using var tool = new ShellCommandTool(new FakeFileSystemService()) { Sandbox = sandbox };

        var dict = Unpack(await tool.CallAsync(Request("echo x"), Ct));

        Assert.Equal(2, (int)dict["exit_code"]!);
        Assert.Equal("out", dict["stdout"]);
        Assert.Equal("err", dict["stderr"]);
        Assert.True((bool)dict["completed"]!);
    }

    [Fact]
    public async Task ShouldSayTheSessionWasReset_WhenTheCommandTimesOut()
    {
        var sandbox = new FakeShellSandbox
        {
            Configure = session => session.DefaultResult = new ShellRunResult(-1, "", "", TimedOut: true),
        };
        using var tool = new ShellCommandTool(new FakeFileSystemService()) { Sandbox = sandbox };

        await using var scope = AgentRunScope.Begin("agent-1");
        var dict = Unpack(await tool.CallAsync(Request("echo x"), Ct));

        Assert.False((bool)dict["completed"]!);
        Assert.Equal(-1, (int)dict["exit_code"]!);
        Assert.Contains("[Command timed out]", dict["stderr"]!.ToString(), StringComparison.Ordinal);
        Assert.EndsWith(ShellCommandTool.SessionResetNote, dict["stderr"]!.ToString(), StringComparison.Ordinal);
        Assert.True(sandbox.Sessions[0].Disposed);
    }

    [Fact]
    public async Task ShouldReportADeadSession_AsAFailedCommand()
    {
        var sandbox = new FakeShellSandbox
        {
            Configure = session => session.ThrowOnRun = new ShellSessionException("The shell session ended."),
        };
        using var tool = new ShellCommandTool(new FakeFileSystemService()) { Sandbox = sandbox };

        var dict = Unpack(await tool.CallAsync(Request("echo x"), Ct));

        Assert.False((bool)dict["completed"]!);
        Assert.Equal(-1, (int)dict["exit_code"]!);
        Assert.Equal("The shell session ended." + ShellCommandTool.SessionResetNote, dict["stderr"]);
    }

    [Fact]
    public async Task ShouldHandTheSessionPhysicalPaths_WhenItRunsOnTheHost()
    {
        var sandbox = new FakeShellSandbox { UsesVirtualPaths = false };
        using var tool = new ShellCommandTool(new DiskBackedFileSystemService(_tempDir, "/src")) { Sandbox = sandbox };

        Unpack(await tool.CallAsync(Request("ls /src/a", workingDirectory: "/src"), Ct));

        var invocation = Assert.Single(sandbox.Sessions[0].Invocations);
        Assert.Equal(Path.Combine(_tempDir, "a"), Assert.Single(invocation.Arguments));
        Assert.Equal(_tempDir, invocation.WorkingDirectory);
    }

    [Fact]
    public async Task ShouldHandTheSessionVirtualPaths_WhenItSeesTheView()
    {
        var sandbox = new FakeShellSandbox { UsesVirtualPaths = true };
        using var tool = new ShellCommandTool(new DiskBackedFileSystemService(_tempDir, "/src")) { Sandbox = sandbox };

        Unpack(await tool.CallAsync(Request("ls /src/a", workingDirectory: "/src"), Ct));

        var invocation = Assert.Single(sandbox.Sessions[0].Invocations);
        Assert.Equal("/src/a", Assert.Single(invocation.Arguments));
        Assert.Equal("/src", invocation.WorkingDirectory);
        var path = Assert.Single(sandbox.Views[0].Paths);
        Assert.Equal("/src", path.Destination);
    }

    [Fact]
    public async Task ShouldRefuseTheCall_WhenTheConfiningSandboxIsUnavailable()
    {
        var sandbox = new FakeShellSandbox
        {
            UsesVirtualPaths = true,
            Availability = new ShellSandboxAvailability("bwrap was not found"),
        };
        using var tool = new ShellCommandTool(new FakeFileSystemService())
        {
            Confinement = new ShellConfinementOptions { Mode = ShellConfinementMode.Bubblewrap },
            Sandbox = sandbox,
        };

        var result = await tool.CallAsync(Request("echo x"), Ct);

        Assert.False(result.Success);
        Assert.Contains("Confinement unavailable", result.Error!, StringComparison.Ordinal);
        Assert.Contains("bwrap was not found", result.Error!, StringComparison.Ordinal);
        Assert.Empty(sandbox.Sessions);
    }
}
