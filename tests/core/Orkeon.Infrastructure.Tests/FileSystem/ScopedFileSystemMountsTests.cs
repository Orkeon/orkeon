using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Infrastructure.Security;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.FileSystem;

/// <summary>
/// Per-scope VFS mounts (P2-O-05): a <see cref="FileSystemService"/> over the boot registry plus an
/// ambient <see cref="AsyncLocalFileSystemScope"/> resolves against a scoped registry when one is
/// entered, falls back to the boot mounts otherwise, isolates sibling scopes, and enforces the same
/// rights / workspace-root guards on scoped mounts as on boot mounts.
/// </summary>
public sealed class ScopedFileSystemMountsTests : IDisposable
{
    private readonly string _root;
    private readonly string _bootDir;
    private readonly string _scopedDir;
    private readonly AsyncLocalFileSystemScope _scope = new();
    private readonly FileSystemRegistry _bootRegistry;
    private readonly FileSystemService _service;

    public ScopedFileSystemMountsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"orkeon-scopedvfs-{Guid.NewGuid():N}");
        _bootDir = Path.Combine(_root, "boot");
        _scopedDir = Path.Combine(_root, "scoped");
        Directory.CreateDirectory(_bootDir);
        Directory.CreateDirectory(_scopedDir);

        _bootRegistry = new FileSystemRegistry([new FileSystemMount(_bootDir, "/work", FileAccessRights.ReadWrite)]);
        var validator = new StubPathValidator().AllowAll();
        _service = new FileSystemService(_bootRegistry, validator, NullLogger<FileSystemService>.Instance, _scope);
    }

    public void Dispose()
    {
        _bootRegistry.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static FileSystemRegistry RwMount(string physical, string virtualPath = "/work")
        => new([new FileSystemMount(physical, virtualPath, FileAccessRights.ReadWrite)]);

    [Fact]
    public async Task NoScope_ShouldWriteToBootMount()
    {
        await _service.WriteAllTextAsync("/work/a.txt", "boot", TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(_bootDir, "a.txt")));
        Assert.Equal("boot", await File.ReadAllTextAsync(Path.Combine(_bootDir, "a.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ScopedMount_ShouldRedirectWrites_ThenRestoreBootAfterDispose()
    {
        using (var scopedRegistry = RwMount(_scopedDir))
        using (_scope.Enter(scopedRegistry))
        {
            await _service.WriteAllTextAsync("/work/b.txt", "scoped", TestContext.Current.CancellationToken);
        }

        // The write landed in the scoped dir, not the boot dir.
        Assert.True(File.Exists(Path.Combine(_scopedDir, "b.txt")));
        Assert.False(File.Exists(Path.Combine(_bootDir, "b.txt")));

        // After the scope is disposed, writes revert to the boot mount.
        await _service.WriteAllTextAsync("/work/c.txt", "boot-again", TestContext.Current.CancellationToken);
        Assert.True(File.Exists(Path.Combine(_bootDir, "c.txt")));
        Assert.False(File.Exists(Path.Combine(_scopedDir, "c.txt")));
    }

    [Fact]
    public async Task SiblingScopes_ShouldBeIsolated()
    {
        var dirA = Path.Combine(_root, "a");
        var dirB = Path.Combine(_root, "b");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);

        using (var regA = RwMount(dirA))
        using (_scope.Enter(regA))
            await _service.WriteAllTextAsync("/work/x.txt", "A", TestContext.Current.CancellationToken);

        using (var regB = RwMount(dirB))
        using (_scope.Enter(regB))
            await _service.WriteAllTextAsync("/work/y.txt", "B", TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(dirA, "x.txt")));
        Assert.False(File.Exists(Path.Combine(dirB, "x.txt")));
        Assert.True(File.Exists(Path.Combine(dirB, "y.txt")));
        Assert.False(File.Exists(Path.Combine(dirA, "y.txt")));
    }

    [Fact]
    public async Task ScopedMount_ShouldReadScopedContent()
    {
        await File.WriteAllTextAsync(Path.Combine(_scopedDir, "seed.txt"), "from-scope", TestContext.Current.CancellationToken);

        using var scopedRegistry = RwMount(_scopedDir);
        using (_scope.Enter(scopedRegistry))
        {
            var content = await _service.TryReadAllTextAsync("/work/seed.txt", TestContext.Current.CancellationToken);
            Assert.Equal("from-scope", content);
        }
    }

    [Fact]
    public async Task ScopedMountRights_ShouldBeEnforced()
    {
        // A read-only scoped mount must reject writes, just like a read-only boot mount would.
        using var readOnlyScoped = new FileSystemRegistry(
            [new FileSystemMount(_scopedDir, "/work", FileAccessRights.ReadOnly)]);
        using (_scope.Enter(readOnlyScoped))
        {
            await Assert.ThrowsAsync<FileAccessDeniedException>(
                () => _service.WriteAllTextAsync("/work/denied.txt", "x", TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task WorkspaceRootGuard_ShouldApplyToScopedMounts()
    {
        // A real PathValidator restricted to the boot dir. Scoped mounts must be subject to the same
        // workspace-root guard: a scope pointing outside the allowed root is denied; inside is allowed.
        var validator = new PathValidator(
            new PathSecurityOptions { DefaultWorkspaceRoot = _bootDir },
            NullLogger<PathValidator>.Instance);
        using var bootRegistry = new FileSystemRegistry([new FileSystemMount(_bootDir, "/work", FileAccessRights.ReadWrite)]);
        var service = new FileSystemService(bootRegistry, validator, NullLogger<FileSystemService>.Instance, _scope);

        var insideDir = Path.Combine(_bootDir, "inside");
        var outsideDir = Path.Combine(_root, "outside");
        Directory.CreateDirectory(insideDir);
        Directory.CreateDirectory(outsideDir);

        // Scoped mount OUTSIDE the workspace root → denied by PathValidator.
        using (var outsideReg = RwMount(outsideDir))
        using (_scope.Enter(outsideReg))
        {
            var denied = service.ResolveAndValidate("/work/z.txt", FileAccessRights.Write);
            Assert.False(denied.IsAllowed);
        }

        // Scoped mount INSIDE the workspace root → allowed.
        using (var insideReg = RwMount(insideDir))
        using (_scope.Enter(insideReg))
        {
            var allowed = service.ResolveAndValidate("/work/z.txt", FileAccessRights.Write);
            Assert.True(allowed.IsAllowed);
        }
    }
}
