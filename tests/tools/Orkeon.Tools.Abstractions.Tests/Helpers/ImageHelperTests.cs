using Orkeon.Tools.Abstractions.Helpers;

namespace Orkeon.Tools.Abstractions.Tests.Helpers;

public class ImageHelperTests
{
    [Fact]
    public void ShouldReturnPng_WhenDetectingMimeTypeWithPngHeader()
    {
        // Arrange - PNG magic bytes
        var data = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        // Act
        var result = ImageHelper.DetectMimeType(data);

        // Assert
        Assert.Equal("image/png", result);
    }

    [Fact]
    public void ShouldReturnJpeg_WhenDetectingMimeTypeWithJpegHeader()
    {
        // Arrange - JPEG magic bytes
        var data = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };

        // Act
        var result = ImageHelper.DetectMimeType(data);

        // Assert
        Assert.Equal("image/jpeg", result);
    }

    [Fact]
    public void ShouldReturnGif_WhenDetectingMimeTypeWithGifHeader()
    {
        // Arrange - GIF magic bytes (GIF8)
        var data = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 };

        // Act
        var result = ImageHelper.DetectMimeType(data);

        // Assert
        Assert.Equal("image/gif", result);
    }

    [Fact]
    public void ShouldReturnWebp_WhenDetectingMimeTypeWithWebpHeader()
    {
        // Arrange - WebP starts with RIFF
        var data = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 };

        // Act
        var result = ImageHelper.DetectMimeType(data);

        // Assert
        Assert.Equal("image/webp", result);
    }

    [Fact]
    public void ShouldReturnOctetStream_WhenDetectingMimeTypeWithUnknownHeader()
    {
        // Arrange - Random bytes
        var data = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 };

        // Act
        var result = ImageHelper.DetectMimeType(data);

        // Assert
        Assert.Equal("application/octet-stream", result);
    }

    [Fact]
    public void ShouldReturnOctetStream_WhenDataIsTooShort()
    {
        // Arrange - Less than 4 bytes
        var data = new byte[] { 0x89, 0x50 };

        // Act
        var result = ImageHelper.DetectMimeType(data);

        // Assert
        Assert.Equal("application/octet-stream", result);
    }

    [Fact]
    public void ShouldReturnOctetStream_WhenDataIsEmpty()
    {
        // Arrange
        var data = Array.Empty<byte>();

        // Act
        var result = ImageHelper.DetectMimeType(data);

        // Assert
        Assert.Equal("application/octet-stream", result);
    }

    [Fact]
    public void ShouldReturnPng_WhenPathHasPngExtension()
    {
        Assert.Equal("image/png", ImageHelper.DetectMimeTypeFromPath("photo.png"));
    }

    [Fact]
    public void ShouldReturnJpeg_WhenPathHasJpgExtension()
    {
        Assert.Equal("image/jpeg", ImageHelper.DetectMimeTypeFromPath("photo.jpg"));
    }

    [Fact]
    public void ShouldReturnJpeg_WhenPathHasJpegExtension()
    {
        Assert.Equal("image/jpeg", ImageHelper.DetectMimeTypeFromPath("photo.jpeg"));
    }

    [Fact]
    public void ShouldReturnGif_WhenPathHasGifExtension()
    {
        Assert.Equal("image/gif", ImageHelper.DetectMimeTypeFromPath("animation.gif"));
    }

    [Fact]
    public void ShouldReturnWebp_WhenPathHasWebpExtension()
    {
        Assert.Equal("image/webp", ImageHelper.DetectMimeTypeFromPath("image.webp"));
    }

    [Fact]
    public void ShouldReturnSvg_WhenPathHasSvgExtension()
    {
        Assert.Equal("image/svg+xml", ImageHelper.DetectMimeTypeFromPath("icon.svg"));
    }

    [Fact]
    public void ShouldReturnOctetStream_WhenPathHasUnknownExtension()
    {
        Assert.Equal("application/octet-stream", ImageHelper.DetectMimeTypeFromPath("file.xyz"));
    }

    [Fact]
    public void ShouldReturnOctetStream_WhenPathHasNoExtension()
    {
        Assert.Equal("application/octet-stream", ImageHelper.DetectMimeTypeFromPath("filename"));
    }

    [Fact]
    public void ShouldReturnMimeType_WhenPathHasUppercaseExtension()
    {
        Assert.Equal("image/png", ImageHelper.DetectMimeTypeFromPath("PHOTO.PNG"));
    }

    [Fact]
    public void ShouldDetectCorrectly_WhenPathContainsDirectories()
    {
        Assert.Equal("image/jpeg", ImageHelper.DetectMimeTypeFromPath("/path/to/photos/image.jpg"));
    }

    [Fact]
    public void ShouldReturnTrue_WhenMimeTypeIsPng()
    {
        Assert.True(ImageHelper.IsSupportedMimeType("image/png"));
    }

    [Fact]
    public void ShouldReturnTrue_WhenMimeTypeIsJpeg()
    {
        Assert.True(ImageHelper.IsSupportedMimeType("image/jpeg"));
    }

    [Fact]
    public void ShouldReturnTrue_WhenMimeTypeIsGif()
    {
        Assert.True(ImageHelper.IsSupportedMimeType("image/gif"));
    }

    [Fact]
    public void ShouldReturnTrue_WhenMimeTypeIsWebp()
    {
        Assert.True(ImageHelper.IsSupportedMimeType("image/webp"));
    }

    [Fact]
    public void ShouldReturnTrue_WhenMimeTypeIsSvg()
    {
        Assert.True(ImageHelper.IsSupportedMimeType("image/svg+xml"));
    }

    [Fact]
    public void ShouldReturnFalse_WhenMimeTypeIsInvalid()
    {
        Assert.False(ImageHelper.IsSupportedMimeType("application/pdf"));
    }

    [Fact]
    public void ShouldReturnFalse_WhenMimeTypeIsTextPlain()
    {
        Assert.False(ImageHelper.IsSupportedMimeType("text/plain"));
    }

    [Fact]
    public void ShouldReturnTrue_WhenMimeTypeIsCaseInsensitive()
    {
        Assert.True(ImageHelper.IsSupportedMimeType("IMAGE/PNG"));
    }
}
