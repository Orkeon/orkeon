using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;

namespace Orkeon.Domain.Tests.ValueObjects;

/// <summary>
/// R3.9 — <see cref="LlmMessage.MultiModalContent"/>: structured multi-modal content
/// flows through chat messages with a text fallback for non-vision providers.
/// </summary>
public class LlmMessageMultiModalTests
{
    [Fact]
    public void ShouldCarryMultiModalContentAndTextFallback_WhenUserWithMultiModalContent()
    {
        // Arrange
        var content = MultiModalContent.Empty()
            .AddText("Describe this")
            .AddImage(ImageContentPart.FromBytes([0x89, 0x50], "image/png"));

        // Act
        var message = LlmMessage.User(content);

        // Assert
        Assert.Equal("user", message.Role);
        Assert.Same(content, message.MultiModalContent);
        Assert.Equal("Describe this", message.Content);
    }

    [Fact]
    public void ShouldConcatenateTextParts_WhenUserWithMultipleTextParts()
    {
        // Arrange
        var content = MultiModalContent.Empty()
            .AddText("First")
            .AddText("Second");

        // Act
        var message = LlmMessage.User(content);

        // Assert — Content is the text-only fallback (ToTextOnly join).
        Assert.Equal("First\nSecond", message.Content);
    }

    [Fact]
    public void ShouldThrowArgumentNull_WhenUserWithNullMultiModalContent()
    {
        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => LlmMessage.User((MultiModalContent)null!));
    }

    [Fact]
    public void ShouldLeaveMultiModalContentNull_WhenUserWithPlainText()
    {
        // Act
        var message = LlmMessage.User("hello");

        // Assert — text messages stay unchanged (no structured content).
        Assert.Null(message.MultiModalContent);
        Assert.Equal("hello", message.Content);
    }

    [Fact]
    public void ShouldEmptyContent_WhenUserWithImageOnlyContent()
    {
        // Arrange
        var content = MultiModalContent.Empty()
            .AddImage(ImageContentPart.FromUri(new Uri("https://example.com/a.png")));

        // Act
        var message = LlmMessage.User(content);

        // Assert — no text parts: the fallback is empty but the image is carried.
        Assert.Equal(string.Empty, message.Content);
        Assert.NotNull(message.MultiModalContent);
        Assert.True(message.MultiModalContent!.HasImages);
    }
}
