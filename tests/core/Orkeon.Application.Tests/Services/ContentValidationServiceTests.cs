using Orkeon.Application.Validation;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;
using Microsoft.Extensions.Options;

namespace Orkeon.Application.Tests.Services;

public class ContentValidationServiceTests
{
    private static readonly string[] s_supportedImageFormats = ["image/png", "image/jpeg", "image/gif", "image/webp"];

    private static ContentValidationService CreateService(Action<MultiModalOptions>? configure = null)
    {
        var options = new MultiModalOptions();
        configure?.Invoke(options);
        return new ContentValidationService(Options.Create(options));
    }

    [Fact]
    public void ShouldReturnValid_WhenValidatingWithValidTextContent()
    {
        // Arrange
        var service = CreateService();
        var content = MultiModalContent.FromText("Hello world");

        // Act
        var result = service.Validate(content);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ShouldReturnError_WhenValidatingImageTooLarge()
    {
        // Arrange
        var service = CreateService(o => o.MaxImageSizeBytes = 100);
        var content = MultiModalContent.Empty()
            .AddImage(ImageContentPart.FromBytes(new byte[200], "image/png"));

        // Act
        var result = service.Validate(content);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("exceeds max size", result.Errors[0]);
    }

    [Fact]
    public void ShouldReturnValid_WhenValidatingImageWithinSizeLimit()
    {
        // Arrange
        var service = CreateService(o => o.MaxImageSizeBytes = 1000);
        var content = MultiModalContent.Empty()
            .AddImage(ImageContentPart.FromBytes(new byte[500], "image/png"));

        // Act
        var result = service.Validate(content);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ShouldReturnError_WhenValidatingUnsupportedImageFormat()
    {
        // Arrange
        var service = CreateService();
        var content = MultiModalContent.Empty()
            .AddImage(ImageContentPart.FromBytes(new byte[10], "image/bmp"));

        // Act
        var result = service.Validate(content);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("Unsupported image format", result.Errors[0]);
        Assert.Contains("image/bmp", result.Errors[0]);
    }

    [Fact]
    public void ShouldReturnValid_WhenValidatingSupportedImageFormats()
    {
        // Arrange
        var service = CreateService();

        foreach (var format in s_supportedImageFormats)
        {
            var content = MultiModalContent.Empty()
                .AddImage(ImageContentPart.FromBytes(new byte[10], format));

            // Act
            var result = service.Validate(content);

            // Assert
            Assert.True(result.IsValid, $"Format {format} should be valid");
        }
    }

    [Fact]
    public void ShouldReturnError_WhenValidatingAudioTooLong()
    {
        // Arrange
        var service = CreateService(o => o.MaxAudioDurationSeconds = 60);
        var audio = AudioContentPart.FromBytes(new byte[10], "audio/wav")
            with
        { Duration = TimeSpan.FromSeconds(120) };
        var content = MultiModalContent.Empty()
            .AddAudio(audio);

        // Act
        var result = service.Validate(content);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("exceeds max duration", result.Errors[0]);
    }

    [Fact]
    public void ShouldReturnValid_WhenValidatingAudioWithinDurationLimit()
    {
        // Arrange
        var service = CreateService(o => o.MaxAudioDurationSeconds = 300);
        var audio = AudioContentPart.FromBytes(new byte[10], "audio/wav")
            with
        { Duration = TimeSpan.FromSeconds(60) };
        var content = MultiModalContent.Empty()
            .AddAudio(audio);

        // Act
        var result = service.Validate(content);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ShouldReturnValid_WhenValidatingAudioWithNoDuration()
    {
        // Arrange
        var service = CreateService();
        var content = MultiModalContent.Empty()
            .AddAudio(AudioContentPart.FromBytes(new byte[10], "audio/wav"));

        // Act
        var result = service.Validate(content);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ShouldReturnError_WhenValidatingUnsupportedAudioFormat()
    {
        // Arrange
        var service = CreateService();
        var content = MultiModalContent.Empty()
            .AddAudio(AudioContentPart.FromBytes(new byte[10], "audio/flac"));

        // Act
        var result = service.Validate(content);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("Unsupported audio format", result.Errors[0]);
        Assert.Contains("audio/flac", result.Errors[0]);
    }

    [Fact]
    public void ShouldReturnValid_WhenValidatingWithValidMixedContent()
    {
        // Arrange
        var service = CreateService();
        var content = MultiModalContent.Empty()
            .AddText("Analyze this image")
            .AddImage(ImageContentPart.FromBytes(new byte[100], "image/png"))
            .AddAudio(AudioContentPart.FromBytes(new byte[50], "audio/wav"));

        // Act
        var result = service.Validate(content);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ShouldReturnValid_WhenValidatingWithEmptyContent()
    {
        // Arrange
        var service = CreateService();
        var content = MultiModalContent.Empty();

        // Act
        var result = service.Validate(content);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ShouldReturnAllErrors_WhenValidatingWithMultipleErrors()
    {
        // Arrange
        var service = CreateService(o =>
        {
            o.MaxImageSizeBytes = 50;
            o.MaxAudioDurationSeconds = 10;
        });
        var audio = AudioContentPart.FromBytes(new byte[10], "audio/wav")
            with
        { Duration = TimeSpan.FromSeconds(60) };
        var content = MultiModalContent.Empty()
            .AddImage(ImageContentPart.FromBytes(new byte[100], "image/png"))
            .AddAudio(audio);

        // Act
        var result = service.Validate(content);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void ShouldSkipsSizeCheck_WhenValidatingImageWithNullData()
    {
        // Arrange
        var service = CreateService(o => o.MaxImageSizeBytes = 10);
        var content = MultiModalContent.Empty()
            .AddImage(ImageContentPart.FromUri(new Uri("https://example.com/img.png"), "image/png"));

        // Act
        var result = service.Validate(content);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ShouldSkipsFormatCheck_WhenValidatingImageWithNullMimeType()
    {
        // Arrange
        var service = CreateService();
        var content = MultiModalContent.Empty()
            .AddImage(new ImageContentPart { Data = new byte[10] });

        // Act
        var result = service.Validate(content);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ShouldIsValid_WhenUsingContentValidationResultWithValid()
    {
        // Act
        var result = ContentValidationResult.Valid();

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
}
