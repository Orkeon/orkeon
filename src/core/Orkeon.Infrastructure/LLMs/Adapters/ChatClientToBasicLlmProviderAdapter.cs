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

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Sending chat via IChatClient adapter")]
    private partial void LogSendingChat();

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "IChatClient availability check failed")]
    private partial void LogAvailabilityCheckFailed(Exception ex);
}
