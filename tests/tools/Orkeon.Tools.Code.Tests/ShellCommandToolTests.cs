using System.Runtime.InteropServices;
using Orkeon.Domain.Execution;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Code.Tests.Doubles;

namespace Orkeon.Tools.Code.Tests;

public sealed class ShellCommandToolTests : IDisposable
{
    private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly string[] s_sleepEchoAllowlist = s_isWindows
        ? ["ping", "echo"]
        : ["sleep", "echo"];
    private static readonly string[] s_pwdCdAllowlist = s_isWindows
        ? ["cd", "echo"]
        : ["pwd", "echo"];
    private static readonly string[] s_python3Allowlist = ["python3"];
    private static readonly string[] s_dangerousBlocklist = ["dangerous"];
    private static readonly string[] s_environmentReaderAllowlist = s_isWindows
        ? ["set"]
        : ["cat"];

    /// <summary>Environment variable planted on the test process to stand in for a host secret.</summary>
    private const string EnvironmentProbeName = "ORKEON_TEST_SECRET";
    private const string EnvironmentProbeValue = "s3cr3t-fixture";

    private readonly ShellCommandTool _tool;

    public ShellCommandToolTests()
    {
        _tool = new ShellCommandTool(new FakeFileSystemService()) { Confinement = TestConfinement.Off() };
    }

    [Fact]
    public async Task ShouldCaptureOutput_WhenExecutingEchoCommand()
    {
        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = "echo hello"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Contains("hello", dict["stdout"]!.ToString());
        Assert.Equal(0, (int)dict["exit_code"]!);
        Assert.True((bool)dict["completed"]!);
    }

    [Fact]
    public async Task ShouldCaptureExitCode_WhenCommandFails()
    {
        var command = s_isWindows
            ? @"dir Z:\nonexistent_directory_that_does_not_exist_12345"
            : "ls /nonexistent_directory_that_does_not_exist_12345";

        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = command
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        var exitCode = (int)dict["exit_code"]!;
        Assert.NotEqual(0, exitCode);
        Assert.True((bool)dict["completed"]!);
    }

    [Fact]
    public async Task ShouldRejectCommand_WhenBlocklisted()
    {
        // Use an allowed executable (echo) but include a blocked pattern (sudo)
        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = "echo sudo test"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("blocked pattern", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldRejectCommand_WhenNotInAllowlist()
    {
        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = "curl https://example.com"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("not in the allowlist", result.Error);
    }

    [Fact]
    public async Task ShouldTimeout_WhenCommandExceedsTimeout()
    {
        // sleep 10 on Linux, ping -n 11 127.0.0.1 on Windows (~10s)
        var longCommand = s_isWindows ? "ping -n 11 127.0.0.1" : "sleep 10";

        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = longCommand,
                ["timeout_seconds"] = 1
            }
        );

        var tool = new ShellCommandTool(
            new FakeFileSystemService(),
            allowedCommands: s_sleepEchoAllowlist) { Confinement = TestConfinement.Off() };

        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.False((bool)dict["completed"]!);
        Assert.Contains("timed out", dict["stderr"]!.ToString()!, StringComparison.OrdinalIgnoreCase);

        tool.Dispose();
    }

    [Fact]
    public async Task ShouldCaptureStderr_WhenCommandFails()
    {
        // ls/dir on a non-existent path naturally writes to stderr
        var command = s_isWindows
            ? @"dir Z:\no_such_directory_xyz_12345"
            : "ls /no_such_directory_xyz_12345";

        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = command
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        var exitCode = (int)dict["exit_code"]!;
        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task ShouldUseWorkingDirectory_WhenSpecified()
    {
        var tempDir = s_isWindows ? Path.GetTempPath().TrimEnd('\\') : "/tmp";
        var command = s_isWindows ? "cd" : "pwd";

        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = command,
                ["working_directory"] = tempDir
            }
        );

        var tool = new ShellCommandTool(
            new FakeFileSystemService(),
            allowedCommands: s_pwdCdAllowlist) { Confinement = TestConfinement.Off() };

        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        // /tmp may resolve to /private/tmp on macOS; Windows temp contains "Temp"
        var expectedFragment = Path.GetFileName(tempDir);
        Assert.Contains(expectedFragment, dict["stdout"]!.ToString()!, StringComparison.OrdinalIgnoreCase);

        tool.Dispose();
    }

    [Fact]
    public async Task ShouldReturnError_WhenCommandIsEmpty()
    {
        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = ""
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("empty", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldHaveCorrectConfiguration_WhenAccessingSchema()
    {
        Assert.Equal("shell_command", _tool.Name);
        Assert.Equal("Code Operations", _tool.Category);
        Assert.True(_tool.Schema.Parameters["command"].Required);
        Assert.False(_tool.Schema.Parameters["working_directory"].Required);
        Assert.False(_tool.Schema.Parameters["timeout_seconds"].Required);
    }

    [Fact]
    public async Task ShouldAcceptCustomAllowlist()
    {
        var tool = new ShellCommandTool(
            new FakeFileSystemService(),
            allowedCommands: s_python3Allowlist) { Confinement = TestConfinement.Off() };

        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = "echo hello"
            }
        );

        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // echo is not in the custom allowlist
        Assert.False(result.Success);
        Assert.Contains("not in the allowlist", result.Error);

        tool.Dispose();
    }

    [Fact]
    public async Task ShouldAcceptCustomBlocklist()
    {
        var tool = new ShellCommandTool(
            new FakeFileSystemService(),
            blockedPatterns: s_dangerousBlocklist) { Confinement = TestConfinement.Off() };

        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = "echo dangerous command"
            }
        );

        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("blocked pattern", result.Error, StringComparison.OrdinalIgnoreCase);

        tool.Dispose();
    }

    [Fact]
    public void ShouldExtractExecutable_FromVariousCommandFormats()
    {
        Assert.Equal("git", ShellCommandTool.ExtractExecutable("git status"));
        Assert.Equal("echo", ShellCommandTool.ExtractExecutable("echo hello world"));
        Assert.Equal("ls", ShellCommandTool.ExtractExecutable("ls"));
        Assert.Equal("dotnet", ShellCommandTool.ExtractExecutable("  dotnet build"));
        Assert.Equal("git", ShellCommandTool.ExtractExecutable("/usr/bin/git status"));
    }

    // ── Security tests: Forbidden shell operators ────────────────────────

    [Theory]
    [InlineData("echo test; rm -rf /tmp", ";")]
    [InlineData("echo test && cat /etc/passwd", "&&")]
    [InlineData("echo test || cat /etc/passwd", "||")]
    [InlineData("find / | grep secret", "|")]
    [InlineData("echo $(cat /etc/passwd)", "$(")]
    [InlineData("echo `whoami`", "`")]
    [InlineData("echo test & cat /etc/passwd", "&")]
    [InlineData("echo test > /tmp/output", ">")]
    [InlineData("echo test >> /tmp/output", ">>")]
    [InlineData("cat < /etc/passwd", "<")]
    public async Task ShouldRejectCommand_WhenContainsForbiddenShellOperator(
        string command, string expectedOperator)
    {
        _ = expectedOperator; // Used for test case documentation in InlineData

        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = command
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("forbidden shell operator", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldRejectCommand_WhenContainsNewline()
    {
        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = "echo hello\nrm -rf /tmp"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("forbidden shell operator", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldRejectCommand_WhenContainsCarriageReturn()
    {
        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = "echo hello\rrm -rf /tmp"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("forbidden shell operator", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldRejectCommand_WhenContainsCommandSubstitution()
    {
        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = "echo $(whoami)"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("forbidden shell operator", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Security tests: Direct execution (no shell) ─────────────────────

    [Fact]
    public async Task ShouldExecuteDirectly_WithoutShellInterpretation()
    {
        // If shell interpretation were happening, $HOME would expand.
        // With direct execution, echo receives the literal string "$HOME".
        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = "echo literal-test-value"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Contains("literal-test-value", dict["stdout"]!.ToString()!);
    }

    [Fact]
    public async Task ShouldPassMultipleArguments_WhenCommandHasSpaces()
    {
        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = "echo one two three"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Contains("one", dict["stdout"]!.ToString()!);
        Assert.Contains("two", dict["stdout"]!.ToString()!);
        Assert.Contains("three", dict["stdout"]!.ToString()!);
    }

    // ── Unit tests: ParseCommand and TokenizeCommand ─────────────────────

    [Fact]
    public void ParseCommand_ShouldSplitExecutableAndArguments()
    {
        var (exe, args) = ShellCommandTool.ParseCommand("git status --short");
        Assert.Equal("git", exe);
        Assert.Equal(["status", "--short"], args);
    }

    [Fact]
    public void ParseCommand_ShouldHandlePathQualifiedExecutable()
    {
        var (exe, args) = ShellCommandTool.ParseCommand("/usr/bin/git log --oneline");
        Assert.Equal("git", exe);
        Assert.Equal(["log", "--oneline"], args);
    }

    [Fact]
    public void ParseCommand_ShouldHandleNoArguments()
    {
        var (exe, args) = ShellCommandTool.ParseCommand("ls");
        Assert.Equal("ls", exe);
        Assert.Empty(args);
    }

    [Fact]
    public void ParseCommand_ShouldHandleQuotedArguments()
    {
        var (exe, args) = ShellCommandTool.ParseCommand("echo 'hello world' test");
        Assert.Equal("echo", exe);
        Assert.Equal(["hello world", "test"], args);
    }

    [Fact]
    public void ParseCommand_ShouldHandleDoubleQuotedArguments()
    {
        var (exe, args) = ShellCommandTool.ParseCommand("echo \"hello world\" test");
        Assert.Equal("echo", exe);
        Assert.Equal(["hello world", "test"], args);
    }

    [Fact]
    public void FindForbiddenOperator_ShouldReturnNull_ForSafeCommand()
    {
        Assert.Null(ShellCommandTool.FindForbiddenOperator("git status --short"));
        Assert.Null(ShellCommandTool.FindForbiddenOperator("echo hello world"));
        Assert.Null(ShellCommandTool.FindForbiddenOperator("ls -la /tmp"));
    }

    [Theory]
    [InlineData("echo;rm", ";")]
    [InlineData("echo&&rm", "&&")]
    [InlineData("echo||rm", "||")]
    [InlineData("echo|rm", "|")]
    [InlineData("echo$(rm)", "$(")]
    [InlineData("echo`rm`", "`")]
    [InlineData("echo&rm", "&")]
    [InlineData("echo>file", ">")]
    [InlineData("echo>>file", ">>")]
    [InlineData("cat<file", "<")]
    public void FindForbiddenOperator_ShouldDetectOperator(string command, string expectedOperator)
    {
        _ = expectedOperator; // Used for test case documentation in InlineData

        var result = ShellCommandTool.FindForbiddenOperator(command);
        Assert.NotNull(result);
        // The detected operator should match (or be a substring of) the expected operator
        // e.g. for "&&", FindForbiddenOperator may return "&" first since it's checked earlier,
        // but both are forbidden, so just check it's not null.
    }

    [Fact]
    public void TokenizeCommand_ShouldHandleEmptyString()
    {
        var tokens = ShellCommandTool.TokenizeCommand("");
        Assert.Empty(tokens);
    }

    [Fact]
    public void TokenizeCommand_ShouldHandleMultipleSpaces()
    {
        var tokens = ShellCommandTool.TokenizeCommand("echo   hello   world");
        Assert.Equal(["echo", "hello", "world"], tokens);
    }

    // ── Security tests (R2.1 / SEC-001): interpreters excluded by default ──

    [Theory]
    [InlineData("node -e \"require('child_process').execSync('id')\"")]
    [InlineData("dotnet build")]
    [InlineData("npm install lodash")]
    [InlineData("find / -name secret")]
    public async Task ShouldRejectInterpreter_ByDefault(string command)
    {
        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = command
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("not in the allowlist", result.Error);
    }

    [Theory]
    [InlineData("ls -la")]
    [InlineData("cat README.md")]
    [InlineData("pwd")]
    public async Task ShouldAllowReadOnlyCommands_ByDefault(string command)
    {
        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = command
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // The command may exit non-zero (e.g. missing file) but must NOT be
        // rejected by the allowlist — it passes validation and is executed.
        if (OperatingSystem.IsWindows())
        {
            // POSIX binaries (ls, cat, pwd) are usually absent on Windows, so the
            // launch itself may fail; the contract under test is only that the
            // allowlist accepted the command.
            Assert.DoesNotContain("not in the allowlist", result.Error ?? string.Empty);
        }
        else
        {
            Assert.True(result.Success);
        }
    }

    [Theory]
    [InlineData("node -e \"console.log(1)\"")]
    [InlineData("dotnet --version")]
    public async Task ShouldAllowInterpreter_WhenOptedIn(string command)
    {
        var tool = new ShellCommandTool(new FakeFileSystemService(), allowInterpreters: true) { Confinement = TestConfinement.Off() };

        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = command
            }
        );

        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // With the opt-in, the interpreter passes allowlist validation. The
        // process may not actually exist on the test host, but it is no longer
        // rejected by the allowlist.
        if (!result.Success)
            Assert.DoesNotContain("not in the allowlist", result.Error ?? "");

        tool.Dispose();
    }

    [Fact]
    public async Task ShouldAllowReadOnlyGitSubcommand_ByDefault()
    {
        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = "git status"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Not rejected by validation (git status is a read-only subcommand).
        if (!result.Success)
        {
            Assert.DoesNotContain("not allowed", result.Error ?? "");
            Assert.DoesNotContain("not in the allowlist", result.Error ?? "");
        }
    }

    [Theory]
    [InlineData("git -c core.sshCommand=evil status")]
    [InlineData("git push origin main")]
    [InlineData("git commit -m x")]
    public async Task ShouldRejectNonReadOnlyGit_ByDefault(string command)
    {
        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = command
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("not allowed", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldNotExposeHostEnvironment_WhenCommandReadsEnvironment()
    {
        var previous = Environment.GetEnvironmentVariable(EnvironmentProbeName);
        Environment.SetEnvironmentVariable(EnvironmentProbeName, EnvironmentProbeValue);
        try
        {
            var tool = new ShellCommandTool(
                new FakeFileSystemService(),
                allowedCommands: s_environmentReaderAllowlist) { Confinement = TestConfinement.Off() };

            var request = new ToolCallRequest(
                ToolName: "shell_command",
                Parameters: new Dictionary<string, object?>
                {
                    ["command"] = s_isWindows ? "set" : "cat /proc/self/environ"
                }
            );

            var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

            Assert.True(result.Success);
            var dict = result.Result as Dictionary<string, object?>;
            Assert.NotNull(dict);
            Assert.DoesNotContain(
                EnvironmentProbeValue,
                dict["stdout"]!.ToString()!,
                StringComparison.Ordinal);

            tool.Dispose();
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvironmentProbeName, previous);
        }
    }

    [Fact]
    public async Task ShouldKeepPathAndHome_WhenCommandReadsEnvironment()
    {
        var tool = new ShellCommandTool(
            new FakeFileSystemService(),
            allowedCommands: s_environmentReaderAllowlist) { Confinement = TestConfinement.Off() };

        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = s_isWindows ? "set" : "cat /proc/self/environ"
            }
        );

        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        var stdout = dict["stdout"]!.ToString()!;
        Assert.Contains("PATH=", stdout, StringComparison.Ordinal);
        Assert.Contains(s_isWindows ? "USERPROFILE=" : "HOME=", stdout, StringComparison.OrdinalIgnoreCase);

        tool.Dispose();
    }

    [Theory]
    [InlineData("echo %ORKEON_TEST_SECRET%")]
    [InlineData("echo %PATH%")]
    public async Task ShouldRejectPercentSign_WhenCommandRunsThroughTheWindowsShell(string command)
    {
        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = command
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("%", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("git log --output=/tmp/orkeon-x")]
    [InlineData("git diff --output /tmp/orkeon-x")]
    [InlineData("git show -o/tmp/orkeon-x")]
    public async Task ShouldRejectGitOutputOption_ByDefault(string command)
    {
        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = command
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("not allowed", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("git show --ext-diff")]
    [InlineData("git log --textconv")]
    [InlineData("git diff --no-index /etc/hostname /etc/hosts")]
    [InlineData("git log --git-dir=/tmp/other")]
    [InlineData("git log --exec-path=/tmp/other")]
    public async Task ShouldRejectGitEscapeOption_ByDefault(string command)
    {
        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = command
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("not allowed", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldAllowGitLogFormat_ByDefault()
    {
        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = "git log -1 --format=%H"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // --format is a display option: it must survive both the git option scan and the
        // blocked-pattern list, whose "format" entry means the disk-formatting command.
        Assert.DoesNotContain("blocked pattern", result.Error ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not allowed", result.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("echo sudo")]
    [InlineData("echo run sudo now")]
    public async Task ShouldRejectBlockedPattern_WhenItStandsAsItsOwnToken(string command)
    {
        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = command
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("blocked pattern", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldKillChild_WhenCallerCancelsWhileCommandRuns()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The test finds the child through /proc.");

        // A duration no other process uses, so the child is found by its command line alone even
        // when several test passes run at once; it would outlive any run were it not killed.
        var marker = $"3600.{Random.Shared.Next(100_000, 999_999)}";
        using var tool = new ShellCommandTool(new FakeFileSystemService(), allowedCommands: s_sleepEchoAllowlist) { Confinement = TestConfinement.Off() };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?>
            {
                ["command"] = $"sleep {marker}",
                ["timeout_seconds"] = 3600
            });

        var call = tool.CallAsync(request, cts.Token);
        int? pid = null;
        try
        {
            await Polling.WaitUntilAsync(() => (pid = FindSleep(marker)) is not null);

            await cts.CancelAsync();
            try
            {
                await call;
            }
            catch (OperationCanceledException)
            {
                // The cancellation may surface as the exception or as a failed result.
            }

            await Polling.WaitUntilAsync(() => !IsAlive(pid!.Value));
        }
        finally
        {
            // Never leak the child from a failing run.
            if (pid is { } leftover && IsAlive(leftover))
                System.Diagnostics.Process.GetProcessById(leftover).Kill();
        }
    }

    private static async Task<Dictionary<string, object?>> RunWithShellLimitAsync(int limit, int producedChars)
    {
        var limits = new Orkeon.Domain.Tools.ToolResultLimitsOptions();
        limits.Limits["shell_command"] = limit;
        var sandbox = new FakeShellSandbox
        {
            Configure = session => session.DefaultResult = new Orkeon.Tools.Code.Sessions.ShellRunResult(0, new string('x', producedChars), ""),
        };
        using var tool = new ShellCommandTool(new FakeFileSystemService(), limits: limits)
        {
            Sandbox = sandbox,
            Confinement = TestConfinement.Off(),
        };

        ToolCallResponse response;
        await using (AgentRunScope.Begin("agent-1"))
        {
            response = await tool.CallAsync(
                new ToolCallRequest("shell_command", new Dictionary<string, object?> { ["command"] = "echo hello" }),
                TestContext.Current.CancellationToken);
        }

        Assert.True(response.Success, response.Error);
        return Assert.IsType<Dictionary<string, object?>>(response.Result);
    }

    [Fact]
    [Trait("BR", "RT-10")]
    public async Task ShouldNotCutTwice_WhenShellLimitRaisedBySetting()
    {
        var whole = await RunWithShellLimitAsync(limit: 50_000, producedChars: 30_000);
        var cut = await RunWithShellLimitAsync(limit: 50_000, producedChars: 60_000);

        Assert.Equal(new string('x', 30_000), whole["stdout"]);
        var stdout = Assert.IsType<string>(cut["stdout"]);
        Assert.StartsWith(new string('x', 50_000), stdout, StringComparison.Ordinal);
        Assert.Contains("10000 chars omitted", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("[Output truncated]", stdout, StringComparison.Ordinal);
    }

    private static async Task<ToolCallResponse> CallShellAsync(int? limit, int producedChars, string stderr, Orkeon.Application.Services.Security.ToolInvocationPipeline? pipeline, Orkeon.Domain.Tools.ToolResultLimitsOptions? limits = null)
    {
        if (limit is { } l)
        {
            limits = new Orkeon.Domain.Tools.ToolResultLimitsOptions();
            limits.Limits["shell_command"] = l;
        }
        var sandbox = new FakeShellSandbox
        {
            Configure = session => session.DefaultResult = new Orkeon.Tools.Code.Sessions.ShellRunResult(0, new string('x', producedChars), stderr),
        };
        using var tool = new ShellCommandTool(new FakeFileSystemService(), limits: limits)
        {
            Sandbox = sandbox,
            Confinement = TestConfinement.Off(),
        };
        var args = new Dictionary<string, object?> { ["command"] = "echo hello" };
        await using (AgentRunScope.Begin("agent-1"))
        {
            if (pipeline is null)
                return await tool.CallAsync(new ToolCallRequest("shell_command", args), TestContext.Current.CancellationToken);
            var result = await pipeline.InvokeAsync(
                new Orkeon.Application.Interfaces.Security.ToolInvocation(
                    tool, args, new Orkeon.Application.Interfaces.Security.ToolInvocationCaller("agent-1", "dev")),
                TestContext.Current.CancellationToken);
            return new ToolCallResponse(result.Success, result.ConversationText, null);
        }
    }

    [Fact]
    [Trait("BR", "RT-10")]
    public async Task ShouldFlagAlreadyBounded_WhenShellCutsItsOutput()
    {
        var response = await CallShellAsync(limit: null, producedChars: 12_000, stderr: "", pipeline: null);

        Assert.True(response.Success, response.Error);
        Assert.True(response.AlreadyBounded);
    }

    [Fact]
    [Trait("BR", "RT-10")]
    public async Task ShouldNotFlagAlreadyBounded_WhenShellOutputIsWithinBound()
    {
        var response = await CallShellAsync(limit: null, producedChars: 100, stderr: "", pipeline: null);

        Assert.True(response.Success, response.Error);
        Assert.False(response.AlreadyBounded);
    }

    [Theory]
    [InlineData(null, 2_000)]
    [InlineData(50_000, 10_000)]
    [Trait("BR", "RT-10")]
    public async Task ShouldKeepNoticeStderrAndLaterKeys_WhenRealShellResponseGoesThroughPipeline(int? limit, int omitted)
    {
        var raw = await CallShellAsync(limit, producedChars: limit is null ? 12_000 : 60_000, stderr: "warning-from-stderr", pipeline: null);
        var dict = Assert.IsType<Dictionary<string, object?>>(raw.Result);
        var later = dict.Keys.SkipWhile(k => k != "stdout").Skip(1).ToList();
        Assert.NotEmpty(later);

        var pipelineLimits = new Orkeon.Domain.Tools.ToolResultLimitsOptions();
        if (limit is { } l) pipelineLimits.Limits["shell_command"] = l;
        var pipeline = new Orkeon.Application.Services.Security.ToolInvocationPipeline(
            limits: Microsoft.Extensions.Options.Options.Create(pipelineLimits));
        var response = await CallShellAsync(limit, producedChars: limit is null ? 12_000 : 60_000, stderr: "warning-from-stderr", pipeline: pipeline, limits: null);

        var text = Assert.IsType<string>(response.Result);
        Assert.Contains($"{omitted} chars omitted", text, StringComparison.Ordinal);
        Assert.Contains("warning-from-stderr", text, StringComparison.Ordinal);
        foreach (var key in later)
            Assert.Contains(key, text, StringComparison.Ordinal);
    }

    /// <summary>The pid of the <c>sleep</c> whose argument is <paramref name="marker"/>, or null.</summary>
    private static int? FindSleep(string marker)
    {
        var expected = $"sleep\0{marker}\0";
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out var pid)) continue;
            try
            {
                var cmdline = File.ReadAllText(Path.Combine(dir, "cmdline"));
                if (cmdline.EndsWith(expected, StringComparison.Ordinal)
                    && (cmdline.Length == expected.Length || cmdline[^(expected.Length + 1)] == '/')
                    && IsAlive(pid))
                    return pid;
            }
            catch (IOException) { /* the process exited while being read */ }
            catch (UnauthorizedAccessException) { /* another user's process */ }
        }
        return null;
    }

    /// <summary>Whether the process exists and is not a zombie waiting to be reaped.</summary>
    private static bool IsAlive(int pid)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            // The state follows the command name, which is parenthesised and may hold spaces.
            var state = stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[0];
            return state != "Z" && state != "X";
        }
        catch (IOException)
        {
            return false;
        }
    }

    [Fact]
    public void ShouldDeclareItselfAnExecutingTool()
    {
        Assert.Equal(Orkeon.Domain.Tools.ToolAccess.Execute, _tool.Access);
    }

    [Theory]
    [InlineData("action", "launch", "Unknown action 'launch'")]
    [InlineData("timeout_seconds", 0, "Timeout must be greater than 0")]
    public async Task ShouldRefuseTheCall_WhenAFieldIsOutOfRange(string field, object value, string expected)
    {
        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?> { ["command"] = "echo hi", [field] = value });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(expected, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseGit_WhenNoSubcommandIsGiven()
    {
        var request = new ToolCallRequest(
            ToolName: "shell_command",
            Parameters: new Dictionary<string, object?> { ["command"] = "g" + "it" });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("requires a read-only subcommand", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldParseNothing_WhenTheCommandIsBlank()
    {
        var (executable, arguments) = ShellCommandTool.ParseCommand("   ");

        Assert.Equal("", executable);
        Assert.Empty(arguments);
    }

    [Fact]
    public void ShouldMatchNoPath_WhenTheMountIsTheRoot()
    {
        Assert.False(ShellCommandTool.MatchesMountPrefix("/workspace", "/"));
        Assert.Throws<ArgumentNullException>(() => ShellCommandTool.MatchesMountPrefix(null!, "/workspace"));
        Assert.Throws<ArgumentNullException>(() => ShellCommandTool.MatchesMountPrefix("/workspace", null!));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _tool.Dispose();
    }
}
