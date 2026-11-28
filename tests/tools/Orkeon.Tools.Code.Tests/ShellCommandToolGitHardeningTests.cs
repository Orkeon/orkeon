using System.Runtime.InteropServices;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Code.Sessions;
using Orkeon.Tools.Code.Tests.Doubles;

namespace Orkeon.Tools.Code.Tests;

/// <summary>
/// A read-only <c>git</c> call (#494 review round 2, T1–T3, T8) runs no program the repository configures:
/// signature verification, attribute-driven filters and a lazy fetch are neutralised by the tool itself, on a
/// real <c>git</c> in a temporary repository whose configured programs would leave a marker file.
/// </summary>
public sealed class ShellCommandToolGitHardeningTests : IDisposable
{
    private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private readonly string _root = Directory.CreateTempSubdirectory("orkeon-shell-git-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ } catch (UnauthorizedAccessException) { /* best-effort */ }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Repo => Path.Combine(_root, "repo");

    private string Marker => Path.Combine(_root, "marker");

    private static Version? HostGitVersion()
    {
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("git", "--version") { RedirectStandardOutput = true, RedirectStandardError = true };
            using var process = System.Diagnostics.Process.Start(start)!;
            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return ShellCommandTool.ParseGitVersion(text);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static void SkipUnlessGit()
    {
        Assert.SkipWhen(s_isWindows, "The repository's programs are POSIX scripts.");
        Assert.SkipUnless(HostGitVersion() >= ShellCommandTool.MinimumSafeGitVersion, "A git recent enough for the tool's overrides is not on the PATH.");
    }

    private static string Git(string directory, params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)}: {stderr}");
        return stdout;
    }

    // A repository with one commit, and a program that leaves the marker when it runs.
    private string NewRepository()
    {
        Directory.CreateDirectory(Repo);
        Git(Repo, "init", "-q");
        Git(Repo, "config", "user.email", "agent@example.com");
        Git(Repo, "config", "user.name", "Agent");
        File.WriteAllText(Path.Combine(Repo, "f.txt"), "one\n");
        Git(Repo, "add", "f.txt");
        Git(Repo, "commit", "-q", "-m", "init");
        var program = Path.Combine(_root, "evil");
        File.WriteAllText(program, $"#!/bin/sh\ntouch '{Marker}'\ncat\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return program;
    }

    private ShellCommandTool NewTool() =>
        new(new DiskBackedFileSystemService(Repo, "/workspace")) { Confinement = TestConfinement.Off() };

    private static Dictionary<string, object?> Run(string command) =>
        new() { ["command"] = command, ["working_directory"] = "/workspace" };

    private async Task<ToolCallResponse> CallAsync(string command)
    {
        using var tool = NewTool();
        Assert.Equal(ToolAccess.Read, tool.AccessFor(Run(command)));
        return await tool.CallAsync(new ToolCallRequest("shell_command", Run(command)), Ct);
    }

    // A commit carrying a PGP signature block, so a format that shows the signature verifies it.
    private string SignedCommit()
    {
        var tree = Git(Repo, "rev-parse", "HEAD^{tree}").Trim();
        var parent = Git(Repo, "rev-parse", "HEAD").Trim();
        var body = $"tree {tree}\nparent {parent}\nauthor a <a@b> 1 +0000\ncommitter a <a@b> 1 +0000\n"
            + "gpgsig -----BEGIN PGP SIGNATURE-----\n \n abc\n -----END PGP SIGNATURE-----\n\nsigned\n";
        var path = Path.Combine(_root, "commit.txt");
        File.WriteAllText(path, body);
        var commit = Git(Repo, "hash-object", "-t", "commit", "-w", path).Trim();
        Git(Repo, "update-ref", "HEAD", commit);
        return commit;
    }

    [Fact]
    [Trait("BR", "CH-494")]
    public async Task A_default_format_that_shows_the_signature_runs_no_gpg_program()
    {
        SkipUnlessGit();
        var program = NewRepository();
        SignedCommit();
        Git(Repo, "config", "gpg.program", program);
        Git(Repo, "config", "gpg.ssh.program", program);
        Git(Repo, "config", "gpg.x509.program", program);
        Git(Repo, "config", "format.pretty", "format:%G? %GS");

        var result = await CallAsync("git log -1");

        Assert.True(result.Success, result.Error);
        Assert.False(File.Exists(Marker), "the repository's gpg.program ran");
    }

    [Theory]
    [Trait("BR", "CH-494")]
    [InlineData("git log --format=%G? -1")]
    [InlineData("git show --pretty=format:%GS")]
    [InlineData("git log --pretty=%GK")]
    public async Task A_signature_placeholder_is_refused(string command)
    {
        var sandbox = new FakeShellSandbox();
        using var tool = new ShellCommandTool(new FakeFileSystemService()) { Sandbox = sandbox, Confinement = TestConfinement.Off() };

        Assert.Equal(ToolAccess.Execute, tool.AccessFor(new Dictionary<string, object?> { ["command"] = command }));
        var result = await tool.CallAsync(new ToolCallRequest("shell_command", new Dictionary<string, object?> { ["command"] = command }), Ct);

        Assert.False(result.Success);
        Assert.Empty(sandbox.Sessions.SelectMany(s => s.Invocations));
    }

    [Theory]
    [Trait("BR", "CH-494")]
    [InlineData("info", "clean", "git diff")]
    [InlineData("info", "clean", "git status")]
    [InlineData("tree", "clean", "git diff")]
    [InlineData("global", "clean", "git diff")]
    [InlineData("info", "process", "git diff")]
    [InlineData("info", "smudge", "git show HEAD:f.txt")]
    public async Task A_filter_driver_the_repository_configures_does_not_run(string attributes, string kind, string command)
    {
        SkipUnlessGit();
        var program = NewRepository();
        var line = "* filter=evil\n";
        switch (attributes)
        {
            case "info":
                await File.WriteAllTextAsync(Path.Combine(Repo, ".git", "info", "attributes"), line, Ct);
                break;
            case "tree":
                await File.WriteAllTextAsync(Path.Combine(Repo, ".gitattributes"), line, Ct);
                break;
            default:
                var file = Path.Combine(_root, "attributes");
                await File.WriteAllTextAsync(file, line, Ct);
                Git(Repo, "config", "core.attributesFile", file);
                break;
        }

        Git(Repo, "config", $"filter.evil.{kind}", program);
        await File.AppendAllTextAsync(Path.Combine(Repo, "f.txt"), "two\n", Ct);
        File.SetLastWriteTimeUtc(Path.Combine(Repo, "f.txt"), DateTime.UtcNow.AddMinutes(5));

        var result = await CallAsync(command);

        Assert.True(result.Success, result.Error);
        Assert.False(File.Exists(Marker), $"the repository's filter.evil.{kind} ran");
    }

    [Fact]
    [Trait("BR", "CH-494")]
    public async Task A_missing_object_of_a_partial_clone_is_not_fetched()
    {
        SkipUnlessGit();
        var program = NewRepository();
        Git(Repo, "config", "core.repositoryformatversion", "1");
        Git(Repo, "config", "extensions.partialClone", "origin");
        Git(Repo, "config", "remote.origin.url", $"ext::{program} %S");
        Git(Repo, "config", "remote.origin.promisor", "true");
        Git(Repo, "config", "protocol.ext.allow", "always");
        var listing = Path.Combine(_root, "tree.txt");
        await File.WriteAllTextAsync(listing, "100644 blob 0123456789012345678901234567890123456789\tmissing\n", Ct);
        var start = new System.Diagnostics.ProcessStartInfo("sh") { WorkingDirectory = Repo, RedirectStandardOutput = true };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add($"git update-ref HEAD $(git commit-tree $(git mktree --missing < '{listing}') -m missing)");
        using (var process = System.Diagnostics.Process.Start(start)!)
            await process.WaitForExitAsync(Ct);

        var result = await CallAsync("git show HEAD");

        Assert.True(result.Success, result.Error);
        Assert.False(File.Exists(Marker), "the partial clone fetched through the repository's remote");
    }

    [Theory]
    [Trait("BR", "CH-494")]
    [InlineData("git status --ignore-submodules=none")]
    [InlineData("git diff --submodule=diff")]
    public async Task A_submodule_option_is_refused(string command)
    {
        var sandbox = new FakeShellSandbox();
        using var tool = new ShellCommandTool(new FakeFileSystemService()) { Sandbox = sandbox, Confinement = TestConfinement.Off() };

        var result = await tool.CallAsync(new ToolCallRequest("shell_command", new Dictionary<string, object?> { ["command"] = command }), Ct);

        Assert.False(result.Success);
        Assert.Empty(sandbox.Sessions.SelectMany(s => s.Invocations));
    }

    [Theory]
    [Trait("BR", "CH-494")]
    [InlineData("git status", "status")]
    [InlineData("git log -1", "log")]
    public async Task A_read_only_git_takes_no_optional_lock_and_fetches_nothing(string command, string subcommand)
    {
        var sandbox = new FakeShellSandbox();
        using var tool = new ShellCommandTool(new FakeFileSystemService())
        {
            Sandbox = sandbox,
            Confinement = TestConfinement.Off(),
            GitVersion = () => new Version(2, 53, 0),
        };

        var result = await tool.CallAsync(new ToolCallRequest("shell_command", new Dictionary<string, object?> { ["command"] = command }), Ct);

        Assert.True(result.Success, result.Error);
        var arguments = sandbox.Sessions.SelectMany(s => s.Invocations).Last().Arguments.ToList();
        var global = arguments[..arguments.IndexOf(subcommand)];
        Assert.Contains("--no-optional-locks", global);
        Assert.Contains("--no-lazy-fetch", global);
        Assert.Contains("gpg.program=false", global);
        Assert.Contains("gpg.ssh.program=false", global);
        Assert.Contains("gpg.x509.program=false", global);
        Assert.Contains("protocol.allow=never", global);
        Assert.Equal("--ignore-submodules=all", arguments[arguments.IndexOf(subcommand) + 1]);
    }

    [Fact]
    [Trait("BR", "CH-494")]
    public async Task The_filter_drivers_the_configuration_names_are_emptied()
    {
        var sandbox = new FakeShellSandbox
        {
            Configure = session => session.Results.Enqueue(new ShellRunResult(0, "filter.lfs.clean\0filter.a b.process\0filter.lfs.smudge\0", "")),
        };
        using var tool = new ShellCommandTool(new FakeFileSystemService()) { Sandbox = sandbox, Confinement = TestConfinement.Off() };

        var result = await tool.CallAsync(new ToolCallRequest("shell_command", new Dictionary<string, object?> { ["command"] = "git diff" }), Ct);

        Assert.True(result.Success, result.Error);
        var invocations = sandbox.Sessions.SelectMany(s => s.Invocations).ToList();
        Assert.Equal(2, invocations.Count);
        Assert.Contains("config", invocations[0].Arguments);
        var arguments = invocations[1].Arguments.ToList();
        var global = arguments[..arguments.IndexOf("diff")];
        foreach (var driver in new[] { "lfs", "a b" })
        {
            Assert.Contains($"filter.{driver}.clean=", global);
            Assert.Contains($"filter.{driver}.smudge=", global);
            Assert.Contains($"filter.{driver}.process=", global);
        }
    }

    [Theory]
    [Trait("BR", "CH-494")]
    [InlineData("filter.a=b.clean\0", 0)]
    [InlineData("", 128)]
    public async Task A_filter_driver_the_tool_cannot_name_refuses_the_call(string names, int exitCode)
    {
        var sandbox = new FakeShellSandbox
        {
            Configure = session => session.Results.Enqueue(new ShellRunResult(exitCode, names, "")),
        };
        using var tool = new ShellCommandTool(new FakeFileSystemService()) { Sandbox = sandbox, Confinement = TestConfinement.Off() };

        var result = await tool.CallAsync(new ToolCallRequest("shell_command", new Dictionary<string, object?> { ["command"] = "git diff" }), Ct);

        var response = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.Equal(-1, (int)response["exit_code"]!);
        Assert.Contains("filter", Assert.IsType<string>(response["stderr"]), StringComparison.Ordinal);
        Assert.Single(sandbox.Sessions.SelectMany(s => s.Invocations));
    }

    [Theory]
    [Trait("BR", "CH-494")]
    [InlineData(2, 43, 9, false)]
    [InlineData(2, 44, 0, true)]
    [InlineData(2, 53, 0, true)]
    public void A_git_too_old_for_every_override_executes(int major, int minor, int patch, bool read)
    {
        using var tool = new ShellCommandTool(new FakeFileSystemService())
        {
            Confinement = TestConfinement.Off(),
            GitVersion = () => new Version(major, minor, patch),
        };

        Assert.Equal(read ? ToolAccess.Read : ToolAccess.Execute, tool.AccessFor(new Dictionary<string, object?> { ["command"] = "git status" }));
    }

    [Fact]
    [Trait("BR", "CH-494")]
    public void A_git_whose_version_is_unknown_executes()
    {
        using var tool = new ShellCommandTool(new FakeFileSystemService()) { Confinement = TestConfinement.Off(), GitVersion = () => null };

        Assert.Equal(ToolAccess.Execute, tool.AccessFor(new Dictionary<string, object?> { ["command"] = "git log" }));
        Assert.Equal(ToolAccess.Read, tool.AccessFor(new Dictionary<string, object?> { ["command"] = "ls" }));
    }

    [Theory]
    [Trait("BR", "CH-494")]
    [InlineData("git version 2.53.0\n", 2, 53, 0)]
    [InlineData("git version 2.45.1.windows.1", 2, 45, 1)]
    [InlineData("git version 2.39.5 (Apple Git-154)", 2, 39, 5)]
    public void The_git_version_is_read_from_its_banner(string banner, int major, int minor, int patch)
    {
        Assert.Equal(new Version(major, minor, patch), ShellCommandTool.ParseGitVersion(banner));
    }

    [Fact]
    [Trait("BR", "CH-494")]
    public void An_unreadable_banner_gives_no_version()
    {
        Assert.Null(ShellCommandTool.ParseGitVersion("not git"));
    }
}
