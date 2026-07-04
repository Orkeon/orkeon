using Microsoft.Extensions.AI;
using Orkeon.Application.Interfaces.Rag;
using Orkeon.Application.Rag;

namespace Orkeon.Infrastructure.Knowledge.Generation;

/// <summary>
/// Response generator that uses Microsoft.Extensions.AI IChatClient to generate
/// LLM responses from augmented prompts.
/// </summary>
public sealed class ChatClientResponseGenerator : IResponseGenerator
{
    private readonly IChatClient _chatClient;

    /// <summary>Initializes a new instance of <see cref="ChatClientResponseGenerator"/>.</summary>
    /// <param name="chatClient">The chat client to use for LLM calls.</param>
    public ChatClientResponseGenerator(IChatClient chatClient)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
    }

    /// <inheritdoc />
    public Task<GeneratedResponse> GenerateAsync(
        AugmentedPrompt prompt, GenerationOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(options);
        return GenerateCoreAsync();

        async Task<GeneratedResponse> GenerateCoreAsync()
        {
            var messages = new List<ChatMessage>();

            var systemPrompt = options.SystemPrompt ?? prompt.SystemPrompt;
            if (!string.IsNullOrEmpty(systemPrompt))
                messages.Add(new ChatMessage(ChatRole.System, systemPrompt));

            messages.Add(new ChatMessage(ChatRole.User, prompt.UserPrompt));

            var chatOptions = new ChatOptions
            {
                Temperature = options.Temperature,
                MaxOutputTokens = options.MaxTokens
            };

            var response = await _chatClient.GetResponseAsync(messages, chatOptions, ct).ConfigureAwait(false);

            return new GeneratedResponse
            {
                Text = response.Text ?? string.Empty,
                TokensUsed = (int)(response.Usage?.TotalTokenCount ?? 0),
                Model = response.ModelId
            };
        }
    }
}
