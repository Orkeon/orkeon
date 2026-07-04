using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Polly;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// LLM Response Format — DeepSeek streaming payload coverage.
/// Streaming (<c>GenerateStreamingAsync</c>) shares <c>BuildRequestPayload</c> with non-streaming,
/// so <see cref="LlmConfig.ResponseFormat"/> must travel the same path. This test locks the
/// contract: the SSE request body must carry <c>response_format</c> when configured, and must
/// omit it otherwise.
/// </summary>
public class DeepSeekResponseFormatStreamingTests
{
    private readonly TestHttpClientFactory _httpClientFactory = new();
    private readonly TestLogger<DeepSeekLlmProvider> _logger = new();
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();

    private static TestHttpMessageHandler CreateEmptySseHandler() =>
        new(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: [DONE]\n\n"),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return response;
        });

    private static async Task<JsonElement> ReadRequestBodyAsync(HttpRequestMessage request)
    {
        var raw = await request.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task GenerateStreamingAsync_ShouldEmitResponseFormat_WhenJsonObjectRequested()
    {
        using var handler = CreateEmptySseHandler();
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", new HttpClient(handler));

        var config = LlmConfig.Create("deepseek-chat", TestApiKey) with
        {
            ResponseFormat = LlmResponseFormat.JsonObject()
        };
        using var provider = new DeepSeekLlmProvider(config, _httpClientFactory, _noOpPolicy, _logger);

        await foreach (var _ in provider.GenerateStreamingAsync("Return as json please.", cancellationToken: TestContext.Current.CancellationToken))
        {
            // drain (handler yields no tokens)
        }

        var body = await ReadRequestBodyAsync(handler.CapturedRequests.Single());
        Assert.True(body.TryGetProperty("stream", out var streamFlag));
        Assert.True(streamFlag.GetBoolean());
        Assert.True(body.TryGetProperty("response_format", out var rf),
            "Streaming payload must include response_format when configured.");
        Assert.Equal("json_object", rf.GetProperty("type").GetString());
    }

    [Fact]
    public async Task GenerateStreamingAsync_ShouldOmitResponseFormat_WhenConfigResponseFormatIsNull()
    {
        using var handler = CreateEmptySseHandler();
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", new HttpClient(handler));

        var config = LlmConfig.Create("deepseek-chat", TestApiKey); // ResponseFormat null
        using var provider = new DeepSeekLlmProvider(config, _httpClientFactory, _noOpPolicy, _logger);

        await foreach (var _ in provider.GenerateStreamingAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken))
        {
            // drain
        }

        var body = await ReadRequestBodyAsync(handler.CapturedRequests.Single());
        Assert.True(body.GetProperty("stream").GetBoolean());
        Assert.False(body.TryGetProperty("response_format", out _),
            "Streaming payload must not include response_format when not configured.");
    }
}
