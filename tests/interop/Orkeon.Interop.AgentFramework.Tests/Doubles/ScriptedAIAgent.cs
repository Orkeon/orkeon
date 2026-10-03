using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Orkeon.Interop.AgentFramework.Tests.Doubles;

/// <summary>
/// A MAF agent that echoes what it received, and counts its sessions and runs — how many were in
/// flight at once too. <see cref="Hold"/> keeps every run waiting, so a test can start a second call
/// while a first one is still running.
/// </summary>
public sealed class ScriptedAIAgent : AIAgent
{
    private int _inFlight;
    private int _maxInFlight;

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

    /// <summary>When set, what the agent answers to the messages of a run, in place of the echo.</summary>
    public Func<IReadOnlyList<ChatMessage>, string>? Respond { get; set; }

    public UsageDetails? Usage { get; set; }

    /// <summary>When set, every run waits for it before answering.</summary>
    public Task? Hold { get; set; }

    /// <summary>Completed once a run is waiting on <see cref="Hold"/>.</summary>
    public TaskCompletionSource RunWaiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The most runs that were in flight at the same time.</summary>
    public int MaxRunsInFlight => Volatile.Read(ref _maxInFlight);

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

    protected override async Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        lock (Runs)
            Runs.Add((list, session));

        // Counted before the first await: a run that is let in starts here, at once.
        var inFlight = Interlocked.Increment(ref _inFlight);
        int max;
        while (inFlight > (max = Volatile.Read(ref _maxInFlight)) && Interlocked.CompareExchange(ref _maxInFlight, inFlight, max) != max)
        {
        }

        try
        {
            if (Hold is { } hold)
            {
                RunWaiting.TrySetResult();
                await hold.WaitAsync(cancellationToken);
            }

            var text = Respond is { } respond ? respond(list) : Reply + " <- " + list[^1].Text;
            return new AgentResponse(new ChatMessage(ChatRole.Assistant, text)) { Usage = Usage };
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await RunCoreAsync(messages, session, options, cancellationToken);
        foreach (var update in response.ToAgentResponseUpdates())
            yield return update;
    }
}
