using Microsoft.Extensions.AI;

namespace Orkeon.Rag.Evaluation;

/// <summary>
/// Deterministic offline <see cref="IChatClient"/> for CI evaluation runs
/// (plan §9.2 — the harness is 100 % CI-runnable, zero network): instead of
/// generating, it returns the <c>Context:</c> block assembled by
/// <see cref="Pipeline.StagedRagPipeline"/> verbatim. The heuristic judge's
/// <c>expected_substrings</c> check then measures whether retrieval actually
/// surfaced the right passages — an extractive, honest proxy for generation.
/// Wired by <c>orkeon rag eval --offline</c> and the offline e2e tests.
/// </summary>
public sealed class ExtractiveOfflineChatClient : IChatClient
{
    /// <summary>Prefix stamped on every extractive answer.</summary>
    public const string AnswerPrefix = "(offline extractive mode — retrieved passages follow)\n";

    private const string ContextMarker = "Context:\n";
    private const string QuestionMarker = "\n\nQuestion:";

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var user = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;
        var answer = AnswerPrefix + ExtractContext(user);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType == typeof(IChatClient) ? this : null;

    /// <inheritdoc />
    public void Dispose()
    {
        // Nothing to dispose.
    }

    /// <summary>
    /// Extracts the numbered context block from the linear pipeline's user prompt
    /// (<c>Context:\n…\n\nQuestion: …</c>); returns the whole text when the
    /// markers are absent (custom prompts stay usable).
    /// </summary>
    internal static string ExtractContext(string userMessage)
    {
        var start = userMessage.IndexOf(ContextMarker, StringComparison.Ordinal);
        if (start < 0)
            return userMessage;

        start += ContextMarker.Length;
        var end = userMessage.IndexOf(QuestionMarker, start, StringComparison.Ordinal);
        return end < 0 ? userMessage[start..] : userMessage[start..end];
    }
}
