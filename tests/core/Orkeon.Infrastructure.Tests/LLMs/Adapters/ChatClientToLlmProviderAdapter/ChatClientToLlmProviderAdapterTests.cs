using Microsoft.Extensions.AI;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Domain.SharedKernel.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs.Adapters;

public sealed class ChatClientToLlmProviderAdapterTests : IDisposable
{
    private readonly MockChatClient _mockChatClient = new();
    private readonly MockLogger<ChatClientToLlmProviderAdapter> _mockLogger = new();

    private ChatClientToLlmProviderAdapter CreateAdapter()
        => new(_mockChatClient, _mockLogger);

    [Fact]
    public async Task ShouldReturnContent_WhenGenerateAsync()
    {
        _mockChatClient.SetGetResponseResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "generated text"))
        {
            ModelId = ModelGpt4
        });

        var adapter = CreateAdapter();
        var result = await adapter.GenerateAsync("prompt", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("generated text", result.Content);
        Assert.Equal(ModelGpt4, result.Model);
    }

    [Fact]
    public async Task ShouldMapRolesCorrectly_WhenChatAsync()
    {
        _mockChatClient.SetGetResponseResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        var adapter = CreateAdapter();
        var messages = new[]
        {
            new LlmMessage { Role = "system", Content = "You are helpful" },
            new LlmMessage { Role = "user", Content = "Hello" },
            new LlmMessage { Role = "assistant", Content = "Hi" }
        };

        await adapter.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(_mockChatClient.LastGetResponseMessages);
        var chatMsgs = _mockChatClient.LastGetResponseMessages!.ToList();
        Assert.Equal(ChatRole.System, chatMsgs[0].Role);
        Assert.Equal(ChatRole.User, chatMsgs[1].Role);
        Assert.Equal(ChatRole.Assistant, chatMsgs[2].Role);
    }

    [Fact]
    public async Task ShouldMapAllOptions_WhenChatAsyncWithConfig()
    {
        _mockChatClient.SetGetResponseResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        var adapter = CreateAdapter();
        var config = LlmConfig.Default() with
        {
            Temperature = 0.3,
            MaxTokens = 200,
            TopP = 0.9,
            FrequencyPenalty = 0.1,
            PresencePenalty = 0.2,
            Model = ModelGpt4o,
            Seed = 42
        };

        await adapter.ChatAsync(
            [new LlmMessage { Role = "user", Content = "test" }],
            config, TestContext.Current.CancellationToken);

        Assert.Equal(1, _mockChatClient.GetResponseCallCount);
        var options = _mockChatClient.LastGetResponseOptions;
        Assert.NotNull(options);
        Assert.Equal(0.3f, options!.Temperature);
        Assert.Equal(200, options.MaxOutputTokens);
        Assert.Equal(0.9f, options.TopP);
        Assert.Equal(0.1f, options.FrequencyPenalty);
        Assert.Equal(0.2f, options.PresencePenalty);
        Assert.Equal(ModelGpt4o, options.ModelId);
        Assert.Equal((long)42, options.Seed);
    }

    [Fact]
    public async Task ShouldMapTokenCount_WhenGenerateAsyncWithTokenUsage()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "text"))
        {
            Usage = new UsageDetails { TotalTokenCount = 150 }
        };

        _mockChatClient.SetGetResponseResult(response);

        var adapter = CreateAdapter();
        var result = await adapter.GenerateAsync("prompt", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(150, result.TokensUsed);
    }

    [Fact]
    public void ShouldThrow_WhenConstructorNullChatClient()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ChatClientToLlmProviderAdapter(null!, _mockLogger));
    }

    public void Dispose()
    {
        _mockChatClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
