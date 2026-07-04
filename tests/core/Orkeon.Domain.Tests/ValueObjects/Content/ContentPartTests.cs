using Orkeon.Domain.SharedKernel.ValueObjects.Content;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.ValueObjects.Content;

public class ContentPartTests
{
    [Fact]
    public void ShouldSetTypeAndText_WhenUsingTextContentPartCreation()
    {
        // Arrange & Act
        var part = new TextContentPart("Hello world");

        // Assert
        Assert.Equal(ContentType.Text, part.Type);
        Assert.Equal("Hello world", part.Text);
        Assert.Null(part.MimeType);
    }

    [Fact]
    public void ShouldSetTypeToText_WhenUsingTextContentPartWithDefaultConstructor()
    {
        // Arrange & Act
        var part = new TextContentPart();

        // Assert
        Assert.Equal(ContentType.Text, part.Type);
        Assert.Equal(string.Empty, part.Text);
    }

    [Fact]
    public void ShouldSetUriAndMimeType_WhenUsingImageContentPartFromUri()
    {
        // Arrange
        var uri = new Uri("https://example.com/image.png");

        // Act
        var part = ImageContentPart.FromUri(uri, "image/png");

        // Assert
        Assert.Equal(ContentType.Image, part.Type);
        Assert.Equal(uri, part.Uri);
        Assert.Equal("image/png", part.MimeType);
        Assert.Null(part.Data);
    }

    [Fact]
    public void ShouldDefaultsMimeTypeToPng_WhenUsingImageContentPartFromUri()
    {
        // Arrange
        var uri = new Uri("https://example.com/image.png");

        // Act
        var part = ImageContentPart.FromUri(uri);

        // Assert
        Assert.Equal("image/png", part.MimeType);
    }

    [Fact]
    public void ShouldDecodeData_WhenUsingImageContentPartFromBase64()
    {
        // Arrange
        var originalBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var base64 = Convert.ToBase64String(originalBytes);

        // Act
        var part = ImageContentPart.FromBase64(base64, "image/png");

        // Assert
        Assert.Equal(ContentType.Image, part.Type);
        Assert.Equal(originalBytes, part.Data);
        Assert.Equal("image/png", part.MimeType);
        Assert.Null(part.Uri);
    }

    [Fact]
    public void ShouldSetDataAndMimeType_WhenUsingImageContentPartFromBytes()
    {
        // Arrange
        var data = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };

        // Act
        var part = ImageContentPart.FromBytes(data, "image/jpeg");

        // Assert
        Assert.Equal(ContentType.Image, part.Type);
        Assert.Equal(data, part.Data);
        Assert.Equal("image/jpeg", part.MimeType);
    }

    [Fact]
    public void ShouldDefaultsMimeTypeToPng_WhenUsingImageContentPartFromBytes()
    {
        // Arrange
        var data = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        // Act
        var part = ImageContentPart.FromBytes(data);

        // Assert
        Assert.Equal("image/png", part.MimeType);
    }

    [Fact]
    public void ShouldSetUriAndMimeType_WhenUsingAudioContentPartFromUri()
    {
        // Arrange
        var uri = new Uri("https://example.com/audio.wav");

        // Act
        var part = AudioContentPart.FromUri(uri, "audio/wav");

        // Assert
        Assert.Equal(ContentType.Audio, part.Type);
        Assert.Equal(uri, part.Uri);
        Assert.Equal("audio/wav", part.MimeType);
        Assert.Null(part.Data);
    }

    [Fact]
    public void ShouldDefaultsMimeTypeToWav_WhenUsingAudioContentPartFromUri()
    {
        // Arrange
        var uri = new Uri("https://example.com/audio.wav");

        // Act
        var part = AudioContentPart.FromUri(uri);

        // Assert
        Assert.Equal("audio/wav", part.MimeType);
    }

    [Fact]
    public void ShouldSetDataAndMimeType_WhenUsingAudioContentPartFromBytes()
    {
        // Arrange
        var data = new byte[] { 0x52, 0x49, 0x46, 0x46 };

        // Act
        var part = AudioContentPart.FromBytes(data, "audio/mp3");

        // Assert
        Assert.Equal(ContentType.Audio, part.Type);
        Assert.Equal(data, part.Data);
        Assert.Equal("audio/mp3", part.MimeType);
    }

    [Fact]
    public void ShouldDefaultsMimeTypeToWav_WhenUsingAudioContentPartFromBytes()
    {
        // Arrange
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        // Act
        var part = AudioContentPart.FromBytes(data);

        // Assert
        Assert.Equal("audio/wav", part.MimeType);
    }

    [Fact]
    public void ShouldCanBeSet_WhenUsingAudioContentPartDuration()
    {
        // Arrange & Act
        var part = AudioContentPart.FromBytes([0x01], "audio/wav")
            with
        { Duration = TimeoutQuick };

        // Assert
        Assert.Equal(TimeoutQuick, part.Duration);
    }

    [Fact]
    public void ShouldSetType_WhenUsingFileContentPartCreation()
    {
        // Arrange & Act
        var part = new FileContentPart
        {
            FileName = "report.pdf",
            Data = [0x25, 0x50, 0x44, 0x46]
        };

        // Assert
        Assert.Equal(ContentType.File, part.Type);
        Assert.Equal("report.pdf", part.FileName);
        Assert.NotNull(part.Data);
        Assert.Equal(4, part.Data.Count);
    }

    [Fact]
    public void ShouldSetUri_WhenUsingFileContentPartWithUri()
    {
        // Arrange & Act
        var uri = new Uri("https://example.com/file.pdf");
        var part = new FileContentPart
        {
            FileName = "file.pdf",
            Uri = uri
        };

        // Assert
        Assert.Equal(ContentType.File, part.Type);
        Assert.Equal(uri, part.Uri);
        Assert.Null(part.Data);
    }

    [Fact]
    public void ShouldCanBeSet_WhenUsingImageContentPartAltText()
    {
        // Arrange & Act
        var part = ImageContentPart.FromUri(new Uri("https://example.com/img.png"))
            with
        { AltText = "A beautiful sunset" };

        // Assert
        Assert.Equal("A beautiful sunset", part.AltText);
    }

    [Fact]
    public void ShouldSupportValueEquality_WhenUsingContentPartRecords()
    {
        // Arrange
        var part1 = new TextContentPart("Hello");
        var part2 = new TextContentPart("Hello");
        var part3 = new TextContentPart("World");

        // Assert
        Assert.Equal(part1, part2);
        Assert.NotEqual(part1, part3);
    }
}
