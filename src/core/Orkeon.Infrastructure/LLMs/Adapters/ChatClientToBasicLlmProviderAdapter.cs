using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.Adapters;

/// <summary>Adapts <see cref="IChatClient"/> to the <see cref="IBasicLlmProvider"/> interface.</summary>
public sealed partial class ChatClientToBasicLlmProviderAdapter : IBasicLlmProvider
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<ChatClientToBasicLlmProviderAdapter> _logger;

    /// <inheritdoc />
    public string Name => "ChatClientAdapter";

    /// <summary>Initializes a new instance of <see cref="ChatClientToBasicLlmProviderAdapter"/>.</summary>
    /// <param name="chatClient">The underlying chat client.</param>
    /// <param name="logger">The logger.</param>
    public ChatClientToBasicLlmProviderAdapter(
        IChatClient chatClient,
        ILogger<ChatClientToBasicLlmProviderAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> ChatAsync(
        string message,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage> { new(ChatRole.User, message) };
        var options = MapConfig(config);

        LogSendingChat();
        var response = await _chatClient.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        return response.Text ?? string.Empty;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Availability probe: any failure pinging the chat client is logged and reported as 'not available' (false) rather than propagated.")]
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = new List<ChatMessage> { new(ChatRole.User, "ping") };
            var options = new ChatOptions { MaxOutputTokens = 1 };
            await _chatClient.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            LogAvailabilityCheckFailed(ex);
            return false;
        }
    }

    private static ChatOptions? MapConfig(LlmConfig? config)
    {
        if (config == null) return null;
        // What the configuration sets, and only that (GAP-36): an unset temperature or top_p
        // stays with the client — it received the engine's 0.7 and 1.0 —, a penalty left at 0,
        // every vendor's default, is sent as nothing, and the stop sequences travel.
        return new ChatOptions
        {
            Temperature = (float?)config.Temperature,
            MaxOutputTokens = config.MaxTokens,
            TopP = (float?)config.TopP,
            FrequencyPenalty = config.FrequencyPenalty != 0.0 ? (float)config.FrequencyPenalty : null,
            PresencePenalty = config.PresencePenalty != 0.0 ? (float)config.PresencePenalty : null,
            // A config that names no model leaves the client on its own (GAP-18).
            ModelId = string.IsNullOrWhiteSpace(config.Model) ? null : config.Model,
            Seed = config.Seed,
            StopSequences = config.StopSequences is { Count: > 0 } stopSequences ? [.. stopSequences] : null,
        };
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Sending chat via IChatClient adapter")]
    private partial void LogSendingChat();

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "IChatClient availability check failed")]
    private partial void LogAvailabilityCheckFailed(Exception ex);
}
