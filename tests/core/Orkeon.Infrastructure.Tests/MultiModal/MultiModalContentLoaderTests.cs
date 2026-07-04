using Microsoft.Extensions.Options;
using Orkeon.Application.Validation;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.MultiModal;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.MultiModal;

/// <summary>
/// R3.9 — <see cref="MultiModalContentLoader"/>: loads image files through the virtual
/// file system, infers the media type, and validates against <see cref="MultiModalOptions"/>.
/// </summary>
public class MultiModalContentLoaderTests
{
    private static readonly byte[] s_pngBytes = [0x89, 0x50, 0x4E, 0x47];

    private static FakeFileSystemService NewFileSystem() =>
        new FakeFileSystemService()
            .AddMount("/workspace", FileAccessRights.ReadOnly)
            .AddFile("/workspace/chart.png", s_pngBytes)
            .AddFile("/workspace/photo.jpeg", [0xFF, 0xD8, 0xFF, 0xE0])
            .AddFile("/workspace/image.bmp", [0x42, 0x4D]);

    private static MultiModalContentLoader NewLoader(
        FakeFileSystemService? fileSystem = null,
        MultiModalOptions? options = null)
    {
        var effectiveOptions = Options.Create(options ?? new MultiModalOptions());
        return new MultiModalContentLoader(
            fileSystem ?? NewFileSystem(),
            new ContentValidationService(effectiveOptions),
            effectiveOptions);
    }

    [Fact]
    public async Task ShouldLoadImageWithBytesAndMediaType_WhenLoadImageAsyncWithPngFile()
    {
        // Arrange
        var loader = NewLoader();

        // Act
        var image = await loader.LoadImageAsync("/workspace/chart.png", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(s_pngBytes, image.Data);
        Assert.Equal("image/png", image.MimeType);
    }

    [Fact]
    public async Task ShouldInferJpegMediaType_WhenLoadImageAsyncWithJpegFile()
    {
        // Arrange
        var loader = NewLoader();

        // Act
        var image = await loader.LoadImageAsync("/workspace/photo.jpeg", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("image/jpeg", image.MimeType);
    }

    [Fact]
    public async Task ShouldThrowFileNotFound_WhenLoadImageAsyncWithMissingFile()
    {
        // Arrange
        var loader = NewLoader();

        // Act + Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => loader.LoadImageAsync("/workspace/missing.png", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowNotSupported_WhenLoadImageAsyncWithUnsupportedExtension()
    {
        // Arrange
        var loader = NewLoader();

        // Act + Assert
        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => loader.LoadImageAsync("/workspace/image.bmp", TestContext.Current.CancellationToken));
        Assert.Contains(".bmp", ex.Message);
    }

    [Fact]
    public async Task ShouldThrowInvalidOperation_WhenLoadImageAsyncWithDisabledOptions()
    {
        // Arrange
        var loader = NewLoader(options: new MultiModalOptions { Enabled = false });

        // Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => loader.LoadImageAsync("/workspace/chart.png", TestContext.Current.CancellationToken));
        Assert.Contains("disabled", ex.Message);
    }

    [Fact]
    public async Task ShouldThrowInvalidOperation_WhenLoadImageAsyncWithOversizedImage()
    {
        // Arrange — the 4-byte png exceeds a 2-byte limit.
        var loader = NewLoader(options: new MultiModalOptions { MaxImageSizeBytes = 2 });

        // Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => loader.LoadImageAsync("/workspace/chart.png", TestContext.Current.CancellationToken));
        Assert.Contains("max size", ex.Message);
    }

    [Fact]
    public async Task ShouldThrowInvalidOperation_WhenLoadImageAsyncWithFormatNotAllowedByOptions()
    {
        // Arrange — host restricts the allowed formats to jpeg only.
        var loader = NewLoader(options: new MultiModalOptions
        {
            SupportedImageFormats = ["image/jpeg"]
        });

        // Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => loader.LoadImageAsync("/workspace/chart.png", TestContext.Current.CancellationToken));
        Assert.Contains("image/png", ex.Message);
    }

    [Fact]
    public async Task ShouldThrowArgumentException_WhenLoadImageAsyncWithEmptyPath()
    {
        // Arrange
        var loader = NewLoader();

        // Act + Assert
        await Assert.ThrowsAsync<ArgumentException>(() => loader.LoadImageAsync(" ", TestContext.Current.CancellationToken));
    }
}
