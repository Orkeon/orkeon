using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tools.Code.Sessions;
using Orkeon.Tools.Code.Tests.Doubles;

namespace Orkeon.Tools.Code.Tests;

/// <summary>
/// <see cref="ShellCommandTool"/> job actions (<c>start_job</c>, <c>job_status</c>, <c>job_tail</c>):
/// a detached command logging under a writable mount, read back by id, ended with the run.
/// </summary>
public sealed class ShellCommandToolJobTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("orkeon-shell-jobs-").FullName;
    private readonly List<IDisposable> _disposables = [];
    private FileSystemService? _fileSystem;

    public void Dispose()
    {
        foreach (var disposable in _disposables)
            disposable.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Output => Path.Combine(_tempDir, "out");

    private static ToolCallRequest Request(string action, string? command = null, string? jobId = null, int? tailLines = null)
    {
        var parameters = new Dictionary<string, object?> { ["action"] = action };
        if (command is not null)
            parameters["command"] = command;
        if (jobId is not null)
            parameters["job_id"] = jobId;
        if (tailLines is not null)
            parameters["tail_lines"] = tailLines.Value;
        return new ToolCallRequest(ToolName: "shell_command", Parameters: parameters);
    }

    private static Dictionary<string, object?> Unpack(ToolCallResponse result)
    {
        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        return dict;
    }

    private static string JobIdOf(Dictionary<string, object?> dict)
        => Assert.IsType<string>(dict["job_id"]);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The registry is captured by the returned FileSystemService and lives for the test.")]
    private ShellCommandTool NewTool(
        FakeShellSandbox? sandbox,
        FileAccessRights workspaceRights = FileAccessRights.ReadWrite,
        FileAccessRights? outputRights = FileAccessRights.ReadWrite,
        ShellConfinementOptions? confinement = null,
        Orkeon.Domain.Tools.ToolResultLimitsOptions? limits = null)
    {
        var workspace = Path.Combine(_tempDir, "ws");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(Output);
        var mounts = new List<FileSystemMount> { new(workspace, "/workspace", workspaceRights) };
        if (outputRights is { } rights)
            mounts.Add(new FileSystemMount(Output, "/output", rights));
        var fileSystem = new FileSystemService(
            new FileSystemRegistry(mounts), new StubPathValidator().AllowAll(), NullLogger<FileSystemService>.Instance);
        _fileSystem = fileSystem;
        var options = confinement ?? TestConfinement.Off();
        var tool = sandbox is null
            ? new ShellCommandTool(fileSystem, limits: limits) { Confinement = options }
            : new ShellCommandTool(fileSystem, limits: limits) { Sandbox = sandbox, Confinement = options };
        _disposables.Add(tool);
        return tool;
    }

    private static FakeDetachedJob JobOf(FakeShellSandbox sandbox, int index = 0)
        => sandbox.Sessions.SelectMany(s => s.Jobs).ElementAt(index);

    // ── Step 1: start_job ──

    [Fact]
    [Trait("BR", "RT-11")]
    public async Task ShouldReturnJobIdAndOutputPath_WhenActionIsStartJob()
    {
        var sandbox = new FakeShellSandbox();
        var tool = NewTool(sandbox);

        Dictionary<string, object?> dict;
        await using (AgentRunScope.Begin("agent-1"))
            dict = Unpack(await tool.CallAsync(Request("start_job", command: "echo hi"), Ct));

        var id = JobIdOf(dict);
        Assert.NotEmpty(id);
        Assert.Equal($"/output/jobs/{id}.log", dict["output_path"]);
        var job = JobOf(sandbox);
        Assert.Equal("echo", job.Invocation.Executable);
        Assert.Equal(["hi"], job.Invocation.Arguments);
        Assert.Equal(Path.Combine(Output, "jobs", id + ".log"), job.LogPath);
        Assert.All(sandbox.Sessions, s => Assert.Empty(s.Invocations));
    }

    [Fact]
    public async Task ShouldRefuseStartJob_WhenNoAgentRunIsActive()
    {
        var sandbox = new FakeShellSandbox();
        var tool = NewTool(sandbox);

        var refused = await Task.Run(() => tool.CallAsync(Request("start_job", command: "echo hi"), Ct), Ct);

        Assert.False(refused.Success);
        Assert.Contains("start_job needs a running agent run", refused.Error, StringComparison.Ordinal);
        Assert.Empty(sandbox.Sessions);
    }

    [Fact]
    [Trait("BR", "RT-11")]
    public async Task ShouldRefuseStartJob_WhenRegistryIsFull()
    {
        var sandbox = new FakeShellSandbox();
        var tool = NewTool(sandbox);

        ToolCallResponse refused;
        await using (AgentRunScope.Begin("agent-1"))
        {
            for (var i = 0; i < 16; i++)
                Unpack(await tool.CallAsync(Request("start_job", command: "echo hi"), Ct));
            refused = await tool.CallAsync(Request("start_job", command: "echo hi"), Ct);
        }

        Assert.False(refused.Success);
        Assert.NotEmpty(refused.Error!);
        Assert.Equal(16, sandbox.Sessions.SelectMany(s => s.Jobs).Count());
    }

    [Fact]
    [Trait("BR", "RT-11")]
    public async Task ShouldStillRefuseRedirectOperator_WhenUserCommandContainsIt()
    {
        var sandbox = new FakeShellSandbox();
        var tool = NewTool(sandbox);

        ToolCallResponse result;
        await using (AgentRunScope.Begin("agent-1"))
        {
            Unpack(await tool.CallAsync(Request("start_job", command: "echo hi"), Ct));
            result = await tool.CallAsync(Request("start_job", command: "echo hi > /output/x.txt"), Ct);
        }

        Assert.False(result.Success);
        Assert.NotEmpty(result.Error!);
        Assert.Single(sandbox.Sessions.SelectMany(s => s.Jobs));
    }

    [Fact]
    [Trait("BR", "RT-11")]
    public async Task ShouldRefuseStartJob_WhenNoMountIsWritable()
    {
        var sandbox = new FakeShellSandbox();
        var tool = NewTool(sandbox, workspaceRights: FileAccessRights.Read, outputRights: null);

        ToolCallResponse result;
        await using (AgentRunScope.Begin("agent-1"))
            result = await tool.CallAsync(Request("start_job", command: "echo hi"), Ct);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Error!);
        Assert.Empty(sandbox.Sessions.SelectMany(s => s.Jobs));
    }

    // ── Step 2: job_status and job_tail ──

    [Fact]
    [Trait("BR", "RT-11")]
    public async Task ShouldReportRunning_WhenJobHasNotExited()
    {
        var sandbox = new FakeShellSandbox();
        var tool = NewTool(sandbox);

        Dictionary<string, object?> status;
        await using (AgentRunScope.Begin("agent-1"))
        {
            var id = JobIdOf(Unpack(await tool.CallAsync(Request("start_job", command: "echo hi"), Ct)));
            status = Unpack(await tool.CallAsync(Request("job_status", jobId: id), Ct));
        }

        Assert.Equal("running", status["status"]);
    }

    [Fact]
    [Trait("BR", "RT-11")]
    public async Task ShouldReportExitCode_WhenJobFinished()
    {
        var sandbox = new FakeShellSandbox();
        var tool = NewTool(sandbox);

        Dictionary<string, object?> status;
        await using (AgentRunScope.Begin("agent-1"))
        {
            var id = JobIdOf(Unpack(await tool.CallAsync(Request("start_job", command: "echo hi"), Ct)));
            JobOf(sandbox).Exit(3);
            status = Unpack(await tool.CallAsync(Request("job_status", jobId: id), Ct));
        }

        Assert.Equal("exited", status["status"]);
        Assert.Equal(3, status["exit_code"]);
    }

    [Fact]
    [Trait("BR", "RT-11")]
    public async Task ShouldReturnLastNLines_WhenActionIsJobTail()
    {
        var sandbox = new FakeShellSandbox();
        var tool = NewTool(sandbox);

        Dictionary<string, object?> tail;
        await using (AgentRunScope.Begin("agent-1"))
        {
            var id = JobIdOf(Unpack(await tool.CallAsync(Request("start_job", command: "echo hi"), Ct)));
            JobOf(sandbox).WriteLog("line1", "line2", "line3", "line4", "line5");
            tail = Unpack(await tool.CallAsync(Request("job_tail", jobId: id, tailLines: 2), Ct));
        }

        var text = Assert.IsType<string>(tail["tail"]).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
        Assert.Equal("line4\nline5", text);
    }

    [Fact]
    [Trait("BR", "RT-10")]
    public async Task ShouldCutJobTailOnConfiguredBound_WhenLimitSetForShell()
    {
        var limits = new Orkeon.Domain.Tools.ToolResultLimitsOptions();
        limits.Limits["shell_command"] = 500;
        var sandbox = new FakeShellSandbox();
        var tool = NewTool(sandbox, limits: limits);

        Dictionary<string, object?> tail;
        await using (AgentRunScope.Begin("agent-1"))
        {
            var id = JobIdOf(Unpack(await tool.CallAsync(Request("start_job", command: "echo hi"), Ct)));
            JobOf(sandbox).WriteLog(new string('b', 1200));
            tail = Unpack(await tool.CallAsync(Request("job_tail", jobId: id), Ct));
        }

        var text = Assert.IsType<string>(tail["tail"]);
        Assert.StartsWith(new string('b', 500), text, StringComparison.Ordinal);
        Assert.Contains("chars omitted", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[Output truncated]", text, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "RT-11")]
    [InlineData("job_status")]
    [InlineData("job_tail")]
    public async Task ShouldRefuseUnknownJobId_WhenStatusOrTailAsked(string action)
    {
        var sandbox = new FakeShellSandbox();
        var tool = NewTool(sandbox);

        ToolCallResponse result;
        await using (AgentRunScope.Begin("agent-1"))
        {
            var started = JobIdOf(Unpack(await tool.CallAsync(Request("start_job", command: "echo hi"), Ct)));
            Assert.NotEqual("no-such-job", started);
            result = await tool.CallAsync(Request(action, jobId: "no-such-job"), Ct);
        }

        Assert.False(result.Success);
        Assert.NotEmpty(result.Error!);
    }

    [Fact]
    [Trait("BR", "RT-11")]
    public async Task ShouldReturnLiveTail_WhenRunningJobHandleProvidesIt()
    {
        var sandbox = new FakeShellSandbox { Configure = s => s.LiveTailForJobs = new ShellLiveTail("a\nb\nc\nd", false) };
        var tool = NewTool(sandbox);

        Dictionary<string, object?> tail;
        await using (AgentRunScope.Begin("agent-1"))
        {
            var id = JobIdOf(Unpack(await tool.CallAsync(Request("start_job", command: "echo hi"), Ct)));
            tail = Unpack(await tool.CallAsync(Request("job_tail", jobId: id, tailLines: 2), Ct));
        }

        Assert.Equal("c\nd", Assert.IsType<string>(tail["tail"]).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n'));
    }

    [Fact]
    [Trait("BR", "RT-11")]
    public async Task ShouldReadFileTail_WhenJobExitedEvenIfHandleProvidesLiveTail()
    {
        var sandbox = new FakeShellSandbox { Configure = s => s.LiveTailForJobs = new ShellLiveTail("a\nb\nc\nd", false) };
        var tool = NewTool(sandbox);

        Dictionary<string, object?> tail;
        await using (AgentRunScope.Begin("agent-1"))
        {
            var id = JobIdOf(Unpack(await tool.CallAsync(Request("start_job", command: "echo hi"), Ct)));
            JobOf(sandbox).WriteLog("f1", "f2", "f3");
            JobOf(sandbox).Exit(0);
            tail = Unpack(await tool.CallAsync(Request("job_tail", jobId: id, tailLines: 2), Ct));
        }

        Assert.Equal("f2\nf3", Assert.IsType<string>(tail["tail"]).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n'));
    }

    [Fact]
    [Trait("BR", "RT-11")]
    public async Task ShouldDropFirstLine_WhenLiveTailReportsTruncatedEvenIfFewerCharsThanTheWindow()
    {
        var text = "cut-fragment\nkept1\nkept2";
        Assert.True(text.Length < 64 * 1024);
        var sandbox = new FakeShellSandbox { Configure = s => s.LiveTailForJobs = new ShellLiveTail(text, true) };
        var tool = NewTool(sandbox);

        Dictionary<string, object?> tail;
        await using (AgentRunScope.Begin("agent-1"))
        {
            var id = JobIdOf(Unpack(await tool.CallAsync(Request("start_job", command: "echo hi"), Ct)));
            tail = Unpack(await tool.CallAsync(Request("job_tail", jobId: id, tailLines: 10), Ct));
        }

        Assert.Equal("kept1\nkept2", Assert.IsType<string>(tail["tail"]).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n'));
    }

    [Fact]
    [Trait("BR", "RT-11")]
    public async Task ShouldKeepFirstLine_WhenLiveTailIsExactlyWindowSizeAndNotTruncated()
    {
        var text = "first-line\n" + new string('x', 64 * 1024 - "first-line\n".Length - 1) + "\n";
        Assert.Equal(64 * 1024, text.Length);
        var sandbox = new FakeShellSandbox { Configure = s => s.LiveTailForJobs = new ShellLiveTail(text, false) };
        var tool = NewTool(sandbox);

        Dictionary<string, object?> tail;
        await using (AgentRunScope.Begin("agent-1"))
        {
            var id = JobIdOf(Unpack(await tool.CallAsync(Request("start_job", command: "echo hi"), Ct)));
            tail = Unpack(await tool.CallAsync(Request("job_tail", jobId: id, tailLines: 10), Ct));
        }

        Assert.Contains("first-line", Assert.IsType<string>(tail["tail"]), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "RT-11")]
    [InlineData("start_job")]
    [InlineData("job_status")]
    [InlineData("job_tail")]
    public async Task ShouldAcceptJsonElementAction_WhenJobStatusOrTailAsked(string action)
    {
        var sandbox = new FakeShellSandbox();
        var tool = NewTool(sandbox);

        Dictionary<string, object?> dict;
        await using (AgentRunScope.Begin("agent-1"))
        {
            var id = JobIdOf(Unpack(await tool.CallAsync(Request("start_job", command: "echo hi"), Ct)));
            var request = Request(action, command: action == "start_job" ? "echo hi" : null, jobId: id);
            request.Parameters["action"] = JsonSerializer.SerializeToElement(action);
            request.Parameters["job_id"] = JsonSerializer.SerializeToElement(id);
            dict = Unpack(await tool.CallAsync(request, Ct));
        }

        if (action == "start_job")
            Assert.Equal(2, sandbox.Sessions.SelectMany(s => s.Jobs).Count());
        else if (action == "job_status")
            Assert.Equal("running", dict["status"]);
        else
            Assert.True(dict.ContainsKey("tail"));
    }

    // ── Step 4: the log opened through the VFS ──

    [Fact]
    [Trait("BR", "RT-11")]
    public async Task ShouldHandVfsLogOpenerToSession_WhenActionIsStartJob()
    {
        var sandbox = new FakeShellSandbox();
        var tool = NewTool(sandbox);

        Dictionary<string, object?> dict;
        await using (AgentRunScope.Begin("agent-1"))
            dict = Unpack(await tool.CallAsync(Request("start_job", command: "echo hi"), Ct));

        var opener = JobOf(sandbox).LogOpener;
        Assert.NotNull(opener);
        var stream = await opener(Ct);
        await using (stream)
            await stream.WriteAsync("through the vfs"u8.ToArray(), Ct);
        var read = await _fileSystem!.TryReadAllTextAsync(Assert.IsType<string>(dict["output_path"]), Ct);
        Assert.Equal("through the vfs", read);
    }

    [Fact]
    [Trait("BR", "RT-11")]
    public async Task ShouldReturnTailFromLog_WhenSessionPumpedOutputThroughTheOpener()
    {
        var sandbox = new FakeShellSandbox { Configure = s => s.PumpedLogText = "p1\np2\np3\np4\n" };
        var tool = NewTool(sandbox);

        Dictionary<string, object?> status;
        Dictionary<string, object?> tail;
        await using (AgentRunScope.Begin("agent-1"))
        {
            var id = JobIdOf(Unpack(await tool.CallAsync(Request("start_job", command: "echo hi"), Ct)));
            JobOf(sandbox).Exit(5);
            status = Unpack(await tool.CallAsync(Request("job_status", jobId: id), Ct));
            tail = Unpack(await tool.CallAsync(Request("job_tail", jobId: id, tailLines: 2), Ct));
        }

        Assert.Equal(5, status["exit_code"]);
        Assert.Equal("p3\np4", Assert.IsType<string>(tail["tail"]).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n'));
    }

    // ── Step 3: run scope, confinement, real process ──

    [Fact]
    [Trait("BR", "RT-11")]
    public async Task ShouldKillRunningJobs_WhenRunScopeEnds()
    {
        var sandbox = new FakeShellSandbox();
        var tool = NewTool(sandbox);

        await using (AgentRunScope.Begin("agent-1"))
        {
            Unpack(await tool.CallAsync(Request("start_job", command: "echo one"), Ct));
            Unpack(await tool.CallAsync(Request("start_job", command: "echo two"), Ct));
            JobOf(sandbox, 0).Exit(0);
        }

        Assert.Equal(0, JobOf(sandbox, 0).KillCount);
        Assert.Equal(1, JobOf(sandbox, 1).KillCount);
    }

    [Fact]
    [Trait("BR", "RT-11")]
    public async Task ShouldReadJobFromLaterCall_WhenSameRunScope()
    {
        var sandbox = new FakeShellSandbox();
        var tool = NewTool(sandbox);
        var otherTool = NewTool(sandbox);

        string id;
        Dictionary<string, object?> status;
        await using (AgentRunScope.Begin("agent-1"))
        {
            id = JobIdOf(Unpack(await tool.CallAsync(Request("start_job", command: "echo hi"), Ct)));
            JobOf(sandbox).Exit(0);
            status = Unpack(await otherTool.CallAsync(Request("job_status", jobId: id), Ct));
        }

        Assert.Equal("exited", status["status"]);
        Assert.Equal(0, status["exit_code"]);

        ToolCallResponse nextRun;
        await using (AgentRunScope.Begin("agent-1"))
            nextRun = await tool.CallAsync(Request("job_status", jobId: id), Ct);
        Assert.False(nextRun.Success);
    }

    [Fact]
    [Trait("BR", "RT-11")]
    public async Task ShouldStartJobInsideConfinedPaths_WhenSandboxActive()
    {
        var sandbox = new FakeShellSandbox { UsesVirtualPaths = true };
        var tool = NewTool(
            sandbox, confinement: new ShellConfinementOptions { Mode = ShellConfinementMode.Bubblewrap });

        Dictionary<string, object?> dict;
        await using (AgentRunScope.Begin("agent-1"))
            dict = Unpack(await tool.CallAsync(Request("start_job", command: "echo hi"), Ct));

        var id = JobIdOf(dict);
        Assert.Equal($"/output/jobs/{id}.log", dict["output_path"]);
        Assert.Contains(sandbox.Views[0].Paths, p => p.Destination == "/output");
        Assert.Equal(Path.Combine(Output, "jobs", id + ".log"), JobOf(sandbox).LogPath);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("BR", "RT-11")]
    public async Task ShouldRunRealDetachedJob_WhenPosixShellAvailable()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "A detached job needs a POSIX shell.");
        Assert.SkipUnless(File.Exists("/bin/sh") || File.Exists("/usr/bin/sh"), "No POSIX shell.");
        var tool = NewTool(sandbox: null);

        Dictionary<string, object?> status;
        string id;
        await using (AgentRunScope.Begin("agent-1"))
        {
            id = JobIdOf(Unpack(await tool.CallAsync(Request("start_job", command: "echo hello"), Ct)));
            do
            {
                await Task.Delay(100, Ct);
                status = Unpack(await tool.CallAsync(Request("job_status", jobId: id), Ct));
            }
            while ((string?)status["status"] == "running");

            Assert.Equal("exited", status["status"]);
            Assert.Equal(0, status["exit_code"]);
            var tail = Unpack(await tool.CallAsync(Request("job_tail", jobId: id, tailLines: 5), Ct));
            Assert.Equal("hello", Assert.IsType<string>(tail["tail"]).Trim());
        }
    }
}
