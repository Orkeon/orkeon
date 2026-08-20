using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Forge;

/// <summary>Which card the dossier shows — one main card at a time, never a dashboard.</summary>
public enum ForgeCard
{
    /// <summary>Nothing to show yet.</summary>
    None,

    /// <summary>The "success" card: acceptance criteria filling up during the interview.</summary>
    Success,

    /// <summary>The narrative proposal.</summary>
    Proposal,

    /// <summary>The try, live, in human terms.</summary>
    Trying,

    /// <summary>The result: deliverable first, then the ✔/✘ checklist, then the decision.</summary>
    Result,

    /// <summary>"Your solution is ready. What now?"</summary>
    Adopt,

    /// <summary>A plain-words error, action-oriented, technical details folded.</summary>
    Error,
}

/// <summary>One ✔/✘ line of the result card.</summary>
public sealed class ForgeCheckViewModel(bool? passed, string statement, string? detail)
{
    /// <summary>True = held, false = did not, null = "judge for yourself".</summary>
    public bool? Passed { get; } = passed;

    /// <summary>The criterion's statement — the success card's words, verbatim.</summary>
    public string Statement { get; } = statement;

    /// <summary>The finding's wording (under a ✘), or the honesty line (under a ?).</summary>
    public string? Detail { get; } = detail;

    /// <summary>Convenience mark for the view.</summary>
    public string Mark => Passed switch { true => "✔", false => "✘", null => "?" };
}

/// <summary>
/// The dossier column (UX study §3-§4): the materialization of what the conversation is
/// about right now. Data only — the action buttons live on <see cref="ForgeTabViewModel"/>,
/// which owns the process.
/// </summary>
public sealed class DossierViewModel : ObservableObject
{
    private readonly IStudioStrings _strings;
    private ForgeCard _card = ForgeCard.None;
    private string _rationale = "";
    private string _needs = "";
    private string _statusLine = "";
    private string _resultSummary = "";
    private string _promotedPath = "";
    private string _installCommand = "";
    private string _errorMessage = "";
    private string _errorCode = "";
    private string _scheduleTime = "08:00";
    private bool _isPromoted;

    /// <summary>Builds the dossier over the localization port.</summary>
    public DossierViewModel(IStudioStrings? strings = null) =>
        _strings = strings ?? EnglishStudioStrings.Instance;

    /// <summary>The card currently shown.</summary>
    public ForgeCard Card
    {
        get => _card;
        private set
        {
            if (SetProperty(ref _card, value))
            {
                OnPropertiesChanged(
                    nameof(IsSuccessCard), nameof(IsProposalCard), nameof(IsTryingCard),
                    nameof(IsResultCard), nameof(IsAdoptCard), nameof(IsErrorCard));
            }
        }
    }

    /// <summary>Success card visible.</summary>
    public bool IsSuccessCard => Card == ForgeCard.Success;

    /// <summary>Proposal card visible.</summary>
    public bool IsProposalCard => Card == ForgeCard.Proposal;

    /// <summary>Trying card visible.</summary>
    public bool IsTryingCard => Card == ForgeCard.Trying;

    /// <summary>Result card visible.</summary>
    public bool IsResultCard => Card == ForgeCard.Result;

    /// <summary>Adopt card visible.</summary>
    public bool IsAdoptCard => Card == ForgeCard.Adopt;

    /// <summary>Error card visible.</summary>
    public bool IsErrorCard => Card == ForgeCard.Error;

    /// <summary>The success card's lines — criteria in the user's words.</summary>
    public ObservableCollection<string> Criteria { get; } = [];

    /// <summary>The proposal's numbered steps, already narrated ("1. … — Role").</summary>
    public ObservableCollection<string> Steps { get; } = [];

    /// <summary>The blueprint's rationale, plain words.</summary>
    public string Rationale { get => _rationale; private set => SetProperty(ref _rationale, value); }

    /// <summary>What the team may touch (trust language lives in the view's label).</summary>
    public string Needs { get => _needs; private set => SetProperty(ref _needs, value); }

    /// <summary>Live activity of the try, in human terms.</summary>
    public ObservableCollection<string> Activity { get; } = [];

    /// <summary>The status sentence of the moment (preparing…, trying…, checking…).</summary>
    public string StatusLine { get => _statusLine; private set => SetProperty(ref _statusLine, value); }

    /// <summary>The result card's ✔/✘ checklist.</summary>
    public ObservableCollection<ForgeCheckViewModel> Checklist { get; } = [];

    /// <summary>The result summary line ("The try is done").</summary>
    public string ResultSummary { get => _resultSummary; private set => SetProperty(ref _resultSummary, value); }

    /// <summary>Whether the arbitration buttons are live.</summary>
    public bool DecisionPending { get; private set; }

    /// <summary>Adopt card: the solution was stored already.</summary>
    public bool IsPromoted { get => _isPromoted; private set => SetProperty(ref _isPromoted, value); }

    /// <summary>Where the promoted folder went.</summary>
    public string PromotedPath { get => _promotedPath; private set => SetProperty(ref _promotedPath, value); }

    /// <summary>The schedule install command — displayed, never executed.</summary>
    public string InstallCommand { get => _installCommand; private set => SetProperty(ref _installCommand, value); }

    /// <summary>Time of day of the optional daily schedule (HH:mm), user-editable.</summary>
    public string ScheduleTime { get => _scheduleTime; set => SetProperty(ref _scheduleTime, value); }

    /// <summary>The error card's plain sentence.</summary>
    public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }

    /// <summary>The FORGE-* code (technical details, folded).</summary>
    public string ErrorCode { get => _errorCode; private set => SetProperty(ref _errorCode, value); }

    /// <summary>Re-reads the whole projection and picks the card of the moment.</summary>
    public void Update(ForgeSessionModel model, bool engineRunning)
    {
        ArgumentNullException.ThrowIfNull(model);

        SyncCriteria(model);
        SyncProposal(model);
        SyncActivity(model);
        SyncChecklist(model);

        DecisionPending = model.DecisionPending;
        OnPropertyChanged(nameof(DecisionPending));

        if (model.Promotion is { } promotion)
        {
            IsPromoted = true;
            PromotedPath = promotion.Path;
            InstallCommand = promotion.Install ?? "";
        }

        if (model.Verdict is { } verdict)
        {
            ResultSummary = string.Format(
                CultureInfo.CurrentCulture,
                _strings[StudioStringKeys.ForgeResultScore],
                verdict.Score.ToString("0.0", CultureInfo.CurrentCulture));
        }

        StatusLine = model.Stage switch
        {
            "blueprint" or "render" or "validate" when engineRunning => _strings[StudioStringKeys.ForgeStatusPreparing],
            "test" when engineRunning => _strings[StudioStringKeys.ForgeStatusTrying],
            "diagnose" when engineRunning => _strings[StudioStringKeys.ForgeStatusJudging],
            _ => "",
        };

        Card = Pick(model, engineRunning);
    }

    private static ForgeCard Pick(ForgeSessionModel model, bool engineRunning)
    {
        var failed = string.Equals(model.FinishedStatus, "failed", StringComparison.Ordinal)
            || (model.LastError is { Recoverable: false });
        if (failed)
            return ForgeCard.Error;

        if (model.Promotion is not null)
            return ForgeCard.Adopt;
        if (model.Stage is "ready" || string.Equals(model.FinishedStatus, "ready", StringComparison.Ordinal))
            return ForgeCard.Adopt;

        if (model.DecisionPending || (model.Verdict is not null && model.Milestone == ForgeMilestone.Try && !model.RunInProgress))
            return ForgeCard.Result;
        if (model.Milestone == ForgeMilestone.Try)
            return ForgeCard.Trying;
        if (model.Proposal is not null && model.Milestone == ForgeMilestone.Propose)
            return ForgeCard.Proposal;
        if (model.Criteria.Count > 0 || model.Milestone == ForgeMilestone.Describe)
            return engineRunning || model.Criteria.Count > 0 ? ForgeCard.Success : ForgeCard.None;

        return ForgeCard.None;
    }

    private void SyncCriteria(ForgeSessionModel model)
    {
        Criteria.Clear();
        foreach (var criterion in model.Criteria)
            Criteria.Add(criterion.Statement);
    }

    private void SyncProposal(ForgeSessionModel model)
    {
        Steps.Clear();
        if (model.Proposal is not { } proposal)
        {
            Rationale = "";
            Needs = "";
            return;
        }

        for (var i = 0; i < proposal.Steps.Count; i++)
        {
            var step = proposal.Steps[i];
            var role = string.IsNullOrWhiteSpace(step.AgentRole) ? "" : $" — {step.AgentRole}";
            Steps.Add(string.Create(CultureInfo.CurrentCulture, $"{i + 1}. {step.Description}{role}"));
        }

        Rationale = proposal.Rationale ?? "";
        Needs = string.Join(", ", proposal.Tools);
    }

    private void SyncActivity(ForgeSessionModel model)
    {
        Activity.Clear();
        foreach (var task in model.Activity)
            Activity.Add($"{task.AgentRole ?? task.TaskId} {(task.Success ? "✔" : "✘")}");
    }

    private void SyncChecklist(ForgeSessionModel model)
    {
        Checklist.Clear();
        foreach (var item in model.BuildChecklist())
        {
            Checklist.Add(new ForgeCheckViewModel(
                item.Passed,
                item.Statement,
                item.Passed is null ? _strings[StudioStringKeys.ForgeCheckUnverified] : item.Detail));
        }
    }
}
