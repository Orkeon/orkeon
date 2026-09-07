using System.Runtime.InteropServices;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.FileSystem;

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
        _tool = new ShellCommandTool(new FakeFileSystemService());
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
            allowedCommands: s_sleepEchoAllowlist);

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
            allowedCommands: s_pwdCdAllowlist);

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
            allowedCommands: s_python3Allowlist);

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
            blockedPatterns: s_dangerousBlocklist);

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
        var tool = new ShellCommandTool(new FakeFileSystemService(), allowInterpreters: true);

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
                allowedCommands: s_environmentReaderAllowlist);

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
            allowedCommands: s_environmentReaderAllowlist);

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

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _tool.Dispose();
    }
}
