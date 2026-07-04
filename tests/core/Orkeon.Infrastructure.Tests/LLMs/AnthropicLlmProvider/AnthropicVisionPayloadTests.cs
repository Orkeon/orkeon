using System.Net;
using System.Text.Json;
using Polly;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// R3.9 — real vision wiring: messages carrying <see cref="LlmMessage.MultiModalContent"/>
/// must be serialized as Anthropic Messages API content blocks
/// (<c>{"type":"image","source":{"type":"base64"|"url",...}}</c>) in the emitted JSON payload.
/// </summary>
public sealed class AnthropicVisionPayloadTests : IDisposable
{
    private const string MinimalSuccessResponse =
        """{"content":[{"type":"text","text":"ok"}],"usage":{"input_tokens":1,"output_tokens":1}}""";

    private readonly MockHttpClientFactory _httpClientFactory;
    private readonly MockHttpMessageHandler _handler;
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy;
    private readonly LlmConfig _config;

    public AnthropicVisionPayloadTests()
    {
        _httpClientFactory = new MockHttpClientFactory();
        _handler = _httpClientFactory.SetupDefaultHandler();
        _handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(MinimalSuccessResponse, System.Text.Encoding.UTF8, "application/json")
        });
        _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
        _config = LlmConfig.Create(ModelClaude3Opus, TestApiKey) with
        {
            BaseUrl = new Uri("https://api.anthropic.com")
        };
    }

    private AnthropicLlmProvider CreateProvider() =>
        new(_config, _httpClientFactory, _noOpPolicy);

    private async Task<JsonDocument> ReadSentPayloadAsync()
    {
        Assert.NotNull(_handler.LastRequest);
        Assert.NotNull(_handler.LastRequest!.Content);
        var body = await _handler.LastRequest.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(body);
    }

    [Fact]
    public async Task ShouldEmitBase64ImageBlock_WhenChatAsyncWithImageBytes()
    {
        // Arrange
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var content = MultiModalContent.Empty()
            .AddText("Describe this image")
            .AddImage(ImageContentPart.FromBytes(bytes, "image/png"));
        using var provider = CreateProvider();

        // Act
        var response = await provider.ChatAsync(
            [LlmMessage.User(content)], cancellationToken: TestContext.Current.CancellationToken);

        // Assert — the emitted JSON carries structured content blocks.
        Assert.Equal("ok", response.Content);
        using var payload = await ReadSentPayloadAsync();
        var message = payload.RootElement.GetProperty("messages")[0];
        Assert.Equal("user", message.GetProperty("role").GetString());

        var blocks = message.GetProperty("content");
        Assert.Equal(JsonValueKind.Array, blocks.ValueKind);
        Assert.Equal(2, blocks.GetArrayLength());

        Assert.Equal("text", blocks[0].GetProperty("type").GetString());
        Assert.Equal("Describe this image", blocks[0].GetProperty("text").GetString());

        Assert.Equal("image", blocks[1].GetProperty("type").GetString());
        var source = blocks[1].GetProperty("source");
        Assert.Equal("base64", source.GetProperty("type").GetString());
        Assert.Equal("image/png", source.GetProperty("media_type").GetString());
        Assert.Equal(Convert.ToBase64String(bytes), source.GetProperty("data").GetString());
    }

    [Fact]
    public async Task ShouldEmitUrlImageBlock_WhenChatAsyncWithImageUrl()
    {
        // Arrange
        var content = MultiModalContent.Empty()
            .AddText("What is this?")
            .AddImage(ImageContentPart.FromUri(new Uri("https://example.com/img.jpg"), "image/jpeg"));
        using var provider = CreateProvider();

        // Act
        await provider.ChatAsync(
            [LlmMessage.User(content)], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        using var payload = await ReadSentPayloadAsync();
        var blocks = payload.RootElement.GetProperty("messages")[0].GetProperty("content");
        var source = blocks[1].GetProperty("source");
        Assert.Equal("url", source.GetProperty("type").GetString());
        Assert.Equal("https://example.com/img.jpg", source.GetProperty("url").GetString());
    }

    [Fact]
    public async Task ShouldKeepPlainStringContent_WhenChatAsyncWithTextOnlyMessage()
    {
        // Arrange — regression: text-only messages keep the historical string content.
        using var provider = CreateProvider();

        // Act
        await provider.ChatAsync(
            [LlmMessage.User("hello")], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        using var payload = await ReadSentPayloadAsync();
        var message = payload.RootElement.GetProperty("messages")[0];
        Assert.Equal(JsonValueKind.String, message.GetProperty("content").ValueKind);
        Assert.Equal("hello", message.GetProperty("content").GetString());
    }

    [Fact]
    public async Task ShouldKeepPlainStringContent_WhenChatAsyncWithTextOnlyMultiModalContent()
    {
        // Arrange — multi-modal content made of text parts only does not need blocks.
        var content = MultiModalContent.FromText("plain text");
        using var provider = CreateProvider();

        // Act
        await provider.ChatAsync(
            [LlmMessage.User(content)], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        using var payload = await ReadSentPayloadAsync();
        var message = payload.RootElement.GetProperty("messages")[0];
        Assert.Equal(JsonValueKind.String, message.GetProperty("content").ValueKind);
    }

    [Fact]
    public async Task ShouldThrowNotSupported_WhenChatAsyncWithAudioContent()
    {
        // Arrange — clear error instead of silently degrading audio to text.
        var content = MultiModalContent.Empty()
            .AddAudio(AudioContentPart.FromBytes([0x01], "audio/wav"));
        using var provider = CreateProvider();

        // Act + Assert
        await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.ChatAsync(
                [LlmMessage.User(content)], cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        // _handler is owned by _httpClientFactory (created via SetupDefaultHandler);
        // disposing it here too is idempotent and satisfies ownership analysis.
        _handler.Dispose();
        _httpClientFactory.Dispose();
        GC.SuppressFinalize(this);
    }
}
