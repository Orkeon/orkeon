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
        _registry = new FileSystemRegistry([new FileSystemMount(_tempDir, "/workspace", FileAccessRights.ReadWrite)]);
        var validator = new PathValidator(new PathSecurityOptions { DefaultWorkspaceRoot = _tempDir }, NullLogger<PathValidator>.Instance);
        _service = new FileSystemService(_registry, validator, NullLogger<FileSystemService>.Instance);
    }

    public void Dispose()
    {
        _registry.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private AgentFileSystemScope Scope(FileAccessRights? access = null)
        => AgentFileSystemScope.Resolve("writer", [new FileSystemScopeEntry("/workspace/project", access)], _service.GetAvailableMounts());

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
        Assert.Contains("Access denied for '/workspace/other/b.txt'", outside.DenialReason, StringComparison.Ordinal);
        Assert.Contains("agent 'writer'", outside.DenialReason, StringComparison.Ordinal);
        Assert.DoesNotContain(_tempDir, outside.DenialReason, StringComparison.Ordinal);
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
