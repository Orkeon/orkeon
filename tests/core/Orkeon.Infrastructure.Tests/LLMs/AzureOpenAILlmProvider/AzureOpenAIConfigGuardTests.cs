using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;

#pragma warning disable CS0618 // LlmConfig.ApiKey is obsolete but is what the guard reads.

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Configuration guard on all four Azure entry points (LLM-01, defects D-01 and D5-02).
/// </summary>
/// <remarks>
/// Azure has no provider-wide endpoint: <c>BuildEndpoint</c> dereferences
/// <c>config.BaseUrl!</c>. Before LLM-01 the four paths guarded that differently —
/// <c>GenerateAsync</c> and <c>ChatAsync</c> returned a typed error, <c>GenerateStreamingAsync</c>
/// broke silently, and <c>ChatStreamingAsync</c> was not overridden at all and threw a
/// <see cref="NullReferenceException"/>. These tests pin the aligned behaviour: each path now
/// names the missing setting in whichever channel it owns — the metadata of a response where
/// there is one, an exception where the sequence carries nothing but tokens.
/// </remarks>
public class AzureOpenAIConfigGuardTests
{
    private const string ExpectedEndpointError = "Azure OpenAI endpoint (BaseUrl) is required";
    private const string ExpectedApiKeyError = "Azure OpenAI API key is required";

    private static readonly LlmMessage[] Messages = [LlmMessage.User("hello")];

    private static AzureOpenAILlmProvider CreateProvider(LlmConfig config) =>
        new(config, new TestHttpClientFactory(), new TestLogger<AzureOpenAILlmProvider>());

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

    // ── The text-streaming path: it refuses instead of streaming nothing ──

    /// <summary>
    /// LLM-00 §8, defect D5-02. This test used to assert <c>Assert.Empty(tokens)</c> plus an
    /// Error log line, and that green was the silence: a caller reading the sequence saw it end
    /// normally, exactly as if the deployment had had nothing to say, while the buffered paths
    /// on the same provider answered "endpoint (BaseUrl) is required". A log line is written
    /// where the operator may look; the caller is told nothing. The three other streaming
    /// implementations throw here since e0c40e5e, and Azure now does too.
    /// </summary>
    [Fact]
    public async Task ShouldRefuseToStream_WhenGenerateStreamingHasNoBaseUrl()
    {
        using var provider = CreateProvider(ConfigWithoutBaseUrl());

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => CollectTokensAsync(provider));

        Assert.Contains(ExpectedEndpointError, ex.Message, StringComparison.Ordinal);
        // Nothing was sent, so no vendor refused anything: a reader separating "refused" from
        // "never reached the API" on the status code must land on the second.
        Assert.Null(ex.StatusCode);
    }

    /// <summary>The other missing setting reaches the caller the same way.</summary>
    [Fact]
    public async Task ShouldRefuseToStream_WhenGenerateStreamingHasNoApiKey()
    {
        using var provider = CreateProvider(ConfigWithoutApiKey());

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => CollectTokensAsync(provider));

        Assert.Contains(ExpectedApiKeyError, ex.Message, StringComparison.Ordinal);
        Assert.Null(ex.StatusCode);
    }

    private static async Task<List<string>> CollectTokensAsync(AzureOpenAILlmProvider provider)
    {
        var tokens = new List<string>();
        await foreach (var token in provider.GenerateStreamingAsync(
            "hello", cancellationToken: TestContext.Current.CancellationToken))
        {
            tokens.Add(token);
        }
        return tokens;
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
