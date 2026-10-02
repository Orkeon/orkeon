using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;

namespace Orkeon.Interop.AgentFramework;

/// <summary>
/// An Orkeon crew exposed as a Microsoft Agent Framework <see cref="AIAgent"/>.
/// <para>
/// Every <c>RunAsync</c> is one crew kickoff: the conversation the caller sends becomes the
/// crew's initial context (the last user message is the request, the messages before it
/// are quoted as history), the crew's final output comes back as one assistant message,
/// with the crew's token telemetry as <see cref="AgentResponse.Usage"/> when it was
/// measured. The crew runs with everything Orkeon gives it -- its mounts, its tools, its
/// budget, its orchestration mode -- the MAF side sees an agent that answers.
/// </para>
/// <para>
/// Two ways to build one. Over an <see cref="ICrewOrchestrationService"/> the caller owns
/// (and the scope it lives in): every turn kicks the same registered crew off through it.
/// Over an <see cref="IServiceScopeFactory"/> and a crew loader -- what
/// <c>ICrewAgentFactory</c> builds: every turn opens a scope of its own, the loader registers
/// the crew in that scope's (scoped) repositories, the scope's orchestrator runs it, and the
/// scope ends with the turn. Two turns, or two agents, never share an orchestrator.
/// </para>
/// <para>
/// A session holds nothing: a crew has no conversation state of its own between kickoffs
/// (memory, when enabled, is the crew's business). The session serialises to an empty
/// object and deserialises from anything, so callers that persist sessions keep working.
/// </para>
/// </summary>
public sealed class CrewAgent : AIAgent
{
    private readonly Func<Application.Interfaces.Services.CrewInput, CancellationToken, Task<Application.Interfaces.Services.CrewOutput>> _kickoff;
    private readonly CrewId? _crewId;
    private readonly string _id;
    private readonly string _name;
    private readonly string? _description;

    /// <summary>Wraps the crew registered under <paramref name="crewId"/>.</summary>
    /// <param name="orchestrator">The Orkeon orchestration service the crew is registered with.</param>
    /// <param name="crewId">The crew to run on every turn.</param>
    /// <param name="name">The agent name MAF displays; <c>orkeon-crew-&lt;id&gt;</c> when omitted.</param>
    /// <param name="description">What the agent does, for MAF orchestrators that route by description.</param>
    public CrewAgent(ICrewOrchestrationService orchestrator, CrewId crewId, string? name = null, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(crewId);
        _kickoff = (input, ct) => orchestrator.KickoffAsync(crewId, input, ct);
        _crewId = crewId;
        _id = "orkeon-crew-" + crewId;
        _name = string.IsNullOrWhiteSpace(name) ? "orkeon-crew-" + crewId : name;
        _description = description;
    }

    /// <summary>
    /// Runs every turn in a scope of its own (GAP-25): <paramref name="loadCrew"/> registers
    /// the crew in that scope and returns its id, the scope's
    /// <see cref="ICrewOrchestrationService"/> kicks it off, and the scope -- the orchestrator,
    /// its repositories, the crew -- is disposed when the turn ends.
    /// </summary>
    /// <param name="scopes">The host's scope factory.</param>
    /// <param name="loadCrew">
    /// Registers the crew in the run's scope (adds its agents, tasks and crew to the scope's
    /// repositories, or loads it through the scope's <c>ICrewFactory</c>) and returns its id.
    /// </param>
    /// <param name="name">The agent name MAF displays; the agent id is <c>orkeon-crew-&lt;name&gt;</c>.</param>
    /// <param name="description">What the agent does, for MAF orchestrators that route by description.</param>
    public CrewAgent(
        IServiceScopeFactory scopes,
        Func<IServiceProvider, CancellationToken, Task<CrewId>> loadCrew,
        string name,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(loadCrew);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _kickoff = async (input, ct) =>
        {
            var scope = scopes.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var crewId = await loadCrew(scope.ServiceProvider, ct).ConfigureAwait(false);
                return await scope.ServiceProvider.GetRequiredService<ICrewOrchestrationService>()
                    .KickoffAsync(crewId, input, ct).ConfigureAwait(false);
            }
        };
        _crewId = null;
        _id = "orkeon-crew-" + name;
        _name = name;
        _description = description;
    }

    /// <summary>Wraps <paramref name="crew"/> (described by its goal); it must already be registered with the orchestrator's repositories.</summary>
    public CrewAgent(ICrewOrchestrationService orchestrator, Crew crew)
        : this(orchestrator, (crew ?? throw new ArgumentNullException(nameof(crew))).Id, null, crew.Goal?.ToString())
    {
    }

    /// <summary>
    /// The Orkeon crew this agent runs; <see langword="null"/> for an agent that loads its crew
    /// in each turn's scope (a fresh crew, with a fresh id, every turn).
    /// </summary>
    public CrewId? CrewId => _crewId;

    /// <inheritdoc />
    protected override string? IdCore => _id;

    /// <inheritdoc />
    public override string? Name => _name;

    /// <inheritdoc />
    public override string? Description => _description;

    /// <summary>The (stateless) session of a crew agent: MAF requires one, the crew keeps nothing in it.</summary>
    private sealed class CrewSession : AgentSession
    {
        public CrewSession()
        {
        }

        public CrewSession(AgentSessionStateBag stateBag) : base(stateBag)
        {
        }
    }

    /// <inheritdoc />
    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default) =>
        new(new CrewSession());

    /// <inheritdoc />
    protected override ValueTask<System.Text.Json.JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        System.Text.Json.JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new(session.StateBag.Serialize());
    }

    /// <inheritdoc />
    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        System.Text.Json.JsonElement serializedState,
        System.Text.Json.JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default) =>
        new(new CrewSession(AgentSessionStateBag.Deserialize(serializedState)));

    /// <inheritdoc />
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var input = Application.Interfaces.Services.CrewInput.Empty(ConversationToContext(messages));
        var output = await _kickoff(input, cancellationToken).ConfigureAwait(false);

        var reply = new ChatMessage(ChatRole.Assistant, output.FinalOutput) { AuthorName = _name };
        var response = new AgentResponse(reply)
        {
            AgentId = _id,
            ResponseId = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow,
            FinishReason = output.Succeeded ? ChatFinishReason.Stop : new ChatFinishReason("error"),
        };
        if (output.TokensUsed is { } tokens)
        {
            response.Usage = new UsageDetails
            {
                InputTokenCount = tokens.PromptTokens,
                OutputTokenCount = tokens.CompletionTokens,
                TotalTokenCount = tokens.TotalTokens,
            };
        }
        return response;
    }

    /// <inheritdoc />
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // A crew answers when it is done: the stream is the one final message. The deltas a
        // streamed crew run yields (KickoffStreamingAsync, GAP-32) are those of all its tasks —
        // ballots, rejected revisions and retried attempts included —, not the answer RunAsync
        // returns, so they are not forwarded. Callers that only know RunStreamingAsync work.
        var response = await RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);
        foreach (var update in response.ToAgentResponseUpdates())
            yield return update;
    }

    /// <summary>
    /// The crew's initial context: the last user message verbatim, preceded by the earlier
    /// turns as a quoted transcript so a multi-turn caller loses nothing.
    /// </summary>
    internal static string ConversationToContext(IEnumerable<ChatMessage> messages)
    {
        var list = messages.Where(m => !string.IsNullOrWhiteSpace(m.Text)).ToList();
        if (list.Count == 0)
            return string.Empty;
        if (list.Count == 1)
            return list[0].Text;

        var last = list[^1];
        var history = string.Join("\n", list.Take(list.Count - 1).Select(m => $"{m.Role.Value}: {m.Text}"));
        return $"Conversation so far:\n{history}\n\nRequest:\n{last.Text}";
    }
}
