namespace Orkeon.Application.Crew.Compaction;

/// <summary>
/// The texts of the pruning of read-tool results (#434): what the decision model reads, and the line a pruned
/// result leaves for the agent.
/// </summary>
internal static class PrunePrompts
{
    /// <summary>The state the decision model judges: the task, then the calls numbered from 1, oldest first.</summary>
    internal static string State(string request, string calls) =>
        $"An agent is working on this task:\n{request}\n\n" +
        $"Its earlier tool calls, oldest first. Each one only read, so running it again gives its result back:\n{calls}";

    /// <summary>One call of the state: its tool, its input, the size of its result and its age.</summary>
    internal static string CallLine(int number, string tool, string input, int chars, int age) =>
        $"{number}. {tool} {input}: a result of {chars} characters, {age} model turn(s) ago";

    /// <summary>Whether the agent still relies on the result of call <paramref name="number"/>.</summary>
    internal static string StillNeeded(int number) =>
        $"Tool call {number} of the state: will the rest of the task still rely on what its result showed?";

    /// <summary>Whether the agent needs the whole result of call <paramref name="number"/>.</summary>
    internal static string NeededWhole(int number) =>
        $"Tool call {number} of the state: does the rest of the task need its full result, not only its first lines?";

    /// <summary>The line a dropped result leaves: what it was and how to get it back.</summary>
    internal static string Dropped(string tool, string input, int chars) =>
        $"[pruned to save context: the result of {tool} {input} ({chars} characters) was removed. " +
        $"Call {tool} again with the same input if you need it.]";

    /// <summary>The line after a result cut to its first characters.</summary>
    internal static string Headed(string tool, string input, int cut, int chars) =>
        $"\n[pruned to save context: {cut} of {chars} characters cut here. Call {tool} {input} again for the full result.]";

    /// <summary>What ends a text cut to fit.</summary>
    internal const string Ellipsis = "...";
}
