using System.Text;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;

namespace Orkeon.Application.Crew.Compaction;

/// <summary>A summary call (#432): its text, the tokens it used, or the reason it failed.</summary>
/// <param name="Text">The summary; null or blank when the model answered nothing.</param>
/// <param name="TokensUsed">The tokens the call used, charged to the task.</param>
/// <param name="Error">Why the call failed, or null.</param>
/// <param name="PromptTokens">The prompt side of <paramref name="TokensUsed"/>, 0 when the provider split nothing.</param>
/// <param name="CompletionTokens">The completion side of <paramref name="TokensUsed"/>, 0 when the provider split nothing.</param>
internal readonly record struct CompactionSummary(
    string? Text, int TokensUsed, string? Error, int PromptTokens = 0, int CompletionTokens = 0);

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
/// counted (<see cref="Observe"/>): what they hold beyond the messages (the tool definitions, the
/// framing) is kept as the overhead every recount of the messages adds. Three failures in a row open the breaker: the loop then trims
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
    private int _overheadTokens;
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
    /// the loop trims its conversation for the rest of the attempt. False from the start when the window
    /// is the settings fallback (<see cref="ContextWindowSources.Settings"/>): the model's own window is
    /// not known and may be far smaller, so the loop trims as well as it compacts.
    /// </summary>
    internal bool ReplacesTrim => !_reverted && _setup.Window.Source != ContextWindowSources.Settings;

    /// <summary>The tokens the summary calls used so far, charged to the task.</summary>
    internal int SpentTokens { get; private set; }

    /// <summary>The prompt side of <see cref="SpentTokens"/>, as the providers split it.</summary>
    internal int SpentPromptTokens { get; private set; }

    /// <summary>The completion side of <see cref="SpentTokens"/>, as the providers split it.</summary>
    internal int SpentCompletionTokens { get; private set; }

    /// <summary>
    /// A call was made over <paramref name="conversation"/> as it stands, and the provider counted
    /// <paramref name="promptTokens"/> prompt tokens (null when it reported no usage). What that count
    /// holds beyond the messages is kept as the prompt's overhead (never below 0), which every later
    /// recount adds; a call with no usage keeps the overhead last measured. The overhead also holds the
    /// drift between the provider's tokenizer and the estimate of the messages: right after a summary, a
    /// recount adds the drift measured on the long conversation to the short one, so the estimate can
    /// stay high until the next call's count corrects it.
    /// </summary>
    internal void Observe(int? promptTokens, ConversationView conversation)
    {
        _lastPromptTokens = promptTokens is > 0 ? promptTokens : null;
        _messagesAtLastCall = conversation.Count;
        if (_lastPromptTokens is { } reported)
            _overheadTokens = Math.Max(0, reported - Count(conversation, 0));
    }

    /// <summary>The estimated prompt of a call over <paramref name="conversation"/> as it stands.</summary>
    internal int Estimate(ConversationView conversation)
    {
        if (_lastPromptTokens is not { } reported || _messagesAtLastCall > conversation.Count)
            return Recount(conversation);
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
                estimate = Recount(conversation);
            }
        }

        if (estimate < TriggerTokens)
            return 0;

        return await CompactAsync(conversation, estimate, cancellationToken).ConfigureAwait(false);
    }

    private async System.Threading.Tasks.Task<int> CompactAsync(ConversationView conversation, int before, CancellationToken cancellationToken)
    {
        // The recent turns kept are all there is, or alone are over the trigger: fewer are kept, down to the last
        // turn, so one summary call can bring the conversation under it. Only when the last turn is all there is
        // nothing failed, and a later turn gives the cut its room.
        var cut = CutIndex(conversation, _setup.KeepRecentTurns);
        if (cut <= 2 || !KeptUnderTrigger(conversation, cut))
            cut = Math.Max(cut, ShrunkCutIndex(conversation));
        if (cut <= 2)
        {
            CompactionLog.LogNothingToCut(_setup.Logger, _setup.AgentRole, _setup.TaskId, before, TriggerTokens);
            return 0;
        }

        var transcript = Transcript(conversation, 2, cut);
        var summary = await _summarize(CompactionPrompts.Instructions,
            CompactionPrompts.Transcript(conversation.TextAt(1), transcript), cancellationToken).ConfigureAwait(false);
        SpentTokens += summary.TokensUsed;
        SpentPromptTokens += summary.PromptTokens;
        SpentCompletionTokens += summary.CompletionTokens;

        if (summary.Error is { } error)
            return summary.TokensUsed + Failed(before, before, conversation.Count, summary.TokensUsed, $"the summary call failed: {error}");
        if (string.IsNullOrWhiteSpace(summary.Text))
            return summary.TokensUsed + Failed(before, before, conversation.Count, summary.TokensUsed, "the summary came back empty");

        // A system message of the span (the soft-budget steering) is an instruction, not history: it stays, after the summary.
        var replaced = cut - 2 - SystemMessages(conversation, 2, cut);
        conversation.ReplaceRange(2, cut - 2, CompactionPrompts.SummaryMessage(replaced, summary.Text.Trim()));
        Rewritten();
        var after = Recount(conversation);
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
    /// Where the kept turns start: the <paramref name="keep"/>-th turn from the end, so a tool result always
    /// stays with the call before it; the end of the conversation when none is kept, 2 when there are no more turns.
    /// A turn starts at an assistant message that follows no other: a response whose call and text come as two
    /// assistant messages is one turn, never cut between its call and its result.
    /// </summary>
    private static int CutIndex(ConversationView conversation, int keep)
    {
        if (keep == 0)
            return conversation.Count;

        var seen = 0;
        for (var i = conversation.Count - 1; i >= 2; i--)
        {
            if (conversation.RoleAt(i) == ConversationView.AssistantRole
                && conversation.RoleAt(i - 1) != ConversationView.AssistantRole
                && ++seen == keep)
                return i;
        }
        return 2;
    }

    /// <summary>
    /// The cut when the <c>KeepRecentTurns</c> turns are all there is or alone pass the trigger: the most turns,
    /// fewer than that, whose messages (the system prompt and the task beside them) come under the trigger, else
    /// the last turn alone; 2 when the last turn is all there is.
    /// </summary>
    private int ShrunkCutIndex(ConversationView conversation)
    {
        var last = 2;
        for (var keep = _setup.KeepRecentTurns - 1; keep >= 1; keep--)
        {
            var cut = CutIndex(conversation, keep);
            if (cut <= 2)
                continue;
            last = cut;
            if (KeptUnderTrigger(conversation, cut))
                return cut;
        }
        return last;
    }

    // Whether the messages a cut at `cut` keeps (the system prompt and the task beside them) come under the trigger.
    private bool KeptUnderTrigger(ConversationView conversation, int cut) =>
        Count(conversation, 0, 2) + Count(conversation, cut) + _overheadTokens < TriggerTokens;

    private static int SystemMessages(ConversationView conversation, int start, int end)
    {
        var count = 0;
        for (var i = start; i < end; i++)
        {
            if (conversation.RoleAt(i) == ConversationView.SystemRole)
                count++;
        }
        return count;
    }

    private static string Transcript(ConversationView conversation, int start, int end)
    {
        var text = new StringBuilder();
        for (var i = start; i < end; i++)
        {
            if (conversation.RoleAt(i) != ConversationView.SystemRole)
                text.Append('[').Append(conversation.RoleAt(i)).Append("] ").Append(conversation.TextAt(i)).Append("\n\n");
        }
        return text.ToString();
    }

    private int Count(ConversationView conversation, int from, int? to = null)
    {
        var total = 0;
        for (var i = from; i < (to ?? conversation.Count); i++)
        {
            var text = conversation.TextAt(i);
            total += MessageOverheadTokens + (_setup.TokenCounter?.CountTokens(text) ?? (text.Length + 3) / 4);
        }
        return total;
    }

    // The whole conversation counted again, with the overhead the provider's last count showed.
    private int Recount(ConversationView conversation) => Count(conversation, 0) + _overheadTokens;

    /// <summary>
    /// The conversation was rewritten since the last call, by a stage, a summary or the loop's 40-message trim: the
    /// next estimate counts it again rather than adding the messages past the last call's count to that call's prompt.
    /// </summary>
    internal void Rewritten() => _lastPromptTokens = null;

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
