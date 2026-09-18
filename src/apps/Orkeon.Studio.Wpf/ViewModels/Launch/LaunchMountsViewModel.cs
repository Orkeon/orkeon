using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Localization;
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

    /// <summary>
    /// The origin column, phrased for the table. The runner's own entries are named as such
    /// rather than folded into "appsettings": the user never wrote them.
    /// </summary>
    public string OriginDisplay => OverridesSettings
        ? string.Format(
            CultureInfo.InvariantCulture,
            _strings[StudioStringKeys.MountsOriginReplaces], OriginName, ReplacedSettingsMount)
        : OriginName;

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

        LaunchMounts = new MountsEditorViewModel(directories, picker, requireAtLeastOne: false, _strings);
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
    /// The adopted team's own mounts, from its sidecar. What the team card's chips show is
    /// exactly this list. Laid on the launch ahead of the per-launch entries, through the
    /// same single <c>--mount</c> flag — except the entries the settings already hold
    /// (<see cref="LaunchMountPlan.WithoutSettingsDuplicates"/>): those are in force
    /// without a word from Studio.
    /// </summary>
    public ObservableCollection<string> TeamMounts { get; } = [];

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
    /// The <c>--mount</c> arguments this panel contributes: the team mounts the settings do not
    /// already hold (STUDIO-15 D-05), then the per-launch ones.
    /// </summary>
    public IReadOnlyList<string> ToMountArguments() =>
        [.. LaunchMountPlan.WithoutSettingsDuplicates([.. TeamMounts], [.. SettingsMounts]), .. LaunchMounts.ToRawEntries()];

    /// <summary>Publishes the selected team's sidecar mounts (empty for a non-team target).</summary>
    public void SetTeamMounts(IReadOnlyList<string> mounts)
    {
        ArgumentNullException.ThrowIfNull(mounts);

        if (TeamMounts.SequenceEqual(mounts, StringComparer.Ordinal))
            return;

        TeamMounts.Clear();
        foreach (var mount in mounts)
            TeamMounts.Add(mount);

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
                ToMountArguments(), [.. SettingsMounts], autoInjection);

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
