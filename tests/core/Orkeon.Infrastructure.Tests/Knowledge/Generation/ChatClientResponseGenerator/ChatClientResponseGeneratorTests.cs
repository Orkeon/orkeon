using Orkeon.Application.Rag;
using Microsoft.Extensions.AI;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.Knowledge.Generation;

public sealed class ChatClientResponseGeneratorTests : IDisposable
{
    private readonly ChatClientResponseGeneratorTestsFixture _fixture = new();

    [Fact]
    public async Task ShouldSendSystemAndUserPrompts_WhenGenerating()
    {
        var generator = _fixture
            .WithChatResponse("Answer text", totalTokens: 10)
            .CreateGenerator();

        var prompt = new AugmentedPrompt
        {
            SystemPrompt = "You are a helper.",
            UserPrompt = "What is AI?"
        };

        await generator.GenerateAsync(prompt, new GenerationOptions(), TestContext.Current.CancellationToken);

        var chatClient = _fixture.GetChatClient();
        Assert.NotNull(chatClient.LastGetResponseMessages);
        var messages = chatClient.LastGetResponseMessages!.ToList();
        Assert.Equal(2, messages.Count);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Contains("You are a helper", messages[0].Text);
        Assert.Equal(ChatRole.User, messages[1].Role);
        Assert.Contains("What is AI?", messages[1].Text);
    }

    [Fact]
    public async Task ShouldUseTemperatureAndMaxTokens_WhenOptionsAreProvided()
    {
        var generator = _fixture
            .WithChatResponse("Response")
            .CreateGenerator();

        var options = new GenerationOptions
        {
            Temperature = 0.5f,
            MaxTokens = 500
        };

        await generator.GenerateAsync(
            new AugmentedPrompt { UserPrompt = "Q" }, options, TestContext.Current.CancellationToken);

        var chatClient = _fixture.GetChatClient();
        Assert.NotNull(chatClient.LastGetResponseOptions);
        Assert.Equal(0.5f, chatClient.LastGetResponseOptions!.Temperature);
        Assert.Equal(500, chatClient.LastGetResponseOptions.MaxOutputTokens);
    }

    [Fact]
    public async Task ShouldReturnTextAndTokenCount_WhenGenerating()
    {
        var generator = _fixture
            .WithChatResponse("Generated text", totalTokens: 42, modelId: ModelGpt4)
            .CreateGenerator();

        var result = await generator.GenerateAsync(
            new AugmentedPrompt { UserPrompt = "Q" },
            new GenerationOptions(), TestContext.Current.CancellationToken);

        Assert.Equal("Generated text", result.Text);
        Assert.Equal(42, result.TokensUsed);
        Assert.Equal(ModelGpt4, result.Model);
    }

    [Fact]
    public async Task ShouldOverrideDefaultSystemPrompt_WhenCustomSystemPromptIsProvided()
    {
        var generator = _fixture
            .WithChatResponse("Answer")
            .CreateGenerator();

        var prompt = new AugmentedPrompt
        {
            SystemPrompt = "Default system prompt",
            UserPrompt = "Question"
        };

        var options = new GenerationOptions
        {
            SystemPrompt = "Custom system prompt"
        };

        await generator.GenerateAsync(prompt, options, TestContext.Current.CancellationToken);

        var chatClient = _fixture.GetChatClient();
        var messages = chatClient.LastGetResponseMessages!.ToList();
        Assert.Equal("Custom system prompt", messages[0].Text);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
