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

    /// <summary>
    /// An Internal mount resolves for a caller that asks for it, and only for that caller.
    /// <para>
    /// This test used to assert the first half alone, under the comment "should resolve
    /// without throwing even though /sandbox is Internal" — which is exactly what made
    /// <c>Internal</c> a hiding place rather than a boundary. The names are documented, so
    /// "hidden from the listing, resolvable by anyone who types it" meant one
    /// <c>file_read /llm-logs/…</c> away from every prompt of the run.
    /// </para>
    /// </summary>
    [Fact]
    public void FileSystemRegistry_ResolvesInternalMount_ForAPrivilegedCallerOnly()
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

        var physicalPath = registry.ResolveAndCheckRights(
            "/sandbox/output.txt", FileAccessRights.Write, includeInternal: true);

        Assert.Equal(Path.GetFullPath(Path.Combine(sandboxDir, "output.txt")), physicalPath);

        Assert.Throws<FileAccessDeniedException>(() =>
            registry.ResolveAndCheckRights("/sandbox/output.txt", FileAccessRights.Write));
    }

    /// <summary>
    /// A denial message is read by the LLM, so it is an agent-facing surface like
    /// <c>list_mounts</c>. It used to enumerate <em>every</em> mount, which handed an agent the
    /// name of the one mount visibility exists to withhold — and annotated it "(writable)". The
    /// exchange-log directory is mounted that way and holds every prompt and API payload.
    /// </summary>
    [Fact]
    public void FileSystemRegistry_UnmountedPathDenial_NamesAgentFacingMountsOnly()
    {
        var wsDir = CreateSubDir("ws");
        var logsDir = CreateSubDir("logs");

        using var registry = new FileSystemRegistry(
        [
            new FileSystemMount(wsDir, "/workspace", FileAccessRights.ReadWrite),
            new FileSystemMount(logsDir, "/llm-logs", FileAccessRights.ReadWrite,
                visibility: MountVisibility.Internal)
        ]);

        var ex = Assert.Throws<FileAccessDeniedException>(() =>
            registry.ResolveAndCheckRights("/nowhere/at/all.txt", FileAccessRights.Read));

        Assert.Contains("/workspace", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("/llm-logs", ex.Message, StringComparison.Ordinal);
        Assert.NotNull(ex.AlternativeMounts);
        Assert.DoesNotContain("/llm-logs", ex.AlternativeMounts);
    }

    /// <summary>The same rule for the "which mount would have granted this?" hint.</summary>
    [Fact]
    public void FileSystemRegistry_RightsDenial_NamesAgentFacingMountsOnly()
    {
        var docsDir = CreateSubDir("docs");
        var logsDir = CreateSubDir("logs");

        using var registry = new FileSystemRegistry(
        [
            new FileSystemMount(docsDir, "/docs", FileAccessRights.ReadOnly),
            new FileSystemMount(logsDir, "/llm-logs", FileAccessRights.ReadWrite,
                visibility: MountVisibility.Internal)
        ]);

        var ex = Assert.Throws<FileAccessDeniedException>(() =>
            registry.ResolveAndCheckRights("/docs/report.md", FileAccessRights.Write));

        Assert.DoesNotContain("/llm-logs", ex.Message, StringComparison.Ordinal);
        Assert.Contains("(none)", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FileSystemMount_Parse_DefaultsToAgentFacing()
    {
        var mount = FileSystemMount.Parse("/tmp/data:/inputs:ro");

        Assert.Equal(MountVisibility.AgentFacing, mount.Visibility);
    }
}
