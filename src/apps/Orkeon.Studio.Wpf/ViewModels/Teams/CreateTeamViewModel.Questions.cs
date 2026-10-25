using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>
/// The wizard's own questions, asked in the panel (refonte R15): the arbitration after the
/// trial and the fill step's « Yes » / « No ». Each is a bubble of the assistant's with its
/// answers as secondary buttons, the recommended one first and focused; the main column keeps
/// the work only — the verdict, the folders to fill. « Yes » leads to the folders: the next
/// bubble gives « Continue » as a button, with the link that brings the folders on screen.
/// </summary>
public sealed partial class CreateTeamViewModel
{
    private ChatQuestion? _decisionQuestion;
    private ChatQuestion? _fillQuestion;
    private ChatQuestion? _fillDropQuestion;

    /// <summary>Raised when the folders to fill must come on screen — the bubble's link.</summary>
    public event EventHandler? FillFoldersRevealRequested;

    /// <summary>The bubble's link: the folders to fill, on screen.</summary>
    public RelayCommand RevealFillFoldersCommand { get; private set; } = null!;

    private void InitializeQuestions() =>
        RevealFillFoldersCommand = new RelayCommand(() => FillFoldersRevealRequested?.Invoke(this, EventArgs.Empty));

    /// <summary>Hands the panel the question of the moment, if the wizard asks one.</summary>
    private void SyncQuestion(bool announce = true) => Chat.SetQuestion(ReadQuestion(), announce);

    /// <summary>Words the questions again in the new language, without announcing them anew.</summary>
    private void OnQuestionsLanguageChanged()
    {
        _decisionQuestion = null;
        _fillQuestion = null;
        _fillDropQuestion = null;
        SyncQuestion(announce: false);
    }

    private ChatQuestion? ReadQuestion()
    {
        if (Decisions.Count > 0)
            return _decisionQuestion ??= DecisionQuestion();
        if (IsFillFoldersQuestion)
            return _fillQuestion ??= new ChatQuestion(
                _strings[StudioStringKeys.WizardFillFoldersQuestion],
                _strings[StudioStringKeys.WizardFillFoldersHint],
                [
                    new ChatReply(_strings[StudioStringKeys.WizardFillFoldersYes], FillFoldersYesCommand, isRecommended: true),
                    new ChatReply(_strings[StudioStringKeys.WizardFillFoldersNo], FillFoldersNoCommand),
                ]);
        if (IsFillingFolders)
            return _fillDropQuestion ??= new ChatQuestion(
                _strings[StudioStringKeys.WizardFillFoldersDrop],
                "",
                [new ChatReply(_strings[StudioStringKeys.WizardFillFoldersContinue], FillFoldersContinueCommand, isRecommended: true)],
                RevealFillFoldersCommand);

        return null;
    }

    /// <summary>The engine's own options, « Looks good to me » first.</summary>
    private ChatQuestion DecisionQuestion()
    {
        var replies = Decisions
            .OrderBy(decision => string.Equals(decision.Value, "accept", StringComparison.Ordinal) ? 0 : 1)
            .Select(decision => new ChatReply(
                decision.Label,
                decision.DecideCommand,
                string.Equals(decision.Value, "accept", StringComparison.Ordinal)))
            .ToList();

        return new ChatQuestion(_strings[StudioStringKeys.WizardDecisionQuestion], "", replies);
    }
}
