using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Security;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Code.Confinement;
using Orkeon.Tools.Code.Sessions;

namespace Orkeon.Tools.Code.Tests;

/// <summary>
/// The access class a <c>shell_command</c> call declares (#494), and the verdict the mode gate draws from it:
/// a <c>run</c> of a default read-only command is a read when the tool can state where it reads (#497), everything
/// else executes.
/// </summary>
public sealed class ShellCommandToolAccessTests
{
    private static readonly string[] s_extraMake = ["make"];

    // One mount, /workspace, which is the working directory; a nested mount when a test asks for one.
    private static FakeFileSystemService Workspace(params string[] otherMounts)
    {
        var fileSystem = new FakeFileSystemService().AddMount("/workspace");
        foreach (var mount in otherMounts)
            fileSystem.AddMount(mount);
        fileSystem.WorkingDirectory = "/workspace";
        return fileSystem;
    }

    private static ShellCommandTool Tool(bool allowInterpreters = false, IEnumerable<string>? extra = null, FakeFileSystemService? fileSystem = null) =>
        new(fileSystem ?? Workspace(), allowInterpreters: allowInterpreters, extraAllowedCommands: extra)
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

    private static Dictionary<string, object?> Run(string command, string? action = null, string? workingDirectory = null)
    {
        var arguments = new Dictionary<string, object?> { ["command"] = command };
        if (action is not null)
            arguments["action"] = action;
        if (workingDirectory is not null)
            arguments["working_directory"] = workingDirectory;
        return arguments;
    }

    [Theory]
    [Trait("BR", "CH-497")]
    [InlineData("cat /home/u/.ssh/id_ed25519")]
    [InlineData("grep -r TOKEN /home/u/.config")]
    [InlineData("ls /")]
    [InlineData("cat /workspaces/x")]
    [InlineData("cat /workspace/../etc/passwd")]
    [InlineData("cat ~/.ssh/id_ed25519")]
    [InlineData("cat ~")]
    [InlineData("cat ../../some/other/project/.env")]
    [InlineData("cat ../workspace/x")]
    [InlineData("grep --file=/etc/passwd x")]
    [InlineData("grep --file=../x y")]
    [InlineData("grep -f/etc/passwd x")]
    [InlineData("grep -f ~/.netrc x")]
    [InlineData("wc a=/etc/passwd")]
    [InlineData("cat C:/Users/u/.ssh/id")]
    [InlineData("git diff -- /etc/passwd")]
    [InlineData("git log -- ../..")]
    [InlineData("git diff --no-index /workspace/a /etc/passwd")]
    public void A_path_the_tool_cannot_place_in_a_mount_executes(string command)
    {
        Assert.Equal(ToolAccess.Execute, Access(Run(command)));
    }

    [Theory]
    [Trait("BR", "CH-497")]
    [InlineData("cat src/a.cs", null)]
    [InlineData("cat ./src/../README.md", null)]
    [InlineData("cat /workspace/src/a.cs", null)]
    [InlineData("grep -rn Foo src", null)]
    [InlineData("grep -rn --include=*.cs Foo .", null)]
    [InlineData("grep -e ^/usr x", null)]
    [InlineData("grep --file=/workspace/patterns x", null)]
    [InlineData("cat ../README.md", "/workspace/src")]
    [InlineData("ls ..", "/workspace/src")]
    [InlineData("git show HEAD:src/a.cs", null)]
    [InlineData("git log --oneline -- src", null)]
    [InlineData("echo /etc/passwd", null)]
    [InlineData("pwd", null)]
    public void A_path_inside_a_mount_reads(string command, string? workingDirectory)
    {
        Assert.Equal(ToolAccess.Read, Access(Run(command, workingDirectory: workingDirectory)));
    }

    [Fact]
    [Trait("BR", "CH-497")]
    public void A_working_directory_outside_the_mounts_executes()
    {
        Assert.Equal(ToolAccess.Execute, Access(Run("ls", workingDirectory: "/home/u")));
        Assert.Equal(ToolAccess.Execute, Access(Run("ls", workingDirectory: "/workspace/..")));
    }

    [Fact]
    [Trait("BR", "CH-497")]
    public void With_no_mount_nothing_is_placed_and_a_command_executes()
    {
        using var tool = Tool(fileSystem: new FakeFileSystemService());

        Assert.Equal(ToolAccess.Execute, tool.AccessFor(Run("cat README.md")));
    }

    [Fact]
    [Trait("BR", "CH-497")]
    public void A_relative_path_that_ends_in_a_nested_mount_executes()
    {
        // The process resolves 'sub/x' from the physical working directory, never through the /workspace/sub mount.
        using var tool = Tool(fileSystem: Workspace("/workspace/sub"));

        Assert.Equal(ToolAccess.Execute, tool.AccessFor(Run("cat sub/x")));
        Assert.Equal(ToolAccess.Read, tool.AccessFor(Run("cat /workspace/sub/x")));
        Assert.Equal(ToolAccess.Read, tool.AccessFor(Run("cat ../x", workingDirectory: "/workspace/sub/d")));
        Assert.Equal(ToolAccess.Execute, tool.AccessFor(Run("cat ../../x", workingDirectory: "/workspace/sub/d")));
    }

    public static TheoryData<string> ConfiningSandboxes => ["bubblewrap", "seatbelt"];

    private static IShellSandbox SandboxNamed(string name) => name switch
    {
        "bubblewrap" => new BubblewrapShellSandbox(() => BubblewrapAvailability.Available("/usr/bin/bwrap")),
        "seatbelt" => new SeatbeltShellSandbox(() => ShellSandboxAvailability.Available),
        _ => new AppContainerShellSandbox(() => ShellSandboxAvailability.Available),
    };

    [Theory]
    [Trait("BR", "CH-497")]
    [MemberData(nameof(ConfiningSandboxes))]
    public void A_shell_confined_to_the_mounts_reads_whatever_it_names(string sandbox)
    {
        using var tool = new ShellCommandTool(Workspace()) { Sandbox = SandboxNamed(sandbox), GitVersion = () => ShellCommandTool.MinimumSafeGitVersion };

        Assert.Equal(ToolAccess.Read, tool.AccessFor(Run("cat /home/u/.ssh/id_ed25519")));
        Assert.Equal(ToolAccess.Read, tool.AccessFor(Run("cat ../../x/.env")));
        Assert.Equal(ToolAccess.Execute, tool.AccessFor(Run("npm test")));
    }

    [Theory]
    [Trait("BR", "CH-497")]
    [MemberData(nameof(ConfiningSandboxes))]
    public void An_extra_read_only_path_makes_a_confined_shell_place_its_arguments(string sandbox)
    {
        using var tool = new ShellCommandTool(Workspace()) { Sandbox = SandboxNamed(sandbox) };
        tool.Confinement.ReadOnlyPaths.Add("/opt/sdk");

        Assert.Equal(ToolAccess.Execute, tool.AccessFor(Run("cat /opt/sdk/secret")));
        Assert.Equal(ToolAccess.Read, tool.AccessFor(Run("cat /workspace/a")));
    }

    [Fact]
    [Trait("BR", "CH-497")]
    public void An_app_container_places_its_arguments()
    {
        using var tool = new ShellCommandTool(Workspace()) { Sandbox = SandboxNamed("appcontainer") };

        Assert.Equal(ToolAccess.Execute, tool.AccessFor(Run("cat /home/u/.ssh/id_ed25519")));
        Assert.Equal(ToolAccess.Read, tool.AccessFor(Run("cat src/a.cs")));
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
        Assert.Equal(ToolAccess.Execute, Access(Run("git commit -m x"), allowInterpreters: true));
        Assert.Equal(ToolAccess.Execute, Access(Run("git -c core.pager=x log"), allowInterpreters: true));
        Assert.Equal(ToolAccess.Execute, Access(Run("git diff --output=x"), allowInterpreters: true));
        Assert.Equal(ToolAccess.Read, Access(Run("ls"), allowInterpreters: true));
    }

    [Theory]
    [Trait("BR", "CH-497")]
    [InlineData("git status")]
    [InlineData("git log --oneline -5")]
    [InlineData("git diff HEAD~1")]
    [InlineData("git show HEAD")]
    public void The_four_git_reads_stay_reads_with_interpreters(string command)
    {
        Assert.Equal(ToolAccess.Read, Access(Run(command), allowInterpreters: true));
        Assert.Equal(ToolAccess.Execute, Access(Run(command + " -- /etc"), allowInterpreters: true));
    }

    [Fact]
    [Trait("BR", "CH-497")]
    public void A_git_too_old_for_the_safe_set_executes_with_interpreters()
    {
        using var tool = new ShellCommandTool(Workspace(), allowInterpreters: true)
        {
            Confinement = TestConfinement.Off(),
            GitVersion = () => new Version(2, 43, 0),
        };

        Assert.Equal(ToolAccess.Execute, tool.AccessFor(Run("git status")));
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
    [InlineData("git status", null, false)]
    [InlineData("ls", null, false)]
    [InlineData("git status", null, true)]
    [InlineData("git log --oneline -5", null, true)]
    public async Task Plan_mode_lets_a_read_only_command_run(string command, string? action, bool interpreters)
    {
        var verdict = await CheckAsync("plan", interpreters, Run(command, action));

        Assert.Equal(PermissionAction.Allow, verdict.Action);
    }

    [Theory]
    [InlineData("git commit -m x", null, false)]
    [InlineData("git diff --output=x", null, false)]
    [InlineData("npm test", null, false)]
    [InlineData("npm test", null, true)]
    [InlineData("ls", "start_job", false)]
    [InlineData("cat /home/u/.ssh/id_ed25519", null, false)]
    [InlineData("cat ../../x/.env", null, false)]
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
    [InlineData("cat /home/u/.ssh/id_ed25519", null)]
    [InlineData("grep -r TOKEN /home/u/.config", null)]
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
