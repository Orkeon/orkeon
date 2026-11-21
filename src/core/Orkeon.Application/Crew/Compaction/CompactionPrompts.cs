namespace Orkeon.Application.Crew.Compaction;

/// <summary>The texts of a compaction (#432): the summary call's instructions and the summary's header.</summary>
internal static class CompactionPrompts
{
    /// <summary>The system prompt of the summary call.</summary>
    internal const string Instructions =
        "You condense the middle of an agent's working conversation so the agent can carry on with less context. " +
        "Write a factual summary, in the language of the conversation, of what the agent did and learned: the tools " +
        "it called and what they returned that still matters (file paths, identifiers, figures, errors and their " +
        "causes), the decisions taken, and what was left to do. Keep exact values; leave out what no later step " +
        "needs. Answer with the summary alone, no preamble, and call no tool.";

    /// <summary>The header of the user message that replaces the middle of the conversation.</summary>
    internal static string SummaryMessage(int replacedMessages, string summary) =>
        $"[Context compacted: {replacedMessages} earlier messages replaced by this summary. The original task " +
        $"and the most recent turns follow unchanged.]\n{summary}";

    /// <summary>The user message of the summary call: the agent's task, then the transcript to condense.</summary>
    internal static string Transcript(string task, string transcript) =>
        $"The agent's task, which stays in its conversation:\n{task}\n\nConversation to summarise:\n\n{transcript}";
}
