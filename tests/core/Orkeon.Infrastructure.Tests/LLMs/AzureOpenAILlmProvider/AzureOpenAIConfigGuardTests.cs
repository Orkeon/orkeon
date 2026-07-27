using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;

#pragma warning disable CS0618 // LlmConfig.ApiKey is obsolete but is what the guard reads.

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Configuration guard on all four Azure entry points (LLM-01, defect D-01).
/// </summary>
/// <remarks>
/// Azure has no provider-wide endpoint: <c>BuildEndpoint</c> dereferences
/// <c>config.BaseUrl!</c>. Before LLM-01 the four paths guarded that differently —
/// <c>GenerateAsync</c> and <c>ChatAsync</c> returned a typed error, <c>GenerateStreamingAsync</c>
/// broke silently, and <c>ChatStreamingAsync</c> was not overridden at all and threw a
/// <see cref="NullReferenceException"/>. These tests pin the aligned behaviour.
/// </remarks>
public class AzureOpenAIConfigGuardTests
{
    private const string ExpectedEndpointError = "Azure OpenAI endpoint (BaseUrl) is required";
    private const string ExpectedApiKeyError = "Azure OpenAI API key is required";

    private static readonly LlmMessage[] Messages = [LlmMessage.User("hello")];

    private static AzureOpenAILlmProvider CreateProvider(
        LlmConfig config, TestLogger<AzureOpenAILlmProvider>? logger = null) =>
        new(config, new TestHttpClientFactory(), logger ?? new TestLogger<AzureOpenAILlmProvider>());

    /// <summary>API key present, resource endpoint missing — the case that used to throw.</summary>
    private static LlmConfig ConfigWithoutBaseUrl() =>
        LlmConfig.Create("gpt-5.6-sol", "test-api-key") with { BaseUrl = null };

    private static LlmConfig ConfigWithoutApiKey() =>
        LlmConfig.Create("gpt-5.6-sol") with { BaseUrl = new Uri("https://my-resource.openai.azure.com") };

    private static void AssertErrorContains(LlmResponse response, string expected) =>
        Assert.Contains(expected, response.Metadata["error"]!.ToString(), StringComparison.Ordinal);

    // ── The two buffered paths: unchanged, pinned as the reference contract ──

    [Fact]
    public async Task ShouldReturnTypedError_WhenGenerateAsyncHasNoBaseUrl()
    {
        using var provider = CreateProvider(ConfigWithoutBaseUrl());

        var result = await provider.GenerateAsync(
            "hello", cancellationToken: TestContext.Current.CancellationToken);

        AssertErrorContains(result, ExpectedEndpointError);
    }

    [Fact]
    public async Task ShouldReturnTypedError_WhenChatAsyncHasNoBaseUrl()
    {
        using var provider = CreateProvider(ConfigWithoutBaseUrl());

        var result = await provider.ChatAsync(
            Messages, cancellationToken: TestContext.Current.CancellationToken);

        AssertErrorContains(result, ExpectedEndpointError);
    }

    // ── D-01: the streamed chat path used to throw ──────────────────────────

    [Fact]
    public async Task ShouldCompleteWithConfigError_WhenChatStreamingHasNoBaseUrl()
    {
        using var provider = CreateProvider(ConfigWithoutBaseUrl());

        var completed = await SingleCompletedEventAsync(provider);

        AssertErrorContains(completed, ExpectedEndpointError);
    }

    [Fact]
    public async Task ShouldCompleteWithConfigError_WhenChatStreamingHasNoApiKey()
    {
        using var provider = CreateProvider(ConfigWithoutApiKey());

        var completed = await SingleCompletedEventAsync(provider);

        AssertErrorContains(completed, ExpectedApiKeyError);
    }

    // ── The text-streaming path: still empty, but no longer silent ──────────

    [Fact]
    public async Task ShouldLogAndEmitNothing_WhenGenerateStreamingHasNoBaseUrl()
    {
        // IAsyncEnumerable<string> carries no error channel, so an empty stream stays the
        // only possible outcome — but it must not be silent.
        var logger = new TestLogger<AzureOpenAILlmProvider>();
        using var provider = CreateProvider(ConfigWithoutBaseUrl(), logger);

        var tokens = new List<string>();
        await foreach (var token in provider.GenerateStreamingAsync(
            "hello", cancellationToken: TestContext.Current.CancellationToken))
        {
            tokens.Add(token);
        }

        Assert.Empty(tokens);
        Assert.True(logger.HasLoggedError("BaseUrl"), "the aborted stream must be reported at Error level");
    }

    private static async Task<LlmResponse> SingleCompletedEventAsync(AzureOpenAILlmProvider provider)
    {
        var events = new List<LlmStreamEvent>();
        await foreach (var ev in provider.ChatStreamingAsync(
            Messages, cancellationToken: TestContext.Current.CancellationToken))
        {
            events.Add(ev);
        }

        var completed = Assert.Single(events);
        Assert.Equal(LlmStreamEventKind.Completed, completed.Kind);
        Assert.NotNull(completed.FinalResponse);
        return completed.FinalResponse;
    }
}
