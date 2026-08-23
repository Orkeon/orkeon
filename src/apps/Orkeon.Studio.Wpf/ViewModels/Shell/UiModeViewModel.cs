using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Shell;

/// <summary>
/// The global Novice/Expert switch of the v3 design ("volets"). Novice explains every step,
/// shows the contextual help column and hides the machinery; Expert shows everything — the
/// exact command line, the technical journal, the raw JSON. One switch for the whole window,
/// persisted per user; screens consume it through <see cref="IsExpert"/> / <see cref="IsNovice"/>
/// / <see cref="HelpOn"/> bindings, the WPF equivalent of the design's
/// <c>expertOnly</c>/<c>noviceOnly</c>/<c>helpOn</c> style flags.
/// </summary>
public sealed class UiModeViewModel : ObservableObject
{
    /// <summary>The guided default: explanations on, machinery folded away.</summary>
    public const string Novice = "novice";

    /// <summary>Everything visible: command lines, journals, raw JSON.</summary>
    public const string Expert = "expert";

    private readonly Action<string>? _persist;
    private string _mode;

    /// <summary>
    /// Builds the switch. Anything other than "expert" reads as novice — the guided mode is
    /// the safe default for a corrupt or missing preference.
    /// </summary>
    /// <param name="initialMode">The persisted mode, or null on first run.</param>
    /// <param name="persist">Called with the new mode after each user switch.</param>
    public UiModeViewModel(string? initialMode = null, Action<string>? persist = null)
    {
        _mode = Normalize(initialMode);
        _persist = persist;
        SetNoviceCommand = new RelayCommand(() => Mode = Novice);
        SetExpertCommand = new RelayCommand(() => Mode = Expert);
    }

    /// <summary>"novice" or "expert" — the value that goes to the preferences file.</summary>
    public string Mode
    {
        get => _mode;
        set
        {
            var normalized = Normalize(value);
            if (!SetProperty(ref _mode, normalized))
                return;

            OnPropertiesChanged(nameof(IsNovice), nameof(IsExpert), nameof(HelpOn));
            _persist?.Invoke(normalized);
        }
    }

    /// <summary>True in the guided mode.</summary>
    public bool IsNovice => _mode == Novice;

    /// <summary>True when the machinery is shown.</summary>
    public bool IsExpert => _mode == Expert;

    /// <summary>Whether the contextual help columns are shown (novice only).</summary>
    public bool HelpOn => IsNovice;

    /// <summary>Switches to the guided mode.</summary>
    public RelayCommand SetNoviceCommand { get; }

    /// <summary>Switches to the full-visibility mode.</summary>
    public RelayCommand SetExpertCommand { get; }

    private static string Normalize(string? mode) =>
        string.Equals(mode, Expert, StringComparison.OrdinalIgnoreCase) ? Expert : Novice;
}
