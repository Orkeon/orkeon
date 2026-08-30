using System.Net;
using System.Text.Json;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Polly;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Vision exposure across the OpenAI-compatible family (LLM-06, G-18).
/// </summary>
/// <remarks>
/// Every provider in the fleet now accepts image input; only OpenAI and Anthropic composed
/// it originally. The family covered here declares the capability and inherits the
/// composition from the base — no per-provider override. Ollama stays out on purpose: it
/// takes a base64 <c>images</c> array rather than OpenAI content parts, a translation that
/// belongs with the rest of its request-shape work (LLM-07). DeepSeek, long the one provider
/// with no vision at all, joined the family when <c>deepseek-v4-flash-vision-exp</c> shipped
/// (measured 2026-08-30).
/// </remarks>
public class ProviderVisionPayloadTests
{
    private static readonly string OkBody = JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content = "ok" } } },
        usage = new { total_tokens = 1, prompt_tokens = 1, completion_tokens = 0 },
    });

    public static TheoryData<string> VisionCapableProviders() =>
    [
        nameof(OpenAIProvider), nameof(AzureOpenAILlmProvider), nameof(GroqLlmProvider),
        nameof(TogetherAiLlmProvider), nameof(MistralLlmProvider), nameof(KimiLlmProvider),
        nameof(QwenLlmProvider), nameof(HuggingFaceLlmProvider), nameof(ZaiLlmProvider),
        nameof(DeepSeekLlmProvider),
    ];

    private static MultiModalContent ImageMessage() =>
        MultiModalContent.Empty()
            .AddText("Describe this image")
            .AddImage(ImageContentPart.FromBytes([0x89, 0x50, 0x4E, 0x47], "image/png"));

    private static async Task<JsonElement> CaptureChatPayloadAsync(
        string providerTypeName, LlmMessage message)
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OkBody);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient(providerTypeName, new HttpClient(handler));

        var type = typeof(OpenAIProvider).Assembly.GetType(
            $"Orkeon.Infrastructure.LLMs.{providerTypeName}", throwOnError: true)!;
        var config = LlmConfig.Create("test-model", TestApiKey) with
        {
            BaseUrl = new Uri("https://provider.test/v1"),
        };

        var provider = (Orkeon.Domain.SharedKernel.ILlmProvider)Activator.CreateInstance(
            type, config, factory, Policy.NoOpAsync<HttpResponseMessage>(), null)!;
        try
        {
            await provider.ChatAsync([message], cancellationToken: TestContext.Current.CancellationToken);
        }
        finally
        {
            (provider as IDisposable)?.Dispose();
        }

        var raw = await handler.CapturedRequests.Single().Content!
            .ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    [Theory]
    [MemberData(nameof(VisionCapableProviders))]
    public async Task ShouldComposeImageParts_OnEveryVisionCapableProvider(string providerTypeName)
    {
        var payload = await CaptureChatPayloadAsync(providerTypeName, LlmMessage.User(ImageMessage()));

        var content = payload.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal(JsonValueKind.Array, content.ValueKind);

        var parts = content.EnumerateArray().ToList();
        Assert.Contains(parts, p => p.GetProperty("type").GetString() == "text");

        var image = Assert.Single(parts, p => p.GetProperty("type").GetString() == "image_url");
        Assert.StartsWith(
            "data:image/png;base64,",
            image.GetProperty("image_url").GetProperty("url").GetString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Enabling vision must not change anything for the text-only calls that make up the vast
    /// majority of traffic: the base composes parts only when a message actually carries an
    /// image, so a plain message keeps its historical string content — including the base
    /// class's own role-prefixed concatenation on the simple chat path.
    /// </summary>
    [Theory]
    [MemberData(nameof(VisionCapableProviders))]
    public async Task ShouldKeepPlainStringContent_ForTextOnlyMessages(string providerTypeName)
    {
        var payload = await CaptureChatPayloadAsync(providerTypeName, LlmMessage.User("just text"));

        var content = payload.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal(JsonValueKind.String, content.ValueKind);
        Assert.Contains("just text", content.GetString()!, StringComparison.Ordinal);
    }

    /// <summary>
    /// DeepSeek used to be the one provider in the fleet with no vision model, and an image
    /// message degraded to its text fallback. That ended with <c>deepseek-v4-flash-vision-exp</c>:
    /// measured live on 2026-08-30, the model reads a base64 image and answers about it. The
    /// capability is declared per provider while reality is per model (D-03) — exactly like
    /// Ollama (<c>llava</c> sees, <c>llama3.2</c> does not) and Z.AI (<c>glm-4.6v-flash</c>
    /// sees, <c>glm-5.2</c> does not) — so the image now travels as structured parts and a
    /// text-only default model answers with the vendor's own error, not a silent downgrade.
    /// </summary>
    [Fact]
    public async Task ShouldEmitStructuredParts_OnDeepSeekWhoseVisionArrivedPerModel()
    {
        var payload = await CaptureChatPayloadAsync(
            nameof(DeepSeekLlmProvider), LlmMessage.User(ImageMessage()));

        var content = payload.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
    }
}
