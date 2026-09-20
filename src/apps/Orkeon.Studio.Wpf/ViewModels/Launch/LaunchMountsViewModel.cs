using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.ViewModels.Mounts;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.ViewModels.Launch;

/// <summary>One row of the effective mount table: what index <c>i</c> resolves to, and where it came from.</summary>
public sealed class EffectiveMountViewModel
{
    private readonly IStudioStrings _strings;

    /// <summary>Wraps a computed effective mount.</summary>
    public EffectiveMountViewModel(EffectiveMount mount, IStudioStrings? strings = null)
    {
        ArgumentNullException.ThrowIfNull(mount);

        Mount = mount;
        _strings = strings ?? EnglishStudioStrings.Instance;
    }

    /// <summary>The underlying Core record.</summary>
    public EffectiveMount Mount { get; }

    /// <summary>Its position in the final array.</summary>
    public int Index => Mount.Index;

    /// <summary>The mount string that will be in force.</summary>
    public string Value => Mount.Value;

    /// <summary>The configuration key the runtime binds it to.</summary>
    public string ConfigurationKey => Mount.ConfigurationKey;

    /// <summary>Whether it comes from the command line or from the file.</summary>
    public MountOrigin Origin => Mount.Origin;

    /// <summary>Whether it replaces the appsettings entry declared on the same virtual root.</summary>
    public bool OverridesSettings => Mount.OverridesSettings;

    /// <summary>The appsettings entry that is being replaced, when there is one.</summary>
    public string? ReplacedSettingsMount => Mount.ReplacedSettingsMount;

    /// <summary>How the entry fares among the entries of its root (VFS-90).</summary>
    public EffectiveMountSelection Selection => Mount.Selection;

    /// <summary>Whether the entry is mounted for the run.</summary>
    public bool IsMounted => Mount.IsMounted;

    /// <summary>
    /// The origin column, phrased for the table. The runner's own entries are named as such
    /// rather than folded into "appsettings": the user never wrote them. A settings entry
    /// sharing its root with others says whether it is the one kept (VFS-90).
    /// </summary>
    public string OriginDisplay => Selection switch
    {
        EffectiveMountSelection.SelectedById => string.Format(
            CultureInfo.InvariantCulture, _strings[StudioStringKeys.MountsOriginSelectedById], OriginName, Mount.SharedRootCount),
        EffectiveMountSelection.NotSelected => string.Format(
            CultureInfo.InvariantCulture, _strings[StudioStringKeys.MountsOriginNotSelected], OriginName),
        EffectiveMountSelection.Conflict => string.Format(
            CultureInfo.InvariantCulture, _strings[StudioStringKeys.MountsOriginConflict], OriginName, Mount.SharedRootCount),
        _ when OverridesSettings => string.Format(
            CultureInfo.InvariantCulture, _strings[StudioStringKeys.MountsOriginReplaces], OriginName, ReplacedSettingsMount),
        _ => OriginName,
    };

    private string OriginName => Origin switch
    {
        MountOrigin.AutoInjected => _strings[StudioStringKeys.MountsOriginAuto],
        MountOrigin.CommandLine => "--mount",
        _ => "appsettings",
    };
}

/// <summary>
/// The launch-time mount panel of spec §5.2: the mounts already declared in the appsettings the
/// launch will use, shown read-only, the per-launch mounts edited with the §4.5 form, and the
/// effective table that spells out the by-root override.
/// <para>
/// The override is the point of the panel. A <c>--mount</c> on a virtual root the appsettings
/// already declare replaces that entry for the run; on a new root it is appended
/// (<see cref="MountOverrideSemantics"/>), and a launcher that showed anything else would be
/// showing a mount list the runtime never sees.
/// </para>
/// </summary>
public sealed class LaunchMountsViewModel : ObservableObject
{
    private readonly IStudioStrings _strings;
    private bool _allowExternalMounts;
    private MountAutoInjection? _autoInjection;

    /// <summary>Builds the panel over a directory probe and the browse dialogs.</summary>
    public LaunchMountsViewModel(
        IDirectoryProbe? directories = null,
        IPathPicker? picker = null,
        IStudioStrings? strings = null)
    {
        _strings = strings ?? EnglishStudioStrings.Instance;

        // Per-run rows are not settings entries: no id (VFS-90, D-07).
        LaunchMounts = new MountsEditorViewModel(directories, picker, requireAtLeastOne: false, _strings, assignIds: false);
        LaunchMounts.Changed += (_, _) => RecomputeEffectiveMounts();

        // Recomputing rebuilds the effective rows, whose origin column is localized.
        _strings.CultureChanged += (_, _) =>
        {
            OnPropertiesChanged(
                nameof(OverrideExplanation), nameof(ExternalMountsExplanation), nameof(ExternalMountsWarning));
            RecomputeEffectiveMounts();
        };

        RecomputeEffectiveMounts();
    }

    /// <summary>Raised when the panel changes, so the command-line preview can be rebuilt.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// The mounts already in force from the appsettings the launch will use — informational,
    /// not editable here, and what a team entry is deduplicated against.
    /// </summary>
    public ObservableCollection<string> SettingsMounts { get; } = [];

    /// <summary>
    /// The adopted team's mounts as they stand for this machine (effective strings — what the
    /// team card's chips show). What of them reaches the command line is
    /// <see cref="Plan"/>'s business (VFS-90): a settings declaration goes by id, the team's
    /// own folders and copies as <c>--mount</c>, an unknown id blocks the launch.
    /// </summary>
    public ObservableCollection<string> TeamMounts { get; } = [];

    /// <summary>The team's mounts, resolved against the settings; empty for a non-team target.</summary>
    public IReadOnlyList<ResolvedTeamMount> ResolvedTeamMounts { get; private set; } = [];

    /// <summary>What the team's folders put on the command line.</summary>
    public LaunchMountPlan Plan { get; private set; } = LaunchMountPlan.For([]);

    /// <summary>The ids the team names that this machine does not declare; non-empty blocks the launch.</summary>
    public IReadOnlyList<string> UnknownTeamMountIds => Plan.UnknownIds;

    /// <summary>The per-launch mounts, edited with the same form as the appsettings editor.</summary>
    public MountsEditorViewModel LaunchMounts { get; }

    /// <summary>What each index will actually resolve to once the lists are combined by root.</summary>
    public ObservableCollection<EffectiveMountViewModel> EffectiveMounts { get; } = [];

    /// <summary>The sentence explaining the by-root override, shown above the table.</summary>
    public string OverrideExplanation => MountOverrideSemantics.ExplanationFor(_strings);

    /// <summary>The sentence explaining what <c>--allow-external-mounts</c> additionally permits.</summary>
    public string ExternalMountsExplanation => MountOverrideSemantics.ExternalMountsExplanationFor(_strings);

    /// <summary>The security warning shown next to the <c>--allow-external-mounts</c> checkbox.</summary>
    public string ExternalMountsWarning => _strings[StudioStringKeys.MountsExternalWarning];

    /// <summary>Whether to pass <c>--allow-external-mounts</c>.</summary>
    public bool AllowExternalMounts
    {
        get => _allowExternalMounts;
        set
        {
            if (SetProperty(ref _allowExternalMounts, value))
                Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// The <c>--mount</c> arguments this panel contributes: the team's own folders and copies
    /// (STUDIO-15 D-05, VFS-90), then the per-launch ones.
    /// </summary>
    public IReadOnlyList<string> ToMountArguments() => [.. Plan.Mounts, .. LaunchMounts.ToRawEntries()];

    /// <summary>The <c>--mount-id</c> arguments this panel contributes: the settings declarations the team names.</summary>
    public IReadOnlyList<string> ToMountIdArguments() => Plan.MountIds;

    /// <summary>Publishes the selected team's mounts, resolved against the settings (empty for a non-team target).</summary>
    public void SetTeamMounts(IReadOnlyList<ResolvedTeamMount> mounts)
    {
        ArgumentNullException.ThrowIfNull(mounts);

        var effective = mounts.Select(m => m.Effective).ToList();
        if (TeamMounts.SequenceEqual(effective, StringComparer.Ordinal) && ResolvedTeamMounts.SequenceEqual(mounts))
            return;

        ResolvedTeamMounts = mounts;
        Plan = LaunchMountPlan.For(mounts);
        TeamMounts.Clear();
        foreach (var mount in effective)
            TeamMounts.Add(mount);

        OnPropertiesChanged(nameof(ResolvedTeamMounts), nameof(Plan), nameof(UnknownTeamMountIds));
        RecomputeEffectiveMounts();
    }

    /// <summary>Publishes the mounts in force from the appsettings the launch will use.</summary>
    public void SetSettingsMounts(IReadOnlyList<string> mounts)
    {
        ArgumentNullException.ThrowIfNull(mounts);

        // Same guard as the team mounts: the tab republishes on every refresh, and recomputing
        // on an unchanged list would re-raise Changed — the event the tab reacts to.
        if (SettingsMounts.SequenceEqual(mounts, StringComparer.Ordinal))
            return;

        SettingsMounts.Clear();
        foreach (var mount in mounts)
            SettingsMounts.Add(mount);

        RecomputeEffectiveMounts();
    }

    /// <summary>
    /// The mounts the runner will inject ahead of the <c>--mount</c> arguments, published by
    /// the tab whenever the target or the options change. Null until a target is resolved.
    /// </summary>
    public MountAutoInjection? AutoInjection
    {
        get => _autoInjection;
        set
        {
            // Compared by content, not by reference: the tab republishes a freshly built value on
            // every form change, and recomputing on an unchanged one would re-raise Changed, which
            // is what the tab reacts to — an endless round trip.
            if (SameMounts(_autoInjection, value))
                return;

            _autoInjection = value;
            RecomputeEffectiveMounts();
        }
    }

    /// <summary>Whether the effective table can be computed at all.</summary>
    public bool HasAutoInjection => _autoInjection is not null;

    private static bool SameMounts(MountAutoInjection? left, MountAutoInjection? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return left.Mounts.SequenceEqual(right.Mounts, StringComparer.Ordinal);
    }

    /// <summary>Recomputes the effective table from the settings, team and launch lists.</summary>
    public void RecomputeEffectiveMounts()
    {
        EffectiveMounts.Clear();

        if (_autoInjection is { } autoInjection)
        {
            var effective = MountOverrideSemantics.ComputeEffectiveMounts(
                ToMountArguments(), [.. SettingsMounts], autoInjection, ToMountIdArguments());

            foreach (var mount in effective)
                EffectiveMounts.Add(new EffectiveMountViewModel(mount, _strings));
        }

        OnPropertiesChanged(nameof(OverriddenCount), nameof(Summary), nameof(HasAutoInjection));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>How many appsettings entries a launch mount on the same root replaces.</summary>
    public int OverriddenCount => EffectiveMounts.Count(m => m.OverridesSettings);

    /// <summary>The one-line verdict above the effective table.</summary>
    public string Summary
    {
        get
        {
            if (!HasAutoInjection)
                return _strings[StudioStringKeys.MountsSelectCrewFirst];

            if (OverriddenCount == 0)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    _strings[StudioStringKeys.MountsEffectiveNone], EffectiveMounts.Count);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                _strings[StudioStringKeys.MountsEffectiveReplaced], EffectiveMounts.Count, OverriddenCount);
        }
    }
}
