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
/// result of a read tool outside the last messages, of a turn the model has answered since, longer than twice the head, is offered to the decision model,
/// which sees the call (tool, input, size, age) and never the result: two yes/no questions per call, "still
/// needed" and "needed whole", give drop, head or keep. A pruned result leaves a line that says how to get it back,
/// its call stays, so the tool-call protocol stays paired. A result kept is judged again once it has aged
/// <see cref="RejudgeTurns"/> model turns. With the decision circuit off (one warning per run, no call), nothing is pruned.
/// Nothing here fails a task.
/// </summary>
internal sealed class ToolResultPruningStage : IContextPressureStage, IConversationPruner
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
    private readonly RunWarnings _warnings = new();

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
    internal int ThresholdTokens(ContextPressure pressure) => ThresholdTokens(pressure, _options.FillShare);

    private static int ThresholdTokens(ContextPressure pressure, double fillShare) =>
        Math.Min((int)Math.Floor(fillShare * Math.Max(0, pressure.BudgetTokens)), pressure.TriggerTokens);

    /// <summary>Where one pruning reads: its numbers, the task the decision model is told, the first message scanned.</summary>
    /// <param name="Options">The fill share, the recent messages kept and the head.</param>
    /// <param name="Task">The task as the decision model reads it, before the cut.</param>
    /// <param name="FirstIndex">The first message whose result may be offered.</param>
    private sealed record PruneScope(ToolResultPruningOptions Options, string Task, int FirstIndex);

    /// <summary>What one pruning asked and did.</summary>
    private sealed record PruneOutcome(int Threshold, int Offered, int Judged, int Kept, int Cut, int Dropped, int FreedTokens);

    /// <inheritdoc />
    public async ValueTask<bool> RelieveAsync(ContextPressure pressure, ConversationView conversation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pressure);
        ArgumentNullException.ThrowIfNull(conversation);
        if (!(pressure.AgentCompaction?.Prune ?? _options.Enabled))
            return false;
        // A crew loop: index 0 is the system prompt and index 1 the task, neither a tool result.
        var task = conversation.Count > 1 ? conversation.TextAt(1) : string.Empty;
        var outcome = await PruneAsync(pressure, conversation, new PruneScope(_options, task, 2), cancellationToken).ConfigureAwait(false);
        return outcome is { } done && done.Cut + done.Dropped > 0;
    }

    /// <summary>
    /// The pruning of an act's conversation (#461): the caller hands the task and the first message scanned, and the
    /// numbers it overrides; the window is resolved as a crew loop's is, and the fill share of the usable window
    /// alone sets the threshold, since an act does not compact. Asked by the caller, so <c>Enabled</c> is not read.
    /// </summary>
    public async ValueTask<ConversationPruneResponse?> PruneAsync(
        ConversationPruneRequest request, ActConversationView conversation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(conversation);
        // A crew run with decision.mode: none asks System One nothing (#429), an act of it included.
        if (SystemOneRunScope.Current?.Mode == Domain.Crew.CrewDecisionMode.None)
            return null;
        var options = new ToolResultPruningOptions
        {
            Enabled = true,
            FillShare = request.FillShare ?? _options.FillShare,
            KeepRecentMessages = request.KeepRecentMessages ?? _options.KeepRecentMessages,
            HeadChars = request.HeadChars ?? _options.HeadChars,
        };
        var window = Window(request);
        var reserved = ContextCompaction.ReservedOutput(request.MaxOutputTokens, request.Model, window.Tokens);
        var pressure = new ContextPressure
        {
            // An act runs on no task of a crew: the notice names none.
            TaskId = string.Empty,
            AgentRole = request.AgentRole,
            EstimatedTokens = request.EstimatedTokens,
            Window = window,
            ReservedOutputTokens = reserved,
            // No compaction trigger: the usable window, so the fill share alone sets the threshold.
            TriggerTokens = Math.Max(0, window.Tokens - reserved),
        };
        var outcome = await PruneAsync(pressure, conversation, new PruneScope(options, request.Task, request.FirstIndex), cancellationToken)
            .ConfigureAwait(false);
        return outcome is null
            ? null
            : new ConversationPruneResponse
            {
                ThresholdTokens = outcome.Threshold,
                Offered = outcome.Offered,
                Judged = outcome.Judged,
                Kept = outcome.Kept,
                Cut = outcome.Cut,
                Dropped = outcome.Dropped,
                FreedTokens = outcome.FreedTokens,
            };
    }

    /// <summary>The window of an act's call, as a crew loop resolves it; without a resolver, the model table, else the session's default.</summary>
    private ContextWindow Window(ConversationPruneRequest request)
    {
        if (_services.GetService<IContextWindowResolver>() is { } resolver)
            return resolver.Resolve(request.Profile, request.Model, request.ContextWindowTokens);
        if (request.ContextWindowTokens is > 0)
            return new ContextWindow(request.ContextWindowTokens.Value, ContextWindowSources.Profile);
        return Orkeon.Constants.Llm.LlmModelContextWindows.ContextWindowTokens(request.Model) is { } documented
            ? new ContextWindow(documented, ContextWindowSources.Table)
            : new ContextWindow(SessionWindowTokens, ContextWindowSources.Settings);
    }

    /// <summary>The window assumed without a resolver nor a documented one: the session setting's default.</summary>
    internal const int SessionWindowTokens = 200_000;

    /// <summary>
    /// One pruning: past the threshold, the candidates are asked in batches, all at once, and the verdicts applied.
    /// Null when nothing was asked: below the threshold, the circuit off, or nothing to offer.
    /// </summary>
    private async ValueTask<PruneOutcome?> PruneAsync(
        ContextPressure pressure, ConversationView conversation, PruneScope scope, CancellationToken cancellationToken)
    {
        var threshold = ThresholdTokens(pressure, scope.Options.FillShare);
        if (pressure.EstimatedTokens < threshold)
            return null;

        var circuit = DecisionCircuit.Of(_services, _scopeCircuit);
        var model = circuit.Enabled ? _services.GetService<IDecisionModel>() : null;
        if (model is null)
        {
            if (RunWarnings.Of(_warnings).First("prune.circuit-off"))
                PruneLog.CircuitOff(_logger, circuit.OffReason ?? DecisionCircuit.OffBy(DecisionCircuitCause.NoProvider).OffReason!);
            return null;
        }

        // The memory of one conversation is read and written by its own loop alone, one model turn after the other:
        // the batches below run at once, but none writes it; the loop does, once they are all back.
        var memory = _memories.GetValue(conversation, static _ => new Memory());
        var candidates = Candidates(conversation, memory, scope);
        if (candidates.Count == 0)
            return null;

        var request = Cut(scope.Task.Trim(), RequestChars);
        // The batches of one pressure are asked together (#461): the model turn waits for the slowest, not their sum.
        var answers = await System.Threading.Tasks.Task.WhenAll(
            candidates.Chunk(BatchCalls).Select(batch => AskAsync(model, request, batch, cancellationToken))).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var verdicts = new Dictionary<string, PruneVerdict>(StringComparer.Ordinal);
        foreach (var (batch, response, failure) in answers)
        {
            if (response is null)
            {
                // A batch the decision model failed on waits RejudgeTurns turns, as a kept one does, rather than
                // being asked again at the next turn (#434).
                PruneLog.JudgeFailed(_logger, pressure.AgentRole, batch.Length, failure ?? string.Empty);
                foreach (var candidate in batch)
                    memory.KeptAtAge[candidate.Call.CallId] = candidate.Age;
                continue;
            }
            foreach (var (callId, verdict) in Verdicts(batch, response))
                verdicts[callId] = verdict;
        }

        var tally = Apply(conversation, memory, candidates, verdicts, scope.Options.HeadChars);
        var outcome = new PruneOutcome(threshold, candidates.Count, verdicts.Count, tally.Kept, tally.Cut, tally.Dropped, tally.FreedTokens);
        // A turn that changed nothing is no event: agent.pruned says what was cut or dropped (#434).
        if (tally.Cut + tally.Dropped == 0)
            return outcome;

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
        return outcome;
    }

    /// <summary>One batch's request: its answer, or the message of its failure; the caller's cancellation propagates.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A batch the decision model fails on is kept and asked again later; nothing here fails a task.")]
    private static async System.Threading.Tasks.Task<(Candidate[] Batch, DecisionResponse? Response, string? Failure)> AskAsync(
        IDecisionModel model, string request, Candidate[] batch, CancellationToken cancellationToken)
    {
        try
        {
            return (batch, await model.DecideAsync(Question(request, batch), cancellationToken).ConfigureAwait(false), null);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return (batch, null, ex.Message);
        }
    }

    /// <summary>
    /// The results worth a verdict, oldest first, at most <see cref="MaxCalls"/>: a read tool's, before the last
    /// messages and of a turn the model has answered since, longer than twice the head, neither settled nor kept
    /// less than <see cref="RejudgeTurns"/> ago.
    /// </summary>
    private static List<Candidate> Candidates(ConversationView conversation, Memory memory, PruneScope scope)
    {
        var candidates = new List<Candidate>();
        var reachable = conversation.Count - scope.Options.KeepRecentMessages;
        var turnsAfter = new int[conversation.Count + 1];
        for (var i = conversation.Count - 1; i >= 0; i--)
            turnsAfter[i] = turnsAfter[i + 1] + (conversation.RoleAt(i) == ConversationView.AssistantRole ? 1 : 0);

        // Before the first index: a crew loop's system prompt and task, an act's seed and prompt (#461).
        for (var i = Math.Max(0, scope.FirstIndex); i < reachable && candidates.Count < MaxCalls; i++)
        {
            if (conversation.RoleAt(i) != ConversationView.ToolRole
                || conversation.ToolCallAt(i) is not { IsRead: true } call
                || memory.Settled.Contains(call.CallId))
                continue;
            var chars = conversation.TextAt(i).Length;
            var age = turnsAfter[i];
            // A result of a turn the model has not answered yet is one it has not read: never offered, however many
            // results that turn holds (#434).
            if (age == 0
                || chars <= 2 * scope.Options.HeadChars
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
    /// whole, keep. A call an answer is missing for is left out, and waits as a kept one does.
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

    private static Tally Apply(
        ConversationView conversation, Memory memory, List<Candidate> candidates, Dictionary<string, PruneVerdict> verdicts, int headChars)
    {
        int kept = 0, cut = 0, dropped = 0;
        long freedChars = 0;
        foreach (var candidate in candidates)
        {
            // An answer with no usable verdict waits RejudgeTurns turns, as a kept result does, rather than being
            // asked again before every model call (#434).
            if (!verdicts.TryGetValue(candidate.Call.CallId, out var verdict))
            {
                memory.KeptAtAge[candidate.Call.CallId] = candidate.Age;
                continue;
            }
            if (verdict == PruneVerdict.Keep)
            {
                kept++;
                memory.KeptAtAge[candidate.Call.CallId] = candidate.Age;
                continue;
            }

            // The note carries the call's whole input, so the agent can make the same call again (#434).
            var input = candidate.Call.Input;
            var content = conversation.TextAt(candidate.Index);
            var head = TextCut.HeadLength(content, headChars);
            var pruned = verdict == PruneVerdict.Drop
                ? PrunePrompts.Dropped(candidate.Call.ToolName, input, content.Length)
                : string.Concat(content.AsSpan(0, head),
                    PrunePrompts.Headed(candidate.Call.ToolName, input, content.Length - head, content.Length));
            memory.Settled.Add(candidate.Call.CallId);
            if (pruned.Length >= content.Length || !conversation.ReplaceToolResult(candidate.Index, pruned))
                continue;
            freedChars += content.Length - pruned.Length;
            if (verdict == PruneVerdict.Drop) dropped++;
            else cut++;
        }
        return new Tally(kept, cut, dropped, (int)Math.Min(int.MaxValue, freedChars / 4));
    }

    private static string Cut(string text, int max) => TextCut.Head(text, max, PrunePrompts.Ellipsis);
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
