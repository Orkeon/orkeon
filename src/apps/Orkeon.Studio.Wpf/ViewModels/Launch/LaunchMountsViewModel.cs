using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Wpf.ViewModels.Mounts;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.ViewModels.Launch;

/// <summary>One row of the effective mount table: what index <c>i</c> resolves to, and where it came from.</summary>
public sealed class EffectiveMountViewModel
{
    /// <summary>Wraps a computed effective mount.</summary>
    public EffectiveMountViewModel(EffectiveMount mount)
    {
        ArgumentNullException.ThrowIfNull(mount);

        Mount = mount;
    }

    /// <summary>The underlying Core record.</summary>
    public EffectiveMount Mount { get; }

    /// <summary>Its position, which is what decides the override.</summary>
    public int Index => Mount.Index;

    /// <summary>The mount string that will be in force.</summary>
    public string Value => Mount.Value;

    /// <summary>The configuration key the runtime binds it to.</summary>
    public string ConfigurationKey => Mount.ConfigurationKey;

    /// <summary>Whether it comes from the command line or from the file.</summary>
    public MountOrigin Origin => Mount.Origin;

    /// <summary>Whether it replaces an appsettings entry at the same index.</summary>
    public bool OverridesSettings => Mount.OverridesSettings;

    /// <summary>The appsettings entry that is being shadowed, when there is one.</summary>
    public string? ReplacedSettingsMount => Mount.ReplacedSettingsMount;

    /// <summary>The origin column, phrased for the table.</summary>
    public string OriginDisplay => OverridesSettings
        ? string.Create(CultureInfo.InvariantCulture, $"--mount (replaces «{ReplacedSettingsMount}»)")
        : Origin == MountOrigin.CommandLine ? "--mount" : "appsettings";
}

/// <summary>
/// The launch-time mount panel of spec §5.2: the mounts already declared in the selected appsettings
/// shown read-only, the per-launch mounts edited with the §4.5 form, and the effective table that
/// spells out the index-based override.
/// <para>
/// The override is the point of the panel. <c>--mount</c> arguments are injected as
/// <c>Orkeon:FileSystem:Mounts:{i}</c>, so they replace the file entry at the same index instead of
/// being merged with it, and a launcher that let the user assume a merge would be actively misleading.
/// </para>
/// </summary>
public sealed class LaunchMountsViewModel : ObservableObject
{
    private bool _allowExternalMounts;

    /// <summary>Builds the panel over a directory probe and the browse dialogs.</summary>
    public LaunchMountsViewModel(IDirectoryProbe? directories = null, IPathPicker? picker = null)
    {
        LaunchMounts = new MountsEditorViewModel(directories, picker, requireAtLeastOne: false);
        LaunchMounts.Changed += (_, _) => RecomputeEffectiveMounts();

        RecomputeEffectiveMounts();
    }

    /// <summary>Raised when the panel changes, so the command-line preview can be rebuilt.</summary>
    public event EventHandler? Changed;

    /// <summary>The mounts already in the selected appsettings — informational, not editable here.</summary>
    public ObservableCollection<string> SettingsMounts { get; } = [];

    /// <summary>The per-launch mounts, edited with the same form as the appsettings editor.</summary>
    public MountsEditorViewModel LaunchMounts { get; }

    /// <summary>What each index will actually resolve to once the two lists are combined.</summary>
    public ObservableCollection<EffectiveMountViewModel> EffectiveMounts { get; } = [];

    /// <summary>The sentence explaining the index-based override, shown above the table.</summary>
    public static string OverrideExplanation => MountOverrideSemantics.Explanation;

    /// <summary>The sentence explaining what <c>--allow-external-mounts</c> additionally permits.</summary>
    public static string ExternalMountsExplanation => MountOverrideSemantics.ExternalMountsExplanation;

    /// <summary>The security warning shown next to the <c>--allow-external-mounts</c> checkbox.</summary>
    public static string ExternalMountsWarning =>
        "Security: this lets a mount point anywhere on the machine, outside the working directory. "
        + "Only enable it for a path you chose deliberately.";

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

    /// <summary>The <c>--mount</c> arguments this panel contributes.</summary>
    public IReadOnlyList<string> ToMountArguments() => LaunchMounts.ToRawEntries();

    /// <summary>Publishes the mounts read from the appsettings the launch will use.</summary>
    public void SetSettingsMounts(IReadOnlyList<string> mounts)
    {
        ArgumentNullException.ThrowIfNull(mounts);

        SettingsMounts.Clear();
        foreach (var mount in mounts)
            SettingsMounts.Add(mount);

        RecomputeEffectiveMounts();
    }

    /// <summary>Recomputes the effective table from the two lists.</summary>
    public void RecomputeEffectiveMounts()
    {
        var effective = MountOverrideSemantics.ComputeEffectiveMounts(ToMountArguments(), [.. SettingsMounts]);

        EffectiveMounts.Clear();
        foreach (var mount in effective)
            EffectiveMounts.Add(new EffectiveMountViewModel(mount));

        OnPropertiesChanged(nameof(OverriddenCount), nameof(Summary));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>How many appsettings entries are shadowed by a launch mount.</summary>
    public int OverriddenCount => EffectiveMounts.Count(m => m.OverridesSettings);

    /// <summary>The one-line verdict above the effective table.</summary>
    public string Summary => OverriddenCount == 0
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"{EffectiveMounts.Count} effective mount(s); no appsettings entry is replaced.")
        : string.Create(
            CultureInfo.InvariantCulture,
            $"{EffectiveMounts.Count} effective mount(s); {OverriddenCount} appsettings entry(ies) replaced by index.");
}
