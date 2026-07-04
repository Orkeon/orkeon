using Orkeon.Domain.FileSystem;

namespace Orkeon.Domain.Tests.FileSystem;

public sealed class MountVisibilityTests : IDisposable
{
    private readonly string _tempDir;

    public MountVisibilityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"orkeon-vis-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateSubDir(string name)
    {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void FileSystemMount_DefaultsToAgentFacing()
    {
        var mount = new FileSystemMount("/tmp", "/workspace", FileAccessRights.ReadWrite);

        Assert.Equal(MountVisibility.AgentFacing, mount.Visibility);
    }

    [Fact]
    public void FileSystemMount_CanBeSetToInternal()
    {
        var mount = new FileSystemMount("/tmp", "/sandbox", FileAccessRights.ReadWrite,
            visibility: MountVisibility.Internal);

        Assert.Equal(MountVisibility.Internal, mount.Visibility);
    }

    [Fact]
    public void FileSystemRegistry_GetAvailableMounts_HidesInternal()
    {
        var wsDir = CreateSubDir("ws");
        var sandboxDir = CreateSubDir("sandbox");

        var mounts = new[]
        {
            new FileSystemMount(wsDir, "/workspace", FileAccessRights.ReadWrite),
            new FileSystemMount(sandboxDir, "/sandbox", FileAccessRights.ReadWrite,
                visibility: MountVisibility.Internal)
        };
        using var registry = new FileSystemRegistry(mounts);

        var visible = registry.GetAvailableMounts();

        Assert.Single(visible);
        Assert.Equal("/workspace", visible[0].VirtualPath);
        Assert.DoesNotContain(visible, m => m.VirtualPath == "/sandbox");
    }

    [Fact]
    public void FileSystemRegistry_AddMount_AddsRuntimeMount()
    {
        var wsDir = CreateSubDir("ws");
        var sandboxDir = CreateSubDir("sandbox");

        using var registry = new FileSystemRegistry(
        [
            new FileSystemMount(wsDir, "/workspace", FileAccessRights.ReadWrite)
        ]);

        registry.AddMount(new FileSystemMount(sandboxDir, "/sandbox", FileAccessRights.ReadWrite,
            visibility: MountVisibility.Internal));

        // Internal mount should not appear in GetAvailableMounts
        var visible = registry.GetAvailableMounts();
        Assert.Single(visible);
        Assert.Equal("/workspace", visible[0].VirtualPath);

        // But GetAllMountsInternal should include it
        var all = registry.GetAllMountsInternal();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, m => m.VirtualPath == "/sandbox");
    }

    [Fact]
    public void FileSystemRegistry_AddMount_DuplicateVirtualPath_Throws()
    {
        var wsDir = CreateSubDir("ws");
        using var registry = new FileSystemRegistry(
        [
            new FileSystemMount(wsDir, "/workspace", FileAccessRights.ReadWrite)
        ]);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            registry.AddMount(new FileSystemMount(wsDir, "/workspace", FileAccessRights.ReadOnly)));

        Assert.Contains("/workspace", ex.Message);
    }

    [Fact]
    public void FileSystemRegistry_ResolveAndValidate_ResolvesInternalMount()
    {
        var wsDir = CreateSubDir("ws");
        var sandboxDir = CreateSubDir("sandbox");

        var mounts = new[]
        {
            new FileSystemMount(wsDir, "/workspace", FileAccessRights.ReadWrite),
            new FileSystemMount(sandboxDir, "/sandbox", FileAccessRights.ReadWrite,
                visibility: MountVisibility.Internal)
        };
        using var registry = new FileSystemRegistry(mounts);

        // Should resolve without throwing even though /sandbox is Internal
        var physicalPath = registry.ResolveAndCheckRights("/sandbox/output.txt", FileAccessRights.Write);

        var expected = Path.GetFullPath(Path.Combine(sandboxDir, "output.txt"));
        Assert.Equal(expected, physicalPath);
    }

    [Fact]
    public void FileSystemMount_Parse_DefaultsToAgentFacing()
    {
        var mount = FileSystemMount.Parse("/tmp/data:/inputs:ro");

        Assert.Equal(MountVisibility.AgentFacing, mount.Visibility);
    }
}
