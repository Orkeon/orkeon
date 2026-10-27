using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Infrastructure.Security;

namespace Orkeon.Infrastructure.Tests.FileSystem;

/// <summary>
/// Issue #215 through the real chain (registry, then <see cref="PathValidator"/>): a shell
/// script on a read-write mount reads, and is never written, created or deleted.
/// </summary>
public sealed class FileSystemServiceBlockedExtensionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemRegistry _registry;

    public FileSystemServiceBlockedExtensionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"orkeon-fs-blocked-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "run.sh"), "#!/usr/bin/env bash\necho hi\n");
        _registry = new FileSystemRegistry([new FileSystemMount(_tempDir, "/workspace", FileAccessRights.ReadWrite)]);
    }

    public void Dispose()
    {
        _registry.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private FileSystemService BuildService(bool blockReads = false)
    {
        var options = new PathSecurityOptions
        {
            DefaultWorkspaceRoot = _tempDir,
            BlockReadsOfBlockedExtensions = blockReads,
        };
        var validator = new PathValidator(options, NullLogger<PathValidator>.Instance);
        return new FileSystemService(_registry, validator, NullLogger<FileSystemService>.Instance);
    }

    [Fact]
    public async Task ShouldReadScript_WhenExtensionIsBlocked()
    {
        var text = await BuildService().TryReadAllTextAsync("/workspace/run.sh", TestContext.Current.CancellationToken);

        Assert.NotNull(text);
        Assert.Contains("echo hi", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldOpenReadStream_WhenExtensionIsBlocked()
    {
        var stream = await BuildService().OpenReadStreamAsync("/workspace/run.sh", TestContext.Current.CancellationToken);
        await using (stream)
        {
            Assert.True(stream.CanRead);
        }
    }

    [Fact]
    public async Task ShouldRefuseWrite_WhenExtensionIsBlocked()
    {
        var service = BuildService();

        await Assert.ThrowsAsync<FileAccessDeniedException>(() =>
            service.WriteAllTextAsync("/workspace/run.sh", "rm -rf /", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<FileAccessDeniedException>(() =>
            service.WriteAllTextAsync("/workspace/new.sh", "echo", TestContext.Current.CancellationToken));
        Assert.Contains("echo hi", await File.ReadAllTextAsync(Path.Combine(_tempDir, "run.sh"), TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_tempDir, "new.sh")));
    }

    [Theory]
    [InlineData(FileAccessRights.Write)]
    [InlineData(FileAccessRights.Create)]
    [InlineData(FileAccessRights.Delete)]
    [InlineData(FileAccessRights.ReadWrite)]
    public void ShouldRefuseEveryRightButRead_WhenExtensionIsBlocked(FileAccessRights rights)
    {
        var result = BuildService().ResolveAndValidate("/workspace/run.sh", rights);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public async Task ShouldRefuseRead_WhenOptionBlocksReadsOfBlockedExtensions()
    {
        var service = BuildService(blockReads: true);

        Assert.False(service.ResolveAndValidate("/workspace/run.sh", FileAccessRights.Read).IsAllowed);
        await Assert.ThrowsAsync<FileAccessDeniedException>(() =>
            service.OpenReadStreamAsync("/workspace/run.sh", TestContext.Current.CancellationToken));
    }
}
