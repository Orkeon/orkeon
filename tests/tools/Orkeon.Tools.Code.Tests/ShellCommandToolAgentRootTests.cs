using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Code.Confinement;
using Orkeon.Tools.Code.Sessions;
using Orkeon.Tools.Code.Tests.Doubles;

namespace Orkeon.Tools.Code.Tests;

/// <summary>
/// <see cref="ShellCommandTool"/> under an agent run scope that declares a virtual working root:
/// the working directory, the path arguments and the sandbox's confined paths stay inside it.
/// </summary>
public sealed class ShellCommandToolAgentRootTests : IDisposable
{
    private const string Root = "/workspace/job";

    private readonly string _tempDir = Directory.CreateTempSubdirectory("orkeon-shell-agent-root-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ToolCallRequest Request(string command, string workingDirectory)
        => new(ToolName: "shell_command", Parameters: new Dictionary<string, object?>
        {
            ["command"] = command,
            ["working_directory"] = workingDirectory,
        });

    private (ShellCommandTool Tool, FakeShellSandbox Sandbox) NewTool()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "job", "sub"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "other"));
        var sandbox = new FakeShellSandbox { UsesVirtualPaths = false };
        var tool = new ShellCommandTool(new DiskBackedFileSystemService(_tempDir, "/workspace")) { Sandbox = sandbox };
        return (tool, sandbox);
    }

    private static string ErrorOf(ToolCallResponse response)
    {
        Assert.False(response.Success);
        return Assert.IsType<string>(response.Error);
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldRunCommand_WhenWorkingDirectoryInsideRoot()
    {
        var (tool, sandbox) = NewTool();
        using var _ = tool;

        ToolCallResponse response;
        await using (AgentRunScope.Begin("agent-1", "dev", Root))
            response = await tool.CallAsync(Request("echo x", "/workspace/job/sub"), Ct);

        Assert.True(response.Success, response.Error);
        var invocation = Assert.Single(Assert.Single(sandbox.Sessions).Invocations);
        Assert.Equal(Path.Combine(_tempDir, "job", "sub"), invocation.WorkingDirectory);
        var confined = Assert.Single(Assert.Single(sandbox.Views).Paths);
        Assert.Equal(Path.Combine(_tempDir, "job"), confined.Source);
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldRefuseShell_WhenWorkingDirectoryOutsideRoot()
    {
        var (tool, sandbox) = NewTool();
        using var _ = tool;

        ToolCallResponse response;
        await using (AgentRunScope.Begin("agent-1", "dev", Root))
            response = await tool.CallAsync(Request("echo x", "/workspace/other"), Ct);

        var error = ErrorOf(response);
        Assert.DoesNotContain(_tempDir, error, StringComparison.Ordinal);
        Assert.All(sandbox.Sessions, s => Assert.Empty(s.Invocations));
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldRefuseShell_WhenNamedPathArgumentOutsideRoot()
    {
        var (tool, sandbox) = NewTool();
        using var _ = tool;

        ToolCallResponse response;
        await using (AgentRunScope.Begin("agent-1", "dev", Root))
            response = await tool.CallAsync(Request("ls /workspace/other/f.txt", "/workspace/job"), Ct);

        var error = ErrorOf(response);
        Assert.DoesNotContain(_tempDir, error, StringComparison.Ordinal);
        Assert.All(sandbox.Sessions, s => Assert.Empty(s.Invocations));
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldDefaultWorkingDirectoryToRoot_WhenRootDeclaredAndNoneGiven()
    {
        var (tool, sandbox) = NewTool();
        using var _ = tool;

        ToolCallResponse response;
        await using (AgentRunScope.Begin("agent-1", "dev", Root))
            response = await tool.CallAsync(Request("echo x", ""), Ct);

        Assert.True(response.Success, response.Error);
        var invocation = Assert.Single(Assert.Single(sandbox.Sessions).Invocations);
        Assert.Equal(Path.Combine(_tempDir, "job"), invocation.WorkingDirectory);
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldConfineSandboxToRoot_WhenRootDeclared()
    {
        var (tool, sandbox) = NewTool();
        using var _ = tool;

        await using (AgentRunScope.Begin("agent-1", "dev", Root))
            Assert.True((await tool.CallAsync(Request("echo x", "/workspace/job"), Ct)).Success);

        var view = Assert.Single(sandbox.Views);
        var physicalRoot = Path.Combine(_tempDir, "job");
        Assert.NotEmpty(view.Paths);
        Assert.All(view.Paths, p =>
        {
            Assert.Equal(physicalRoot, p.Source);
            Assert.Equal(Root, p.Destination);
        });
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The registry is captured by the returned FileSystemService and lives for the test.")]
    private (ShellCommandTool Tool, FakeShellSandbox Sandbox, string Workspace, string Output) NewMultiMountTool()
    {
        var workspace = Path.Combine(_tempDir, "ws");
        var output = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(Path.Combine(workspace, "job"));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(workspace, "job", ".env"), "secret");
        var registry = new FileSystemRegistry(
        [
            new FileSystemMount(workspace, "/workspace", FileAccessRights.ReadWrite,
                [new SubPathOverride("job/.env", FileAccessRights.None)]),
            new FileSystemMount(output, "/output", FileAccessRights.ReadWrite),
        ]);
        var fileSystem = new FileSystemService(registry, new StubPathValidator().AllowAll(), NullLogger<FileSystemService>.Instance);
        var sandbox = new FakeShellSandbox { UsesVirtualPaths = false };
        return (new ShellCommandTool(fileSystem) { Sandbox = sandbox }, sandbox, workspace, output);
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldKeepMountOverridesUnderRoot_WhenRootDeclared()
    {
        var (tool, sandbox, workspace, _) = NewMultiMountTool();
        using var _ = tool;

        await using (AgentRunScope.Begin("agent-1", "dev", Root))
            Assert.True((await tool.CallAsync(Request("echo x", "/workspace/job"), Ct)).Success);

        var view = Assert.Single(sandbox.Views);
        Assert.Contains(view.Paths, p =>
            p.Source == Path.Combine(workspace, "job", ".env") && p.Destination == "/workspace/job/.env");
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldKeepOtherMountsBound_WhenRootDeclared()
    {
        var (tool, sandbox, workspace, output) = NewMultiMountTool();
        using var _ = tool;

        ToolCallResponse response;
        await using (AgentRunScope.Begin("agent-1", "dev", Root))
            response = await tool.CallAsync(Request("cat /output/a.txt", "/workspace/job"), Ct);

        Assert.True(response.Success, response.Error);
        var view = Assert.Single(sandbox.Views);
        Assert.Contains(view.Paths, p => p.Source == output && p.Destination == "/output");
        Assert.Contains(view.Paths, p => p.Source == Path.Combine(workspace, "job") && p.Destination == Root);
        Assert.DoesNotContain(view.Paths, p => p.Destination == "/workspace");
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The registry is captured by the returned FileSystemService and lives for the test.")]
    private (ShellCommandTool Tool, FakeShellSandbox Sandbox, string Workspace) NewOverrideAboveRootTool(FileAccessRights jobRights)
    {
        var workspace = Path.Combine(_tempDir, "ws");
        Directory.CreateDirectory(Path.Combine(workspace, "job", "sub"));
        var registry = new FileSystemRegistry(
        [
            new FileSystemMount(workspace, "/workspace", FileAccessRights.ReadWrite,
                [new SubPathOverride("job", jobRights)]),
        ]);
        var fileSystem = new FileSystemService(registry, new StubPathValidator().AllowAll(), NullLogger<FileSystemService>.Instance);
        var sandbox = new FakeShellSandbox { UsesVirtualPaths = false };
        return (new ShellCommandTool(fileSystem) { Sandbox = sandbox }, sandbox, workspace);
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldBindRootReadOnly_WhenOverrideAboveRootIsRead()
    {
        var (tool, sandbox, workspace) = NewOverrideAboveRootTool(FileAccessRights.Read);
        using var _ = tool;
        const string deepRoot = "/workspace/job/sub";

        await using (AgentRunScope.Begin("agent-1", "dev", deepRoot))
            await tool.CallAsync(Request("echo x", deepRoot), Ct);

        var view = Assert.Single(sandbox.Views);
        var bound = Assert.Single(view.Paths, p => p.Destination == deepRoot);
        Assert.Equal(Path.Combine(workspace, "job", "sub"), bound.Source);
        Assert.Equal(ConfinedAccess.ReadOnly, bound.Access);
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldNotBindRoot_WhenOverrideAboveRootIsNone()
    {
        var (tool, sandbox, _) = NewOverrideAboveRootTool(FileAccessRights.None);
        using var _ = tool;
        const string deepRoot = "/workspace/job/sub";

        ToolCallResponse response;
        await using (AgentRunScope.Begin("agent-1", "dev", deepRoot))
            response = await tool.CallAsync(Request("echo x", deepRoot), Ct);

        Assert.False(response.Success, "a root whose rights are None must not run a command");
        Assert.All(sandbox.Sessions, sess => Assert.Empty(sess.Invocations));
        var exposed = sandbox.Views.SelectMany(v => v.Paths)
            .Where(p => p.Destination == deepRoot || p.Destination == "/workspace")
            .Select(p => $"{p.Destination}:{p.Access}")
            .ToList();
        Assert.Empty(exposed);
    }

    // ── a linked git worktree as the root (#188) ────────────────────────────

    private const string WorktreeRoot = "/workspace/.orkeon/worktrees/p1";

    /// <summary>
    /// A main checkout at the mount root (its <c>.git</c>, a working tree file) and a linked worktree
    /// at <see cref="WorktreeRoot"/> whose <c>.git</c> file says <paramref name="gitdir"/>; the layout
    /// <c>git worktree add</c> writes, without running git.
    /// </summary>
    private (ShellCommandTool Tool, FakeShellSandbox Sandbox) NewWorktreeTool(string gitdir, string commondir = "../..")
    {
        var git = Path.Combine(_tempDir, ".git");
        var worktreeGit = Path.Combine(git, "worktrees", "p1");
        Directory.CreateDirectory(Path.Combine(git, "objects"));
        Directory.CreateDirectory(Path.Combine(git, "refs", "heads"));
        Directory.CreateDirectory(Path.Combine(git, "hooks"));
        Directory.CreateDirectory(worktreeGit);
        File.WriteAllText(Path.Combine(git, "HEAD"), "ref: refs/heads/main\n");
        File.WriteAllText(Path.Combine(git, "config"), "[core]\n");
        File.WriteAllText(Path.Combine(worktreeGit, "HEAD"), "ref: refs/heads/p1\n");
        File.WriteAllText(Path.Combine(worktreeGit, "commondir"), commondir + "\n");
        File.WriteAllText(Path.Combine(worktreeGit, "gitdir"), "../../../.orkeon/worktrees/p1/.git\n");
        File.WriteAllText(Path.Combine(_tempDir, "README.md"), "main tree");
        var root = Path.Combine(_tempDir, ".orkeon", "worktrees", "p1");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".git"), $"gitdir: {gitdir}\n");

        var sandbox = new FakeShellSandbox { UsesVirtualPaths = false };
        var tool = new ShellCommandTool(new DiskBackedFileSystemService(_tempDir, "/workspace")) { Sandbox = sandbox };
        return (tool, sandbox);
    }

    private static async Task<ShellSessionView> ViewAtWorktreeRootAsync(ShellCommandTool tool, FakeShellSandbox sandbox)
    {
        await using (AgentRunScope.Begin("agent-1", "dev", WorktreeRoot))
            Assert.True((await tool.CallAsync(Request("echo x", WorktreeRoot), Ct)).Success);
        return Assert.Single(sandbox.Views);
    }

    [Fact]
    [Trait("BR", "GH-188")]
    public async Task ShouldBindWorktreeGitDirsAtVirtualPaths_WhenRootGitdirIsRelative()
    {
        var (tool, sandbox) = NewWorktreeTool("../../../.git/worktrees/p1");
        using var _ = tool;

        var view = await ViewAtWorktreeRootAsync(tool, sandbox);

        var git = Path.Combine(_tempDir, ".git");
        Assert.Contains(view.Paths, p => p.Source == git && p.Destination == "/workspace/.git" && p.Access == ConfinedAccess.ReadWrite);
        Assert.Contains(view.Paths, p => p.Source == Path.Combine(git, "worktrees", "p1")
            && p.Destination == "/workspace/.git/worktrees/p1" && p.Access == ConfinedAccess.ReadWrite);
        Assert.Contains(view.Paths, p => p.Destination == "/workspace/.git/config" && p.Access == ConfinedAccess.ReadOnly);
        Assert.Contains(view.Paths, p => p.Destination == "/workspace/.git/hooks" && p.Access == ConfinedAccess.ReadOnly);
        // Nothing else of the main checkout: its working tree stays unbound.
        Assert.All(view.Paths, p => Assert.True(
            ShellCommandTool.MatchesMountPrefix(p.Destination, WorktreeRoot) || ShellCommandTool.MatchesMountPrefix(p.Destination, "/workspace/.git"),
            $"unexpected bind {p.Destination}"));
    }

    [Fact]
    [Trait("BR", "GH-188")]
    public async Task ShouldBindWorktreeGitDirsAtHostPaths_WhenRootGitdirIsAbsoluteHostPathInMount()
    {
        var git = Path.Combine(_tempDir, ".git");
        var (tool, sandbox) = NewWorktreeTool(Path.Combine(git, "worktrees", "p1"));
        using var _ = tool;

        var view = await ViewAtWorktreeRootAsync(tool, sandbox);

        Assert.Contains(view.Paths, p => p.Source == git && p.Destination == git && p.Access == ConfinedAccess.ReadWrite);
        var worktreeGit = Path.Combine(git, "worktrees", "p1");
        Assert.Contains(view.Paths, p => p.Source == worktreeGit && p.Destination == worktreeGit && p.Access == ConfinedAccess.ReadWrite);
        Assert.Contains(view.Paths, p => p.Destination == Path.Combine(git, "config") && p.Access == ConfinedAccess.ReadOnly);
        Assert.DoesNotContain(view.Paths, p => p.Destination == "/workspace" || p.Source == _tempDir);
    }

    [Fact]
    [Trait("BR", "GH-188")]
    public async Task ShouldBindWorktreeGitDirsAtVirtualPaths_WhenRootGitdirIsAbsoluteVirtualPath()
    {
        var (tool, sandbox) = NewWorktreeTool("/workspace/.git/worktrees/p1");
        using var _ = tool;

        var view = await ViewAtWorktreeRootAsync(tool, sandbox);

        Assert.Contains(view.Paths, p => p.Destination == "/workspace/.git" && p.Access == ConfinedAccess.ReadWrite);
        Assert.Contains(view.Paths, p => p.Destination == "/workspace/.git/worktrees/p1" && p.Access == ConfinedAccess.ReadWrite);
    }

    [Theory]
    [Trait("BR", "GH-188")]
    [InlineData("../../../../elsewhere/.git/worktrees/p1", "../..")]
    [InlineData("../../../../../../../../../../../../../../../../../../.git/worktrees/p1", "../..")]
    [InlineData("../../../.git/worktrees/p1", "../../..")]
    [InlineData("../../../.git/worktrees/p1", "../../../../../../../../../../../../../../../../../..")]
    [InlineData("../../../README.md", "../..")]
    public async Task ShouldBindRootAlone_WhenWorktreeGitdirOrCommondirLeavesMountOrHoldsRoot(string gitdir, string commondir)
    {
        var (tool, sandbox) = NewWorktreeTool(gitdir, commondir);
        using var _ = tool;

        var view = await ViewAtWorktreeRootAsync(tool, sandbox);

        var bound = Assert.Single(view.Paths);
        Assert.Equal(WorktreeRoot, bound.Destination);
    }

    [Fact]
    [Trait("BR", "GH-188")]
    public async Task ShouldBindRootAlone_WhenWorktreeGitdirIsAbsoluteHostPathOutsideMount()
    {
        var outside = Directory.CreateTempSubdirectory("orkeon-shell-outside-").FullName;
        try
        {
            var outsideGit = Path.Combine(outside, ".git", "worktrees", "p1");
            Directory.CreateDirectory(Path.Combine(outside, ".git", "objects"));
            Directory.CreateDirectory(outsideGit);
            await File.WriteAllTextAsync(Path.Combine(outside, ".git", "HEAD"), "ref: refs/heads/main\n", Ct);
            await File.WriteAllTextAsync(Path.Combine(outsideGit, "HEAD"), "ref: refs/heads/p1\n", Ct);
            await File.WriteAllTextAsync(Path.Combine(outsideGit, "commondir"), "../..\n", Ct);
            var (tool, sandbox) = NewWorktreeTool(outsideGit);
            using var _ = tool;

            var view = await ViewAtWorktreeRootAsync(tool, sandbox);

            Assert.Equal(WorktreeRoot, Assert.Single(view.Paths).Destination);
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    [Trait("BR", "GH-188")]
    public async Task ShouldBindRootAlone_WhenRootHoldsGitDirectory()
    {
        var (tool, sandbox) = NewTool();
        using var _ = tool;
        Directory.CreateDirectory(Path.Combine(_tempDir, "job", ".git", "objects"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "job", ".git", "HEAD"), "ref: refs/heads/main\n", Ct);

        await using (AgentRunScope.Begin("agent-1", "dev", Root))
            Assert.True((await tool.CallAsync(Request("echo x", Root), Ct)).Success);

        Assert.Equal(Root, Assert.Single(Assert.Single(sandbox.Views).Paths).Destination);
    }

    [Fact]
    [Trait("BR", "GH-188")]
    public async Task ShouldBindNoGitDir_WhenNoAgentRootDeclared()
    {
        var (tool, sandbox) = NewWorktreeTool("../../../.git/worktrees/p1");
        using var _ = tool;

        Assert.True((await tool.CallAsync(Request("echo x", WorktreeRoot), Ct)).Success);

        var bound = Assert.Single(Assert.Single(sandbox.Views).Paths);
        Assert.Equal("/workspace", bound.Destination);
    }
}
