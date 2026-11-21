namespace Orkeon.Application.Crew.Skills;

/// <summary>The texts of the skills block and of the question the decision model answers (#434).</summary>
internal static class SkillsPrompts
{
    /// <summary>The heading of the block, in the agent's system prompt.</summary>
    internal const string Heading = "## Skills";

    /// <summary>What the block says of a list the crew wrote (or a fallback).</summary>
    internal const string ListIntro =
        "The playbooks below come with your role. Follow them where they apply to the task; the task and its expected output come first.";

    /// <summary>What the block says of a pick of the decision model.</summary>
    internal const string PickIntro =
        "The playbooks below were picked for this task from the skills library. Use them where they help; the task and its expected output come first.";

    /// <summary>The instructions of the choice question.</summary>
    internal const string QuestionInstructions =
        "Which of these playbooks would help an agent carry out the task described in the state?";

    /// <summary>The option that says no skill fits.</summary>
    internal const string NoSkillFits = "None of these playbooks helps with this task.";

    /// <summary>The note under a skill cut by a cap: the file is read no further than the cap, so its length is not known.</summary>
    internal static string CutNote(int shown) =>
        FormattableString.Invariant($"[skill cut: the first {shown} characters shown]");

    /// <summary>Why a crew on <c>decision.mode: none</c> takes no pick: the fallback stands.</summary>
    internal const string DecisionNone = "the crew runs with decision.mode: none and asks System One nothing";

    /// <summary>Why an answer of the decision model picks nothing: the fallback stands.</summary>
    internal const string NoUsableChoice = "its answer held no usable choice";
}
