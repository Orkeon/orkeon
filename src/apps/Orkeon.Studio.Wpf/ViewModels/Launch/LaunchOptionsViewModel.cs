using System.Collections.Generic;
using System.Collections.ObjectModel;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.ViewModels.Launch;

/// <summary>One repeatable <c>-V KEY=VALUE</c> pair.</summary>
public sealed class RunVariableViewModel : ObservableObject
{
    private string _key = "";
    private string _value = "";

    /// <summary>The variable name, which may not contain <c>=</c>.</summary>
    public string Key
    {
        get => _key;
        set
        {
            if (SetProperty(ref _key, value))
                Edited?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The value substituted into the YAML crew.</summary>
    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
                Edited?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised on every edit, so the command-line preview can be rebuilt.</summary>
    public event EventHandler? Edited;

    /// <summary>Converts to the Core record.</summary>
    public RunVariable ToVariable() => new(Key, Value);
}

/// <summary>Which appsettings file a launch uses (spec §5.2).</summary>
public enum SettingsSelectionMode
{
    /// <summary>Pass nothing: the runtime walks its own resolution chain.</summary>
    Automatic,

    /// <summary>Pass <c>--settings &lt;path&gt;</c>.</summary>
    ExplicitPath,
}

/// <summary>
/// The run options of spec §5.2, mapped one-for-one onto <c>RunCommandOptions</c>.
/// <para>
/// Availability follows <see cref="RunOptionAvailability"/>: the YAML-only and script-only options
/// are hidden for the other dialect rather than merely disabled, so the panel never offers a flag
/// the CLI would ignore.
/// </para>
/// </summary>
public sealed class LaunchOptionsViewModel : ObservableObject
{
    private readonly IPathPicker _picker;
    private RunTarget? _target;
    private SettingsSelectionMode _settingsMode = SettingsSelectionMode.Automatic;
    private string? _settingsPath;
    private string? _initialContext;
    private string? _inputsJson;
    private string? _inputsFilePath;
    private int _verbosity;
    private bool _llmLogEnabled;
    private string? _llmLogPath;
    private RunVariableViewModel? _selectedVariable;

    /// <summary>Builds the option panel over the browse dialogs.</summary>
    public LaunchOptionsViewModel(IPathPicker? picker = null)
    {
        _picker = picker ?? NullPathPicker.Instance;

        Variables.CollectionChanged += (_, e) =>
        {
            foreach (var item in e.NewItems?.OfType<RunVariableViewModel>() ?? [])
                item.Edited += OnOptionEdited;

            foreach (var item in e.OldItems?.OfType<RunVariableViewModel>() ?? [])
                item.Edited -= OnOptionEdited;

            OnOptionEdited(this, EventArgs.Empty);
        };

        AddVariableCommand = new RelayCommand(() => AddVariable(), () => AreVariablesAvailable);
        RemoveVariableCommand = new RelayCommand(RemoveSelectedVariable, () => SelectedVariable is not null);
        BrowseSettingsCommand = new RelayCommand(BrowseSettings);
        BrowseInputsFileCommand = new RelayCommand(BrowseInputsFile, () => IsScriptTarget);
        BrowseLlmLogPathCommand = new RelayCommand(BrowseLlmLogPath);
    }

    /// <summary>Raised whenever an option changes, so the command-line preview can be rebuilt.</summary>
    public event EventHandler? Changed;

    /// <summary>The three verbosity levels the CLI accepts.</summary>
    public static IReadOnlyList<int> VerbosityChoices { get; } = [0, 1, RunLaunchOptions.MaxVerbosity];

    /// <summary>Appends a <c>-V</c> row.</summary>
    public RelayCommand AddVariableCommand { get; }

    /// <summary>Removes <see cref="SelectedVariable"/>.</summary>
    public RelayCommand RemoveVariableCommand { get; }

    /// <summary>Picks the appsettings file passed as <c>--settings</c>.</summary>
    public RelayCommand BrowseSettingsCommand { get; }

    /// <summary>Picks the file passed as <c>--inputs-file</c>.</summary>
    public RelayCommand BrowseInputsFileCommand { get; }

    /// <summary>Picks the file passed as <c>--llm-log-path</c>.</summary>
    public RelayCommand BrowseLlmLogPathCommand { get; }

    /// <summary>The repeatable <c>-V KEY=VALUE</c> pairs (YAML targets only).</summary>
    public ObservableCollection<RunVariableViewModel> Variables { get; } = [];

    /// <summary>The target the options are gated on. Setting it re-gates the whole panel.</summary>
    public RunTarget? Target
    {
        get => _target;
        set
        {
            if (!SetProperty(ref _target, value))
                return;

            OnPropertiesChanged(
                nameof(IsYamlTarget),
                nameof(IsScriptTarget),
                nameof(AreVariablesAvailable),
                nameof(IsInitialContextAvailable),
                nameof(AreInputsAvailable));

            AddVariableCommand.RaiseCanExecuteChanged();
            BrowseInputsFileCommand.RaiseCanExecuteChanged();
            OnOptionEdited(this, EventArgs.Empty);
        }
    }

    /// <summary>Whether the target is a YAML crew, so <c>-V</c> and <c>--initial-context</c> apply.</summary>
    public bool IsYamlTarget => Target?.Dialect == RunTargetDialect.Yaml;

    /// <summary>Whether the target is a script, so <c>--inputs</c> and <c>--inputs-file</c> apply.</summary>
    public bool IsScriptTarget => Target?.Dialect == RunTargetDialect.Script;

    /// <summary>Whether the <c>-V</c> list should be shown at all.</summary>
    public bool AreVariablesAvailable => IsOptionAvailable(RunOption.Variables);

    /// <summary>Whether the initial-context box should be shown at all.</summary>
    public bool IsInitialContextAvailable => IsOptionAvailable(RunOption.InitialContext);

    /// <summary>Whether the inputs boxes should be shown at all.</summary>
    public bool AreInputsAvailable => IsOptionAvailable(RunOption.Inputs);

    /// <summary>Whether an option applies to the detected dialect.</summary>
    public bool IsOptionAvailable(RunOption option) =>
        Target is { } target && RunOptionAvailability.IsAvailable(target.Dialect, option);

    /// <summary>Automatic resolution, or an explicit <c>--settings</c> path.</summary>
    public SettingsSelectionMode SettingsMode
    {
        get => _settingsMode;
        set
        {
            if (SetProperty(ref _settingsMode, value))
            {
                OnPropertiesChanged(nameof(IsSettingsAutomatic), nameof(IsSettingsExplicit));
                OnOptionEdited(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Whether the runtime resolution chain is left alone.</summary>
    public bool IsSettingsAutomatic => SettingsMode == SettingsSelectionMode.Automatic;

    /// <summary>Whether an explicit file is passed.</summary>
    public bool IsSettingsExplicit => SettingsMode == SettingsSelectionMode.ExplicitPath;

    /// <summary>The file passed as <c>--settings</c>, used only in explicit mode.</summary>
    public string? SettingsPath
    {
        get => _settingsPath;
        set
        {
            if (SetProperty(ref _settingsPath, value))
            {
                OnPropertyChanged(nameof(EffectiveSettingsPath));
                OnOptionEdited(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>The path actually passed on the command line, <see langword="null"/> in automatic mode.</summary>
    public string? EffectiveSettingsPath => IsSettingsExplicit ? _settingsPath : null;

    /// <summary>The <c>--initial-context</c> payload (YAML targets only).</summary>
    public string? InitialContext
    {
        get => _initialContext;
        set
        {
            if (SetProperty(ref _initialContext, value))
                OnOptionEdited(this, EventArgs.Empty);
        }
    }

    /// <summary>The inline <c>--inputs</c> JSON (script targets only).</summary>
    public string? InputsJson
    {
        get => _inputsJson;
        set
        {
            if (SetProperty(ref _inputsJson, value))
                OnOptionEdited(this, EventArgs.Empty);
        }
    }

    /// <summary>The <c>--inputs-file</c> path (script targets only).</summary>
    public string? InputsFilePath
    {
        get => _inputsFilePath;
        set
        {
            if (SetProperty(ref _inputsFilePath, value))
                OnOptionEdited(this, EventArgs.Empty);
        }
    }

    /// <summary>The <c>--verbose</c> level: 0, 1 or 2.</summary>
    public int Verbosity
    {
        get => _verbosity;
        set
        {
            if (SetProperty(ref _verbosity, value))
                OnOptionEdited(this, EventArgs.Empty);
        }
    }

    /// <summary>Whether to pass <c>--llm-log</c>.</summary>
    public bool LlmLogEnabled
    {
        get => _llmLogEnabled;
        set
        {
            if (SetProperty(ref _llmLogEnabled, value))
                OnOptionEdited(this, EventArgs.Empty);
        }
    }

    /// <summary>The <c>--llm-log-path</c> destination.</summary>
    public string? LlmLogPath
    {
        get => _llmLogPath;
        set
        {
            if (SetProperty(ref _llmLogPath, value))
                OnOptionEdited(this, EventArgs.Empty);
        }
    }

    /// <summary>The <c>-V</c> row targeted by <see cref="RemoveVariableCommand"/>.</summary>
    public RunVariableViewModel? SelectedVariable
    {
        get => _selectedVariable;
        set
        {
            if (SetProperty(ref _selectedVariable, value))
                RemoveVariableCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Appends a <c>-V</c> row and selects it.</summary>
    public RunVariableViewModel AddVariable(string key = "", string value = "")
    {
        var variable = new RunVariableViewModel { Key = key, Value = value };
        Variables.Add(variable);
        SelectedVariable = variable;
        return variable;
    }

    /// <summary>
    /// Assembles the Core options record. Options that do not apply to the current dialect are left
    /// out rather than carried through, so the builder never has to reject them.
    /// </summary>
    public RunLaunchOptions ToOptions(
        IReadOnlyList<string>? mounts = null,
        bool allowExternalMounts = false,
        bool validate = false) => new()
    {
        SettingsPath = EffectiveSettingsPath,
        Variables = IsYamlTarget ? [.. Variables.Select(v => v.ToVariable())] : [],
        InitialContext = IsYamlTarget ? InitialContext : null,
        InputsJson = IsScriptTarget ? InputsJson : null,
        InputsFilePath = IsScriptTarget ? InputsFilePath : null,
        Mounts = mounts ?? [],
        AllowExternalMounts = allowExternalMounts,
        Verbosity = Verbosity,
        LlmLogEnabled = LlmLogEnabled,
        LlmLogPath = LlmLogPath,
        Validate = validate,
    };

    private void RemoveSelectedVariable()
    {
        if (SelectedVariable is { } variable)
            Variables.Remove(variable);
    }

    private void BrowseSettings()
    {
        var picked = _picker.PickFile("Select an appsettings.json", "JSON files|*.json|All files|*.*", SettingsPath);
        if (picked is not { Length: > 0 })
            return;

        SettingsPath = picked;
        SettingsMode = SettingsSelectionMode.ExplicitPath;
    }

    private void BrowseInputsFile()
    {
        var picked = _picker.PickFile("Select an inputs file", "JSON files|*.json|All files|*.*", InputsFilePath);
        if (picked is { Length: > 0 })
            InputsFilePath = picked;
    }

    private void BrowseLlmLogPath()
    {
        var picked = _picker.PickSaveFile("Select the LLM log destination", "All files|*.*", LlmLogPath);
        if (picked is { Length: > 0 })
            LlmLogPath = picked;
    }

    private void OnOptionEdited(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
