using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.FileSystem;

namespace Orkeon.Infrastructure.Tests.FileSystem;

public sealed class FileSystemServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemService _service;

    public FileSystemServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"orkeon-fs-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _service = BuildService(_tempDir, "/src", FileAccessRights.ReadOnly);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ---------------------------------------------------------------------
    // EnumerateFilesAsync
    // ---------------------------------------------------------------------

    [Fact]
    public async Task EnumerateFilesAsync_FlatDir_YieldsAllEntries()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "a.ts"), "a", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "b.ts"), "bb", TestContext.Current.CancellationToken);

        var entries = await CollectAsync(_service.EnumerateFilesAsync("/src", options: null, CancellationToken.None));

        var paths = entries.Where(e => e.Kind == VirtualEntryKind.File).Select(e => e.VirtualPath).OrderBy(p => p).ToList();
        Assert.Equal(["/src/a.ts", "/src/b.ts"], paths);
    }

    [Fact]
    public async Task EnumerateFilesAsync_Recursive_DescendsIntoSubdirs()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "sub"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "top.ts"), "t", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "sub", "child.ts"), "c", TestContext.Current.CancellationToken);

        var entries = await CollectAsync(_service.EnumerateFilesAsync(
            "/src", new VirtualEnumerationOptions(Recursive: true), CancellationToken.None));

        Assert.Contains(entries, e => e.VirtualPath == "/src/top.ts");
        Assert.Contains(entries, e => e.VirtualPath == "/src/sub/child.ts");
    }

    [Fact]
    public async Task EnumerateFilesAsync_NonRecursive_SkipsSubdirContents()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "sub"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "top.ts"), "t", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "sub", "child.ts"), "c", TestContext.Current.CancellationToken);

        var entries = await CollectAsync(_service.EnumerateFilesAsync(
            "/src", new VirtualEnumerationOptions(Recursive: false), CancellationToken.None));

        var files = entries.Where(e => e.Kind == VirtualEntryKind.File).Select(e => e.VirtualPath).ToList();
        Assert.Single(files);
        Assert.Contains("/src/top.ts", files);
    }

    [Fact]
    public async Task EnumerateFilesAsync_ExcludeGlob_FiltersMatchingPaths()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "node_modules"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "keep.ts"), "k", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "skip.log"), "s", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "node_modules", "lib.js"), "l", TestContext.Current.CancellationToken);

        var options = new VirtualEnumerationOptions(
            Recursive: true,
            Exclude: ["**/*.log", "node_modules/**"]);

        var entries = await CollectAsync(_service.EnumerateFilesAsync("/src", options, CancellationToken.None));
        var files = entries.Where(e => e.Kind == VirtualEntryKind.File).Select(e => e.VirtualPath).ToList();

        Assert.Contains("/src/keep.ts", files);
        Assert.DoesNotContain("/src/skip.log", files);
        Assert.DoesNotContain("/src/node_modules/lib.js", files);
    }

    [Fact]
    public async Task EnumerateFilesAsync_MaxDepth_TrimsDeepEntries()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "a", "b", "c"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "a", "f1.ts"), "1", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "a", "b", "f2.ts"), "2", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "a", "b", "c", "f3.ts"), "3", TestContext.Current.CancellationToken);

        var options = new VirtualEnumerationOptions(Recursive: true, MaxDepth: 2);
        var entries = await CollectAsync(_service.EnumerateFilesAsync("/src", options, CancellationToken.None));
        var files = entries.Where(e => e.Kind == VirtualEntryKind.File).Select(e => e.VirtualPath).ToList();

        Assert.Contains("/src/a/f1.ts", files);
        Assert.Contains("/src/a/b/f2.ts", files);
        Assert.DoesNotContain("/src/a/b/c/f3.ts", files);
    }

    [Fact]
    public async Task EnumerateFilesAsync_DeniedRoot_Throws()
    {
        await Assert.ThrowsAsync<FileAccessDeniedException>(async () =>
        {
            await foreach (var _ in _service.EnumerateFilesAsync("/not-a-mount", options: null, CancellationToken.None))
            {
                // consume to force enumeration
            }
        });
    }

    [Fact]
    public async Task EnumerateFilesAsync_RespectsCancellation()
    {
        for (var i = 0; i < 20; i++)
            await File.WriteAllTextAsync(Path.Combine(_tempDir, $"f{i}.ts"), "x", TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in _service.EnumerateFilesAsync("/src", options: null, cts.Token))
            {
            }
        });
    }

    // ---------------------------------------------------------------------
    // OpenReadStreamAsync
    // ---------------------------------------------------------------------

    [Fact]
    public async Task OpenReadStreamAsync_ReadsFile_ComputesSha256()
    {
        var content = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "bin.dat"), content, TestContext.Current.CancellationToken);

        byte[] hash;
        await using (var stream = await _service.OpenReadStreamAsync("/src/bin.dat", CancellationToken.None))
        using (var sha = SHA256.Create())
        {
            hash = await sha.ComputeHashAsync(stream, CancellationToken.None);
        }

        var expected = SHA256.HashData(content);
        Assert.Equal(expected, hash);
    }

    [Fact]
    public async Task OpenReadStreamAsync_MissingFile_ThrowsFileNotFound()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _service.OpenReadStreamAsync("/src/does-not-exist.txt", CancellationToken.None));
    }

    [Fact]
    public async Task OpenReadStreamAsync_OutsideMount_ThrowsAccessDenied()
    {
        await Assert.ThrowsAsync<FileAccessDeniedException>(() =>
            _service.OpenReadStreamAsync("/elsewhere/file.txt", CancellationToken.None));
    }

    // ---------------------------------------------------------------------
    // TryReadAllBytesAsync / TryReadAllTextAsync
    // ---------------------------------------------------------------------

    [Fact]
    public async Task TryReadAllBytesAsync_Existing_ReturnsContent()
    {
        var content = new byte[] { 9, 8, 7 };
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "x.bin"), content, TestContext.Current.CancellationToken);

        var bytes = await _service.TryReadAllBytesAsync("/src/x.bin", CancellationToken.None);
        Assert.Equal(content, bytes);
    }

    [Fact]
    public async Task TryReadAllBytesAsync_Missing_ReturnsNull()
    {
        var bytes = await _service.TryReadAllBytesAsync("/src/missing.bin", CancellationToken.None);
        Assert.Null(bytes);
    }

    [Fact]
    public async Task TryReadAllTextAsync_Existing_ReturnsContent()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "x.txt"), "hello", TestContext.Current.CancellationToken);

        var text = await _service.TryReadAllTextAsync("/src/x.txt", CancellationToken.None);
        Assert.Equal("hello", text);
    }

    [Fact]
    public async Task TryReadAllTextAsync_Missing_ReturnsNull()
    {
        var text = await _service.TryReadAllTextAsync("/src/missing.txt", CancellationToken.None);
        Assert.Null(text);
    }

    [Fact]
    public async Task TryRead_OutsideMount_ThrowsAccessDenied()
    {
        await Assert.ThrowsAsync<FileAccessDeniedException>(() =>
            _service.TryReadAllBytesAsync("/nope/x.bin", CancellationToken.None));

        await Assert.ThrowsAsync<FileAccessDeniedException>(() =>
            _service.TryReadAllTextAsync("/nope/x.txt", CancellationToken.None));
    }

    // ---------------------------------------------------------------------
    // GetEntryKindAsync
    // ---------------------------------------------------------------------

    [Fact]
    public async Task GetEntryKindAsync_File_ReturnsFile()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "x.ts"), "x", TestContext.Current.CancellationToken);

        var kind = await _service.GetEntryKindAsync("/src/x.ts", CancellationToken.None);
        Assert.Equal(VirtualEntryKind.File, kind);
    }

    [Fact]
    public async Task GetEntryKindAsync_Directory_ReturnsDirectory()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "sub"));

        var kind = await _service.GetEntryKindAsync("/src/sub", CancellationToken.None);
        Assert.Equal(VirtualEntryKind.Directory, kind);
    }

    [Fact]
    public async Task GetEntryKindAsync_Missing_ThrowsFileNotFound()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _service.GetEntryKindAsync("/src/missing", CancellationToken.None));
    }

    [Fact]
    public async Task GetEntryKindAsync_OutsideMount_ThrowsAccessDenied()
    {
        await Assert.ThrowsAsync<FileAccessDeniedException>(() =>
            _service.GetEntryKindAsync("/outside/x", CancellationToken.None));
    }

    // ---------------------------------------------------------------------
    // Physical path leak guard
    // ---------------------------------------------------------------------

    [Fact]
    public async Task VirtualFileEntry_NeverContainsPhysicalBasePath()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "deep"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "deep", "x.ts"), "x", TestContext.Current.CancellationToken);

        var entries = await CollectAsync(_service.EnumerateFilesAsync("/src", options: null, CancellationToken.None));

        foreach (var entry in entries)
        {
            Assert.DoesNotContain(_tempDir, entry.VirtualPath, StringComparison.Ordinal);
            Assert.StartsWith("/src", entry.VirtualPath, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    // ---------------------------------------------------------------------
    // ExistsAsync
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ExistsAsync_ReturnsTrueForExistingFile()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "exists.txt"), "x", TestContext.Current.CancellationToken);
        Assert.True(await _service.ExistsAsync("/src/exists.txt", CancellationToken.None));
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalseForMissingPath()
    {
        Assert.False(await _service.ExistsAsync("/src/no-such-file.txt", CancellationToken.None));
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalseWhenDenied()
    {
        // Path outside any mount — should return false, not throw
        Assert.False(await _service.ExistsAsync("/outside-mount/file.txt", CancellationToken.None));
    }

    // ---------------------------------------------------------------------
    // CreateDirectoryAsync
    // ---------------------------------------------------------------------

    [Fact]
    public async Task CreateDirectoryAsync_IsIdempotent()
    {
        var svc = BuildService(_tempDir, "/src", FileAccessRights.ReadWrite);
        await svc.CreateDirectoryAsync("/src/newdir", CancellationToken.None);
        await svc.CreateDirectoryAsync("/src/newdir", CancellationToken.None); // second call must not throw
        Assert.True(Directory.Exists(Path.Combine(_tempDir, "newdir")));
    }

    [Fact]
    public async Task CreateDirectoryAsync_Denied_ThrowsAccessDeniedException()
    {
        // _service is ReadOnly
        await Assert.ThrowsAsync<FileAccessDeniedException>(() =>
            _service.CreateDirectoryAsync("/src/forbidden", CancellationToken.None));
    }

    // ---------------------------------------------------------------------
    // DeleteAsync
    // ---------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_File_Succeeds()
    {
        var svc = BuildService(_tempDir, "/src", FileAccessRights.ReadWrite);
        var physical = Path.Combine(_tempDir, "todelete.txt");
        await File.WriteAllTextAsync(physical, "bye", TestContext.Current.CancellationToken);

        var result = await svc.DeleteAsync("/src/todelete.txt", recursive: false, CancellationToken.None);
        Assert.True(result);
        Assert.False(File.Exists(physical));
    }

    [Fact]
    public async Task DeleteAsync_MissingPath_ReturnsFalse()
    {
        var svc = BuildService(_tempDir, "/src", FileAccessRights.ReadWrite);
        var result = await svc.DeleteAsync("/src/ghost.txt", recursive: false, CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAsync_Directory_Recursive_Succeeds()
    {
        var svc = BuildService(_tempDir, "/src", FileAccessRights.ReadWrite);
        var dir = Path.Combine(_tempDir, "subdir");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "child.txt"), "c", TestContext.Current.CancellationToken);

        var result = await svc.DeleteAsync("/src/subdir", recursive: true, CancellationToken.None);
        Assert.True(result);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public async Task DeleteAsync_Denied_ThrowsAccessDeniedException()
    {
        // _service is ReadOnly
        await Assert.ThrowsAsync<FileAccessDeniedException>(() =>
            _service.DeleteAsync("/src/anything.txt", recursive: false, CancellationToken.None));
    }

    // ---------------------------------------------------------------------
    // EnumerateFilesAsync with SearchPattern
    // ---------------------------------------------------------------------

    [Fact]
    public async Task EnumerateFilesAsync_WithSearchPattern_FiltersMatching()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "a.json"), "{}", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "b.json"), "{}", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "c.txt"), "txt", TestContext.Current.CancellationToken);

        var options = new VirtualEnumerationOptions(SearchPattern: "*.json");
        var entries = await CollectAsync(_service.EnumerateFilesAsync("/src", options, CancellationToken.None));
        var files = entries.Where(e => e.Kind == VirtualEntryKind.File).Select(e => e.VirtualPath).OrderBy(p => p).ToList();

        Assert.Equal(["/src/a.json", "/src/b.json"], files);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The registry is captured by the returned FileSystemService and must outlive this factory; it lives for the duration of the test.")]
    private static FileSystemService BuildService(string basePath, string virtualPath, FileAccessRights rights)
    {
        var mount = new FileSystemMount(basePath, virtualPath, rights);
        var registry = new FileSystemRegistry([mount]);

        var pathValidator = new StubPathValidator()
            .RespondWith((p, _) => PathValidationResult.Allowed(p));

        return new FileSystemService(registry, pathValidator, NullLogger<FileSystemService>.Instance);
    }

    private static async Task<List<VirtualFileEntry>> CollectAsync(IAsyncEnumerable<VirtualFileEntry> source)
    {
        var list = new List<VirtualFileEntry>();
        await foreach (var entry in source)
            list.Add(entry);
        return list;
    }
}
