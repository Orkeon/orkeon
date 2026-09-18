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
    /// <summary>
    /// Reads the set. An absent file yields <see cref="ModelProfileSet.Empty"/> with no error;
    /// a file that exists but cannot be read or parsed yields the empty set <em>and</em> the
    /// reason in <see cref="ModelProfileLoadResult.Error"/> — the two must never look alike
    /// (STUDIO-12 C6).
    /// </summary>
    Task<ModelProfileLoadResult> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the set, replacing whatever was there.</summary>
    Task SaveAsync(ModelProfileSet profiles, CancellationToken cancellationToken = default);
}

/// <summary>
/// What a load produced: the set, plus — when the backing file exists but could not be
/// read or parsed — the reason, so a caller can say it instead of showing a first-run
/// screen over a file the user hand-wrote with a typo in it.
/// </summary>
/// <param name="Set">The profiles read; <see cref="ModelProfileSet.Empty"/> when nothing could be.</param>
/// <param name="Error">Why an existing file could not be read; null when the load was clean or the file absent.</param>
public sealed record ModelProfileLoadResult(ModelProfileSet Set, string? Error = null)
{
    /// <summary>The clean empty load: no file, no profile, no error.</summary>
    public static ModelProfileLoadResult Empty { get; } = new(ModelProfileSet.Empty);

    /// <summary>True when an existing file could not be read.</summary>
    public bool Failed => Error is not null;
}

/// <summary>In-memory store: the default for tests and for machines with no config directory.</summary>
public sealed class InMemoryModelProfileStore : IModelProfileStore
{
    private ModelProfileSet _set = ModelProfileSet.Empty;

    /// <inheritdoc />
    public Task<ModelProfileLoadResult> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ModelProfileLoadResult(_set));

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
/// the CLI already owns, where <c>studio-history.json</c> also lives. Loading never crashes:
/// a missing file is the empty set, and a corrupt or unreadable one degrades to the empty
/// set <em>with its reason attached</em> — the profiles are Studio comfort state, not the
/// source of truth for any run, but a file that fails to parse must not pass for an empty
/// one (STUDIO-12 C6: a hand-written camelCase file used to load zero profiles, silently).
/// Reads are case-insensitive on property names; writes stay PascalCase.
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; the model profiles are UI state written " +
    "to the per-user configuration directory on the physical disk, before any VFS mount exists.")]
public sealed class ModelProfileFileStore : IModelProfileStore
{
    /// <summary>Name of the file inside the per-user configuration directory.</summary>
    public const string FileName = "studio-model-profiles.json";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>
    /// Reads accept any casing of the property names: the file is documented as hand-editable,
    /// and <c>"profiles"</c> is what most people type.
    /// </summary>
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

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
    public async Task<ModelProfileLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(FilePath))
                return ModelProfileLoadResult.Empty;

            var json = await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false);
            var set = JsonSerializer.Deserialize<ModelProfileSet>(json, ReadOptions);
            return set is null
                ? new ModelProfileLoadResult(ModelProfileSet.Empty, $"{FilePath}: the file holds no profile set (its content is 'null').")
                : new ModelProfileLoadResult(set);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new ModelProfileLoadResult(ModelProfileSet.Empty, $"{FilePath}: {ex.Message}");
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

            var json = JsonSerializer.Serialize(profiles, WriteOptions);
            await File.WriteAllTextAsync(FilePath, json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Comfort state: losing a write must never take the screen down with it.
        }
    }
}
