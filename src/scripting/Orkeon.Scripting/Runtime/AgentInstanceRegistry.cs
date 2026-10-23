using System.Globalization;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Builders;

namespace Orkeon.Scripting.Runtime;

/// <summary>What one run of an agent instance carries into its context: the registry, its id and the transcript it resumes from.</summary>
internal sealed record AgentInstanceRun(AgentInstanceRegistry Registry, string Id, IReadOnlyList<LlmMessage> Seed);

/// <summary>What <see cref="AgentInstanceRegistry.BeginSend"/> hands the follow-up run: the agent, its token, its fresh child budget and its context binding.</summary>
internal sealed record AgentInstanceSend(JsAgent Agent, AgentExecutionBudget Budget, AgentInstanceRun Run, CancellationToken Signal);

/// <summary>The addressable child agents of a crew (RT-03): ids, states and the transcript each keeps.</summary>
internal sealed class AgentInstanceRegistry(int maxInstances)
{
    /// <summary>The most messages one instance keeps in its transcript.</summary>
    internal const int MaxTranscriptMessages = 40;

    /// <summary>The most characters (sum of message contents) one instance keeps in its transcript.</summary>
    internal const int MaxTranscriptCharacters = 32_000;

    private readonly Lock _sync = new();
    private readonly List<Entry> _entries = [];
    private int _last;

    /// <summary>The transcript cut to both caps, the oldest messages dropped first.</summary>
    internal static IReadOnlyList<LlmMessage> BoundTranscript(IReadOnlyList<LlmMessage> transcript)
    {
        var kept = 0;
        var characters = 0;
        for (var i = transcript.Count - 1; i >= 0 && kept < MaxTranscriptMessages; i--)
        {
            characters += transcript[i].Content.Length;
            if (characters > MaxTranscriptCharacters) break;
            kept++;
        }

        return [.. transcript.Skip(transcript.Count - kept)];
    }

    /// <summary>Adds a running instance of <paramref name="agent"/>, whose sends count against the <paramref name="budget"/> it runs under; returns its id. The oldest ended instance goes when the cap is exceeded.</summary>
    internal string Register(JsAgent agent, AgentExecutionBudget budget, CancellationTokenSource cancellation)
    {
        lock (_sync)
        {
            var id = "a" + (++_last).ToString(CultureInfo.InvariantCulture);
            _entries.Add(new Entry(id, agent, budget) { Cancellation = cancellation });
            Evict();
            return id;
        }
    }

    /// <summary>Ends a running instance as completed or failed; a stopped, ended or unknown one is left as it is.</summary>
    internal void Finish(string id, bool succeeded)
    {
        lock (_sync)
        {
            if (Find(id) is not { State: State.Running } entry) return;
            entry.State = succeeded ? State.Completed : State.Failed;
            entry.Cancellation?.Dispose();
            entry.Cancellation = null;
            Evict();
        }
    }

    /// <summary>Cancels a running instance at once; <c>false</c> when there is none running under that id.</summary>
    internal bool Stop(string id)
    {
        lock (_sync)
        {
            if (Find(id) is not { State: State.Running } entry) return false;
            entry.State = State.Stopped;
            entry.Cancellation!.Cancel();
            entry.Cancellation.Dispose();
            entry.Cancellation = null;
            return true;
        }
    }

    /// <summary>
    /// Starts a follow-up run of an ended instance: draws a spawn and a child budget from the one it runs under
    /// (refused past <see cref="AgentExecutionBudget.MaxSpawnedAgents"/>) and marks it running, which
    /// refuses a second send until <see cref="Finish"/>.
    /// </summary>
    internal AgentInstanceSend BeginSend(string id)
    {
        lock (_sync)
        {
            var entry = Find(id) ?? throw new InvalidOperationException($"No agent instance '{id}': it never started, or it was evicted.");
            if (entry.State == State.Running)
                throw new InvalidOperationException($"Agent instance '{id}' is running: send to it once it has ended.");
            entry.Allowance.RecordSpawn();
            var budget = entry.Budget.CreateChildBudget();
            entry.State = State.Running;
            entry.Cancellation = new CancellationTokenSource();
            return new AgentInstanceSend(entry.Agent, budget, new AgentInstanceRun(this, id, entry.Transcript), entry.Cancellation.Token);
        }
    }

    /// <summary>
    /// Stores what an ended act exchanged as the instance's transcript, system messages left out. An act
    /// ended by an interrupt loses its trailing assistant turn: the tool calls it made have no results.
    /// </summary>
    internal void Keep(string id, IReadOnlyList<LlmMessage> messages, bool interrupted)
    {
        var transcript = messages.Where(m => m.Role != "system").ToList();
        if (interrupted && transcript is [.., { Role: "assistant", RawToolCalls: not null }])
            transcript.RemoveAt(transcript.Count - 1);
        lock (_sync)
        {
            if (Find(id) is { } entry) entry.Transcript = BoundTranscript(transcript);
        }
    }

    /// <summary>The kept instances, oldest first, with the id of their agent and their state label.</summary>
    internal List<(string Id, string Agent, string State)> List()
    {
        lock (_sync)
        {
            return [.. _entries.Select(e => (e.Id, e.Agent.id, e.State switch
            {
                State.Running => "running",
                State.Completed => "completed",
                State.Failed => "failed",
                _ => "stopped",
            }))];
        }
    }

    private Entry? Find(string id) => _entries.Find(e => string.Equals(e.Id, id, StringComparison.Ordinal));

    private void Evict()
    {
        while (_entries.Count > maxInstances)
        {
            var oldestEnded = _entries.FindIndex(e => e.State != State.Running);
            if (oldestEnded < 0) return;
            _entries.RemoveAt(oldestEnded);
        }
    }

    private enum State { Running, Completed, Failed, Stopped }

    private sealed class Entry(string id, JsAgent agent, AgentExecutionBudget budget)
    {
        internal string Id { get; } = id;
        internal JsAgent Agent { get; } = agent;
        internal AgentExecutionBudget Budget { get; } = budget;

        // The run that started the instance is one of the spawns its budget allows; each send takes another.
        internal AgentExecutionBudget Allowance { get; } = new() { MaxSpawnedAgents = Math.Max(0, budget.MaxSpawnedAgents - 1) };
        internal State State { get; set; } = State.Running;
        internal CancellationTokenSource? Cancellation { get; set; }
        internal IReadOnlyList<LlmMessage> Transcript { get; set; } = [];
    }
}
