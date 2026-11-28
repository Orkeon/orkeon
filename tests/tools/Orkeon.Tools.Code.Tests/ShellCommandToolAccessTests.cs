using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Security;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.Code.Tests;

/// <summary>
/// The access class a <c>shell_command</c> call declares (#494), and the verdict the mode gate draws from it:
/// a <c>run</c> of a default read-only command is a read, everything else executes.
/// </summary>
public sealed class ShellCommandToolAccessTests
{
    private static readonly string[] s_extraMake = ["make"];

    private static ShellCommandTool Tool(bool allowInterpreters = false, IEnumerable<string>? extra = null) =>
        new(new FakeFileSystemService(), allowInterpreters: allowInterpreters, extraAllowedCommands: extra)
        {
            Confinement = TestConfinement.Off(),
            // A git recent enough for the whole safe set, whatever the host has (see ShellCommandToolGitHardeningTests).
            GitVersion = () => ShellCommandTool.MinimumSafeGitVersion,
        };

    private static ToolAccess Access(Dictionary<string, object?> arguments, bool allowInterpreters = false, IEnumerable<string>? extra = null)
    {
        using var tool = Tool(allowInterpreters, extra);
        return tool.AccessFor(arguments);
    }

    private static Dictionary<string, object?> Run(string command, string? action = null)
    {
        var arguments = new Dictionary<string, object?> { ["command"] = command };
        if (action is not null)
            arguments["action"] = action;
        return arguments;
    }

    [Theory]
    [InlineData("git status")]
    [InlineData("git log --oneline -5")]
    [InlineData("git diff HEAD~1")]
    [InlineData("ls")]
    [InlineData("ls -la /workspace")]
    [InlineData("cat README.md")]
    [InlineData("grep -n foo src")]
    public void A_run_of_a_read_only_command_is_a_read(string command)
    {
        Assert.Equal(ToolAccess.Read, Access(Run(command)));
        Assert.Equal(ToolAccess.Read, Access(Run(command, "run")));
    }

    [Theory]
    [InlineData("git commit -m x")]
    [InlineData("git diff --output=x")]
    [InlineData("git diff -ox")]
    [InlineData("git -c core.pager=x log")]
    [InlineData("git")]
    [InlineData("npm test")]
    [InlineData("ls | grep x")]
    [InlineData("cat a > b")]
    [InlineData("rm -rf x")]
    [InlineData("")]
    public void A_command_that_may_write_or_is_refused_executes(string command)
    {
        Assert.Equal(ToolAccess.Execute, Access(Run(command)));
    }

    [Theory]
    [InlineData("start_job")]
    [InlineData("job_status")]
    [InlineData("job_tail")]
    public void Any_action_but_run_executes(string action)
    {
        Assert.Equal(ToolAccess.Execute, Access(Run("ls", action)));
    }

    [Fact]
    public void A_command_the_extra_allowlist_adds_executes()
    {
        Assert.Equal(ToolAccess.Execute, Access(Run("make"), extra: s_extraMake));
        Assert.Equal(ToolAccess.Read, Access(Run("ls"), extra: s_extraMake));
    }

    [Fact]
    public void Interpreters_and_the_git_they_unlock_execute()
    {
        Assert.Equal(ToolAccess.Execute, Access(Run("npm test"), allowInterpreters: true));
        Assert.Equal(ToolAccess.Execute, Access(Run("git status"), allowInterpreters: true));
        Assert.Equal(ToolAccess.Execute, Access(Run("git commit -m x"), allowInterpreters: true));
        Assert.Equal(ToolAccess.Read, Access(Run("ls"), allowInterpreters: true));
    }

    [Fact]
    public void A_json_string_argument_is_read_like_a_string()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""{"command":"git status","action":"run"}""");
        var arguments = document.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => (object?)p.Value.Clone());

        Assert.Equal(ToolAccess.Read, Access(arguments));
    }

    [Theory]
    [InlineData("git status", null)]
    [InlineData("ls", null)]
    public async Task Plan_mode_lets_a_read_only_command_run(string command, string? action)
    {
        var verdict = await CheckAsync("plan", false, Run(command, action));

        Assert.Equal(PermissionAction.Allow, verdict.Action);
    }

    [Theory]
    [InlineData("git commit -m x", null, false)]
    [InlineData("git diff --output=x", null, false)]
    [InlineData("npm test", null, false)]
    [InlineData("npm test", null, true)]
    [InlineData("ls", "start_job", false)]
    public async Task Plan_mode_denies_the_rest_naming_what_it_allows(string command, string? action, bool interpreters)
    {
        var verdict = await CheckAsync("plan", interpreters, Run(command, action));

        Assert.Equal(PermissionAction.Deny, verdict.Action);
        Assert.Contains("plan mode", verdict.Message, StringComparison.Ordinal);
        Assert.Contains("read-only command", verdict.Message, StringComparison.Ordinal);
        Assert.Contains("git status/log/diff/show", verdict.Message, StringComparison.Ordinal);
        foreach (var name in s_readOnlyCommands)
            Assert.Contains($"{name}, ", verdict.Message, StringComparison.Ordinal);
    }

    // Every default read-only command but git, which the message names with its subcommands.
    private static readonly string[] s_readOnlyCommands = ["ls", "cat", "pwd", "which", "grep", "wc", "echo", "dir", "type", "where"];

    [Theory]
    [InlineData("git commit -m x", null)]
    [InlineData("git diff --output=x", null)]
    [InlineData("npm test", null)]
    [InlineData("ls", "start_job")]
    public async Task Default_mode_still_asks_for_what_may_write(string command, string? action)
    {
        var verdict = await CheckAsync("default", true, Run(command, action), interactive: true);

        Assert.Equal(PermissionAction.Ask, verdict.Action);
    }

    [Theory]
    [InlineData("git status")]
    [InlineData("ls")]
    public async Task Default_mode_runs_a_read_only_command(string command)
    {
        var verdict = await CheckAsync("default", false, Run(command), interactive: true);

        Assert.Equal(PermissionAction.Allow, verdict.Action);
    }

    private static Task<PermissionVerdict> CheckAsync(
        string mode, bool allowInterpreters, Dictionary<string, object?> arguments, bool interactive = false) =>
        new ModePermissionGate(interactive).CheckAsync(
            "shell_command", arguments, mode, Access(arguments, allowInterpreters), TestContext.Current.CancellationToken);
}
