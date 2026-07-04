using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel;

namespace Orkeon.Infrastructure.LLMs.Adapters;

/// <summary>Adapts <see cref="IChatClient"/> to the <see cref="ILlmProvider"/> interface.</summary>
public sealed class ChatClientToLlmProviderAdapter : ILlmProvider
{
    private readonly IChatClient _chatClient;

    /// <inheritdoc />
    public string Name => "ChatClientAdapter";

    /// <summary>Initializes a new instance of <see cref="ChatClientToLlmProviderAdapter"/>.</summary>
    /// <param name="chatClient">The underlying chat client.</param>
    /// <param name="logger">The logger.</param>
    public ChatClientToLlmProviderAdapter(
        IChatClient chatClient,
        ILogger<ChatClientToLlmProviderAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
        ArgumentNullException.ThrowIfNull(logger);
        _ = logger;
    }

    /// <inheritdoc />
    public async Task<LlmResponse> GenerateAsync(
        string prompt,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };
        var options = MapConfig(config);

        var response = await _chatClient.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        return MapResponse(response, config);
    }

    /// <inheritdoc />
    public async Task<LlmResponse> ChatAsync(
        LlmMessage[] messages,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        var chatMessages = messages.Select(m => new ChatMessage(MapRole(m.Role), m.Content)).ToList();
        var options = MapConfig(config);

        var response = await _chatClient.GetResponseAsync(chatMessages, options, cancellationToken).ConfigureAwait(false);
        return MapResponse(response, config);
    }

#pragma warning disable CA1308 // lowercase is the normalized switch subject for the role token
    private static ChatRole MapRole(string role) => role?.ToLowerInvariant() switch
#pragma warning restore CA1308
    {
        "system" => ChatRole.System,
        "assistant" => ChatRole.Assistant,
        "tool" => ChatRole.Tool,
        _ => ChatRole.User
    };

    private static ChatOptions? MapConfig(LlmConfig? config)
    {
        if (config == null) return null;
        return new ChatOptions
        {
            Temperature = (float)config.Temperature,
            MaxOutputTokens = config.MaxTokens,
            TopP = (float)config.TopP,
            FrequencyPenalty = (float)config.FrequencyPenalty,
            PresencePenalty = (float)config.PresencePenalty,
            ModelId = config.Model,
            Seed = config.Seed.HasValue ? (long)config.Seed.Value : null
        };
    }

    private static LlmResponse MapResponse(ChatResponse response, LlmConfig? config)
    {
        var usage = response.Usage;
        return new LlmResponse
        {
            Content = response.Text ?? string.Empty,
            TokensUsed = (int)(usage?.TotalTokenCount ?? 0),
            Model = response.ModelId ?? config?.Model,
            Metadata = new Dictionary<string, object>
            {
                ["provider"] = "IChatClient"
            }
        };
    }
}
