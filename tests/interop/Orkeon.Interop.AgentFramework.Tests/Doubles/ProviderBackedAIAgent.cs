using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Interop.AgentFramework.Tests.Doubles;

/// <summary>
/// A MAF agent that answers through an Orkeon <see cref="ILlmProvider"/> — what
/// <c>chatClient.AsAIAgent()</c> over Orkeon's own model is, as the example builds its reviewer: each
/// run is one chat call to the provider, its answer and usage handed back.
/// </summary>
public sealed class ProviderBackedAIAgent(string name, ILlmProvider provider) : AIAgent
{
    private sealed class Session : AgentSession
    {
        public Session()
        {
        }

        public Session(AgentSessionStateBag bag) : base(bag)
        {
        }
    }

    private int _runs;

    /// <summary>How many runs the agent made.</summary>
    public int Runs => Volatile.Read(ref _runs);

    protected override string? IdCore => "provider-backed-" + name;

    public override string? Name => name;

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default) =>
        new(new Session());

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default) =>
        new(session.StateBag.Serialize());

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedState, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default) =>
        new(new Session(AgentSessionStateBag.Deserialize(serializedState)));

    protected override async Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _runs);
        var conversation = messages.Select(m => new LlmMessage { Role = m.Role.Value, Content = m.Text }).ToArray();
        var answer = await provider.ChatAsync(conversation, cancellationToken: cancellationToken);
        return new AgentResponse(new ChatMessage(ChatRole.Assistant, answer.Content))
        {
            Usage = new UsageDetails
            {
                InputTokenCount = answer.PromptTokens,
                OutputTokenCount = answer.CompletionTokens,
                TotalTokenCount = answer.TokensUsed,
            },
        };
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await RunCoreAsync(messages, session, options, cancellationToken);
        foreach (var update in response.ToAgentResponseUpdates())
            yield return update;
    }
}
