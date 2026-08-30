using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>One quick reply under a question: what the chip says, and what it records.</summary>
/// <param name="Label">The chip text.</param>
/// <param name="Value">
/// What lands in the thread and in the brief. It is not the label: « Choisir un dossier… »
/// is an invitation, « Dossier à choisir » is the fact it records.
/// </param>
public sealed record AssistantChip(string Label, string Value);

/// <summary>
/// One question of the composition interview: what the assistant asks, the detail and the
/// hint under it, what the input suggests, the three quick replies — and the recap label
/// the answer files itself under.
/// </summary>
public sealed record AssistantQuestion(
    string Fact,
    string Body,
    string Detail,
    string Hint,
    string Placeholder,
    IReadOnlyList<AssistantChip> Chips);

/// <summary>
/// The three questions « Composer l'équipe » asks before the engine is ever started
/// (30/08 mock, T-02). They are content, not code: every word comes from the catalogue,
/// so a language switch rewrites them like any other label.
/// </summary>
public static class AssistantInterview
{
    /// <summary>Builds the interview in the current language.</summary>
    public static IReadOnlyList<AssistantQuestion> Build(IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        return
        [
            Question(strings, "Q1", StudioStringKeys.ChatQ1Hint),
            // Q2 carries no hint at all in the mock — not an empty one. There is no key for
            // it, so nothing to translate and nothing for the parity suite to flag.
            Question(strings, "Q2", hintKey: null),
            Question(strings, "Q3", StudioStringKeys.ChatQ3Hint),
        ];
    }

    private static AssistantQuestion Question(IStudioStrings strings, string n, string? hintKey) => new(
        strings[$"Vm_Chat_{n}_Fact"],
        strings[$"Vm_Chat_{n}_Body"],
        strings[$"Vm_Chat_{n}_Detail"],
        hintKey is null ? "" : strings[hintKey],
        strings[$"Vm_Chat_{n}_Placeholder"],
        [
            new AssistantChip(strings[$"Vm_Chat_{n}_Chip1"], strings[$"Vm_Chat_{n}_Value1"]),
            new AssistantChip(strings[$"Vm_Chat_{n}_Chip2"], strings[$"Vm_Chat_{n}_Value2"]),
            new AssistantChip(strings[$"Vm_Chat_{n}_Chip3"], strings[$"Vm_Chat_{n}_Value3"]),
        ]);
}
