using System.IO;
using System.Text.Json;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Studio.Wpf.Services;

/// <summary>
/// Theme and language, persisted per user. Pure presentation state: nothing here exists in
/// Orkeon.Studio.Core, and no ViewModel knows this file exists. Every disk touch is tolerant —
/// a missing, corrupt or unwritable file always degrades to the defaults, never to a crash.
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application. The UI preferences file lives in the user's "
    + "local application data, is read before any VFS mount exists, and never touches crew data.")]
public sealed record UiPreferences
{
    /// <summary>"dark" or "light"; anything else means the default (light).</summary>
    public string? Theme { get; init; }

    /// <summary>"en" or "fr"; anything else means the default (en).</summary>
    public string? Language { get; init; }

    /// <summary>Convenience view of <see cref="Theme"/>.</summary>
    public bool IsDark => string.Equals(Theme, "dark", StringComparison.OrdinalIgnoreCase);

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Orkeon", "Studio", "ui-preferences.json");

    /// <summary>Loads the stored preferences, or the defaults when there is nothing usable.</summary>
    public static UiPreferences Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(FilePath)) ?? new UiPreferences()
                : new UiPreferences();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new UiPreferences();
        }
    }

    /// <summary>Persists the current choices; a failure to write is silently accepted.</summary>
    public static void Save(bool dark, string language)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            var prefs = new UiPreferences { Theme = dark ? "dark" : "light", Language = language };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(prefs));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferences are a comfort, not a feature: losing them must never surface.
        }
    }
}
