using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Crew.Compaction;

/// <summary>
/// The pruning of a loop's older read-tool results (#434), the YAML loops' counterpart of a script's
/// <c>onContextPressure</c>, run as the first <see cref="IContextPressureStage"/> before compaction. Once the
/// estimate reaches <see cref="ToolResultPruningOptions.FillShare"/> of the usable window (or the trigger), each
/// result of a read tool outside the last messages, longer than twice the head, is offered to the decision model,
/// which sees the call (tool, input, size, age) and never the result: two yes/no questions per call, "still
/// needed" and "needed whole", give drop, head or keep. A pruned result leaves a line that says how to get it back,
/// its call stays, so the tool-call protocol stays paired. A result kept is judged again once it has aged
/// <see cref="RejudgeTurns"/> model turns. With the decision circuit off (one warning, no call), nothing is pruned.
/// Nothing here fails a task.
/// </summary>
internal sealed class ToolResultPruningStage : IContextPressureStage
{
    /// <summary>The probability of a yes from which a call counts as needed, or needed whole.</summary>
    internal const double YesProbability = 0.5;

    /// <summary>Calls judged by one request to the decision model: two questions each.</summary>
    internal const int BatchCalls = 8;

    /// <summary>Calls judged at one pressure, oldest first; the rest is offered at the next model call.</summary>
    internal const int MaxCalls = 3 * BatchCalls;

    /// <summary>Model turns a result kept waits before it is judged again: what a task needs does not change at every call.</summary>
    internal const int RejudgeTurns = 8;

    /// <summary>The task, as the decision model reads it, in characters.</summary>
    internal const int RequestChars = 2_000;

    /// <summary>One call's input, as the decision model and the pruned line read it, in characters.</summary>
    internal const int InputChars = 200;

    /// <summary>The name of the "still needed" question of call <c>n</c>.</summary>
    internal static string NeededName(int number) => $"needed_{number}";

    /// <summary>The name of the "needed whole" question of call <c>n</c>.</summary>
    internal static string WholeName(int number) => $"whole_{number}";

    private readonly IServiceProvider _services;
    private readonly ToolResultPruningOptions _options;
    private readonly DecisionCircuit? _scopeCircuit;
    private readonly IAgentPruneObserver? _observer;
    private readonly ILogger _logger;
    private readonly ConditionalWeakTable<ConversationView, Memory> _memories = [];
    private int _offWarned;

    /// <param name="services">The container whose decision circuit and model the stage uses.</param>
    /// <param name="options">The pruning settings.</param>
    /// <param name="scopeCircuit">The circuit of the run's scope when it is not the container's (#431); null follows the container's.</param>
    /// <param name="observer">Told of each pruning (<c>agent.pruned</c>); null logs it alone.</param>
    /// <param name="logger">Where the warnings and the tally go.</param>
    public ToolResultPruningStage(
        IServiceProvider services, ToolResultPruningOptions options, DecisionCircuit? scopeCircuit,
        IAgentPruneObserver? observer, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _services = services;
        _options = options;
        _scopeCircuit = scopeCircuit;
        _observer = observer;
        _logger = logger;
    }

    /// <summary>What a loop's results became, by call id: settled (cut or removed) or kept at an age.</summary>
    private sealed class Memory
    {
        internal HashSet<string> Settled { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, int> KeptAtAge { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>One result offered to the decision model.</summary>
    internal sealed record Candidate(int Index, ConversationToolCall Call, int ResultChars, int Age);

    /// <summary>The estimate from which results are judged: the fill share of the usable window, at most the trigger.</summary>
    internal int ThresholdTokens(ContextPressure pressure) =>
        Math.Min((int)Math.Floor(_options.FillShare * Math.Max(0, pressure.BudgetTokens)), pressure.TriggerTokens);

    /// <inheritdoc />
    public async ValueTask<bool> RelieveAsync(ContextPressure pressure, ConversationView conversation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pressure);
        ArgumentNullException.ThrowIfNull(conversation);
        if (!(pressure.AgentCompaction?.Prune ?? _options.Enabled))
            return false;
        var threshold = ThresholdTokens(pressure);
        if (pressure.EstimatedTokens < threshold)
            return false;

        var circuit = _scopeCircuit is { Enabled: false } ? _scopeCircuit : DecisionCircuit.Of(_services);
        var model = circuit.Enabled ? _services.GetService<IDecisionModel>() : null;
        if (model is null)
        {
            if (Interlocked.Exchange(ref _offWarned, 1) == 0)
                PruneLog.CircuitOff(_logger, circuit.OffReason ?? DecisionCircuit.OffBy(DecisionCircuitCause.NoProvider).OffReason!);
            return false;
        }

        var memory = _memories.GetValue(conversation, static _ => new Memory());
        var candidates = Candidates(conversation, memory);
        if (candidates.Count == 0)
            return false;

        var request = Cut(conversation.Count > 1 ? conversation.TextAt(1).Trim() : string.Empty, RequestChars);
        var verdicts = new Dictionary<string, PruneVerdict>(StringComparer.Ordinal);
        foreach (var batch in candidates.Chunk(BatchCalls))
        {
            try
            {
                var response = await model.DecideAsync(Question(request, batch), cancellationToken).ConfigureAwait(false);
                foreach (var (callId, verdict) in Verdicts(batch, response))
                    verdicts[callId] = verdict;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                PruneLog.JudgeFailed(_logger, pressure.AgentRole, batch.Length, ex.Message);
            }
        }

        var tally = Apply(conversation, memory, candidates, verdicts);
        PruneLog.Pruned(_logger, pressure.AgentRole, pressure.TaskId, candidates.Count, verdicts.Count, tally.Dropped, tally.Cut, tally.FreedTokens);
        _observer?.Pruned(new AgentPruneNotice
        {
            TaskId = pressure.TaskId,
            AgentRole = pressure.AgentRole,
            EstimatedTokens = pressure.EstimatedTokens,
            ThresholdTokens = threshold,
            Offered = candidates.Count,
            Judged = verdicts.Count,
            Kept = tally.Kept,
            Cut = tally.Cut,
            Dropped = tally.Dropped,
            FreedTokens = tally.FreedTokens,
        });
        return tally.Cut + tally.Dropped > 0;
    }

    /// <summary>
    /// The results worth a verdict, oldest first, at most <see cref="MaxCalls"/>: a read tool's, before the last
    /// messages, longer than twice the head, neither settled nor kept less than <see cref="RejudgeTurns"/> ago.
    /// </summary>
    private List<Candidate> Candidates(ConversationView conversation, Memory memory)
    {
        var candidates = new List<Candidate>();
        var reachable = conversation.Count - _options.KeepRecentMessages;
        var turnsAfter = new int[conversation.Count + 1];
        for (var i = conversation.Count - 1; i >= 0; i--)
            turnsAfter[i] = turnsAfter[i + 1] + (conversation.RoleAt(i) == ConversationView.AssistantRole ? 1 : 0);

        // Index 0 is the system prompt and index 1 the task: neither is a tool result.
        for (var i = 2; i < reachable && candidates.Count < MaxCalls; i++)
        {
            if (conversation.RoleAt(i) != ConversationView.ToolRole
                || conversation.ToolCallAt(i) is not { IsRead: true } call
                || memory.Settled.Contains(call.CallId))
                continue;
            var chars = conversation.TextAt(i).Length;
            var age = turnsAfter[i];
            if (chars <= 2 * _options.HeadChars
                || (memory.KeptAtAge.TryGetValue(call.CallId, out var keptAt) && age - keptAt < RejudgeTurns))
                continue;
            candidates.Add(new Candidate(i, call, chars, age));
        }
        return candidates;
    }

    /// <summary>The state and the two questions of each call of <paramref name="batch"/>, numbered from 1.</summary>
    internal static DecisionRequest Question(string request, IReadOnlyList<Candidate> batch)
    {
        var lines = batch.Select((c, i) => PrunePrompts.CallLine(i + 1, c.Call.ToolName, Cut(c.Call.Input, InputChars), c.ResultChars, c.Age));
        var questions = ImmutableDictionary.CreateBuilder<string, DecisionQuestion>(StringComparer.Ordinal);
        for (var i = 0; i < batch.Count; i++)
        {
            questions[NeededName(i + 1)] = new NoulQuestion { Instructions = JsonValue.Create(PrunePrompts.StillNeeded(i + 1)) };
            questions[WholeName(i + 1)] = new NoulQuestion { Instructions = JsonValue.Create(PrunePrompts.NeededWhole(i + 1)) };
        }
        return new DecisionRequest
        {
            State = JsonValue.Create(PrunePrompts.State(request, string.Join('\n', lines)))!,
            Questions = questions.ToImmutable(),
        };
    }

    /// <summary>
    /// The verdict of each call the answers settle, by call id: not needed, drop; needed but not whole, head; needed
    /// whole, keep. A call an answer is missing for is left out.
    /// </summary>
    internal static IEnumerable<(string CallId, PruneVerdict Verdict)> Verdicts(IReadOnlyList<Candidate> batch, DecisionResponse response)
    {
        for (var i = 0; i < batch.Count; i++)
        {
            if (Yes(response, NeededName(i + 1)) is not { } needed || Yes(response, WholeName(i + 1)) is not { } whole)
                continue;
            yield return (batch[i].Call.CallId, !needed ? PruneVerdict.Drop : whole ? PruneVerdict.Keep : PruneVerdict.Head);
        }
    }

    private static bool? Yes(DecisionResponse response, string name) =>
        response.Answers.TryGetValue(name, out var answer) && answer is NoulAnswer noul ? noul.Noul >= YesProbability : null;

    private readonly record struct Tally(int Kept, int Cut, int Dropped, int FreedTokens);

    private Tally Apply(ConversationView conversation, Memory memory, List<Candidate> candidates, Dictionary<string, PruneVerdict> verdicts)
    {
        int kept = 0, cut = 0, dropped = 0;
        long freedChars = 0;
        foreach (var candidate in candidates)
        {
            if (!verdicts.TryGetValue(candidate.Call.CallId, out var verdict))
                continue;
            if (verdict == PruneVerdict.Keep)
            {
                kept++;
                memory.KeptAtAge[candidate.Call.CallId] = candidate.Age;
                continue;
            }

            var input = Cut(candidate.Call.Input, InputChars);
            var content = conversation.TextAt(candidate.Index);
            var pruned = verdict == PruneVerdict.Drop
                ? PrunePrompts.Dropped(candidate.Call.ToolName, input, content.Length)
                : string.Concat(content.AsSpan(0, _options.HeadChars),
                    PrunePrompts.Headed(candidate.Call.ToolName, input, content.Length - _options.HeadChars, content.Length));
            memory.Settled.Add(candidate.Call.CallId);
            if (pruned.Length >= content.Length || !conversation.ReplaceToolResult(candidate.Index, pruned))
                continue;
            freedChars += content.Length - pruned.Length;
            if (verdict == PruneVerdict.Drop) dropped++;
            else cut++;
        }
        return new Tally(kept, cut, dropped, (int)Math.Min(int.MaxValue, freedChars / 4));
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max] + PrunePrompts.Ellipsis;
}

/// <summary>What becomes of one read-tool result (#434), as the script's <c>onContextPressure</c> answers it.</summary>
internal enum PruneVerdict
{
    /// <summary>The result stays whole.</summary>
    Keep,

    /// <summary>The result is cut to its first characters.</summary>
    Head,

    /// <summary>The result is removed, a line saying how to get it back.</summary>
    Drop,
}
