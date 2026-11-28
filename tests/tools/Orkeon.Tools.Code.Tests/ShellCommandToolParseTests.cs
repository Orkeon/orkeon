using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Code.Tests.Doubles;

namespace Orkeon.Tools.Code.Tests;

/// <summary>
/// Validation, the access class and execution read one and the same parse of a command (#494 review S1),
/// and a read-only <c>git</c> call stays read-only whatever the repository configures (S2).
/// </summary>
public sealed class ShellCommandToolParseTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (ShellCommandTool Tool, FakeShellSandbox Sandbox) NewTool(IEnumerable<string>? extra = null)
    {
        var sandbox = new FakeShellSandbox();
        var tool = new ShellCommandTool(new FakeFileSystemService(), extraAllowedCommands: extra)
        {
            Sandbox = sandbox,
            Confinement = TestConfinement.Off(),
        };
        return (tool, sandbox);
    }

    private static Dictionary<string, object?> Run(string command) => new() { ["command"] = command };

    [Theory]
    [Trait("BR", "CH-494")]
    [InlineData("\"/x/ls /y/sh\" -c id")]
    [InlineData("'/x/ls /y/sh' -c id")]
    [InlineData("l\"s /y/\"sh -c id")]
    [InlineData("\"ls \" -la")]
    public async Task A_quoted_executable_holding_a_space_is_refused_and_executes(string command)
    {
        var (tool, sandbox) = NewTool();
        using (tool)
        {
            Assert.Equal(ToolAccess.Execute, tool.AccessFor(Run(command)));

            var result = await tool.CallAsync(new ToolCallRequest("shell_command", Run(command)), Ct);

            Assert.False(result.Success);
            Assert.Empty(sandbox.Sessions.SelectMany(s => s.Invocations));
        }
    }

    [Fact]
    [Trait("BR", "CH-494")]
    public async Task The_executable_validated_is_the_one_run()
    {
        var (tool, sandbox) = NewTool();
        using (tool)
        {
            var result = await tool.CallAsync(new ToolCallRequest("shell_command", Run("\"ls\" -la")), Ct);

            Assert.True(result.Success, result.Error);
            Assert.Equal("ls", Assert.Single(Assert.Single(sandbox.Sessions).Invocations).Executable);
            var workspace = new FakeFileSystemService().AddMount("/workspace");
            workspace.WorkingDirectory = "/workspace";
            using var placed = new ShellCommandTool(workspace) { Confinement = TestConfinement.Off() };
            Assert.Equal(ToolAccess.Read, placed.AccessFor(Run("\"ls\" -la /workspace")));
        }
    }

    [Theory]
    [Trait("BR", "CH-494")]
    [InlineData("rm \"-rf\" /")]
    [InlineData("rm '-rf' /")]
    [InlineData("rm -r\"f\" /")]
    public async Task A_blocked_pattern_split_by_quotes_is_still_refused(string command)
    {
        var (tool, sandbox) = NewTool(["rm"]);
        using (tool)
        {
            var result = await tool.CallAsync(new ToolCallRequest("shell_command", Run(command)), Ct);

            Assert.False(result.Success);
            Assert.Contains("blocked pattern", result.Error, StringComparison.Ordinal);
            Assert.Empty(sandbox.Sessions.SelectMany(s => s.Invocations));
        }
    }

    [Theory]
    [Trait("BR", "CH-494")]
    [InlineData("git STATUS")]
    [InlineData("git Log")]
    [InlineData("git log --show-signature")]
    public async Task A_git_subcommand_is_matched_exactly(string command)
    {
        var (tool, sandbox) = NewTool();
        using (tool)
        {
            Assert.Equal(ToolAccess.Execute, tool.AccessFor(Run(command)));

            var result = await tool.CallAsync(new ToolCallRequest("shell_command", Run(command)), Ct);

            Assert.False(result.Success);
            Assert.Empty(sandbox.Sessions.SelectMany(s => s.Invocations));
        }
    }

    [Theory]
    [Trait("BR", "CH-494")]
    [InlineData("git status", "status", false)]
    [InlineData("git diff HEAD~1", "diff", true)]
    [InlineData("git show HEAD", "show", true)]
    [InlineData("git log -p -1", "log", true)]
    public async Task A_read_only_git_runs_with_the_tools_safe_overrides(string command, string subcommand, bool diffing)
    {
        var (tool, sandbox) = NewTool();
        using (tool)
        {
            var result = await tool.CallAsync(new ToolCallRequest("shell_command", Run(command)), Ct);

            Assert.True(result.Success, result.Error);
            var arguments = sandbox.Sessions.SelectMany(s => s.Invocations).Last().Arguments.ToList();
            var at = arguments.IndexOf(subcommand);
            Assert.True(at > 0, string.Join(' ', arguments));
            var global = arguments[..at];
            Assert.Contains("core.fsmonitor=false", global);
            Assert.Contains("core.pager=cat", global);
            Assert.Contains("log.showSignature=false", global);
            Assert.All(global.Where(option => !option.StartsWith("--", StringComparison.Ordinal)).Where((_, i) => i % 2 == 0), option => Assert.Equal("-c", option));
            Assert.Equal(diffing, arguments.Contains("--no-ext-diff"));
            Assert.Equal(diffing, arguments.Contains("--no-textconv"));
            if (diffing)
                Assert.Equal(subcommand, arguments[arguments.IndexOf("--no-ext-diff") - 2]);
        }
    }

    [Fact]
    [Trait("BR", "CH-494")]
    public async Task A_git_the_interpreters_unlock_runs_as_written()
    {
        var sandbox = new FakeShellSandbox();
        using var tool = new ShellCommandTool(new FakeFileSystemService(), allowInterpreters: true)
        {
            Sandbox = sandbox,
            Confinement = TestConfinement.Off(),
            GitVersion = () => ShellCommandTool.MinimumSafeGitVersion,
        };

        var result = await tool.CallAsync(new ToolCallRequest("shell_command", Run("git commit -m x")), Ct);

        Assert.True(result.Success, result.Error);
        Assert.Equal(["commit", "-m", "x"], Assert.Single(Assert.Single(sandbox.Sessions).Invocations).Arguments);
    }

    [Fact]
    [Trait("BR", "CH-497")]
    public async Task One_of_the_four_git_reads_keeps_the_safe_options_with_interpreters()
    {
        var sandbox = new FakeShellSandbox();
        using var tool = new ShellCommandTool(new FakeFileSystemService(), allowInterpreters: true)
        {
            Sandbox = sandbox,
            Confinement = TestConfinement.Off(),
            GitVersion = () => ShellCommandTool.MinimumSafeGitVersion,
        };

        var result = await tool.CallAsync(new ToolCallRequest("shell_command", Run("git status")), Ct);

        Assert.True(result.Success, result.Error);
        // The filter drivers are read first, then the call runs behind the tool's own configuration.
        var call = sandbox.Sessions.SelectMany(session => session.Invocations).Last().Arguments;
        Assert.Contains("status", call);
        Assert.Contains("core.fsmonitor=false", call);
        Assert.NotEqual("status", call[0]);
    }
}
