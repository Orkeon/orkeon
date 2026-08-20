using System.Collections.ObjectModel;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Forge;

/// <summary>One row of "Mes solutions" — a problem in progress or a problem solved.</summary>
public sealed class ForgeSolutionViewModel(ForgeSolutionSummary summary)
{
    /// <summary>The underlying summary.</summary>
    public ForgeSolutionSummary Summary { get; } = summary;

    /// <summary>Display name; the slug only ever shows at level 3.</summary>
    public string Title { get; } = string.IsNullOrWhiteSpace(summary.Title) ? summary.Slug : summary.Title!;

    /// <summary>Session slug (level 3).</summary>
    public string Slug { get; } = summary.Slug;

    /// <summary>"Reprendre" applies.</summary>
    public bool CanResume { get; } = summary.CanResume;

    /// <summary>"Relancer" applies.</summary>
    public bool CanRelaunch { get; } = summary.CanRelaunch;

    /// <summary>Where the adopted solution lives, when it was adopted.</summary>
    public string? PromotedTo { get; } = summary.PromotedTo;
}

/// <summary>
/// "Mes solutions" (UX study §5): sessions in progress and adopted solutions in one list —
/// the user has problems being solved and problems solved, not two notions.
/// </summary>
public sealed class ForgeSolutionsViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<ForgeSolutionSummary>> _load;
    private bool _isEmpty = true;

    /// <summary>Builds the list over a catalog reader (the test seam).</summary>
    public ForgeSolutionsViewModel(Func<IReadOnlyList<ForgeSolutionSummary>> load) =>
        _load = load ?? throw new ArgumentNullException(nameof(load));

    /// <summary>The rows, most recently touched first.</summary>
    public ObservableCollection<ForgeSolutionViewModel> Solutions { get; } = [];

    /// <summary>True when there is nothing yet — the empty state invites the first problem.</summary>
    public bool IsEmpty { get => _isEmpty; private set => SetProperty(ref _isEmpty, value); }

    /// <summary>Re-reads the catalog.</summary>
    public void Refresh()
    {
        Solutions.Clear();
        foreach (var summary in _load())
            Solutions.Add(new ForgeSolutionViewModel(summary));
        IsEmpty = Solutions.Count == 0;
    }
}
