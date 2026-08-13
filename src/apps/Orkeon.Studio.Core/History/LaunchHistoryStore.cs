using Orkeon.Compliance.Vfs;
using Orkeon.Studio.Core.Storage;

namespace Orkeon.Studio.Core.History;

/// <summary>
/// Persistence of the recent-launch list. An interface so the front-ends and their view
/// models can be tested without a disk.
/// </summary>
public interface ILaunchHistoryStore
{
    /// <summary>Reads the history; an absent or unreadable file yields <see cref="LaunchHistory.Empty"/>.</summary>
    Task<LaunchHistory> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the history, replacing whatever was there.</summary>
    Task SaveAsync(LaunchHistory history, CancellationToken cancellationToken = default);

    /// <summary>Loads, prepends <paramref name="entry"/>, saves, and returns the new history.</summary>
    Task<LaunchHistory> RecordAsync(LaunchHistoryEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>
/// JSON file store, living next to the per-user <c>appsettings.json</c> so Studio keeps all
/// of its user state in the one directory the CLI already owns
/// (<c>%APPDATA%\Orkeon</c> / <c>$XDG_CONFIG_HOME/Orkeon</c>).
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; the launch history is UI state written " +
    "to the per-user configuration directory on the physical disk, before any VFS mount exists.")]
public sealed class LaunchHistoryFileStore : ILaunchHistoryStore
{
    /// <summary>Name of the file inside the per-user configuration directory.</summary>
    public const string FileName = "studio-history.json";

    /// <summary>Creates a store over an explicit file path.</summary>
    public LaunchHistoryFileStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
    }

    /// <summary>Absolute path of the backing file.</summary>
    public string FilePath { get; }

    /// <summary>
    /// The default location: <see cref="FileName"/> in the directory holding the global
    /// <c>appsettings.json</c>.
    /// </summary>
    /// <returns>False on a machine with no per-user configuration directory (a bare container
    /// without <c>HOME</c>); <paramref name="error"/> then carries the reason.</returns>
    public static bool TryGetDefaultPath(out string? path, out string? error)
    {
        if (!SettingsLocations.TryGetGlobalSettingsPath(out var settingsPath, out error))
        {
            path = null;
            return false;
        }

        var directory = System.IO.Path.GetDirectoryName(settingsPath);
        if (string.IsNullOrEmpty(directory))
        {
            path = null;
            error = $"Cannot determine the configuration directory from {settingsPath}.";
            return false;
        }

        path = System.IO.Path.Combine(directory, FileName);
        error = null;
        return true;
    }

    /// <summary>The store at <see cref="TryGetDefaultPath"/>'s location.</summary>
    /// <exception cref="InvalidOperationException">No per-user configuration directory exists.</exception>
    public static LaunchHistoryFileStore ForCurrentUser() =>
        TryGetDefaultPath(out var path, out var error)
            ? new LaunchHistoryFileStore(path!)
            : throw new InvalidOperationException(error);

    /// <inheritdoc />
    public async Task<LaunchHistory> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
            return LaunchHistory.Empty;

        string json;
        try
        {
            json = await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return LaunchHistory.Empty;
        }

        return LaunchHistory.TryParse(json, out var history, out _) ? history : LaunchHistory.Empty;
    }

    /// <inheritdoc />
    public async Task SaveAsync(LaunchHistory history, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(history);

        var directory = System.IO.Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(FilePath, history.ToJson(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<LaunchHistory> RecordAsync(LaunchHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var history = (await LoadAsync(cancellationToken).ConfigureAwait(false)).Add(entry);
        await SaveAsync(history, cancellationToken).ConfigureAwait(false);
        return history;
    }
}
