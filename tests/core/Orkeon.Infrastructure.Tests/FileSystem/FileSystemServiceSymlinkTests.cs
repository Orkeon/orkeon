using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.FileSystem;

namespace Orkeon.Infrastructure.Tests.FileSystem;

/// <summary>
/// A symbolic link anywhere on a path, not only on the file or its parent, must not carry a
/// virtual path out of its mount: the VFS resolves every existing component and compares the
/// real path with the mount's real root. The path validator is a pass-through stub here, so a
/// refusal can only come from the VFS itself.
/// </summary>
public sealed class FileSystemServiceSymlinkTests : IDisposable
{
    private readonly string _root;
    private readonly string _mount;
    private readonly string _outside;
    private readonly List<FileSystemRegistry> _registries = [];

    public FileSystemServiceSymlinkTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"orkeon-fs-symlink-{Guid.NewGuid():N}");
        _mount = Directory.CreateDirectory(Path.Combine(_root, "mount")).FullName;
        _outside = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
        Directory.CreateDirectory(Path.Combine(_mount, "a"));
        Directory.CreateDirectory(Path.Combine(_outside, "sub"));
        File.WriteAllText(Path.Combine(_outside, "sub", "file.txt"), "secret");
    }

    public void Dispose()
    {
        foreach (var registry in _registries)
            registry.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task ShouldRefuseRead_WhenADirectoryLinkTwoLevelsUpLeavesTheMount()
    {
        LinkDirectory(Path.Combine(_mount, "a", "link"), _outside);
        var service = BuildService(_mount, "/workspace", FileAccessRights.ReadWrite);

        var validation = service.ResolveAndValidate("/workspace/a/link/sub/file.txt", FileAccessRights.Read);

        Assert.False(validation.IsAllowed);
        await Assert.ThrowsAsync<FileAccessDeniedException>(() =>
            service.TryReadAllTextAsync("/workspace/a/link/sub/file.txt", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldRefuseWriteAndCreateNothing_WhenADirectoryLinkTwoLevelsUpLeavesTheMount()
    {
        LinkDirectory(Path.Combine(_mount, "a", "link"), _outside);
        var service = BuildService(_mount, "/workspace", FileAccessRights.ReadWrite);

        await Assert.ThrowsAsync<FileAccessDeniedException>(() =>
            service.WriteAllTextAsync("/workspace/a/link/sub/new.txt", "x", TestContext.Current.CancellationToken));

        Assert.False(File.Exists(Path.Combine(_outside, "sub", "new.txt")));
    }

    [Fact]
    public async Task ShouldRefuseWriteAndCreateNothing_WhenADanglingLinkPointsOutside()
    {
        var missing = Path.Combine(_outside, "missing");
        LinkDirectory(Path.Combine(_mount, "a", "dangling"), missing);
        var service = BuildService(_mount, "/workspace", FileAccessRights.ReadWrite);

        await Assert.ThrowsAsync<FileAccessDeniedException>(() =>
            service.WriteAllTextAsync("/workspace/a/dangling/deep/new.txt", "x", TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void ShouldRefuse_WhenALinkLeadsIntoAnotherMount()
    {
        var other = Directory.CreateDirectory(Path.Combine(_root, "other", "sub")).Parent!.FullName;
        LinkDirectory(Path.Combine(_mount, "a", "link"), other);
        var registry = Track(new FileSystemRegistry(
        [
            new FileSystemMount(_mount, "/workspace", FileAccessRights.ReadWrite),
            new FileSystemMount(other, "/ro", FileAccessRights.ReadOnly),
        ]));
        var service = new FileSystemService(registry, PassThroughValidator(), NullLogger<FileSystemService>.Instance);

        var validation = service.ResolveAndValidate("/workspace/a/link/sub/file.txt", FileAccessRights.Write);

        Assert.False(validation.IsAllowed);
    }

    [Fact]
    public async Task ShouldAllowReadAndWrite_WhenALinkStaysInsideTheMount()
    {
        Directory.CreateDirectory(Path.Combine(_mount, "b", "sub"));
        await File.WriteAllTextAsync(Path.Combine(_mount, "b", "sub", "file.txt"), "inside", TestContext.Current.CancellationToken);
        LinkDirectory(Path.Combine(_mount, "a", "inner"), Path.Combine(_mount, "b"));
        var service = BuildService(_mount, "/workspace", FileAccessRights.ReadWrite);

        var text = await service.TryReadAllTextAsync("/workspace/a/inner/sub/file.txt", TestContext.Current.CancellationToken);
        await service.WriteAllTextAsync("/workspace/a/inner/sub/new/deeper.txt", "y", TestContext.Current.CancellationToken);

        Assert.Equal("inside", text);
        Assert.True(File.Exists(Path.Combine(_mount, "b", "sub", "new", "deeper.txt")));
    }

    [Fact]
    public async Task ShouldServeTheMountsFiles_WhenTheMountRootIsALink()
    {
        var linkedRoot = Path.Combine(_root, "linked-root");
        LinkDirectory(linkedRoot, _mount);
        await File.WriteAllTextAsync(Path.Combine(_mount, "a", "own.txt"), "own", TestContext.Current.CancellationToken);
        var service = BuildService(linkedRoot, "/workspace", FileAccessRights.ReadWrite);

        var text = await service.TryReadAllTextAsync("/workspace/a/own.txt", TestContext.Current.CancellationToken);
        await service.WriteAllTextAsync("/workspace/a/fresh/new.txt", "z", TestContext.Current.CancellationToken);

        Assert.Equal("own", text);
        Assert.True(File.Exists(Path.Combine(_mount, "a", "fresh", "new.txt")));
    }

    private static void LinkDirectory(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip("The platform or the account cannot create symbolic links.");
        }
    }

    private static StubPathValidator PassThroughValidator()
        => new StubPathValidator().RespondWith((p, _) => PathValidationResult.Allowed(p));

    private FileSystemRegistry Track(FileSystemRegistry registry)
    {
        _registries.Add(registry);
        return registry;
    }

    private FileSystemService BuildService(string basePath, string virtualPath, FileAccessRights rights)
    {
        var registry = Track(new FileSystemRegistry([new FileSystemMount(basePath, virtualPath, rights)]));
        return new FileSystemService(registry, PassThroughValidator(), NullLogger<FileSystemService>.Instance);
    }
}
