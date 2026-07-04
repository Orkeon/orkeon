using Orkeon.Domain.SharedKernel.ValueObjects.Content;
using Microsoft.Extensions.AI;
using Orkeon.Infrastructure.LLMs.Converters;

namespace Orkeon.Infrastructure.Tests.LLMs.Converters;

public class ContentConverterTests
{
    [Fact]
    public void ShouldReturnTextContent_WhenToAIContentsTextOnly()
    {
        // Arrange
        var content = MultiModalContent.FromText("Hello world");

        // Act
        var result = ContentConverter.ToAIContents(content);

        // Assert
        Assert.Single(result);
        var textContent = Assert.IsType<TextContent>(result[0]);
        Assert.Equal("Hello world", textContent.Text);
    }

    [Fact]
    public void ShouldReturnMultipleTextContents_WhenToAIContentsMultipleTexts()
    {
        // Arrange
        var content = MultiModalContent.Empty()
            .AddText("First")
            .AddText("Second");

        // Act
        var result = ContentConverter.ToAIContents(content);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, item => Assert.IsType<TextContent>(item));
        Assert.Equal("First", ((TextContent)result[0]).Text);
        Assert.Equal("Second", ((TextContent)result[1]).Text);
    }

    [Fact]
    public void ShouldReturnUriContent_WhenToAIContentsImageUri()
    {
        // Arrange — real vision wiring (R3.9): URI images map to UriContent.
        var content = MultiModalContent.Empty()
            .AddImage(ImageContentPart.FromUri(new Uri("https://example.com/img.png"), "image/png"));

        // Act
        var result = ContentConverter.ToAIContents(content);

        // Assert
        Assert.Single(result);
        var uriContent = Assert.IsType<UriContent>(result[0]);
        Assert.Equal("image/png", uriContent.MediaType);
        Assert.Equal("https://example.com/img.png", uriContent.Uri.AbsoluteUri);
    }

    [Fact]
    public void ShouldReturnDataContent_WhenToAIContentsImageBytes()
    {
        // Arrange — real vision wiring (R3.9): raw image bytes map to DataContent.
        var data = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var content = MultiModalContent.Empty()
            .AddImage(ImageContentPart.FromBytes(data, "image/png"));

        // Act
        var result = ContentConverter.ToAIContents(content);

        // Assert
        Assert.Single(result);
        var dataContent = Assert.IsType<DataContent>(result[0]);
        Assert.Equal("image/png", dataContent.MediaType);
        Assert.Equal(data, dataContent.Data.ToArray());
    }

    [Fact]
    public void ShouldReturnUriContent_WhenToAIContentsImageWithAltText()
    {
        // Arrange — alt text is not transported by M.E.AI content types; the part
        // still maps to a real UriContent (no text degradation).
        var image = ImageContentPart.FromUri(new Uri("https://example.com/img.png"))
            with
        { AltText = "A sunset" };
        var content = MultiModalContent.Empty()
            .AddImage(image);

        // Act
        var result = ContentConverter.ToAIContents(content);

        // Assert
        var uriContent = Assert.IsType<UriContent>(result[0]);
        Assert.Equal("https://example.com/img.png", uriContent.Uri.AbsoluteUri);
    }

    [Fact]
    public void ShouldReturnUriContent_WhenToAIContentsAudioUri()
    {
        // Arrange
        var content = MultiModalContent.Empty()
            .AddAudio(AudioContentPart.FromUri(new Uri("https://example.com/audio.wav"), "audio/wav"));

        // Act
        var result = ContentConverter.ToAIContents(content);

        // Assert
        Assert.Single(result);
        var uriContent = Assert.IsType<UriContent>(result[0]);
        Assert.Equal("audio/wav", uriContent.MediaType);
        Assert.Equal("https://example.com/audio.wav", uriContent.Uri.AbsoluteUri);
    }

    [Fact]
    public void ShouldReturnDataContent_WhenToAIContentsAudioBytes()
    {
        // Arrange
        var audio = AudioContentPart.FromBytes([0x01, 0x02], "audio/wav");
        var content = MultiModalContent.Empty()
            .AddAudio(audio);

        // Act
        var result = ContentConverter.ToAIContents(content);

        // Assert
        var dataContent = Assert.IsType<DataContent>(result[0]);
        Assert.Equal("audio/wav", dataContent.MediaType);
        Assert.Equal(new byte[] { 0x01, 0x02 }, dataContent.Data.ToArray());
    }

    [Fact]
    public void ShouldReturnNamedDataContent_WhenToAIContentsFileContent()
    {
        // Arrange
        var content = MultiModalContent.Empty()
            .AddFile(new FileContentPart
            {
                FileName = "report.pdf",
                Data = [0x25, 0x50, 0x44, 0x46]
            });

        // Act
        var result = ContentConverter.ToAIContents(content);

        // Assert
        Assert.Single(result);
        var dataContent = Assert.IsType<DataContent>(result[0]);
        Assert.Equal("report.pdf", dataContent.Name);
        Assert.Equal(4, dataContent.Data.Length);
    }

    [Fact]
    public void ShouldReturnAllTypes_WhenToAIContentsMixed()
    {
        // Arrange
        var content = MultiModalContent.Empty()
            .AddText("Analyze this:")
            .AddImage(ImageContentPart.FromUri(new Uri("https://example.com/img.png")))
            .AddAudio(AudioContentPart.FromBytes([0x01]));

        // Act
        var result = ContentConverter.ToAIContents(content);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("Analyze this:", Assert.IsType<TextContent>(result[0]).Text);
        Assert.IsType<UriContent>(result[1]);
        Assert.IsType<DataContent>(result[2]);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenToAIContentsImageWithoutDataOrUri()
    {
        // Arrange — empty image part: nothing to send.
        var content = MultiModalContent.Empty().AddImage(new ImageContentPart());

        // Act + Assert
        Assert.Throws<ArgumentException>(() => ContentConverter.ToAIContents(content));
    }

    [Fact]
    public void ShouldReturnEmptyList_WhenToAIContentsEmptyContent()
    {
        // Arrange
        var content = MultiModalContent.Empty();

        // Act
        var result = ContentConverter.ToAIContents(content);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ShouldReturnTextPart_WhenFromAIContentsTextContent()
    {
        // Arrange
        var aiContents = new AIContent[] { new TextContent("Hello") };

        // Act
        var result = ContentConverter.FromAIContents(aiContents);

        // Assert
        Assert.Single(result.Parts);
        var part = Assert.IsType<TextContentPart>(result.Parts[0]);
        Assert.Equal("Hello", part.Text);
    }

    [Fact]
    public void ShouldReturnAllTextParts_WhenFromAIContentsMultipleTextContents()
    {
        // Arrange
        var aiContents = new AIContent[]
        {
            new TextContent("First"),
            new TextContent("Second"),
            new TextContent("Third")
        };

        // Act
        var result = ContentConverter.FromAIContents(aiContents);

        // Assert
        Assert.Equal(3, result.Parts.Count);
        Assert.Equal("First", ((TextContentPart)result.Parts[0]).Text);
        Assert.Equal("Second", ((TextContentPart)result.Parts[1]).Text);
        Assert.Equal("Third", ((TextContentPart)result.Parts[2]).Text);
    }

    [Fact]
    public void ShouldReturnImagePart_WhenFromAIContentsImageDataContent()
    {
        // Arrange
        var bytes = new byte[] { 0x89, 0x50 };
        var aiContents = new AIContent[] { new DataContent(bytes, "image/png") };

        // Act
        var result = ContentConverter.FromAIContents(aiContents);

        // Assert
        var part = Assert.IsType<ImageContentPart>(result.Parts[0]);
        Assert.Equal("image/png", part.MimeType);
        Assert.Equal(bytes, part.Data);
    }

    [Fact]
    public void ShouldReturnImagePart_WhenFromAIContentsImageUriContent()
    {
        // Arrange
        var aiContents = new AIContent[]
        {
            new UriContent(new Uri("https://example.com/img.jpg"), "image/jpeg")
        };

        // Act
        var result = ContentConverter.FromAIContents(aiContents);

        // Assert
        var part = Assert.IsType<ImageContentPart>(result.Parts[0]);
        Assert.Equal("image/jpeg", part.MimeType);
        Assert.Equal("https://example.com/img.jpg", part.Uri!.AbsoluteUri);
    }

    [Fact]
    public void ShouldReturnAudioPart_WhenFromAIContentsAudioDataContent()
    {
        // Arrange
        var aiContents = new AIContent[] { new DataContent(new byte[] { 0x01 }, "audio/wav") };

        // Act
        var result = ContentConverter.FromAIContents(aiContents);

        // Assert
        var part = Assert.IsType<AudioContentPart>(result.Parts[0]);
        Assert.Equal("audio/wav", part.MimeType);
    }

    [Fact]
    public void ShouldReturnFilePart_WhenFromAIContentsBinaryDataContent()
    {
        // Arrange
        var aiContents = new AIContent[]
        {
            new DataContent(new byte[] { 0x25 }, "application/pdf") { Name = "report.pdf" }
        };

        // Act
        var result = ContentConverter.FromAIContents(aiContents);

        // Assert
        var part = Assert.IsType<FileContentPart>(result.Parts[0]);
        Assert.Equal("report.pdf", part.FileName);
        Assert.Equal("application/pdf", part.MimeType);
    }

    [Fact]
    public void ShouldReturnEmptyContent_WhenFromAIContentsEmptyEnumerable()
    {
        // Arrange
        var aiContents = Array.Empty<AIContent>();

        // Act
        var result = ContentConverter.FromAIContents(aiContents);

        // Assert
        Assert.Empty(result.Parts);
    }

    [Fact]
    public void ShouldTextContentPreserved_WhenRoundTrip()
    {
        // Arrange
        var original = MultiModalContent.FromText("Hello world");

        // Act
        var aiContents = ContentConverter.ToAIContents(original);
        var roundTripped = ContentConverter.FromAIContents(aiContents);

        // Assert
        Assert.Equal(original.ToTextOnly(), roundTripped.ToTextOnly());
    }

    [Fact]
    public void ShouldImagePreserved_WhenRoundTripWithImageBytes()
    {
        // Arrange
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var original = MultiModalContent.Empty()
            .AddText("Look:")
            .AddImage(ImageContentPart.FromBytes(bytes, "image/png"));

        // Act
        var roundTripped = ContentConverter.FromAIContents(ContentConverter.ToAIContents(original));

        // Assert
        Assert.Equal(2, roundTripped.Parts.Count);
        var image = Assert.IsType<ImageContentPart>(roundTripped.Parts[1]);
        Assert.Equal(bytes, image.Data);
        Assert.Equal("image/png", image.MimeType);
    }

    [Fact]
    public void ShouldMapRoleSystem_WhenToChatMessage()
    {
        // Arrange
        var content = MultiModalContent.FromText("System prompt");

        // Act
        var message = ContentConverter.ToChatMessage("system", content);

        // Assert
        Assert.Equal(ChatRole.System, message.Role);
        Assert.Single(message.Contents);
        var textContent = Assert.IsType<TextContent>(message.Contents[0]);
        Assert.Equal("System prompt", textContent.Text);
    }

    [Fact]
    public void ShouldMapRoleUser_WhenToChatMessage()
    {
        // Arrange
        var content = MultiModalContent.FromText("User message");

        // Act
        var message = ContentConverter.ToChatMessage("user", content);

        // Assert
        Assert.Equal(ChatRole.User, message.Role);
    }

    [Fact]
    public void ShouldMapRoleAssistant_WhenToChatMessage()
    {
        // Arrange
        var content = MultiModalContent.FromText("Assistant response");

        // Act
        var message = ContentConverter.ToChatMessage("assistant", content);

        // Assert
        Assert.Equal(ChatRole.Assistant, message.Role);
    }

    [Fact]
    public void ShouldDefaultToUser_WhenToChatMessageUnknownRole()
    {
        // Arrange
        var content = MultiModalContent.FromText("Unknown role message");

        // Act
        var message = ContentConverter.ToChatMessage("unknown", content);

        // Assert
        Assert.Equal(ChatRole.User, message.Role);
    }

    [Fact]
    public void ShouldExtractRoleAndContent_WhenFromChatMessage()
    {
        // Arrange
        var message = new ChatMessage(ChatRole.Assistant, "Hello from assistant");

        // Act
        var (role, content) = ContentConverter.FromChatMessage(message);

        // Assert
        Assert.Equal("assistant", role);
        Assert.Single(content.Parts);
        Assert.Equal("Hello from assistant", content.ToTextOnly());
    }

    [Fact]
    public void ShouldExtractCorrectly_WhenFromChatMessageSystemRole()
    {
        // Arrange
        var message = new ChatMessage(ChatRole.System, "System instructions");

        // Act
        var (role, content) = ContentConverter.FromChatMessage(message);

        // Assert
        Assert.Equal("system", role);
        Assert.Equal("System instructions", content.ToTextOnly());
    }

    [Fact]
    public void ShouldToChatMessageFromChatMessagePreserved_WhenRoundTrip()
    {
        // Arrange
        var originalContent = MultiModalContent.FromText("Round trip test");

        // Act
        var message = ContentConverter.ToChatMessage("user", originalContent);
        var (role, content) = ContentConverter.FromChatMessage(message);

        // Assert
        Assert.Equal("user", role);
        Assert.Equal("Round trip test", content.ToTextOnly());
    }
}
