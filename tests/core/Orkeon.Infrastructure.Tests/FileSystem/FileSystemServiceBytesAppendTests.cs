using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.FileSystem;

/// <summary>
/// Tests for P1-VFS-03 APIs: WriteAllBytesAsync, AppendAllTextAsync, TryGetEntryAsync,
/// and the VirtualFileEntry CreationTime extension.
/// </summary>
public sealed class FileSystemServiceBytesAppendTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemService _service;       // ReadOnly mount
    private readonly FileSystemService _rwService;     // ReadWrite mount

    public FileSystemServiceBytesAppendTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"orkeon-p1vfs03-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _service = BuildService(_tempDir, "/src", FileAccessRights.ReadOnly);
        _rwService = BuildService(_tempDir, "/src", FileAccessRights.ReadWrite);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // -------------------------------------------------------------------------
    // WriteAllBytesAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WriteAllBytesAsync_RoundtripsWithReadAll()
    {
        var content = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var count = await _rwService.WriteAllBytesAsync("/src/bin.dat", content, CancellationToken.None);

        Assert.Equal(content.Length, count);

        var read = await _rwService.TryReadAllBytesAsync("/src/bin.dat", CancellationToken.None);
        Assert.NotNull(read);
        Assert.Equal(content, read);
    }

    [Fact]
    public async Task WriteAllBytesAsync_CreatesParentDirectories()
    {
        var content = new byte[] { 1, 2, 3 };
        await _rwService.WriteAllBytesAsync("/src/a/b/c/data.bin", content, CancellationToken.None);

        var physical = Path.Combine(_tempDir, "a", "b", "c", "data.bin");
        Assert.True(File.Exists(physical));
        Assert.Equal(content, await File.ReadAllBytesAsync(physical, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WriteAllBytesAsync_Denied_Throws()
    {
        await Assert.ThrowsAsync<FileAccessDeniedException>(() =>
            _service.WriteAllBytesAsync("/src/denied.bin", [0x01], CancellationToken.None));
    }

    // -------------------------------------------------------------------------
    // AppendAllTextAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AppendAllTextAsync_AppendsToExistingFile()
    {
        // Seed an existing file
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "log.txt"), "first\n", TestContext.Current.CancellationToken);

        await _rwService.AppendAllTextAsync("/src/log.txt", "second\n", CancellationToken.None);

        var result = await File.ReadAllTextAsync(Path.Combine(_tempDir, "log.txt"), TestContext.Current.CancellationToken);
        Assert.Equal("first\nsecond\n", result);
    }

    [Fact]
    public async Task AppendAllTextAsync_CreatesFileIfMissing()
    {
        var count = await _rwService.AppendAllTextAsync("/src/new.txt", "hello", CancellationToken.None);

        Assert.True(count > 0);
        Assert.True(File.Exists(Path.Combine(_tempDir, "new.txt")));
        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(_tempDir, "new.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AppendAllTextAsync_UsesUtf8NoBom()
    {
        // Write a known multi-byte Unicode string and verify no BOM is prepended
        var text = "caf\u00e9"; // "café" — \u00e9 encodes to 2 bytes in UTF-8
        var count = await _rwService.AppendAllTextAsync("/src/utf8.txt", text, CancellationToken.None);

        var physical = Path.Combine(_tempDir, "utf8.txt");
        var rawBytes = await File.ReadAllBytesAsync(physical, TestContext.Current.CancellationToken);

        // No UTF-8 BOM (EF BB BF)
        Assert.False(rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF,
            "File must not start with a UTF-8 BOM.");

        // Byte count returned equals the UTF-8 encoding of the content
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(text), count);

        // Decodes back to the original string
        Assert.Equal(text, System.Text.Encoding.UTF8.GetString(rawBytes));
    }

    // -------------------------------------------------------------------------
    // TryGetEntryAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryGetEntryAsync_ReturnsNullForMissing()
    {
        var entry = await _rwService.TryGetEntryAsync("/src/does-not-exist.txt", CancellationToken.None);
        Assert.Null(entry);
    }

    [Fact]
    public async Task TryGetEntryAsync_ReturnsEntryForFile()
    {
        var content = new byte[] { 10, 20, 30 };
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "file.dat"), content, TestContext.Current.CancellationToken);

        var entry = await _rwService.TryGetEntryAsync("/src/file.dat", CancellationToken.None);

        Assert.NotNull(entry);
        Assert.Equal("/src/file.dat", entry.VirtualPath);
        Assert.Equal(VirtualEntryKind.File, entry.Kind);
        Assert.Equal(content.Length, entry.SizeBytes);
    }

    [Fact]
    public async Task TryGetEntryAsync_ReturnsEntryForDirectory()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "subdir"));

        var entry = await _rwService.TryGetEntryAsync("/src/subdir", CancellationToken.None);

        Assert.NotNull(entry);
        Assert.Equal("/src/subdir", entry.VirtualPath);
        Assert.Equal(VirtualEntryKind.Directory, entry.Kind);
        Assert.Equal(0, entry.SizeBytes);
    }

    [Fact]
    public async Task TryGetEntryAsync_PopulatesCreationTime()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-2);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "timed.txt"), "x", TestContext.Current.CancellationToken);

        var entry = await _rwService.TryGetEntryAsync("/src/timed.txt", CancellationToken.None);

        Assert.NotNull(entry);
        Assert.NotNull(entry.CreationTime);
        // CreationTime should be reasonably recent — within the last minute
        Assert.True(entry.CreationTime!.Value >= before,
            $"CreationTime {entry.CreationTime} is before the test start {before}");
    }

    // -------------------------------------------------------------------------
    // TryGetEntryAsync — access denied returns null, no exception
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryGetEntryAsync_OutsideMount_ReturnsNull()
    {
        // /outside/x.txt is not under any mount — should return null, not throw
        var entry = await _rwService.TryGetEntryAsync("/outside/x.txt", CancellationToken.None);
        Assert.Null(entry);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The registry is captured by the returned FileSystemService and must outlive this factory; it lives for the duration of the test.")]
    private static FileSystemService BuildService(string basePath, string virtualPath, FileAccessRights rights)
    {
        var mount = new FileSystemMount(basePath, virtualPath, rights);
        var registry = new FileSystemRegistry([mount]);

        var pathValidator = new StubPathValidator()
            .RespondWith((p, _) => PathValidationResult.Allowed(p));

        return new FileSystemService(registry, pathValidator, NullLogger<FileSystemService>.Instance);
    }
}
