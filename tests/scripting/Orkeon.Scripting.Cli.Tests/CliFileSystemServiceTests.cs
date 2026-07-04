using System.Reflection;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// Coverage for the CLI bootstrap <c>CliFileSystemService</c>. The type is
/// <see langword="internal"/> to the <c>orkeon</c> assembly (which does not expose
/// <c>InternalsVisibleTo</c>), so it is instantiated reflectively and driven through the
/// public <see cref="IFileSystemService"/> surface. The adapter maps a single host folder
/// to a <c>/script</c> virtual root and is exercised against a real temp directory.
/// </summary>
public sealed class CliFileSystemServiceTests : IDisposable
{
    private const string TypeName = "Orkeon.Scripting.Cli.CliFileSystemService";

    private readonly string _base;

    public CliFileSystemServiceTests()
    {
        _base = Path.Combine(Path.GetTempPath(), "ork-clifs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_base);
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, recursive: true); } catch { /* best-effort */ }
    }

    private static IFileSystemService Create(string physicalBase, string virtualRoot = "/script")
    {
        var type = typeof(Program).Assembly.GetType(TypeName, throwOnError: true)!;
        var instance = Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: new object[] { physicalBase, virtualRoot },
            culture: null)!;
        return (IFileSystemService)instance;
    }

    [Fact]
    public void ResolveAndValidate_allows_path_inside_mount()
    {
        var fs = Create(_base);

        var result = fs.ResolveAndValidate("/script/sub/file.txt", FileAccessRights.Read);

        Assert.True(result.IsAllowed);
        Assert.NotNull(result.ResolvedPath);
        Assert.StartsWith(Path.GetFullPath(_base), result.ResolvedPath!, StringComparison.Ordinal);
        Assert.EndsWith("file.txt", result.ResolvedPath!, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveAndValidate_denies_path_outside_mount()
    {
        var fs = Create(_base);

        var result = fs.ResolveAndValidate("/elsewhere/file.txt", FileAccessRights.Read);

        Assert.False(result.IsAllowed);
        Assert.Null(result.ResolvedPath);
        Assert.Contains("outside CLI mount", result.DenialReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveAndValidate_denies_path_traversal()
    {
        var fs = Create(_base);

        // Climbs above the physical base via ../ — must be rejected by the prefix check.
        var result = fs.ResolveAndValidate("/script/../../etc/passwd", FileAccessRights.Read);

        Assert.False(result.IsAllowed);
        Assert.Contains("traversal", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToVirtualPath_maps_physical_file_back_to_virtual()
    {
        var fs = Create(_base);
        var physical = Path.Combine(_base, "dir", "a.txt");

        var virt = fs.ToVirtualPath(physical);

        Assert.Equal("/script/dir/a.txt", virt);
    }

    [Fact]
    public void ToVirtualPath_returns_root_for_base_itself()
    {
        var fs = Create(_base);

        var virt = fs.ToVirtualPath(_base);

        Assert.Equal("/script", virt);
    }

    [Fact]
    public void ToVirtualPath_returns_null_for_path_outside_base()
    {
        var fs = Create(_base);
        var outside = Path.Combine(Path.GetTempPath(), "ork-other-" + Guid.NewGuid().ToString("N"), "x.txt");

        var virt = fs.ToVirtualPath(outside);

        Assert.Null(virt);
    }

    [Fact]
    public void GetAvailableMounts_exposes_single_readonly_script_mount()
    {
        var fs = Create(_base, "/script");

        var mounts = fs.GetAvailableMounts();

        var mount = Assert.Single(mounts);
        Assert.Equal("/script", mount.VirtualPath);
        Assert.Equal(FileAccessRights.Read, mount.DefaultRights);
        Assert.Empty(mount.Overrides);
    }

    [Fact]
    public void Custom_virtual_root_with_trailing_slash_is_trimmed()
    {
        var fs = Create(_base, "/mnt/");

        var mounts = fs.GetAvailableMounts();
        Assert.Equal("/mnt", Assert.Single(mounts).VirtualPath);

        var resolved = fs.ResolveAndValidate("/mnt/file.txt", FileAccessRights.Read);
        Assert.True(resolved.IsAllowed);
    }

    [Fact]
    public async Task TryReadAllTextAsync_reads_existing_file()
    {
        await File.WriteAllTextAsync(Path.Combine(_base, "hello.txt"), "content", TestContext.Current.CancellationToken);
        var fs = Create(_base);

        var text = await fs.TryReadAllTextAsync("/script/hello.txt", CancellationToken.None);

        Assert.Equal("content", text);
    }

    [Fact]
    public async Task TryReadAllTextAsync_returns_null_for_missing_file()
    {
        var fs = Create(_base);

        var text = await fs.TryReadAllTextAsync("/script/absent.txt", CancellationToken.None);

        Assert.Null(text);
    }

    [Fact]
    public async Task TryReadAllTextAsync_returns_null_for_denied_path()
    {
        var fs = Create(_base);

        var text = await fs.TryReadAllTextAsync("/outside/secret.txt", CancellationToken.None);

        Assert.Null(text);
    }

    [Fact]
    public async Task Unsupported_write_and_query_operations_throw_NotSupported()
    {
        var fs = Create(_base);
        var ct = CancellationToken.None;

        await Assert.ThrowsAsync<NotSupportedException>(() => fs.TryReadAllBytesAsync("/script/x", ct));
        await Assert.ThrowsAsync<NotSupportedException>(() => fs.OpenReadStreamAsync("/script/x", ct));
        await Assert.ThrowsAsync<NotSupportedException>(() => fs.GetEntryKindAsync("/script/x", ct));
        await Assert.ThrowsAsync<NotSupportedException>(() => fs.WriteAllTextAsync("/script/x", "c", ct));
        await Assert.ThrowsAsync<NotSupportedException>(() => fs.ExistsAsync("/script/x", ct));
        await Assert.ThrowsAsync<NotSupportedException>(() => fs.CreateDirectoryAsync("/script/x", ct));
        await Assert.ThrowsAsync<NotSupportedException>(() => fs.DeleteAsync("/script/x", false, ct));
        await Assert.ThrowsAsync<NotSupportedException>(() => fs.WriteAllBytesAsync("/script/x", Array.Empty<byte>(), ct));
        await Assert.ThrowsAsync<NotSupportedException>(() => fs.AppendAllTextAsync("/script/x", "c", ct));
        await Assert.ThrowsAsync<NotSupportedException>(() => fs.TryGetEntryAsync("/script/x", ct));
        await Assert.ThrowsAsync<NotSupportedException>(() => fs.OpenWriteStreamAsync("/script/x", ct));
        await Assert.ThrowsAsync<NotSupportedException>(() => fs.OpenAppendStreamAsync("/script/x", ct));
        await Assert.ThrowsAsync<NotSupportedException>(() => fs.CopyAsync("/script/x", "/script/y", false, ct));
    }

    [Fact]
    public void EnumerateFilesAsync_throws_NotSupported()
    {
        var fs = Create(_base);

        // Synchronous throw: the method body throws before returning the async enumerable.
        Assert.Throws<NotSupportedException>(() =>
            fs.EnumerateFilesAsync("/script", null, CancellationToken.None));
    }
}
