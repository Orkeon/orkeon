using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Infrastructure.Security;

namespace Orkeon.Infrastructure.Tests.FileSystem;

/// <summary>
/// #349 through the real chain: an agent's file-system scope refuses, during its tool calls, every path
/// outside it on every mount, in the file system's own shape; the framework's own I/O stays unscoped.
/// </summary>
public sealed class FileSystemServiceAgentScopeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemRegistry _registry;
    private readonly FileSystemService _service;

    public FileSystemServiceAgentScopeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"orkeon-fs-scope-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempDir, "project"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "other"));
        File.WriteAllText(Path.Combine(_tempDir, "project", "a.txt"), "inside");
        File.WriteAllText(Path.Combine(_tempDir, "other", "b.txt"), "outside");
        Directory.CreateDirectory(Path.Combine(_tempDir, "archive"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "project", "locked"));
        _registry = new FileSystemRegistry(
        [
            new FileSystemMount(Path.Combine(_tempDir, "project"), "/workspace/project", FileAccessRights.ReadWrite,
                [new SubPathOverride("locked", FileAccessRights.ReadOnly)]),
            new FileSystemMount(_tempDir, "/workspace", FileAccessRights.ReadWrite),
            new FileSystemMount(Path.Combine(_tempDir, "archive"), "/archive", FileAccessRights.ReadWrite),
        ]);
        var validator = new PathValidator(new PathSecurityOptions { DefaultWorkspaceRoot = _tempDir }, NullLogger<PathValidator>.Instance);
        _service = new FileSystemService(_registry, validator, NullLogger<FileSystemService>.Instance);
    }

    public void Dispose()
    {
        _registry.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private AgentFileSystemScope Scope(FileAccessRights? access = null, params FileSystemScopeEntry[] more)
        => AgentFileSystemScope.Resolve(
            "writer", [new FileSystemScopeEntry("/workspace/project", access), .. more], _service.GetAvailableMounts());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void AssertNamesScopeOnly(string virtualPath, string? denial)
    {
        Assert.NotNull(denial);
        Assert.StartsWith($"Access denied for '{virtualPath}': ", denial, StringComparison.Ordinal);
        Assert.Contains("of agent 'writer'. Its scope: /workspace/project", denial, StringComparison.Ordinal);
        // The refused path is quoted as written; past it, no mount the scope leaves out is named.
        Assert.DoesNotContain("/archive", denial[$"Access denied for '{virtualPath}': ".Length..], StringComparison.Ordinal);
        Assert.DoesNotContain("Available mounts", denial, StringComparison.Ordinal);
        Assert.DoesNotContain("Mounts granting", denial, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldRefusePathOutsideScope_WhenAgentToolCalls()
    {
        await using var run = AgentRunScope.Begin("id", "writer", null, Scope());
        using var call = AgentRunScope.EnterToolCall();

        var inside = await _service.TryReadAllTextAsync("/workspace/project/a.txt", TestContext.Current.CancellationToken);
        var outside = _service.ResolveAndValidate("/workspace/other/b.txt", FileAccessRights.Read);

        Assert.Equal("inside", inside);
        Assert.False(outside.IsAllowed);
        Assert.Equal(
            "Access denied for '/workspace/other/b.txt': the path is outside the file-system scope of agent 'writer'. Its scope: /workspace/project.",
            outside.DenialReason);
        Assert.DoesNotContain(_tempDir, outside.DenialReason, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "#349")]
    [InlineData("/nowhere/x.md", FileAccessRights.Read)]
    [InlineData("/archive/x.md", FileAccessRights.Write)]
    [InlineData("/workspace/project/locked/x.md", FileAccessRights.Write)]
    public async Task ShouldNameScopeAndNoOtherMount_WhenScopedPathIsDenied(string virtualPath, FileAccessRights required)
    {
        await using var run = AgentRunScope.Begin("id", "writer", null, Scope());
        using var call = AgentRunScope.EnterToolCall();

        var denied = _service.ResolveAndValidate(virtualPath, required);

        Assert.False(denied.IsAllowed);
        AssertNamesScopeOnly(virtualPath, denied.DenialReason);
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldNotWriteBesideScope_WhenPathHoldsBackslash()
    {
        await using var run = AgentRunScope.Begin("id", "writer", null, AgentFileSystemScope.Resolve(
            "writer", [new FileSystemScopeEntry("/workspace/other")], _service.GetAvailableMounts()));
        using var call = AgentRunScope.EnterToolCall();

        if (OperatingSystem.IsWindows())
        {
            await _service.WriteAllTextAsync("/workspace/other\\x.md", "note", Ct);
            Assert.True(File.Exists(Path.Combine(_tempDir, "other", "x.md")));
            return;
        }

        await Assert.ThrowsAsync<FileAccessDeniedException>(() => _service.WriteAllTextAsync("/workspace/other\\x.md", "note", Ct));
        Assert.False(File.Exists(Path.Combine(_tempDir, "other\\x.md")));
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldRefuseDelete_WhenScopeEntryIsReadWriteNoDelete()
    {
        await using var run = AgentRunScope.Begin("id", "writer", null, Scope(FileAccessRights.ReadWriteNoDelete));
        using var call = AgentRunScope.EnterToolCall();

        Assert.True(_service.ResolveAndValidate("/workspace/project/a.txt", FileAccessRights.Write).IsAllowed);
        var error = await Assert.ThrowsAsync<FileAccessDeniedException>(
            () => _service.DeleteAsync("/workspace/project/a.txt", recursive: false, Ct));

        Assert.Equal(
            "Access denied for '/workspace/project/a.txt': the path needs Delete but the file-system scope grants rwnd there of agent 'writer'. Its scope: /workspace/project (rwnd).",
            error.Message);
        Assert.True(File.Exists(Path.Combine(_tempDir, "project", "a.txt")));
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldRefuseRecursiveDelete_WhenSubtreeHoldsNarrowerEntry()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "project", "keep"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "project", "keep", "k.txt"), "kept", Ct);
        await using var run = AgentRunScope.Begin("id", "writer", null,
            Scope(null, new FileSystemScopeEntry("/workspace/project/keep", FileAccessRights.ReadOnly)));
        using var call = AgentRunScope.EnterToolCall();

        var error = await Assert.ThrowsAsync<FileAccessDeniedException>(
            () => _service.DeleteAsync("/workspace/project", recursive: true, Ct));

        Assert.Contains("of agent 'writer'", error.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_tempDir, "project", "keep", "k.txt")));
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldListOnlyReadableChildren_WhenEnumeratingScopedDirectory()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "project", "drop"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "project", "drop", "d.txt"), "dropped", Ct);
        await using var run = AgentRunScope.Begin("id", "writer", null,
            Scope(null, new FileSystemScopeEntry("/workspace/project/drop", FileAccessRights.Write | FileAccessRights.Create)));
        using var call = AgentRunScope.EnterToolCall();

        var listed = new List<string>();
        await foreach (var entry in _service.EnumerateFilesAsync(
            "/workspace/project", new VirtualEnumerationOptions { Recursive = true }, Ct))
            listed.Add(entry.VirtualPath);

        Assert.Contains("/workspace/project/a.txt", listed);
        Assert.DoesNotContain(listed, p => p.StartsWith("/workspace/project/drop", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldRefuseWrite_WhenScopeEntryIsReadOnly()
    {
        await using var run = AgentRunScope.Begin("id", "writer", null, Scope(FileAccessRights.ReadOnly));
        using var call = AgentRunScope.EnterToolCall();

        Assert.True(_service.ResolveAndValidate("/workspace/project/a.txt", FileAccessRights.Read).IsAllowed);
        Assert.False(_service.ResolveAndValidate("/workspace/project/a.txt", FileAccessRights.Write).IsAllowed);
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldLeaveFrameworkIoUnscoped_WhenNoToolCallRuns()
    {
        await using var run = AgentRunScope.Begin("id", "writer", null, Scope());

        Assert.True(_service.ResolveAndValidate("/workspace/other/b.txt", FileAccessRights.Write).IsAllowed);
    }
}
