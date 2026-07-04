using System.Text.Json;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;
using Orkeon.Infrastructure.LLMs.Converters;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.LLMs.Converters;

/// <summary>
/// R3.9 — real vision wiring: provider payload conversions (Anthropic content blocks,
/// OpenAI content parts) and image loading from the virtual file system.
/// </summary>
public class ContentConverterPayloadTests
{
    /// <summary>Mirrors the snake_case serialization options used by the LLM providers.</summary>
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static JsonDocument SerializeToJson(IReadOnlyList<object> payload) =>
        JsonDocument.Parse(JsonSerializer.Serialize(payload, s_jsonOptions));

    // ─────────────────────────────────────────────────────────────
    //  ToAnthropicContentBlocks
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ShouldEmitBase64ImageBlock_WhenToAnthropicContentBlocksWithImageBytes()
    {
        // Arrange
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var content = MultiModalContent.Empty()
            .AddText("Describe this image")
            .AddImage(ImageContentPart.FromBytes(bytes, "image/png"));

        // Act
        var blocks = ContentConverter.ToAnthropicContentBlocks(content);

        // Assert — Anthropic Messages API shape: {"type":"image","source":{"type":"base64",...}}
        using var doc = SerializeToJson(blocks);
        var root = doc.RootElement;
        Assert.Equal(2, root.GetArrayLength());

        Assert.Equal("text", root[0].GetProperty("type").GetString());
        Assert.Equal("Describe this image", root[0].GetProperty("text").GetString());

        Assert.Equal("image", root[1].GetProperty("type").GetString());
        var source = root[1].GetProperty("source");
        Assert.Equal("base64", source.GetProperty("type").GetString());
        Assert.Equal("image/png", source.GetProperty("media_type").GetString());
        Assert.Equal(Convert.ToBase64String(bytes), source.GetProperty("data").GetString());
    }

    [Fact]
    public void ShouldEmitUrlImageBlock_WhenToAnthropicContentBlocksWithHttpsUri()
    {
        // Arrange
        var content = MultiModalContent.Empty()
            .AddImage(ImageContentPart.FromUri(new Uri("https://example.com/img.jpg"), "image/jpeg"));

        // Act
        var blocks = ContentConverter.ToAnthropicContentBlocks(content);

        // Assert — URL source: {"type":"image","source":{"type":"url","url":...}}
        using var doc = SerializeToJson(blocks);
        var source = doc.RootElement[0].GetProperty("source");
        Assert.Equal("url", source.GetProperty("type").GetString());
        Assert.Equal("https://example.com/img.jpg", source.GetProperty("url").GetString());
    }

    [Fact]
    public void ShouldEmitBase64ImageBlock_WhenToAnthropicContentBlocksWithDataUrl()
    {
        // Arrange — a base64 data: URL is unpacked into a base64 source block.
        var base64 = Convert.ToBase64String(new byte[] { 0x01, 0x02, 0x03 });
        var content = MultiModalContent.Empty()
            .AddImage(ImageContentPart.FromUri(new Uri($"data:image/webp;base64,{base64}")));

        // Act
        var blocks = ContentConverter.ToAnthropicContentBlocks(content);

        // Assert
        using var doc = SerializeToJson(blocks);
        var source = doc.RootElement[0].GetProperty("source");
        Assert.Equal("base64", source.GetProperty("type").GetString());
        Assert.Equal("image/webp", source.GetProperty("media_type").GetString());
        Assert.Equal(base64, source.GetProperty("data").GetString());
    }

    [Fact]
    public void ShouldThrowNotSupported_WhenToAnthropicContentBlocksWithAudioPart()
    {
        // Arrange — clear error instead of silent text degradation.
        var content = MultiModalContent.Empty()
            .AddAudio(AudioContentPart.FromBytes([0x01], "audio/wav"));

        // Act + Assert
        var ex = Assert.Throws<NotSupportedException>(
            () => ContentConverter.ToAnthropicContentBlocks(content));
        Assert.Contains("Audio", ex.Message);
        Assert.Contains("Anthropic", ex.Message);
    }

    [Fact]
    public void ShouldThrowNotSupported_WhenToAnthropicContentBlocksWithUnsupportedMediaType()
    {
        // Arrange — image/bmp is not accepted by the vision APIs.
        var content = MultiModalContent.Empty()
            .AddImage(ImageContentPart.FromBytes([0x42, 0x4D], "image/bmp"));

        // Act + Assert
        var ex = Assert.Throws<NotSupportedException>(
            () => ContentConverter.ToAnthropicContentBlocks(content));
        Assert.Contains("image/bmp", ex.Message);
        Assert.Contains("image/png", ex.Message);
    }

    [Fact]
    public void ShouldThrowNotSupported_WhenToAnthropicContentBlocksWithUnsupportedUriScheme()
    {
        // Arrange — ftp:// is neither http(s) nor data:.
        var content = MultiModalContent.Empty()
            .AddImage(ImageContentPart.FromUri(new Uri("ftp://example.com/img.png")));

        // Act + Assert
        var ex = Assert.Throws<NotSupportedException>(
            () => ContentConverter.ToAnthropicContentBlocks(content));
        Assert.Contains("ftp", ex.Message);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenToAnthropicContentBlocksWithEmptyImagePart()
    {
        // Arrange — image part with neither Data nor Uri.
        var content = MultiModalContent.Empty().AddImage(new ImageContentPart());

        // Act + Assert
        Assert.Throws<ArgumentException>(
            () => ContentConverter.ToAnthropicContentBlocks(content));
    }

    // ─────────────────────────────────────────────────────────────
    //  ToOpenAIContentParts
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ShouldEmitDataUrlImagePart_WhenToOpenAIContentPartsWithImageBytes()
    {
        // Arrange
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF };
        var content = MultiModalContent.Empty()
            .AddText("What is shown here?")
            .AddImage(ImageContentPart.FromBytes(bytes, "image/jpeg"));

        // Act
        var parts = ContentConverter.ToOpenAIContentParts(content);

        // Assert — Chat Completions shape: {"type":"image_url","image_url":{"url":"data:..."}}
        using var doc = SerializeToJson(parts);
        var root = doc.RootElement;
        Assert.Equal(2, root.GetArrayLength());

        Assert.Equal("text", root[0].GetProperty("type").GetString());
        Assert.Equal("What is shown here?", root[0].GetProperty("text").GetString());

        Assert.Equal("image_url", root[1].GetProperty("type").GetString());
        var url = root[1].GetProperty("image_url").GetProperty("url").GetString();
        Assert.Equal($"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}", url);
    }

    [Fact]
    public void ShouldEmitUrlImagePart_WhenToOpenAIContentPartsWithHttpsUri()
    {
        // Arrange
        var content = MultiModalContent.Empty()
            .AddImage(ImageContentPart.FromUri(new Uri("https://example.com/photo.png")));

        // Act
        var parts = ContentConverter.ToOpenAIContentParts(content);

        // Assert
        using var doc = SerializeToJson(parts);
        var url = doc.RootElement[0].GetProperty("image_url").GetProperty("url").GetString();
        Assert.Equal("https://example.com/photo.png", url);
    }

    [Fact]
    public void ShouldPassThroughDataUrl_WhenToOpenAIContentPartsWithDataUrl()
    {
        // Arrange — data: URLs are already the OpenAI wire format.
        var dataUrl = $"data:image/png;base64,{Convert.ToBase64String(new byte[] { 0x10 })}";
        var content = MultiModalContent.Empty()
            .AddImage(ImageContentPart.FromUri(new Uri(dataUrl)));

        // Act
        var parts = ContentConverter.ToOpenAIContentParts(content);

        // Assert
        using var doc = SerializeToJson(parts);
        var url = doc.RootElement[0].GetProperty("image_url").GetProperty("url").GetString();
        Assert.Equal(dataUrl, url);
    }

    [Fact]
    public void ShouldThrowNotSupported_WhenToOpenAIContentPartsWithFilePart()
    {
        // Arrange
        var content = MultiModalContent.Empty()
            .AddFile(new FileContentPart { FileName = "doc.pdf", Data = [0x25] });

        // Act + Assert
        var ex = Assert.Throws<NotSupportedException>(
            () => ContentConverter.ToOpenAIContentParts(content));
        Assert.Contains("File", ex.Message);
        Assert.Contains("OpenAI", ex.Message);
    }

    [Fact]
    public void ShouldThrowNotSupported_WhenToOpenAIContentPartsWithUnsupportedMediaType()
    {
        // Arrange
        var content = MultiModalContent.Empty()
            .AddImage(ImageContentPart.FromBytes([0x42], "image/tiff"));

        // Act + Assert
        var ex = Assert.Throws<NotSupportedException>(
            () => ContentConverter.ToOpenAIContentParts(content));
        Assert.Contains("image/tiff", ex.Message);
    }

    // ─────────────────────────────────────────────────────────────
    //  LoadImageAsync (virtual file system → base64-ready bytes)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ShouldLoadBytesAndInferMediaType_WhenLoadImageAsyncWithPngFile()
    {
        // Arrange
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var fileSystem = new FakeFileSystemService()
            .AddMount("/workspace", FileAccessRights.ReadOnly)
            .AddFile("/workspace/chart.png", bytes);

        // Act
        var image = await ContentConverter.LoadImageAsync(
            fileSystem, "/workspace/chart.png", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(bytes, image.Data);
        Assert.Equal("image/png", image.MimeType);
        Assert.Null(image.Uri);
    }

    [Fact]
    public async Task ShouldThrowFileNotFound_WhenLoadImageAsyncWithMissingFile()
    {
        // Arrange
        var fileSystem = new FakeFileSystemService()
            .AddMount("/workspace", FileAccessRights.ReadOnly);

        // Act + Assert
        var ex = await Assert.ThrowsAsync<FileNotFoundException>(
            () => ContentConverter.LoadImageAsync(
                fileSystem, "/workspace/missing.png", TestContext.Current.CancellationToken));
        Assert.Contains("/workspace/missing.png", ex.Message);
    }

    [Fact]
    public async Task ShouldThrowNotSupported_WhenLoadImageAsyncWithUnsupportedExtension()
    {
        // Arrange
        var fileSystem = new FakeFileSystemService()
            .AddMount("/workspace", FileAccessRights.ReadOnly)
            .AddFile("/workspace/image.bmp", new byte[] { 0x42, 0x4D });

        // Act + Assert
        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => ContentConverter.LoadImageAsync(
                fileSystem, "/workspace/image.bmp", TestContext.Current.CancellationToken));
        Assert.Contains(".bmp", ex.Message);
        Assert.Contains(".png", ex.Message);
    }

    [Theory]
    [InlineData("/workspace/a.png", "image/png")]
    [InlineData("/workspace/a.jpg", "image/jpeg")]
    [InlineData("/workspace/a.JPEG", "image/jpeg")]
    [InlineData("/workspace/a.gif", "image/gif")]
    [InlineData("/workspace/a.webp", "image/webp")]
    public void ShouldInferMediaType_WhenInferImageMediaTypeWithSupportedExtension(
        string fileName, string expectedMediaType)
    {
        // Act + Assert
        Assert.Equal(expectedMediaType, ContentConverter.InferImageMediaType(fileName));
    }
}
