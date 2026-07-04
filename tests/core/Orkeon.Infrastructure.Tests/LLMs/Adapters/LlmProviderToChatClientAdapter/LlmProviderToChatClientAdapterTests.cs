using Orkeon.Application.Common.DTOs;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Microsoft.Extensions.AI;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.LLMs.Adapters;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs.Adapters;

public class LlmProviderToChatClientAdapterTests
{
    private static readonly string[] s_streamingChunks = ["chunk1", "chunk2"];

    private readonly MockLlmProvider _llmProvider = new();

    private LlmProviderToChatClientAdapter CreateAdapter()
        => new(_llmProvider);

    [Fact]
    public async Task ShouldDelegateToLlmProvider_WhenGetResponseAsync()
    {
        _llmProvider.SetChatResult(new LlmResponse
        {
            Content = "response text",
            TokensUsed = 50,
            Model = TestModelName
        });

        using var adapter = CreateAdapter();
        var messages = new[] { new ChatMessage(ChatRole.User, "hello") };
        var result = await adapter.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("response text", result.Text);
        Assert.Equal(TestModelName, result.ModelId);
        Assert.Equal(50, result.Usage?.TotalTokenCount);
    }

    [Fact]
    public async Task ShouldMapOptionsToLlmConfig_WhenGetResponseAsync()
    {
        _llmProvider.SetChatFunc((msgs, config) =>
        {
            // Validate that options were mapped correctly
            if (config?.Temperature == 0.5 && config?.MaxTokens == 100)
                return new LlmResponse { Content = "ok" };
            throw new InvalidOperationException("Config not mapped correctly");
        });

        using var adapter = CreateAdapter();
        var options = new ChatOptions { Temperature = 0.5f, MaxOutputTokens = 100 };
        await adapter.GetResponseAsync([new ChatMessage(ChatRole.User, "test")], options, TestContext.Current.CancellationToken);

        Assert.Equal(1, _llmProvider.ChatCallCount);
        Assert.NotNull(_llmProvider.LastChatConfig);
        Assert.Equal(0.5, _llmProvider.LastChatConfig!.Temperature);
        Assert.Equal(100, _llmProvider.LastChatConfig.MaxTokens);
    }

    [Fact]
    public async Task ShouldMapMultipleRoles_WhenGetResponseAsync()
    {
        _llmProvider.SetChatResult(new LlmResponse { Content = "ok" });

        using var adapter = CreateAdapter();
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, "system msg"),
            new ChatMessage(ChatRole.User, "user msg")
        };
        await adapter.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(_llmProvider.LastChatMessages);
        Assert.Equal(2, _llmProvider.LastChatMessages!.Length);
        Assert.Equal("system", _llmProvider.LastChatMessages[0].Role);
        Assert.Equal("user", _llmProvider.LastChatMessages[1].Role);
    }

    [Fact]
    public async Task ShouldStream_WhenGetStreamingResponseAsyncWithStreamingProvider()
    {
        var streamingProvider = new MockStreamingLlmProvider();
        streamingProvider.SupportsStreaming = true;
        streamingProvider.SetStreamingChunks(s_streamingChunks);

        using var adapter = new LlmProviderToChatClientAdapter(streamingProvider);
        var messages = new[] { new ChatMessage(ChatRole.User, "test") };

        var chunks = new List<string>();
        await foreach (var update in adapter.GetStreamingResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken))
        {
            if (update.Text != null) chunks.Add(update.Text);
        }

        Assert.Equal(2, chunks.Count);
        Assert.Equal("chunk1", chunks[0]);
        Assert.Equal("chunk2", chunks[1]);
    }

    [Fact]
    public async Task ShouldFallback_WhenGetStreamingResponseAsyncWithoutStreamingProvider()
    {
        _llmProvider.SetChatResult(new LlmResponse { Content = "full response" });

        using var adapter = CreateAdapter();
        var messages = new[] { new ChatMessage(ChatRole.User, "test") };

        var chunks = new List<string>();
        await foreach (var update in adapter.GetStreamingResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken))
        {
            if (update.Text != null) chunks.Add(update.Text);
        }

        Assert.Single(chunks);
        Assert.Equal("full response", chunks[0]);
    }

    [Fact]
    public void ShouldReturnSelf_WhenGetServiceForIChatClient()
    {
        using var adapter = CreateAdapter();
        var result = adapter.GetService(typeof(IChatClient));
        Assert.Same(adapter, result);
    }

    [Fact]
    public void ShouldReturnNull_WhenGetServiceForOtherType()
    {
        using var adapter = CreateAdapter();
        Assert.Null(adapter.GetService(typeof(string)));
    }

    [Fact]
    public void ShouldThrow_WhenConstructorNullProvider()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new LlmProviderToChatClientAdapter(null!));
    }

    [Fact]
    public void ShouldReturnAdapterName_WhenMetadata()
    {
        using var adapter = CreateAdapter();
        Assert.Equal(nameof(LlmProviderToChatClientAdapter), adapter.Metadata.ProviderName);
    }

    [Fact]
    public async Task ShouldMapResponseFormatFromAdditionalProperties_WhenGetResponseAsync()
    {
        _llmProvider.SetChatResult(new LlmResponse { Content = "ok" });

        using var adapter = CreateAdapter();
        var options = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [LlmChatOptionsKeys.ResponseFormat] = LlmResponseFormat.JsonObject()
            }
        };

        await adapter.GetResponseAsync([new ChatMessage(ChatRole.User, "test")], options, TestContext.Current.CancellationToken);

        Assert.NotNull(_llmProvider.LastChatConfig);
        Assert.NotNull(_llmProvider.LastChatConfig!.ResponseFormat);
        Assert.Equal("json_object", _llmProvider.LastChatConfig.ResponseFormat!.Type);
    }

    [Fact]
    public async Task ShouldOmitResponseFormat_WhenAdditionalPropertiesAbsent()
    {
        _llmProvider.SetChatResult(new LlmResponse { Content = "ok" });

        using var adapter = CreateAdapter();
        var options = new ChatOptions { Temperature = 0.5f };

        await adapter.GetResponseAsync([new ChatMessage(ChatRole.User, "test")], options, TestContext.Current.CancellationToken);

        Assert.NotNull(_llmProvider.LastChatConfig);
        Assert.Null(_llmProvider.LastChatConfig!.ResponseFormat);
    }

    private static async IAsyncEnumerable<string> AsyncEnumerable(string[] items)
    {
        foreach (var item in items)
        {
            yield return item;
        }
        await Task.CompletedTask;
    }
}
