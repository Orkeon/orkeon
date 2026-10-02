namespace Orkeon.Infrastructure.Parsing;

/// <summary>
/// The JSON object a model was asked for, out of what it actually wrote: models wrap it in a
/// <c>```json</c> fence or a sentence, even when told not to. Shared by the readers of a ballot
/// (<c>AgentBallotCollector</c>) and of the crew's plan (<see cref="ExecutionPlanParser"/>, GAP-31).
/// </summary>
internal static class LlmJsonText
{
    /// <summary>
    /// The first <c>{</c> to the last <c>}</c> of <paramref name="reply"/>: tolerates a fenced block or
    /// a sentence around the object; null when the reply holds no braces. The span is not validated —
    /// the caller parses it and says what it could not read.
    /// </summary>
    /// <param name="reply">What the model answered.</param>
    internal static string? ExtractObject(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return null;
        var start = reply.IndexOf('{', StringComparison.Ordinal);
        var end = reply.LastIndexOf('}');
        return start >= 0 && end > start ? reply[start..(end + 1)] : null;
    }
}
