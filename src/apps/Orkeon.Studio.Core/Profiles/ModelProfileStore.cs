using System.Text.Json;
using Orkeon.Compliance.Vfs;
using Orkeon.Studio.Core.Storage;

namespace Orkeon.Studio.Core.Profiles;

/// <summary>
/// Persistence of the model-profile set. An interface so the front-ends and their view
/// models can be tested without a disk.
/// </summary>
public interface IModelProfileStore
{
    /// <summary>Reads the set; an absent or unreadable file yields <see cref="ModelProfileSet.Empty"/>.</summary>
    Task<ModelProfileSet> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the set, replacing whatever was there.</summary>
    Task SaveAsync(ModelProfileSet profiles, CancellationToken cancellationToken = default);
}

/// <summary>In-memory store: the default for tests and for machines with no config directory.</summary>
public sealed class InMemoryModelProfileStore : IModelProfileStore
{
    private ModelProfileSet _set = ModelProfileSet.Empty;

    /// <inheritdoc />
    public Task<ModelProfileSet> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_set);

    /// <inheritdoc />
    public Task SaveAsync(ModelProfileSet profiles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        _set = profiles;
        return Task.CompletedTask;
    }
}

/// <summary>
/// JSON file store, living next to the per-user <c>appsettings.json</c> — the one directory
/// the CLI already owns, where <c>studio-history.json</c> also lives. Loading is tolerant:
/// a missing, corrupt or unreadable file degrades to the empty set, never to a crash — the
/// profiles are Studio comfort state, not the source of truth for any run.
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; the model profiles are UI state written " +
    "to the per-user configuration directory on the physical disk, before any VFS mount exists.")]
public sealed class ModelProfileFileStore : IModelProfileStore
{
    /// <summary>Name of the file inside the per-user configuration directory.</summary>
    public const string FileName = "studio-model-profiles.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Creates a store over an explicit file path.</summary>
    public ModelProfileFileStore(string filePath)
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
    /// <returns>False on a machine with no per-user configuration directory;
    /// <paramref name="error"/> then carries the reason.</returns>
    public static bool TryGetDefaultPath(out string? path, out string? error)
    {
        if (!SettingsLocations.TryGetGlobalSettingsPath(out var settingsPath, out error))
        {
            path = null;
            return false;
        }

        var directory = Path.GetDirectoryName(settingsPath);
        if (string.IsNullOrEmpty(directory))
        {
            path = null;
            error = $"Cannot determine the configuration directory from {settingsPath}.";
            return false;
        }

        path = Path.Combine(directory, FileName);
        error = null;
        return true;
    }

    /// <inheritdoc />
    public async Task<ModelProfileSet> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(FilePath))
                return ModelProfileSet.Empty;

            var json = await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<ModelProfileSet>(json, Options) ?? ModelProfileSet.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return ModelProfileSet.Empty;
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(ModelProfileSet profiles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(profiles, Options);
            await File.WriteAllTextAsync(FilePath, json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Comfort state: losing a write must never take the screen down with it.
        }
    }
}
