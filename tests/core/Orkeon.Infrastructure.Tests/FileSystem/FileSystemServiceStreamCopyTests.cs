using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.FileSystem;

namespace Orkeon.Infrastructure.Tests.FileSystem;

/// <summary>
/// Tests for P2-VFS-04 APIs: OpenWriteStreamAsync, OpenAppendStreamAsync, CopyAsync.
/// </summary>
public sealed class FileSystemServiceStreamCopyTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemService _rwService;
    private readonly FileSystemService _roService;

    public FileSystemServiceStreamCopyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"orkeon-p2vfs04-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _rwService = BuildService(_tempDir, "/src", FileAccessRights.ReadWrite);
        _roService = BuildService(_tempDir, "/src", FileAccessRights.ReadOnly);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // -------------------------------------------------------------------------
    // OpenWriteStreamAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OpenWriteStreamAsync_RoundtripsWithReadAll()
    {
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        await using (var stream = await _rwService.OpenWriteStreamAsync("/src/write.bin", TestContext.Current.CancellationToken))
        {
            await stream.WriteAsync(data, TestContext.Current.CancellationToken);
        }

        var read = await _rwService.TryReadAllBytesAsync("/src/write.bin", CancellationToken.None);
        Assert.NotNull(read);
        Assert.Equal(data, read);
    }

    [Fact]
    public async Task OpenWriteStreamAsync_OverwritesExistingFile()
    {
        var original = new byte[] { 0xFF, 0xFF, 0xFF };
        await _rwService.WriteAllBytesAsync("/src/overwrite.bin", original, CancellationToken.None);

        var replacement = new byte[] { 0xAB };
        await using (var stream = await _rwService.OpenWriteStreamAsync("/src/overwrite.bin", TestContext.Current.CancellationToken))
        {
            await stream.WriteAsync(replacement, TestContext.Current.CancellationToken);
        }

        var read = await _rwService.TryReadAllBytesAsync("/src/overwrite.bin", CancellationToken.None);
        Assert.Equal(replacement, read);
    }

    // -------------------------------------------------------------------------
    // OpenAppendStreamAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OpenAppendStreamAsync_AppendsToExistingFile()
    {
        var first = new byte[] { 0x01, 0x02 };
        await _rwService.WriteAllBytesAsync("/src/append.bin", first, CancellationToken.None);

        var second = new byte[] { 0x03, 0x04 };
        await using (var stream = await _rwService.OpenAppendStreamAsync("/src/append.bin", TestContext.Current.CancellationToken))
        {
            await stream.WriteAsync(second, TestContext.Current.CancellationToken);
        }

        var read = await _rwService.TryReadAllBytesAsync("/src/append.bin", CancellationToken.None);
        Assert.NotNull(read);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04 }, read);
    }

    [Fact]
    public async Task OpenAppendStreamAsync_CreatesFileWhenMissing()
    {
        var data = new byte[] { 0xAA, 0xBB };
        await using (var stream = await _rwService.OpenAppendStreamAsync("/src/new-append.bin", TestContext.Current.CancellationToken))
        {
            await stream.WriteAsync(data, TestContext.Current.CancellationToken);
        }

        var read = await _rwService.TryReadAllBytesAsync("/src/new-append.bin", CancellationToken.None);
        Assert.Equal(data, read);
    }

    // -------------------------------------------------------------------------
    // CopyAsync — intra-mount
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CopyAsync_IntraMount_DuplicatesFile()
    {
        var content = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        await _rwService.WriteAllBytesAsync("/src/original.bin", content, CancellationToken.None);

        await _rwService.CopyAsync("/src/original.bin", "/src/copy.bin", ct: TestContext.Current.CancellationToken);

        var copied = await _rwService.TryReadAllBytesAsync("/src/copy.bin", CancellationToken.None);
        Assert.NotNull(copied);
        Assert.Equal(content, copied);

        // Source still intact
        var source = await _rwService.TryReadAllBytesAsync("/src/original.bin", CancellationToken.None);
        Assert.Equal(content, source);
    }

    [Fact]
    public async Task CopyAsync_OverwriteFalse_ThrowsWhenDstExists()
    {
        await _rwService.WriteAllBytesAsync("/src/a.bin", [0x01], CancellationToken.None);
        await _rwService.WriteAllBytesAsync("/src/b.bin", [0x02], CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(() =>
            _rwService.CopyAsync("/src/a.bin", "/src/b.bin", overwrite: false, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CopyAsync_OverwriteTrue_ReplacesDst()
    {
        await _rwService.WriteAllBytesAsync("/src/src.bin", [0x42], CancellationToken.None);
        await _rwService.WriteAllBytesAsync("/src/dst.bin", [0x00, 0x00, 0x00], CancellationToken.None);

        await _rwService.CopyAsync("/src/src.bin", "/src/dst.bin", overwrite: true, TestContext.Current.CancellationToken);

        var result = await _rwService.TryReadAllBytesAsync("/src/dst.bin", CancellationToken.None);
        Assert.Equal(new byte[] { 0x42 }, result);
    }

    [Fact]
    public async Task CopyAsync_SourceDenied_Throws()
    {
        // _roService has ReadOnly rights — writing src will be done directly on disk
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "source.bin"), [0x01], TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<FileAccessDeniedException>(() =>
            _roService.CopyAsync("/src/source.bin", "/src/dest.bin", ct: TestContext.Current.CancellationToken));
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
