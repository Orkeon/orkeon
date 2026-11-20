using System.Text;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;

namespace Orkeon.Application.Crew.Compaction;

/// <summary>A summary call (#432): its text, the tokens it used, or the reason it failed.</summary>
/// <param name="Text">The summary; null or blank when the model answered nothing.</param>
/// <param name="TokensUsed">The tokens the call used, charged to the task.</param>
/// <param name="Error">Why the call failed, or null.</param>
internal readonly record struct CompactionSummary(string? Text, int TokensUsed, string? Error);

/// <summary>Writes the summary of <paramref name="transcript"/> under <paramref name="instructions"/>, with no tool.</summary>
internal delegate System.Threading.Tasks.Task<CompactionSummary> SummaryCall(
    string instructions, string transcript, CancellationToken cancellationToken);

/// <summary>What a <see cref="ContextCompactor"/> works with, fixed for one loop execution.</summary>
internal sealed record ContextCompactorSetup
{
    internal required ContextWindow Window { get; init; }
    internal required int ReservedOutputTokens { get; init; }
    internal required double TriggerShare { get; init; }
    internal required int KeepRecentTurns { get; init; }
    internal required string TaskId { get; init; }
    internal required string AgentRole { get; init; }
    internal Domain.Agent.AgentCompaction? AgentCompaction { get; init; }
    internal ITokenCounter? TokenCounter { get; init; }
    internal IReadOnlyList<IContextPressureStage> Stages { get; init; } = [];
    internal IAgentCompactionObserver? Observer { get; init; }
    internal required ILogger Logger { get; init; }
}

/// <summary>
/// The compaction of one loop execution (#432). Before each model call the loop hands it the
/// conversation (<see cref="BeforeCallAsync"/>): it estimates the prompt (the last call's prompt
/// tokens plus a count of the messages added since, or a count of the whole conversation when no
/// usage is known), runs the pressure stages, and past the trigger replaces the middle of the
/// conversation with one summary. After each call the loop reports the prompt tokens the provider
/// counted (<see cref="Observe"/>). Three failures in a row open the breaker: the loop then trims
/// its conversation as it did before compaction (<see cref="ReplacesTrim"/> turns false).
/// </summary>
internal sealed class ContextCompactor
{
    /// <summary>The failures in a row that open the breaker.</summary>
    internal const int MaxConsecutiveFailures = 3;

    // A message's framing (role, separators) in the provider's count, beside its text.
    private const int MessageOverheadTokens = 4;

    private readonly ContextCompactorSetup _setup;
    private readonly SummaryCall _summarize;
    private int? _lastPromptTokens;
    private int _messagesAtLastCall;
    private int _consecutiveFailures;
    private bool _reverted;

    internal ContextCompactor(ContextCompactorSetup setup, SummaryCall summarize)
    {
        _setup = setup;
        _summarize = summarize;
        TriggerTokens = (int)Math.Floor(setup.TriggerShare * (setup.Window.Tokens - setup.ReservedOutputTokens));
    }

    /// <summary>The estimate at which the loop compacts.</summary>
    internal int TriggerTokens { get; }

    /// <summary>
    /// Whether compaction stands in for the 40-message trim: true until the breaker opens, after which
    /// the loop trims its conversation for the rest of the attempt.
    /// </summary>
    internal bool ReplacesTrim => !_reverted;

    /// <summary>The tokens the summary calls used so far, charged to the task.</summary>
    internal int SpentTokens { get; private set; }

    /// <summary>
    /// A call was made over the first <paramref name="messagesSent"/> messages, and the provider counted
    /// <paramref name="promptTokens"/> prompt tokens (null when it reported no usage).
    /// </summary>
    internal void Observe(int? promptTokens, int messagesSent)
    {
        _lastPromptTokens = promptTokens is > 0 ? promptTokens : null;
        _messagesAtLastCall = messagesSent;
    }

    /// <summary>The estimated prompt of a call over <paramref name="conversation"/> as it stands.</summary>
    internal int Estimate(ConversationView conversation)
    {
        if (_lastPromptTokens is not { } reported || _messagesAtLastCall > conversation.Count)
            return Count(conversation, 0);
        return reported + Count(conversation, _messagesAtLastCall);
    }

    /// <summary>
    /// Measures <paramref name="conversation"/> before a call, runs the pressure stages, and compacts
    /// it when the estimate reaches the trigger. Returns the tokens this step's summary call used.
    /// </summary>
    internal async System.Threading.Tasks.Task<int> BeforeCallAsync(ConversationView conversation, CancellationToken cancellationToken)
    {
        if (_reverted)
            return 0;

        var estimate = Estimate(conversation);
        foreach (var stage in _setup.Stages)
        {
            if (await stage.RelieveAsync(Pressure(estimate), conversation, cancellationToken).ConfigureAwait(false))
            {
                Rewritten();
                estimate = Count(conversation, 0);
            }
        }

        if (estimate < TriggerTokens)
            return 0;

        return await CompactAsync(conversation, estimate, cancellationToken).ConfigureAwait(false);
    }

    private async System.Threading.Tasks.Task<int> CompactAsync(ConversationView conversation, int before, CancellationToken cancellationToken)
    {
        var cut = CutIndex(conversation);
        if (cut <= 2)
            return Failed(before, before, conversation.Count, 0, "the recent turns are all that is left to keep");

        var summary = await _summarize(CompactionPrompts.Instructions,
            CompactionPrompts.Transcript(Transcript(conversation, 2, cut)), cancellationToken).ConfigureAwait(false);
        SpentTokens += summary.TokensUsed;

        if (summary.Error is { } error)
            return summary.TokensUsed + Failed(before, before, conversation.Count, summary.TokensUsed, $"the summary call failed: {error}");
        if (string.IsNullOrWhiteSpace(summary.Text))
            return summary.TokensUsed + Failed(before, before, conversation.Count, summary.TokensUsed, "the summary came back empty");

        var replaced = cut - 2;
        conversation.ReplaceRange(2, replaced, CompactionPrompts.SummaryMessage(replaced, summary.Text.Trim()));
        Rewritten();
        var after = Count(conversation, 0);
        var kept = conversation.Count - 1;

        if (after >= TriggerTokens)
            return summary.TokensUsed + Failed(before, after, kept, summary.TokensUsed, "the conversation is still at or over the trigger after it");

        _consecutiveFailures = 0;
        CompactionLog.LogCompacted(_setup.Logger, _setup.AgentRole, _setup.TaskId, replaced, before, after, _setup.Window.Tokens);
        Notify(AgentCompactionOutcomes.Compacted, before, after, kept, summary.TokensUsed);
        return summary.TokensUsed;
    }

    // Returns 0 so a failure path adds nothing beside the summary's own tokens.
    private int Failed(int before, int after, int kept, int summaryTokens, string reason)
    {
        _consecutiveFailures++;
        if (_consecutiveFailures >= MaxConsecutiveFailures)
        {
            _reverted = true;
            CompactionLog.LogReverted(_setup.Logger, _setup.AgentRole, _setup.TaskId, _consecutiveFailures, reason);
            Notify(AgentCompactionOutcomes.Reverted, before, after, kept, summaryTokens);
        }
        else
        {
            CompactionLog.LogFailed(_setup.Logger, _setup.AgentRole, _setup.TaskId, _consecutiveFailures, reason);
            Notify(AgentCompactionOutcomes.Failed, before, after, kept, summaryTokens);
        }
        return 0;
    }

    /// <summary>
    /// Where the kept turns start: the <c>KeepRecentTurns</c>-th assistant message from the end, so a
    /// tool result always stays with the call before it; the end of the conversation when none is kept.
    /// </summary>
    private int CutIndex(ConversationView conversation)
    {
        var keep = _setup.KeepRecentTurns;
        if (keep == 0)
            return conversation.Count;

        var seen = 0;
        for (var i = conversation.Count - 1; i >= 2; i--)
        {
            if (conversation.RoleAt(i) == ConversationView.AssistantRole && ++seen == keep)
                return i;
        }
        return 2;
    }

    private static string Transcript(ConversationView conversation, int start, int end)
    {
        var text = new StringBuilder();
        for (var i = start; i < end; i++)
            text.Append('[').Append(conversation.RoleAt(i)).Append("] ").Append(conversation.TextAt(i)).Append("\n\n");
        return text.ToString();
    }

    private int Count(ConversationView conversation, int from)
    {
        var total = 0;
        for (var i = from; i < conversation.Count; i++)
        {
            var text = conversation.TextAt(i);
            total += MessageOverheadTokens + (_setup.TokenCounter?.CountTokens(text) ?? (text.Length + 3) / 4);
        }
        return total;
    }

    private void Rewritten() => _lastPromptTokens = null;

    private ContextPressure Pressure(int estimate) => new()
    {
        TaskId = _setup.TaskId,
        AgentRole = _setup.AgentRole,
        AgentCompaction = _setup.AgentCompaction,
        EstimatedTokens = estimate,
        Window = _setup.Window,
        ReservedOutputTokens = _setup.ReservedOutputTokens,
        TriggerTokens = TriggerTokens,
    };

    private void Notify(string outcome, int before, int after, int kept, int summaryTokens) =>
        _setup.Observer?.Compacted(new AgentCompactionNotice
        {
            TaskId = _setup.TaskId,
            AgentRole = _setup.AgentRole,
            Outcome = outcome,
            TokensBefore = before,
            TokensAfter = after,
            Window = _setup.Window.Tokens,
            WindowSource = _setup.Window.Source,
            KeptMessages = kept,
            SummaryTokens = summaryTokens,
        });
}
