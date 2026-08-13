using Orkeon.Compliance.Vfs;
using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Core.Storage;

/// <summary>
/// Loads and saves <see cref="AppSettingsDocument"/> files on the physical disk.
/// Saving is whole-file: the document keeps every key it was loaded with, so a save
/// rewrites the file without dropping what Studio does not model.
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; it reads and writes the user's " +
    "appsettings.json on the physical disk, before any VFS mount exists.")]
public static class AppSettingsFile
{
    /// <summary>True when a settings file exists at <paramref name="path"/>.</summary>
    public static bool Exists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.Exists(path);
    }

    /// <summary>Loads and parses a settings file.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="System.Text.Json.JsonException">The file is not a well-formed JSON object.</exception>
    public static async Task<AppSettingsDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return AppSettingsDocument.Parse(json);
    }

    /// <summary>
    /// Loads a settings file, reporting a missing or malformed file as a message —
    /// the "open" path of the UIs, where neither case is exceptional.
    /// </summary>
    public static async Task<(AppSettingsDocument? Document, string? Error)> TryLoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
            return (null, $"File not found: {path}");

        string json;
        try
        {
            json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            return (null, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return (null, ex.Message);
        }

        return AppSettingsDocument.TryParse(json, out var document, out var error)
            ? (document, null)
            : (null, error);
    }

    /// <summary>
    /// Writes the document to <paramref name="path"/>, creating the parent directory —
    /// the global per-user directory does not exist on a fresh machine.
    /// </summary>
    public static async Task SaveAsync(
        AppSettingsDocument document,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(path, document.ToJson(), cancellationToken).ConfigureAwait(false);
    }
}
