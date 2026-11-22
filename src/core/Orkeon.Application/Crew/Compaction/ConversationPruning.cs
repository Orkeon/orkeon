using System.Diagnostics.CodeAnalysis;
using LlmMessage = Orkeon.Domain.SharedKernel.ValueObjects.LlmMessage;

namespace Orkeon.Application.Crew.Compaction;

/// <summary>
/// The pruning of a conversation a caller owns (#461), the seam a script's <c>ctx.llm.act</c> asks through: the
/// orchestrator's pruning of older read-tool results (<c>Orkeon:Compaction:Prune</c>, #434), for a loop the
/// orchestrator does not run. The host's implementation is the run scope's pruning stage: the decision circuit of
/// the run, its once-per-run warning and the <c>agent.pruned</c> observer are the crew loops' own.
/// </summary>
[Experimental("ORKEXP009", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public interface IConversationPruner
{
    /// <summary>
    /// Prunes <paramref name="conversation"/> when its estimate reaches the fill share of the call's usable window:
    /// each offered result is judged by the decision model, from its call alone, and kept, cut or dropped.
    /// Never throws for a decision failure; the caller's cancellation propagates.
    /// </summary>
    /// <param name="request">The task, the first message scanned, the estimate, the call's window and the numbers.</param>
    /// <param name="conversation">The conversation, which the pruning may rewrite.</param>
    /// <param name="cancellationToken">The caller's cancellation.</param>
    /// <returns>What was asked and done; null when nothing was asked (below the threshold, the circuit off, nothing to offer).</returns>
    ValueTask<ConversationPruneResponse?> PruneAsync(
        ConversationPruneRequest request, ActConversationView conversation, CancellationToken cancellationToken);
}

/// <summary>One pruning a caller asks for (#461).</summary>
[Experimental("ORKEXP009", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record ConversationPruneRequest
{
    /// <summary>The role of the agent the loop runs for.</summary>
    public required string AgentRole { get; init; }

    /// <summary>The task as the decision model reads it (cut at 2,000 characters): an act's prompt.</summary>
    public required string Task { get; init; }

    /// <summary>The first message whose result may be offered: the messages before it (system, seed, prompt) never are.</summary>
    public required int FirstIndex { get; init; }

    /// <summary>The estimated prompt of the next call, in tokens.</summary>
    public required int EstimatedTokens { get; init; }

    /// <summary>The profile the call runs on, whose window is resolved; <c>default</c> for the host's own.</summary>
    public string Profile { get; init; } = "default";

    /// <summary>The model the call names, or null when it leaves it to the profile.</summary>
    public string? Model { get; init; }

    /// <summary>The call's own <c>ContextWindowTokens</c>, or null.</summary>
    public int? ContextWindowTokens { get; init; }

    /// <summary>The call's output cap, from which the output reserve is computed as a crew loop's is; null for the model's documented one.</summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>The fill share of the usable window for this call; null takes <c>Orkeon:Compaction:Prune:FillShare</c>.</summary>
    public double? FillShare { get; init; }

    /// <summary>The last messages never offered for this call; null takes <c>Orkeon:Compaction:Prune:KeepRecentMessages</c>.</summary>
    public int? KeepRecentMessages { get; init; }

    /// <summary>What a head verdict keeps for this call; null takes <c>Orkeon:Compaction:Prune:HeadChars</c>.</summary>
    public int? HeadChars { get; init; }
}

/// <summary>What one pruning asked and did (#461), the fields of <c>agent.pruned</c> but the task and the estimate.</summary>
[Experimental("ORKEXP009", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record ConversationPruneResponse
{
    /// <summary>The estimate from which results were judged, in tokens.</summary>
    public required int ThresholdTokens { get; init; }

    /// <summary>The results offered to the decision model.</summary>
    public required int Offered { get; init; }

    /// <summary>The results it answered for.</summary>
    public required int Judged { get; init; }

    /// <summary>The results kept whole.</summary>
    public required int Kept { get; init; }

    /// <summary>The results cut to their first characters.</summary>
    public required int Cut { get; init; }

    /// <summary>The results removed.</summary>
    public required int Dropped { get; init; }

    /// <summary>The tokens the pruning freed, at four characters a token.</summary>
    public required int FreedTokens { get; init; }
}

/// <summary>
/// An act's conversation (<see cref="LlmMessage"/>) as a <see cref="ConversationView"/> (#461). A tool result is
/// offered only when it was noted (<see cref="NoteReadCall"/>): the caller notes the results of the reads that ran
/// in its loop, so a result it seeded, or of a write, is never offered. Keep one view per conversation: the pruning's
/// memory of what it settled is held by the view.
/// </summary>
[Experimental("ORKEXP009", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed class ActConversationView : ConversationView
{
    private readonly LlmConversationView _inner;

    // The noted reads, by the position of their result: a provider's call ids need not be unique over a loop, and an
    // act's messages are only ever appended to, never compacted, so a position stays valid.
    private readonly Dictionary<int, ConversationToolCall> _reads = [];

    /// <summary>A view over <paramref name="messages"/>, which the pruning rewrites in place.</summary>
    /// <param name="messages">The loop's messages.</param>
    [SuppressMessage("Design", "CA1002", Justification = "The view rewrites the loop's own list in place, as the native loop's view does.")]
    public ActConversationView(List<LlmMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        _inner = new LlmConversationView(messages);
    }

    /// <summary>Notes the result of a read call that ran in the loop, so it may be offered.</summary>
    /// <param name="index">The position of the result's message in the loop's messages.</param>
    /// <param name="callId">An id of the call unique over the loop: what the pruning remembers the result by.</param>
    /// <param name="toolName">The tool that ran.</param>
    /// <param name="arguments">The input it ran with.</param>
    public void NoteReadCall(int index, string callId, string toolName, IReadOnlyDictionary<string, object?> arguments)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentNullException.ThrowIfNull(callId);
        ArgumentNullException.ThrowIfNull(toolName);
        ArgumentNullException.ThrowIfNull(arguments);
        _reads[index] = new ConversationToolCall
        {
            CallId = callId,
            ToolName = toolName,
            Input = System.Text.Json.JsonSerializer.Serialize(arguments),
            IsRead = true,
        };
    }

    /// <inheritdoc />
    public override int Count => _inner.Count;

    /// <inheritdoc />
    public override string RoleAt(int index) => _inner.RoleAt(index);

    /// <inheritdoc />
    public override string TextAt(int index) => _inner.TextAt(index);

    /// <inheritdoc />
    public override ConversationToolCall? ToolCallAt(int index) =>
        RoleAt(index) == ToolRole && _reads.TryGetValue(index, out var call) ? call : null;

    /// <inheritdoc />
    public override bool ReplaceToolResult(int index, string text) => _inner.ReplaceToolResult(index, text);

    /// <summary>An act is not compacted (#461): nothing calls it.</summary>
    internal override void ReplaceRange(int start, int count, string userText) => _inner.ReplaceRange(start, count, userText);
}
