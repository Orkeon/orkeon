using Orkeon.Application.Rag;
using Microsoft.Extensions.AI;
using Orkeon.Infrastructure.Knowledge.Generation;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.Knowledge.Generation;

public sealed class ChatClientResponseGeneratorTestsFixture : IDisposable
{
    private readonly MockChatClient _mockChatClient = new();

    public ChatClientResponseGeneratorTestsFixture WithChatResponse(ChatResponse response)
    {
        _mockChatClient.SetGetResponseResult(response);
        return this;
    }

    public ChatClientResponseGeneratorTestsFixture WithChatResponse(string text, int? totalTokens = null, string? modelId = null)
    {
        var response = new ChatResponse(
            new ChatMessage(ChatRole.Assistant, text));

        if (totalTokens.HasValue)
            response.Usage = new UsageDetails { TotalTokenCount = totalTokens.Value };

        if (modelId is not null)
            response.ModelId = modelId;

        _mockChatClient.SetGetResponseResult(response);
        return this;
    }

    public ChatClientResponseGenerator CreateGenerator() => new(_mockChatClient);

    public static Task<GeneratedResponse> GenerateAsync(ChatClientResponseGenerator generator, AugmentedPrompt prompt, GenerationOptions options)
        => generator.GenerateAsync(prompt, options);

    public MockChatClient GetChatClient() => _mockChatClient;

    public void Dispose()
    {
        _mockChatClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
