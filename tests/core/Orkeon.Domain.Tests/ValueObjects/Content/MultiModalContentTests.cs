using Orkeon.Domain.SharedKernel.ValueObjects.Content;

namespace Orkeon.Domain.Tests.ValueObjects.Content;

public class MultiModalContentTests
{
    [Fact]
    public void ShouldCreateTextContent_WhenConstructingUsingString()
    {
        // Act
        var content = MultiModalContent.FromText("Hello world");

        // Assert
        Assert.Single(content.Parts);
        var part = Assert.IsType<TextContentPart>(content.Parts[0]);
        Assert.Equal("Hello world", part.Text);
    }

    [Fact]
    public void ShouldCreateEmptyContent_WhenConstructingWithDefault()
    {
        // Act
        var content = MultiModalContent.Empty();

        // Assert
        Assert.Empty(content.Parts);
    }

    [Fact]
    public void ShouldAddTextPart_WhenAddingText()
    {
        // Arrange
        var content = MultiModalContent.Empty();

        // Act
        var result = content.AddText("Hello");

        // Assert
        Assert.Single(result.Parts);
        var part = Assert.IsType<TextContentPart>(result.Parts[0]);
        Assert.Equal("Hello", part.Text);
        Assert.Empty(content.Parts); // original unchanged
    }

    [Fact]
    public void ShouldAddImagePart_WhenAddingImage()
    {
        // Arrange
        var content = MultiModalContent.Empty();
        var image = ImageContentPart.FromUri(new Uri("https://example.com/img.png"));

        // Act
        var result = content.AddImage(image);

        // Assert
        Assert.Single(result.Parts);
        var part = Assert.IsType<ImageContentPart>(result.Parts[0]);
        Assert.Equal(new Uri("https://example.com/img.png"), part.Uri);
        Assert.Empty(content.Parts); // original unchanged
    }

    [Fact]
    public void ShouldAddAudioPart_WhenAddingAudio()
    {
        // Arrange
        var content = MultiModalContent.Empty();
        var audio = AudioContentPart.FromUri(new Uri("https://example.com/audio.wav"));

        // Act
        var result = content.AddAudio(audio);

        // Assert
        Assert.Single(result.Parts);
        var part = Assert.IsType<AudioContentPart>(result.Parts[0]);
        Assert.Equal(new Uri("https://example.com/audio.wav"), part.Uri);
        Assert.Empty(content.Parts); // original unchanged
    }

    [Fact]
    public void ShouldAddFilePart_WhenAddingFile()
    {
        // Arrange
        var content = MultiModalContent.Empty();
        var file = new FileContentPart { FileName = "report.pdf" };

        // Act
        var result = content.AddFile(file);

        // Assert
        Assert.Single(result.Parts);
        var part = Assert.IsType<FileContentPart>(result.Parts[0]);
        Assert.Equal("report.pdf", part.FileName);
        Assert.Empty(content.Parts); // original unchanged
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingHasTextWhenTextPresent()
    {
        // Arrange
        var content = MultiModalContent.FromText("Some text");

        // Act & Assert
        Assert.True(content.HasText);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingHasTextWhenNoText()
    {
        // Arrange
        var content = MultiModalContent.Empty()
            .AddImage(ImageContentPart.FromUri(new Uri("https://example.com/img.png")));

        // Act & Assert
        Assert.False(content.HasText);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingHasImagesWhenImagePresent()
    {
        // Arrange
        var content = MultiModalContent.Empty()
            .AddImage(ImageContentPart.FromUri(new Uri("https://example.com/img.png")));

        // Act & Assert
        Assert.True(content.HasImages);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingHasImagesWhenNoImage()
    {
        // Arrange
        var content = MultiModalContent.FromText("Just text");

        // Act & Assert
        Assert.False(content.HasImages);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingHasAudioWhenAudioPresent()
    {
        // Arrange
        var content = MultiModalContent.Empty()
            .AddAudio(AudioContentPart.FromUri(new Uri("https://example.com/audio.wav")));

        // Act & Assert
        Assert.True(content.HasAudio);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingHasAudioWhenNoAudio()
    {
        // Arrange
        var content = MultiModalContent.FromText("Text only");

        // Act & Assert
        Assert.False(content.HasAudio);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingIsTextOnlyWhenOnlyText()
    {
        // Arrange
        var content = MultiModalContent.FromText("Hello")
            .AddText("World");

        // Act & Assert
        Assert.True(content.IsTextOnly);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingIsTextOnlyWhenMixed()
    {
        // Arrange
        var content = MultiModalContent.FromText("Hello")
            .AddImage(ImageContentPart.FromUri(new Uri("https://example.com/img.png")));

        // Act & Assert
        Assert.False(content.IsTextOnly);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingIsTextOnlyWhenEmpty()
    {
        // Arrange
        var content = MultiModalContent.Empty();

        // Act & Assert
        Assert.False(content.IsTextOnly);
    }

    [Fact]
    public void ShouldCreateTextContent_WhenUsingImplicitConversionFromString()
    {
        // Act
        MultiModalContent content = "Hello world";

        // Assert
        Assert.Single(content.Parts);
        var part = Assert.IsType<TextContentPart>(content.Parts[0]);
        Assert.Equal("Hello world", part.Text);
    }

    [Fact]
    public void ShouldExtractTextParts_WhenUsingToTextOnly()
    {
        // Arrange
        var content = MultiModalContent.Empty()
            .AddText("First line")
            .AddImage(ImageContentPart.FromUri(new Uri("https://example.com/img.png")))
            .AddText("Second line");

        // Act
        var text = content.ToTextOnly();

        // Assert
        Assert.Equal("First line\nSecond line", text);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingToTextOnlyWhenNoText()
    {
        // Arrange
        var content = MultiModalContent.Empty()
            .AddImage(ImageContentPart.FromUri(new Uri("https://example.com/img.png")));

        // Act
        var text = content.ToTextOnly();

        // Assert
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void ShouldReturnTextOnly_WhenCallingToString()
    {
        // Arrange
        var content = MultiModalContent.FromText("Hello world");

        // Act
        var result = content.ToString();

        // Assert
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void ShouldWork_WhenUsingBuilderPatternUsingChaining()
    {
        // Act
        var content = MultiModalContent.Empty()
            .AddText("Analyze this image:")
            .AddImage(ImageContentPart.FromUri(new Uri("https://example.com/chart.png")))
            .AddText("And this audio:")
            .AddAudio(AudioContentPart.FromUri(new Uri("https://example.com/meeting.wav")));

        // Assert
        Assert.Equal(4, content.Parts.Count);
        Assert.IsType<TextContentPart>(content.Parts[0]);
        Assert.IsType<ImageContentPart>(content.Parts[1]);
        Assert.IsType<TextContentPart>(content.Parts[2]);
        Assert.IsType<AudioContentPart>(content.Parts[3]);
    }

    [Fact]
    public void ShouldNotMutateOriginal_WhenAddingParts()
    {
        // Arrange
        var content = MultiModalContent.FromText("Hello");

        // Act
        var withText = content.AddText("World");
        var withImage = content.AddImage(ImageContentPart.FromUri(new Uri("https://example.com/img.png")));

        // Assert - original unchanged
        Assert.Single(content.Parts);
        Assert.Equal(2, withText.Parts.Count);
        Assert.Equal(2, withImage.Parts.Count);
    }

    [Fact]
    public void ShouldPreserveOrder_WhenAddingMultipleTexts()
    {
        // Arrange & Act
        var content = MultiModalContent.Empty()
            .AddText("First")
            .AddText("Second")
            .AddText("Third");

        // Assert
        Assert.Equal(3, content.Parts.Count);
        Assert.Equal("First", ((TextContentPart)content.Parts[0]).Text);
        Assert.Equal("Second", ((TextContentPart)content.Parts[1]).Text);
        Assert.Equal("Third", ((TextContentPart)content.Parts[2]).Text);
    }

    [Fact]
    public void ShouldSupportValueEquality()
    {
        // Arrange
        var content1 = MultiModalContent.FromText("Hello").AddText("World");
        var content2 = MultiModalContent.FromText("Hello").AddText("World");
        var content3 = MultiModalContent.FromText("Hello").AddText("Different");

        // Assert
        Assert.Equal(content1, content2);
        Assert.NotEqual(content1, content3);
        Assert.Equal(content1.GetHashCode(), content2.GetHashCode());
    }
}
