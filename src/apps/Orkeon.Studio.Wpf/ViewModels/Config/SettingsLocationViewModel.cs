using System.Collections.Generic;
using System.Collections.ObjectModel;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Storage;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>Where a document may be saved (spec §4.3).</summary>
public enum SettingsLocationMode
{
    /// <summary>The per-user file written by <c>orkeon init</c>. The default.</summary>
    Global,

    /// <summary>Any path the user picks, typically next to a crew.</summary>
    CustomPath,
}

/// <summary>
/// The save-location picker of spec §4.3, together with the full resolution chain. Showing the chain
/// is the point: it is what tells the user which file the runtime will actually load, given that a
/// file next to the crew silently wins over the global one.
/// </summary>
public sealed class SettingsLocationViewModel : ObservableObject
{
    private readonly IPathPicker _picker;
    private readonly IStudioStrings _strings;
    private SettingsLocationMode _mode = SettingsLocationMode.Global;
    private string? _customPath;
    private string? _globalPathError;

    /// <summary>Resolves the global path and wires the browse dialogs.</summary>
    public SettingsLocationViewModel(
        IPathPicker? picker = null,
        string? globalPathOverride = null,
        IStudioStrings? strings = null)
    {
        _picker = picker ?? NullPathPicker.Instance;
        _strings = strings ?? EnglishStudioStrings.Instance;
        _strings.CultureChanged += (_, _) => RefreshResolutionChain();

        if (globalPathOverride is { Length: > 0 })
        {
            GlobalPath = globalPathOverride;
        }
        else if (SettingsLocations.TryGetGlobalSettingsPath(out var path, out var error))
        {
            GlobalPath = path;
        }
        else
        {
            _globalPathError = error;
        }

        foreach (var step in SettingsLocations.ResolutionChainFor(_strings))
            ResolutionChain.Add(step);

        BrowseCommand = new RelayCommand(Browse);
        UseGlobalCommand = new RelayCommand(() => Mode = SettingsLocationMode.Global);
    }

    /// <summary>Re-publishes the chain in the new culture (STUDIO-11).</summary>
    private void RefreshResolutionChain()
    {
        ResolutionChain.Clear();
        foreach (var step in SettingsLocations.ResolutionChainFor(_strings))
            ResolutionChain.Add(step);

        OnPropertyChanged(nameof(ResolutionChainLines));
    }

    /// <summary>The four steps the runtime walks to find an appsettings file.</summary>
    public ObservableCollection<SettingsResolutionStep> ResolutionChain { get; } = [];

    /// <summary>Opens the save dialog and switches to <see cref="SettingsLocationMode.CustomPath"/>.</summary>
    public RelayCommand BrowseCommand { get; }

    /// <summary>Switches back to the global file.</summary>
    public RelayCommand UseGlobalCommand { get; }

    /// <summary>The per-user file, when the platform lets us resolve one.</summary>
    public string? GlobalPath { get; }

    /// <summary>Why the global path could not be resolved, when it could not.</summary>
    public string? GlobalPathError
    {
        get => _globalPathError;
        private set => SetProperty(ref _globalPathError, value);
    }

    /// <summary>Which of the two targets is selected.</summary>
    public SettingsLocationMode Mode
    {
        get => _mode;
        set
        {
            if (SetProperty(ref _mode, value))
                OnPropertiesChanged(nameof(IsGlobal), nameof(IsCustom), nameof(EffectivePath), nameof(CanSave));
        }
    }

    /// <summary>Whether the global file is selected.</summary>
    public bool IsGlobal => Mode == SettingsLocationMode.Global;

    /// <summary>Whether a free path is selected.</summary>
    public bool IsCustom => Mode == SettingsLocationMode.CustomPath;

    /// <summary>The free path, normalized so that a folder becomes <c>&lt;folder&gt;/appsettings.json</c>.</summary>
    public string? CustomPath
    {
        get => _customPath;
        set
        {
            if (SetProperty(ref _customPath, value))
                OnPropertiesChanged(nameof(EffectivePath), nameof(CanSave));
        }
    }

    /// <summary>The path a save would actually write to.</summary>
    public string? EffectivePath => IsGlobal
        ? GlobalPath
        : CustomPath is { Length: > 0 } path ? SettingsLocations.NormalizeTargetPath(path) : null;

    /// <summary>Whether a save can be attempted at all.</summary>
    public bool CanSave => EffectivePath is { Length: > 0 };

    /// <summary>Points the picker at an explicit path, e.g. the file that was just opened.</summary>
    public void UseCustomPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        CustomPath = path;
        Mode = SettingsLocationMode.CustomPath;
    }

    private void Browse()
    {
        var picked = _picker.PickSaveFile(
            _strings[StudioStringKeys.DialogSaveAppSettings],
            _strings[StudioStringKeys.DialogFilterJson],
            CustomPath ?? GlobalPath);

        if (picked is { Length: > 0 })
            UseCustomPath(picked);
    }

    /// <summary>The label under the location radio buttons, naming the file that will be written.</summary>
    public static string SummaryFileName => AppSettingsDocument.FileName;

    /// <summary>The resolution chain, flattened for a read-only text block.</summary>
    public IReadOnlyList<string> ResolutionChainLines =>
        [.. ResolutionChain.Select(step => $"{step.Order}. {step.Title} — {step.Description}")];
}
