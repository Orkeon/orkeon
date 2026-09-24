using System.IO;
using Orkeon.Compliance.Vfs;
using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.Services;

/// <summary>
/// Theme, language, Novice/Expert mode and Settings › Studio, persisted per user. Pure
/// presentation state: nothing here exists in Orkeon.Studio.Core, and no ViewModel knows this
/// file exists — they read and write through <see cref="StudioUiPreferences"/>. Every disk
/// touch is tolerant — a missing, corrupt or unwritable file always degrades to the defaults,
/// never to a crash — and every write MERGES (<see cref="UiPreferencesDocument"/>): a gesture
/// writes the keys it owns and leaves every other key as it found it (STUDIO-35 D-06).
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application. The UI preferences file lives in the user's "
    + "local application data, is read before any VFS mount exists, and never touches crew data.")]
public sealed record UiPreferences
{
    /// <summary>"dark" or "light"; anything else means the default (light).</summary>
    public string? Theme { get; init; }

    /// <summary>
    /// The language the user EXPLICITLY picked, or null when they never did. Null is the
    /// meaningful value: it means the machine decides again on every start, so changing the
    /// Windows language is still followed. A detected language must never be written here —
    /// the first launch would freeze it forever.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>"novice" or "expert"; anything else means the default (novice).</summary>
    public string? Mode { get; init; }

    /// <summary>What Settings › Studio holds: the balance's automatic reading and alert thresholds (STUDIO-35).</summary>
    public StudioSettings Studio { get; init; } = StudioSettings.Default;

    /// <summary>Convenience view of <see cref="Theme"/>.</summary>
    public bool IsDark => string.Equals(Theme, "dark", StringComparison.OrdinalIgnoreCase);

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Orkeon", "Studio", "ui-preferences.json");

    /// <summary>Loads the stored preferences, or the defaults when there is nothing usable.</summary>
    public static UiPreferences Load()
    {
        string? text;
        try
        {
            text = File.Exists(FilePath) ? File.ReadAllText(FilePath) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            text = null;
        }

        var document = UiPreferencesDocument.Parse(text);
        return new UiPreferences
        {
            Theme = document.Theme,
            Language = document.Language,
            Mode = document.Mode,
            Studio = document.Studio,
        };
    }

    /// <summary>
    /// Persists the window's appearance; a failure to write is silently accepted.
    /// <paramref name="language"/> is null unless the user picked one — passing the running
    /// language here is the bug this signature exists to make hard: every theme or mode
    /// toggle would then stamp the detected language into the file. Settings › Studio, and any
    /// key a later Studio writes, stay as they were.
    /// </summary>
    public static void Save(bool dark, string? language, string mode) =>
        Merge(document => document.SetAppearance(dark ? "dark" : "light", language, mode));

    /// <summary>Persists Settings › Studio; the theme, the language and the mode stay as they were.</summary>
    public static void SaveStudio(StudioSettings settings) =>
        Merge(document => document.SetStudio(settings));

    /// <summary>
    /// Reads the file, applies one change, writes it back. A file that exists and cannot be read
    /// is left alone: writing over it would erase what it holds.
    /// </summary>
    private static void Merge(Action<UiPreferencesDocument> change)
    {
        try
        {
            var document = UiPreferencesDocument.Parse(File.Exists(FilePath) ? File.ReadAllText(FilePath) : null);
            change(document);

            var directory = Path.GetDirectoryName(FilePath);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(FilePath, document.ToJson());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferences are a comfort, not a feature: losing them must never surface.
        }
    }
}
