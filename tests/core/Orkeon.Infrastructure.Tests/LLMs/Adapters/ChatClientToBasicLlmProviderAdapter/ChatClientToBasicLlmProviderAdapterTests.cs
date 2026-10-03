using Orkeon.Domain.SharedKernel.ValueObjects;
using Microsoft.Extensions.AI;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Adapters;

public sealed class ChatClientToBasicLlmProviderAdapterTests : IDisposable
{
    private readonly MockChatClient _mockChatClient = new();
    private readonly MockLogger<ChatClientToBasicLlmProviderAdapter> _mockLogger = new();

    private ChatClientToBasicLlmProviderAdapter CreateAdapter()
        => new(_mockChatClient, _mockLogger);

    [Fact]
    public async Task ShouldDelegateToIChatClient_WhenChatAsync()
    {
        var expected = "Hello from ChatClient";
        _mockChatClient.SetGetResponseResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, expected)));

        var adapter = CreateAdapter();
        var result = await adapter.ChatAsync("test prompt", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ShouldMapOptions_WhenChatAsyncWithConfig()
    {
        _mockChatClient.SetGetResponseResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        var adapter = CreateAdapter();
        var config = LlmConfig.OnProfile() with { Temperature = 0.5, MaxTokens = 100 };
        var result = await adapter.ChatAsync("test", config, TestContext.Current.CancellationToken);

        Assert.Equal("ok", result);
        Assert.Equal(1, _mockChatClient.GetResponseCallCount);
        var options = _mockChatClient.LastGetResponseOptions;
        Assert.NotNull(options);
        Assert.Equal(0.5f, options!.Temperature);
        Assert.Equal(100, options.MaxOutputTokens);
        // GAP-18: a config that names no model leaves the model to the client — never "".
        Assert.Null(options.ModelId);
    }

    [Fact]
    public async Task ShouldLeaveTheSamplingToTheClient_WhenTheConfigSetsNone()
    {
        // GAP-36: an unset temperature or top_p is the client's own, never 0.7 or 1.0; a penalty
        // left at 0 is sent as nothing.
        _mockChatClient.SetGetResponseResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        await CreateAdapter().ChatAsync("test", LlmConfig.OnProfile(), TestContext.Current.CancellationToken);

        var options = _mockChatClient.LastGetResponseOptions!;
        Assert.Null(options.Temperature);
        Assert.Null(options.TopP);
        Assert.Null(options.FrequencyPenalty);
        Assert.Null(options.PresencePenalty);
        Assert.Null(options.StopSequences);
    }

    [Fact]
    public async Task ShouldCarryTheStopSequences_WhenChatAsyncWithConfig()
    {
        _mockChatClient.SetGetResponseResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        await CreateAdapter().ChatAsync(
            "test", LlmConfig.OnProfile() with { StopSequences = ["END"] }, TestContext.Current.CancellationToken);

        Assert.Equal(["END"], _mockChatClient.LastGetResponseOptions!.StopSequences!);
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenChatAsyncNullResponse()
    {
        _mockChatClient.SetGetResponseResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, (string?)null)));

        var adapter = CreateAdapter();
        var result = await adapter.ChatAsync("test", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task ShouldReturnTrue_WhenIsAvailableAsyncWhenClientResponds()
    {
        _mockChatClient.SetGetResponseResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "pong")));

        var adapter = CreateAdapter();
        var available = await adapter.IsAvailableAsync(TestContext.Current.CancellationToken);

        Assert.True(available);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenIsAvailableAsyncWhenClientThrows()
    {
        _mockChatClient.SetGetResponseException(new HttpRequestException("Connection refused"));

        var adapter = CreateAdapter();
        var available = await adapter.IsAvailableAsync(TestContext.Current.CancellationToken);

        Assert.False(available);
    }

    [Fact]
    public void ShouldReturnChatClientAdapter_WhenName()
    {
        var adapter = CreateAdapter();
        Assert.Equal("ChatClientAdapter", adapter.Name);
    }

    [Fact]
    public void ShouldThrow_WhenConstructorNullChatClient()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ChatClientToBasicLlmProviderAdapter(null!, _mockLogger));
    }

    [Fact]
    public void ShouldThrow_WhenConstructorNullLogger()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ChatClientToBasicLlmProviderAdapter(_mockChatClient, null!));
    }

    public void Dispose()
    {
        _mockChatClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
