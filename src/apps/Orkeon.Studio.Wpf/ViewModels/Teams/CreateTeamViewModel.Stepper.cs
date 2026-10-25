using System.Globalization;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>
/// The stepper's own state (refonte, lot 3): which steps are validated, the way back to the
/// previous step, the session indicator at the end of the line (running, waiting for you,
/// paused with « Resume », or the draft saved at a time), and the sentences the steps' action
/// bars carry. Everything here is read from the engine's projection and the run's outcome;
/// nothing new is asked of the engine.
/// </summary>
public sealed partial class CreateTeamViewModel
{
    private TimeProvider _clock = TimeProvider.System;
    private bool _stoppedByUser;
    private DateTimeOffset? _draftSavedAt;

    /// <summary>« Back »: shows the step before the one on screen.</summary>
    public RelayCommand GoBackCommand { get; private set; } = null!;

    /// <summary>« Resume »: picks up a session the user stopped, where it stopped.</summary>
    public AsyncRelayCommand ResumeHereCommand { get; private set; } = null!;

    private void InitializeStepper(TimeProvider? clock)
    {
        _clock = clock ?? TimeProvider.System;
        GoBackCommand = new RelayCommand(() => GoStep(Step - 1), () => Step > 1);
        ResumeHereCommand = new AsyncRelayCommand(ResumeHereAsync, () => CanResumeHere);
    }

    /// <summary>Whether step 1 is behind the session: a later step was reached.</summary>
    public bool IsStep1Done => MaxStep > 1;

    /// <summary>Whether step 2 is behind the session.</summary>
    public bool IsStep2Done => MaxStep > 2;

    /// <summary>Whether step 3 is behind the session.</summary>
    public bool IsStep3Done => MaxStep > 3;

    /// <summary>Whether step 2 was reached: its pill opens it again.</summary>
    public bool IsStep2Reached => MaxStep >= 2;

    /// <summary>Whether step 3 was reached.</summary>
    public bool IsStep3Reached => MaxStep >= 3;

    /// <summary>Whether step 4 was reached.</summary>
    public bool IsStep4Reached => MaxStep >= 4;

    /// <summary>
    /// The need on one line, for the grey summary under the stepper past step 1: its line
    /// breaks and runs of spaces fold into single spaces.
    /// </summary>
    public string NeedSummary =>
        string.Join(' ', Need.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Whether the engine works for the session right now — « Running ».</summary>
    public bool IsSessionWorking => IsEngineWorking;

    /// <summary>Whether the engine lives but waits for the user — an answer, a choice, the folders.</summary>
    public bool IsSessionWaiting => IsEngineRunning && IsEngineWaitingOnUser;

    /// <summary>Whether the user stopped the session and it can be picked up again — « Paused · Resume ».</summary>
    public bool IsSessionPaused => !IsEngineRunning && _stoppedByUser && _model.Slug is not null && _model.Promotion is null;

    /// <summary>Whether « Resume » can run: the session the user stopped, with no engine alive.</summary>
    public bool CanResumeHere => IsSessionPaused;

    /// <summary>Whether the session is saved on disk and nothing runs: the draft line, with its time.</summary>
    public bool IsDraftSaved =>
        !IsEngineRunning && !_stoppedByUser && _draftSavedAt is not null
        && _model.Slug is not null && _model.Promotion is null;

    /// <summary>The indicator's words at the end of the stepper line; empty when there is nothing to say.</summary>
    public string SessionStateLabel
    {
        get
        {
            if (IsSessionWorking)
                return _strings[StudioStringKeys.WizardSessionRunning];
            if (IsSessionWaiting)
                return _strings[StudioStringKeys.WizardSessionWaiting];
            if (IsSessionPaused)
                return _strings[StudioStringKeys.WizardSessionPaused];
            if (IsDraftSaved && _draftSavedAt is { } savedAt)
            {
                var local = TimeZoneInfo.ConvertTime(savedAt, _clock.LocalTimeZone);
                return _strings.Format(
                    StudioStringKeys.WizardDraftSavedAt,
                    local.ToString("HH:mm", CultureInfo.InvariantCulture));
            }

            return string.Empty;
        }
    }

    /// <summary>Whether the indicator shows at all.</summary>
    public bool HasSessionState => SessionStateLabel.Length > 0;

    /// <summary>The rows of the team's folders still waiting for an answer (a folder to choose).</summary>
    public int FoldersToChooseCount => MountRows.Count(row => row.CanChooseFolder);

    /// <summary>Whether a folder is still to choose: « Create all inside the team » shows only then.</summary>
    public bool HasFoldersToChoose => FoldersToChooseCount > 0;

    /// <summary>The step-2 action bar's sentence: the folders left to choose, else what the pause means.</summary>
    public string Step2Hint
    {
        get
        {
            if (FoldersToChooseCount is var count and > 0)
                return _strings.Format(StudioStringKeys.WizardFoldersToChoose, count);

            return CanTryTeam ? _strings[StudioStringKeys.WizardPausedHint] : string.Empty;
        }
    }

    /// <summary>
    /// The verdict's summary sentence — « 2 of 3 criteria met » — read from the checklist; empty
    /// when no criterion was judged one by one (a mechanical verdict says so on its own line).
    /// </summary>
    public string VerdictSummary
    {
        get
        {
            if (Checklist.Count == 0 || VerdictIsMechanical)
                return string.Empty;

            // The criteria are the checklist's last lines; the findings that name none come first
            // and are not criteria. A remark blocks nothing, so its criterion is met (the engine's
            // own rule for « passing »).
            var criteria = Math.Min(_model.Criteria.Count, Checklist.Count);
            if (criteria == 0)
                return string.Empty;

            var met = Checklist.Skip(Checklist.Count - criteria).Count(line => line.Passed == true || line.IsRemark);
            return _strings.Format(StudioStringKeys.WizardVerdictSummary, met, criteria);
        }
    }

    /// <summary>Whether the verdict card leads with its summary sentence.</summary>
    public bool HasVerdictSummary => VerdictSummary.Length > 0;

    /// <summary>A run ended: stopped by the user, or the session saved where the engine left it.</summary>
    private void NoteRunEnded(bool cancelled)
    {
        _stoppedByUser = cancelled;
        if (!cancelled && _model.Slug is not null)
            _draftSavedAt = _clock.GetUtcNow();
        RaiseSessionStateChanged();
    }

    /// <summary>A new run, a reset or a resume: the previous run's outcome no longer speaks.</summary>
    private void ForgetRunOutcome()
    {
        _stoppedByUser = false;
        RaiseSessionStateChanged();
    }

    /// <summary>A new creation: neither the stop nor the saved time of the previous one.</summary>
    private void ForgetSessionState()
    {
        _draftSavedAt = null;
        ForgetRunOutcome();
    }

    private void RaiseStepperChanged()
    {
        OnPropertiesChanged(
            nameof(IsStep1Done), nameof(IsStep2Done), nameof(IsStep3Done),
            nameof(IsStep2Reached), nameof(IsStep3Reached), nameof(IsStep4Reached));
        GoStep1Command?.RaiseCanExecuteChanged();
        GoStep2Command?.RaiseCanExecuteChanged();
        GoStep3Command?.RaiseCanExecuteChanged();
        GoStep4Command?.RaiseCanExecuteChanged();
        GoBackCommand?.RaiseCanExecuteChanged();
    }

    private void RaiseSessionStateChanged()
    {
        OnPropertiesChanged(
            nameof(IsSessionWorking), nameof(IsSessionWaiting), nameof(IsSessionPaused),
            nameof(IsDraftSaved), nameof(CanResumeHere), nameof(SessionStateLabel), nameof(HasSessionState));
        ResumeHereCommand?.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// « Resume » on the stepper: the same gesture as « Resume » under « Creations in progress »
    /// in My teams, on the session this screen holds — read back from the session catalogue,
    /// which knows its saved state.
    /// </summary>
    private async Task ResumeHereAsync()
    {
        if (_model.Slug is not { } slug)
            return;

        var session = ForgeSessionCatalog.List(_workspace)
            .FirstOrDefault(s => string.Equals(s.Slug, slug, StringComparison.Ordinal));
        if (session is not { CanResume: true })
        {
            StatusMessage = _strings[StudioStringKeys.WizardResumeUnavailable];
            return;
        }

        await ResumeAsync(session).ConfigureAwait(true);
    }
}
