namespace Orkeon.Studio.Wpf.ViewModels.Launch;

/// <summary>
/// One choice of a history filter (lot 5): <paramref name="Team"/> is the normalized target folder
/// the option keeps, <paramref name="Tone"/> the status tone (ok | fail | warn | idle); null keeps all.
/// </summary>
public sealed record LaunchHistoryFilterOption(string Label, string? Team = null, string? Tone = null)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}

/// <summary>The runs of one day, under its header: « Today », « Yesterday » or the date.</summary>
public sealed record LaunchHistoryDayGroup(string Header, IReadOnlyList<LaunchHistoryEntryViewModel> Entries);
