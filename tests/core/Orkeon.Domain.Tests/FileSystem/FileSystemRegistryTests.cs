using Orkeon.Domain.FileSystem;

namespace Orkeon.Domain.Tests.FileSystem;

public sealed class FileSystemRegistryTests : IDisposable
{
    private readonly string _tempDir;

    public FileSystemRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"orkeon-vfs-test-{Guid.NewGuid():N}");
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
    public void Constructor_DuplicateVirtualPaths_ThrowsInvalidOperationException()
    {
        var mounts = new[]
        {
            new FileSystemMount("/a", "/workspace", FileAccessRights.ReadWrite),
            new FileSystemMount("/b", "/workspace", FileAccessRights.ReadOnly)
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new FileSystemRegistry(mounts));
        Assert.Contains("/workspace", ex.Message);
    }

    [Fact]
    public void ResolveAndCheckRights_ValidPath_ReturnsPhysicalPath()
    {
        var baseDir = CreateSubDir("ws");
        var srcDir = CreateSubDir("ws/src");
        File.WriteAllText(Path.Combine(srcDir, "file.cs"), "");

        var mount = new FileSystemMount(baseDir, "/workspace", FileAccessRights.ReadWrite);
        using var registry = new FileSystemRegistry([mount]);

        var physicalPath = registry.ResolveAndCheckRights("/workspace/src/file.cs", FileAccessRights.Read);

        var expected = Path.GetFullPath(Path.Combine(baseDir, "src", "file.cs"));
        Assert.Equal(expected, physicalPath);
    }

    [Fact]
    public void ResolveAndCheckRights_UnknownMount_ThrowsFileAccessDeniedException()
    {
        var baseDir = CreateSubDir("ws");
        var mount = new FileSystemMount(baseDir, "/workspace", FileAccessRights.ReadWrite);
        using var registry = new FileSystemRegistry([mount]);

        var ex = Assert.Throws<FileAccessDeniedException>(() =>
            registry.ResolveAndCheckRights("/unknown/file.txt", FileAccessRights.Read));

        Assert.Equal("/unknown/file.txt", ex.VirtualPath);
        Assert.Equal(FileAccessRights.Read, ex.RequiredRight);
        Assert.NotNull(ex.AlternativeMounts);
        Assert.Contains("/workspace", ex.AlternativeMounts!);
    }

    [Fact]
    public void ResolveAndCheckRights_InsufficientRights_ThrowsFileAccessDeniedException()
    {
        var baseDir = CreateSubDir("ws");
        var mount = new FileSystemMount(baseDir, "/workspace", FileAccessRights.ReadOnly);
        using var registry = new FileSystemRegistry([mount]);

        var ex = Assert.Throws<FileAccessDeniedException>(() =>
            registry.ResolveAndCheckRights("/workspace/file.txt", FileAccessRights.Write));

        Assert.Equal("/workspace/file.txt", ex.VirtualPath);
        Assert.Equal(FileAccessRights.Write, ex.RequiredRight);
    }

    [Fact]
    public void ResolveAndCheckRights_PathTraversal_ThrowsFileAccessDeniedException()
    {
        var baseDir = CreateSubDir("ws");
        var mount = new FileSystemMount(baseDir, "/workspace", FileAccessRights.ReadWrite);
        using var registry = new FileSystemRegistry([mount]);

        var ex = Assert.Throws<FileAccessDeniedException>(() =>
            registry.ResolveAndCheckRights("/workspace/../../etc/passwd", FileAccessRights.Read));

        Assert.Contains("path traversal", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveAndCheckRights_LongestPrefixMatch_SelectsCorrectMount()
    {
        var wsDir = CreateSubDir("ws");
        var vendorDir = CreateSubDir("vendor");

        var mounts = new[]
        {
            new FileSystemMount(wsDir, "/workspace", FileAccessRights.ReadWrite),
            new FileSystemMount(vendorDir, "/workspace/vendor", FileAccessRights.ReadOnly)
        };
        using var registry = new FileSystemRegistry(mounts);

        // /workspace/vendor/lib.dll should match /workspace/vendor, not /workspace
        var ex = Assert.Throws<FileAccessDeniedException>(() =>
            registry.ResolveAndCheckRights("/workspace/vendor/lib.dll", FileAccessRights.Write));

        Assert.Equal(FileAccessRights.Write, ex.RequiredRight);
    }

    [Fact]
    public void ResolveAndCheckRights_BoundaryCheck_DoesNotMatchSimilarPrefix()
    {
        var wsDir = CreateSubDir("ws");
        var toolsDir = CreateSubDir("tools");

        var mounts = new[]
        {
            new FileSystemMount(wsDir, "/workspace", FileAccessRights.ReadOnly),
            new FileSystemMount(toolsDir, "/workspace-tools", FileAccessRights.ReadWrite)
        };
        using var registry = new FileSystemRegistry(mounts);

        // /workspace-tools/file.txt should match /workspace-tools, not /workspace
        var physicalPath = registry.ResolveAndCheckRights("/workspace-tools/file.txt", FileAccessRights.Write);

        var expected = Path.GetFullPath(Path.Combine(toolsDir, "file.txt"));
        Assert.Equal(expected, physicalPath);
    }

    [Fact]
    public void ToVirtualPath_ValidPhysicalPath_ReturnsVirtualPath()
    {
        var baseDir = CreateSubDir("ws");
        var mount = new FileSystemMount(baseDir, "/workspace", FileAccessRights.ReadWrite);
        using var registry = new FileSystemRegistry([mount]);

        var physicalPath = Path.Combine(baseDir, "src", "file.cs");
        var virtualPath = registry.ToVirtualPath(physicalPath);

        Assert.Equal("/workspace/src/file.cs", virtualPath);
    }

    [Fact]
    public void ToVirtualPath_ExactBaseDir_ReturnsMountVirtualPath()
    {
        var baseDir = CreateSubDir("ws");
        var mount = new FileSystemMount(baseDir, "/workspace", FileAccessRights.ReadWrite);
        using var registry = new FileSystemRegistry([mount]);

        var virtualPath = registry.ToVirtualPath(baseDir);

        Assert.Equal("/workspace", virtualPath);
    }

    /// <summary>
    /// Coming back from a physical path, the mount that wins is the one whose BASE PATH is
    /// most specific. The registry orders its list by virtual-path length — the ordering the
    /// other direction needs — and taking the first list hit answered with whichever mount
    /// happened to have the longer virtual spelling. Here the nested mount has the shorter
    /// one, so the two orderings disagree and only the physical answer is right: a file in
    /// the vendor directory named "/workspace/vendor/lib.dll" re-resolves through the
    /// read-write parent instead of the read-only mount it actually lives in.
    /// </summary>
    [Fact]
    public void ToVirtualPath_NestedBasePaths_PrefersTheMostSpecificMount()
    {
        var wsDir = CreateSubDir("ws");
        var vendorDir = Path.Combine(wsDir, "vendor");
        Directory.CreateDirectory(vendorDir);

        var mounts = new[]
        {
            new FileSystemMount(wsDir, "/workspace", FileAccessRights.ReadWrite),
            new FileSystemMount(vendorDir, "/vd", FileAccessRights.ReadOnly),
        };
        using var registry = new FileSystemRegistry(mounts);

        Assert.Equal("/vd/lib.dll", registry.ToVirtualPath(Path.Combine(vendorDir, "lib.dll")));
        Assert.Equal("/vd", registry.ToVirtualPath(vendorDir));
        Assert.Equal("/workspace/src/a.cs", registry.ToVirtualPath(Path.Combine(wsDir, "src", "a.cs")));
    }

    /// <summary>The boundary is a separator, in this direction too.</summary>
    [Fact]
    public void ToVirtualPath_SiblingWhoseNameExtendsTheMount_IsUnmapped()
    {
        var wsDir = CreateSubDir("ws");
        CreateSubDir("ws-old");

        using var registry = new FileSystemRegistry(
            [new FileSystemMount(wsDir, "/workspace", FileAccessRights.ReadWrite)]);

        Assert.Null(registry.ToVirtualPath(Path.Combine(_tempDir, "ws-old", "a.cs")));
    }

    [Fact]
    public void ToVirtualPath_UnmappedPath_ReturnsNull()
    {
        var baseDir = CreateSubDir("ws");
        var mount = new FileSystemMount(baseDir, "/workspace", FileAccessRights.ReadWrite);
        using var registry = new FileSystemRegistry([mount]);

        var virtualPath = registry.ToVirtualPath("/some/other/path");

        Assert.Null(virtualPath);
    }

    [Fact]
    public void GetAvailableMounts_NeverExposesPhysicalPaths()
    {
        var baseDir = CreateSubDir("ws");
        var mount = new FileSystemMount(baseDir, "/workspace", FileAccessRights.ReadWrite);
        using var registry = new FileSystemRegistry([mount]);

        var mountInfos = registry.GetAvailableMounts();

        Assert.Single(mountInfos);
        Assert.Equal("/workspace", mountInfos[0].VirtualPath);
        Assert.Equal(FileAccessRights.ReadWrite, mountInfos[0].DefaultRights);

        // MountInfo record should NOT have a BasePath property
        var properties = typeof(MountInfo).GetProperties();
        Assert.DoesNotContain(properties, p => p.Name == "BasePath");
    }

    [Fact]
    public void GetAvailableMounts_ReturnsAllMounts()
    {
        var dir1 = CreateSubDir("a");
        var dir2 = CreateSubDir("b");

        var mounts = new[]
        {
            new FileSystemMount(dir1, "/alpha", FileAccessRights.ReadWrite),
            new FileSystemMount(dir2, "/beta", FileAccessRights.ReadOnly)
        };
        using var registry = new FileSystemRegistry(mounts);

        var infos = registry.GetAvailableMounts();

        Assert.Equal(2, infos.Count);
        var virtualPaths = infos.Select(i => i.VirtualPath).ToList();
        Assert.Contains("/alpha", virtualPaths);
        Assert.Contains("/beta", virtualPaths);
    }

    [Fact]
    public void ErrorMessages_NeverContainPhysicalPaths()
    {
        var baseDir = CreateSubDir("secret-location");
        var mount = new FileSystemMount(baseDir, "/workspace", FileAccessRights.ReadOnly);
        using var registry = new FileSystemRegistry([mount]);

        // Insufficient rights error
        var ex = Assert.Throws<FileAccessDeniedException>(() =>
            registry.ResolveAndCheckRights("/workspace/file.txt", FileAccessRights.Write));

        Assert.DoesNotContain(baseDir, ex.Message);
        Assert.DoesNotContain("secret-location", ex.Message);
    }

    [Fact]
    public void ErrorMessages_UnknownMount_NeverContainPhysicalPaths()
    {
        var baseDir = CreateSubDir("secret-dir");
        var mount = new FileSystemMount(baseDir, "/workspace", FileAccessRights.ReadWrite);
        using var registry = new FileSystemRegistry([mount]);

        var ex = Assert.Throws<FileAccessDeniedException>(() =>
            registry.ResolveAndCheckRights("/unknown/file.txt", FileAccessRights.Read));

        Assert.DoesNotContain(baseDir, ex.Message);
        Assert.DoesNotContain("secret-dir", ex.Message);
    }

    [Fact]
    public void CaseSensitivity_DataNotEqualdata()
    {
        var dir1 = CreateSubDir("upper");
        var dir2 = CreateSubDir("lower");

        var mounts = new[]
        {
            new FileSystemMount(dir1, "/Data", FileAccessRights.ReadWrite),
            new FileSystemMount(dir2, "/data", FileAccessRights.ReadOnly)
        };
        // Should not throw duplicate error - they are different virtual paths
        using var registry = new FileSystemRegistry(mounts);

        // /Data should match /Data mount (rw)
        var path1 = registry.ResolveAndCheckRights("/Data/file.txt", FileAccessRights.Write);
        Assert.Contains("upper", path1);

        // /data should match /data mount (ro) - writing should fail
        var ex = Assert.Throws<FileAccessDeniedException>(() =>
            registry.ResolveAndCheckRights("/data/file.txt", FileAccessRights.Write));
        Assert.Equal(FileAccessRights.Write, ex.RequiredRight);
    }

    [Fact]
    public void Constructor_NullMounts_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new FileSystemRegistry(null!));
    }

    [Fact]
    public void Constructor_EmptyMounts_CreatesEmptyRegistry()
    {
        using var registry = new FileSystemRegistry(Enumerable.Empty<FileSystemMount>());

        Assert.Empty(registry.GetAvailableMounts());
    }

    [Fact]
    public void ResolveAndCheckRights_NullPath_ThrowsArgumentException()
    {
        var baseDir = CreateSubDir("ws");
        var mount = new FileSystemMount(baseDir, "/workspace", FileAccessRights.ReadWrite);
        using var registry = new FileSystemRegistry([mount]);

        Assert.ThrowsAny<ArgumentException>(() =>
            registry.ResolveAndCheckRights(null!, FileAccessRights.Read));
    }
}
