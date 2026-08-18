namespace Orkeon.Studio.Core.Localization;

/// <summary>
/// Localization port for every string Core (and the ViewModels compiled against it)
/// fabricates below the view layer (STUDIO-11). The three Studio front-ends share
/// the Core formatters; this port lets each front decide the language: WPF bridges
/// it onto its resx-backed <c>I18n</c> (hot-swappable), the TUIs keep the English
/// default. Keys live in <see cref="StudioStringKeys"/>; the English fallback in
/// <see cref="EnglishStudioStrings"/> is also the key registry of record.
/// </summary>
public interface IStudioStrings
{
    /// <summary>Resolves a key to the current culture's string (the key itself when unknown).</summary>
    string this[string key] { get; }

    /// <summary>Raised when the culture changes, so ViewModels can re-emit their bindings.</summary>
    event EventHandler? CultureChanged;
}

/// <summary>
/// Keys of the strings Core fabricates. One constant per string — the English text
/// lives in <see cref="EnglishStudioStrings"/>, the French one in each front's
/// resource bridge (WPF: <c>Strings.resx</c>/<c>Strings.fr.resx</c>).
/// </summary>
public static class StudioStringKeys
{
    /// <summary>"Validation: no findings."</summary>
    public const string ValidationNoFindings = "Core_Validation_NoFindings";

    /// <summary>"Validation: {0} error(s), {1} warning(s), {2} note(s)."</summary>
    public const string ValidationSummary = "Core_Validation_Summary";

    /// <summary>"Nothing ran — {0}"</summary>
    public const string LaunchNothingRan = "Core_Launch_NothingRan";

    /// <summary>"Exit code {0} — {1}"</summary>
    public const string LaunchExitCode = "Core_Launch_ExitCode";
}

/// <summary>
/// English defaults — the culture-neutral fallback every front starts from, and the
/// single place a new Core string is declared before its translations exist.
/// </summary>
public sealed class EnglishStudioStrings : IStudioStrings
{
    /// <summary>The shared instance (the class is immutable).</summary>
    public static EnglishStudioStrings Instance { get; } = new();

    private static readonly Dictionary<string, string> Strings = new(StringComparer.Ordinal)
    {
        [StudioStringKeys.ValidationNoFindings] = "Validation: no findings.",
        [StudioStringKeys.ValidationSummary] = "Validation: {0} error(s), {1} warning(s), {2} note(s).",
        [StudioStringKeys.LaunchNothingRan] = "Nothing ran — {0}",
        [StudioStringKeys.LaunchExitCode] = "Exit code {0} — {1}",
    };

    /// <inheritdoc />
    public string this[string key] => Strings.GetValueOrDefault(key, key);

    /// <inheritdoc />
    public event EventHandler? CultureChanged
    {
        add { }     // English defaults never change culture.
        remove { }
    }
}
