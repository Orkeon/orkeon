using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Interfaces.Ports;

/// <summary>Observes every model call: announced at send, progress while streamed, closed at the end.</summary>
public interface ILlmCallObserver
{
    /// <summary>A call is sent.</summary>
    void Started(string callId, LlmCallMode mode, int estimatedPromptTokens);

    /// <summary>Cumulative estimated tokens received so far.</summary>
    void Progress(string callId, int estimatedCompletionTokens);

    /// <summary>The call is closed.</summary>
    void Finished(string callId, LlmCallMode mode, bool fellBack, int? promptTokens, int? completionTokens);
}

/// <summary>An observer that observes nothing.</summary>
public sealed class NullLlmCallObserver : ILlmCallObserver
{
    /// <summary>Shared instance.</summary>
    public static readonly NullLlmCallObserver Instance = new();

    /// <inheritdoc />
    public void Started(string callId, LlmCallMode mode, int estimatedPromptTokens)
    {
        // Observes nothing: the null observer drops every notification on purpose.
    }

    /// <inheritdoc />
    public void Progress(string callId, int estimatedCompletionTokens)
    {
        // Observes nothing: the null observer drops every notification on purpose.
    }

    /// <inheritdoc />
    public void Finished(string callId, LlmCallMode mode, bool fellBack, int? promptTokens, int? completionTokens)
    {
        // Observes nothing: the null observer drops every notification on purpose.
    }
}
