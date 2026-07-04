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
/// must be serialized as OpenAI Chat Completions content parts
/// (<c>{"type":"image_url","image_url":{"url":...}}</c>, URL or base64 data URL)
/// in the emitted JSON payload.
/// </summary>
public sealed class OpenAIVisionPayloadTests : IDisposable
{
    private const string MinimalSuccessResponse =
        """{"choices":[{"message":{"content":"ok"}}],"usage":{"total_tokens":2}}""";

    private readonly MockHttpClientFactory _httpClientFactory;
    private readonly MockHttpMessageHandler _handler;
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy;
    private readonly LlmConfig _config;

    public OpenAIVisionPayloadTests()
    {
        _httpClientFactory = new MockHttpClientFactory();
        _handler = _httpClientFactory.SetupDefaultHandler();
        _handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(MinimalSuccessResponse, System.Text.Encoding.UTF8, "application/json")
        });
        _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
        _config = LlmConfig.Create("gpt-4o", TestApiKey);
    }

    private OpenAIProvider CreateProvider() =>
        new(_config, _httpClientFactory, _noOpPolicy);

    private async Task<JsonDocument> ReadSentPayloadAsync()
    {
        Assert.NotNull(_handler.LastRequest);
        Assert.NotNull(_handler.LastRequest!.Content);
        var body = await _handler.LastRequest.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(body);
    }

    [Fact]
    public async Task ShouldEmitDataUrlImagePart_WhenChatAsyncWithImageBytes()
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

        // Assert — the emitted JSON carries structured content parts.
        Assert.Equal("ok", response.Content);
        using var payload = await ReadSentPayloadAsync();
        var message = payload.RootElement.GetProperty("messages")[0];
        Assert.Equal("user", message.GetProperty("role").GetString());

        var parts = message.GetProperty("content");
        Assert.Equal(JsonValueKind.Array, parts.ValueKind);
        Assert.Equal(2, parts.GetArrayLength());

        Assert.Equal("text", parts[0].GetProperty("type").GetString());
        Assert.Equal("Describe this image", parts[0].GetProperty("text").GetString());

        Assert.Equal("image_url", parts[1].GetProperty("type").GetString());
        var url = parts[1].GetProperty("image_url").GetProperty("url").GetString();
        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(bytes)}", url);
    }

    [Fact]
    public async Task ShouldEmitUrlImagePart_WhenChatAsyncWithImageUrl()
    {
        // Arrange
        var content = MultiModalContent.Empty()
            .AddText("What is this?")
            .AddImage(ImageContentPart.FromUri(new Uri("https://example.com/photo.png")));
        using var provider = CreateProvider();

        // Act
        await provider.ChatAsync(
            [LlmMessage.User(content)], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        using var payload = await ReadSentPayloadAsync();
        var parts = payload.RootElement.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal("image_url", parts[1].GetProperty("type").GetString());
        Assert.Equal(
            "https://example.com/photo.png",
            parts[1].GetProperty("image_url").GetProperty("url").GetString());
    }

    [Fact]
    public async Task ShouldKeepPlainStringContent_WhenChatAsyncWithTextOnlyMessage()
    {
        // Arrange — regression: without images the historical prompt path is preserved.
        using var provider = CreateProvider();

        // Act
        await provider.ChatAsync(
            [LlmMessage.User("hello")], cancellationToken: TestContext.Current.CancellationToken);

        // Assert — content stays a plain JSON string.
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
