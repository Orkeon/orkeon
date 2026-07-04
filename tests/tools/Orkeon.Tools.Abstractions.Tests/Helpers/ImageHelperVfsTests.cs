using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Abstractions.Helpers;

namespace Orkeon.Tools.Abstractions.Tests.Helpers;

public class ImageHelperVfsTests
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];

    [Fact]
    public async Task LoadFromVfsAsync_ShouldReturnImageContentPart_WhenFileExists()
    {
        var fs = new FakeFileSystemService();
        fs.AddFile("/workspace/images/photo.png", PngBytes);

        var result = await ImageHelper.LoadFromVfsAsync(fs, "/workspace/images/photo.png", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("image/png", result.MimeType);
    }

    [Fact]
    public async Task LoadFromVfsAsync_ShouldThrowFileNotFoundException_WhenFileDoesNotExist()
    {
        var fs = new FakeFileSystemService();

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => ImageHelper.LoadFromVfsAsync(fs, "/workspace/images/missing.png", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadFromVfsAsync_ShouldDetectMimeType_FromMagicBytes()
    {
        var fs = new FakeFileSystemService();
        var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        fs.AddFile("/workspace/images/photo.jpg", jpegBytes);

        var result = await ImageHelper.LoadFromVfsAsync(fs, "/workspace/images/photo.jpg", TestContext.Current.CancellationToken);

        Assert.Equal("image/jpeg", result.MimeType);
    }

    [Fact]
    public async Task LoadFromVfsAsync_ShouldReturnCorrectData_WhenFileHasContent()
    {
        var fs = new FakeFileSystemService();
        fs.AddFile("/workspace/images/photo.png", PngBytes);

        var result = await ImageHelper.LoadFromVfsAsync(fs, "/workspace/images/photo.png", TestContext.Current.CancellationToken);

        Assert.NotNull(result.Data);
        Assert.Equal(PngBytes.Length, result.Data.Count);
    }
}
