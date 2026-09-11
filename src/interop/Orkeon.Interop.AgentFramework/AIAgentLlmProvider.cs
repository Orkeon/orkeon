using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Interop.AgentFramework;

/// <summary>
/// A Microsoft Agent Framework <see cref="AIAgent"/> used as the language model of an
/// Orkeon agent (<c>AgentBuilder.WithLlm(...)</c>, or
/// <see cref="AgentBuilderExtensions.WithAgentFrameworkAgent"/>).
/// <para>
/// Each Orkeon prompt is one MAF run on a session this provider keeps for its lifetime, so
/// a MAF agent with memory or context providers sees one continuous conversation across
/// the Orkeon agent's iterations. Tool calling stays on the MAF side: whatever tools the
/// MAF agent carries, it uses; Orkeon's own tools are not offered to it (an Orkeon agent
/// that needs both wraps the MAF agent as a tool instead -- <see cref="AIAgentTool"/>).
/// </para>
/// </summary>
public sealed class AIAgentLlmProvider : ILlmProvider
{
    private readonly AIAgent _agent;
    private readonly Lazy<Task<AgentSession>> _session;

    /// <summary>Wraps <paramref name="agent"/>.</summary>
    public AIAgentLlmProvider(AIAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        _agent = agent;
        // One session for the provider's lifetime, created on first use. Session creation
        // is not tied to a caller's token: the first caller's cancellation must not poison
        // the session for the callers after it.
        _session = new(() => _agent.CreateSessionAsync(CancellationToken.None).AsTask(), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public string Name => "agent-framework:" + (_agent.Name ?? _agent.Id);

    /// <inheritdoc />
    public LlmProviderCapabilities Capabilities => LlmProviderCapabilities.Unknown;

    /// <inheritdoc />
    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        RunAsync([new ChatMessage(ChatRole.User, prompt)], cancellationToken);

    /// <inheritdoc />
    public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return RunAsync(messages.Select(m => new ChatMessage(MapRole(m.Role), m.Content)).ToList(), cancellationToken);
    }

    private async Task<LlmResponse> RunAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        var session = await _session.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        var response = await _agent.RunAsync(messages, session, options: null, cancellationToken).ConfigureAwait(false);
        return ToLlmResponse(response, _agent);
    }

    internal static LlmResponse ToLlmResponse(AgentResponse response, AIAgent agent)
    {
        var usage = response.Usage;
        return new LlmResponse
        {
            Content = response.Text,
            Model = agent.Name ?? agent.Id,
            TokensUsed = (int)(usage?.TotalTokenCount ?? 0),
            PromptTokens = usage?.InputTokenCount is { } input ? (int)input : null,
            CompletionTokens = usage?.OutputTokenCount is { } output ? (int)output : null,
        };
    }

#pragma warning disable CA1308 // the role token is normalised for the switch, not compared
    private static ChatRole MapRole(string role) => role?.ToLowerInvariant() switch
#pragma warning restore CA1308
    {
        "system" => ChatRole.System,
        "assistant" => ChatRole.Assistant,
        "tool" => ChatRole.Tool,
        _ => ChatRole.User,
    };
}
