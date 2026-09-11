using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Orkeon.Interop.AgentFramework.Tests.Doubles;

/// <summary>A MAF agent that echoes what it received, and counts its sessions and runs.</summary>
public sealed class ScriptedAIAgent : AIAgent
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

    public int SessionsCreated { get; private set; }

    public List<(IReadOnlyList<ChatMessage> Messages, AgentSession? Session)> Runs { get; } = [];

    public string Reply { get; set; } = "scripted reply";

    public UsageDetails? Usage { get; set; }

    protected override string? IdCore => "scripted-agent";

    public override string? Name => "Scripted";

    public override string? Description => "Echoes what it is told";

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
    {
        SessionsCreated++;
        return new(new Session());
    }

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default) =>
        new(session.StateBag.Serialize());

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedState, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default) =>
        new(new Session(AgentSessionStateBag.Deserialize(serializedState)));

    protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        Runs.Add((list, session));
        var response = new AgentResponse(new ChatMessage(ChatRole.Assistant, Reply + " <- " + list[^1].Text)) { Usage = Usage };
        return Task.FromResult(response);
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await RunCoreAsync(messages, session, options, cancellationToken);
        foreach (var update in response.ToAgentResponseUpdates())
            yield return update;
    }
}
