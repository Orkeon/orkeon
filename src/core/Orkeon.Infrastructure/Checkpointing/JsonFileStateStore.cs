using System.Text.Json;
using System.Text.RegularExpressions;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Domain.Constants.Serialization;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Constants.Security;

namespace Orkeon.Infrastructure.Checkpointing;

/// <summary>
/// File-system implementation of <see cref="IStateStore"/> that persists
/// each session as an individual JSON file in a configurable virtual directory.
/// Supports time-travel via versioned files stored in per-session subdirectories.
/// </summary>
public sealed partial class JsonFileStateStore : IStateStore
{
    private readonly IFileSystemService _fs;
    private readonly string _baseVirtualPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = SerializationDefaults.JsonMaxDepth
    };

    private const int MaxSessionIdLength = SecurityDefaults.MaxSessionIdLength;

    [GeneratedRegex(@"^[a-zA-Z0-9_\-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 5000)]
    private static partial Regex SessionIdPattern();

    /// <summary>
    /// Initializes a new instance of <see cref="JsonFileStateStore"/> using the VFS.
    /// </summary>
    /// <param name="fs">Virtual file system service.</param>
    /// <param name="baseVirtualPath">Virtual base path (e.g. "/state") where session files are stored.</param>
    public JsonFileStateStore(IFileSystemService fs, string baseVirtualPath)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseVirtualPath);
        _fs = fs;
        _baseVirtualPath = baseVirtualPath.TrimEnd('/');
    }

    /// <inheritdoc />
    public Task SaveAsync(SessionState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateSessionId(state.SessionId);
        return SaveCoreAsync();

        async Task SaveCoreAsync()
        {
            var filePath = GetFilePath(state.SessionId);
            var json = JsonSerializer.Serialize(state, JsonOptions);
            await _fs.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<SessionState?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        ValidateSessionId(sessionId);
        var filePath = GetFilePath(sessionId);
        var json = await _fs.TryReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        if (json is null) return null;
        return JsonSerializer.Deserialize<SessionState>(json, JsonOptions);
    }

    /// <inheritdoc />
    public async Task<SessionState?> GetLatestForCrewAsync(string crewId, CancellationToken ct = default)
    {
        var allSessions = await LoadAllSessionsAsync(ct).ConfigureAwait(false);
        return allSessions
            .Where(s => s.CrewId == crewId)
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct = default)
    {
        var allSessions = await LoadAllSessionsAsync(ct).ConfigureAwait(false);
        return allSessions
            .Select(s => new SessionSummary
            {
                SessionId = s.SessionId,
                CrewId = s.CrewId,
                Phase = s.Phase,
                CompletedTaskCount = s.TaskCheckpoints.Count(t => t.Value.Status == CheckpointStatus.Completed),
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        ValidateSessionId(sessionId);
        var filePath = GetFilePath(sessionId);
        await _fs.DeleteAsync(filePath, recursive: false, ct).ConfigureAwait(false);
        // Also remove any versioned session directory
        var sessionDir = GetSessionVersionDir(sessionId);
        await _fs.DeleteAsync(sessionDir, recursive: true, ct).ConfigureAwait(false);
    }

    // ── Time-travel methods ──

    /// <inheritdoc />
    public Task<VersionedState> SaveVersionedAsync(SessionState state, string? stepId = null, string? label = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateSessionId(state.SessionId);
        return SaveVersionedCoreAsync();

        async Task<VersionedState> SaveVersionedCoreAsync()
        {
            var sessionDir = GetSessionVersionDir(state.SessionId);
            await _fs.CreateDirectoryAsync(sessionDir, ct).ConfigureAwait(false);

            var manifest = await LoadManifestAsync(state.SessionId, ct).ConfigureAwait(false);
            var nextVersion = manifest.NextVersion;
            var versionId = Guid.NewGuid().ToString();
            var createdAt = DateTime.UtcNow;

            var versioned = new VersionedState
            {
                VersionId = versionId,
                SessionId = state.SessionId,
                Version = nextVersion,
                State = state,
                StepId = stepId,
                Label = label,
                CreatedAt = createdAt
            };

            var versionFile = GetVersionFilePath(state.SessionId, nextVersion);
            var json = JsonSerializer.Serialize(versioned, JsonOptions);
            await _fs.WriteAllTextAsync(versionFile, json, ct).ConfigureAwait(false);

            manifest.NextVersion = nextVersion + 1;
            manifest.Entries.Add(new ManifestEntry
            {
                VersionId = versionId,
                Version = nextVersion,
                StepId = stepId,
                Label = label,
                CreatedAt = createdAt
            });
            await SaveManifestAsync(state.SessionId, manifest, ct).ConfigureAwait(false);

            await SaveAsync(state, ct).ConfigureAwait(false);

            return versioned;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VersionSummary>> GetHistoryAsync(string sessionId, int? limit = null, CancellationToken ct = default)
    {
        ValidateSessionId(sessionId);
        var manifest = await LoadManifestAsync(sessionId, ct).ConfigureAwait(false);
        if (manifest.Entries.Count == 0)
            return Array.Empty<VersionSummary>();

        var entries = manifest.Entries.OrderByDescending(e => e.Version).ToList();
        if (limit.HasValue)
            entries = entries.Take(limit.Value).ToList();

        var summaries = new List<VersionSummary>();
        foreach (var entry in entries)
        {
            var versioned = await LoadVersionFileAsync(sessionId, entry.Version, ct).ConfigureAwait(false);
            if (versioned != null)
            {
                summaries.Add(new VersionSummary
                {
                    VersionId = versioned.VersionId,
                    SessionId = versioned.SessionId,
                    Version = versioned.Version,
                    Phase = versioned.State.Phase,
                    CompletedTaskCount = versioned.State.TaskCheckpoints.Count(t => t.Value.Status == CheckpointStatus.Completed),
                    StepId = versioned.StepId,
                    Label = versioned.Label,
                    CreatedAt = versioned.CreatedAt
                });
            }
        }

        return summaries;
    }

    /// <inheritdoc />
    public async Task<VersionedState?> GetVersionAsync(string sessionId, string versionId, CancellationToken ct = default)
    {
        ValidateSessionId(sessionId);
        var manifest = await LoadManifestAsync(sessionId, ct).ConfigureAwait(false);
        var entry = manifest.Entries.FirstOrDefault(e => e.VersionId == versionId);
        if (entry == null)
            return null;

        return await LoadVersionFileAsync(sessionId, entry.Version, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<VersionedState?> GetAtAsync(string sessionId, DateTime timestamp, CancellationToken ct = default)
    {
        ValidateSessionId(sessionId);
        var manifest = await LoadManifestAsync(sessionId, ct).ConfigureAwait(false);
        var entry = manifest.Entries
            .Where(e => e.CreatedAt <= timestamp)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefault();

        if (entry == null)
            return null;

        return await LoadVersionFileAsync(sessionId, entry.Version, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<VersionedState?> GetByStepAsync(string sessionId, string stepId, CancellationToken ct = default)
    {
        ValidateSessionId(sessionId);
        var manifest = await LoadManifestAsync(sessionId, ct).ConfigureAwait(false);
        var entry = manifest.Entries.FirstOrDefault(e => e.StepId == stepId);
        if (entry == null)
            return null;

        return await LoadVersionFileAsync(sessionId, entry.Version, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<VersionedState> ForkAsync(string sourceSessionId, string sourceVersionId, string? label = null, CancellationToken ct = default)
    {
        ValidateSessionId(sourceSessionId);
        var sourceVersion = await GetVersionAsync(sourceSessionId, sourceVersionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Version '{sourceVersionId}' not found in session '{sourceSessionId}'");

        var newSessionId = Guid.NewGuid().ToString();
        var newVersionId = Guid.NewGuid().ToString();
        var createdAt = DateTime.UtcNow;
        var finalLabel = label ?? $"Fork from {sourceSessionId}@v{sourceVersion.Version}";

        var forkedState = sourceVersion.State with
        {
            SessionId = newSessionId,
            UpdatedAt = createdAt
        };

        var forkedVersion = new VersionedState
        {
            VersionId = newVersionId,
            SessionId = newSessionId,
            Version = 1,
            State = forkedState,
            Label = finalLabel,
            ForkedFromSession = sourceSessionId,
            ForkedFromVersion = sourceVersionId,
            CreatedAt = createdAt
        };

        var sessionDir = GetSessionVersionDir(newSessionId);
        await _fs.CreateDirectoryAsync(sessionDir, ct).ConfigureAwait(false);

        var versionFile = GetVersionFilePath(newSessionId, 1);
        var json = JsonSerializer.Serialize(forkedVersion, JsonOptions);
        await _fs.WriteAllTextAsync(versionFile, json, ct).ConfigureAwait(false);

        var manifest = new VersionManifest
        {
            NextVersion = 2,
            Entries =
            [
                new()
                {
                    VersionId = newVersionId,
                    Version = 1,
                    Label = finalLabel,
                    CreatedAt = createdAt
                }
            ]
        };
        await SaveManifestAsync(newSessionId, manifest, ct).ConfigureAwait(false);

        await SaveAsync(forkedState, ct).ConfigureAwait(false);

        return forkedVersion;
    }

    /// <inheritdoc />
    public async Task<StateDiff> DiffAsync(string sessionId, string fromVersionId, string toVersionId, CancellationToken ct = default)
    {
        ValidateSessionId(sessionId);
        var fromVersion = await GetVersionAsync(sessionId, fromVersionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Version '{fromVersionId}' not found");
        var toVersion = await GetVersionAsync(sessionId, toVersionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Version '{toVersionId}' not found");

        return ComputeDiff(fromVersion, toVersion);
    }

    // ── Private helpers ──

    private static void ValidateSessionId(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));

        if (sessionId.Length > MaxSessionIdLength)
            throw new ArgumentException(
                $"Session ID exceeds the maximum allowed length of {MaxSessionIdLength} characters.",
                nameof(sessionId));

        if (!SessionIdPattern().IsMatch(sessionId))
            throw new ArgumentException(
                "Session ID contains invalid characters. Only alphanumeric characters, hyphens, and underscores are allowed.",
                nameof(sessionId));
    }

    private string GetFilePath(string sessionId) => $"{_baseVirtualPath}/{sessionId}.json";

    private string GetSessionVersionDir(string sessionId) => $"{_baseVirtualPath}/{sessionId}";

    private string GetVersionFilePath(string sessionId, int version) =>
        $"{GetSessionVersionDir(sessionId)}/v{version:D3}.json";

    private string GetManifestFilePath(string sessionId) =>
        $"{GetSessionVersionDir(sessionId)}/manifest.json";

    private async Task<VersionManifest> LoadManifestAsync(string sessionId, CancellationToken ct)
    {
        var manifestPath = GetManifestFilePath(sessionId);
        var json = await _fs.TryReadAllTextAsync(manifestPath, ct).ConfigureAwait(false);
        if (json is null) return new VersionManifest();

        try
        {
            return JsonSerializer.Deserialize<VersionManifest>(json, JsonOptions) ?? new VersionManifest();
        }
        catch (JsonException)
        {
            return new VersionManifest();
        }
    }

    private async Task SaveManifestAsync(string sessionId, VersionManifest manifest, CancellationToken ct)
    {
        var manifestPath = GetManifestFilePath(sessionId);
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        await _fs.WriteAllTextAsync(manifestPath, json, ct).ConfigureAwait(false);
    }

    private async Task<VersionedState?> LoadVersionFileAsync(string sessionId, int version, CancellationToken ct)
    {
        var filePath = GetVersionFilePath(sessionId, version);
        var json = await _fs.TryReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        if (json is null) return null;

        try
        {
            return JsonSerializer.Deserialize<VersionedState>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<List<SessionState>> LoadAllSessionsAsync(CancellationToken ct)
    {
        if (!await _fs.ExistsAsync(_baseVirtualPath, ct).ConfigureAwait(false))
            return [];

        var sessions = new List<SessionState>();
        var opts = new VirtualEnumerationOptions(Recursive: false, SearchPattern: "*.json");
        await foreach (var entry in _fs.EnumerateFilesAsync(_baseVirtualPath, opts, ct).ConfigureAwait(false))
        {
            var session = await TryLoadSessionEntryAsync(entry, ct).ConfigureAwait(false);
            if (session != null)
                sessions.Add(session);
        }

        return sessions;
    }

    private async Task<SessionState?> TryLoadSessionEntryAsync(VirtualFileEntry entry, CancellationToken ct)
    {
        if (entry.Kind != VirtualEntryKind.File) return null;

        var sessionId = ExtractSessionId(entry.VirtualPath);
        if (!IsValidSessionId(sessionId)) return null;

        var json = await _fs.TryReadAllTextAsync(entry.VirtualPath, ct).ConfigureAwait(false);
        if (json is null) return null;

        return TryDeserializeSession(json);
    }

    private static string ExtractSessionId(string virtualPath)
    {
        var fileName = virtualPath.Contains('/', StringComparison.Ordinal)
            ? virtualPath[(virtualPath.LastIndexOf('/') + 1)..]
            : virtualPath;
        var dotIndex = fileName.LastIndexOf('.');
        return dotIndex >= 0 ? fileName[..dotIndex] : fileName;
    }

    private static bool IsValidSessionId(string sessionId) =>
        SessionIdPattern().IsMatch(sessionId) && sessionId.Length <= MaxSessionIdLength;

    private static SessionState? TryDeserializeSession(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SessionState>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // Skip malformed files
            return null;
        }
    }

    private static StateDiff ComputeDiff(VersionedState from, VersionedState to)
    {
        var fromTasks = from.State.TaskCheckpoints;
        var toTasks = to.State.TaskCheckpoints;

        var allTaskIds = fromTasks.Keys.Union(toTasks.Keys).ToList();
        var addedIds = toTasks.Keys.Except(fromTasks.Keys).ToList();
        var removedIds = fromTasks.Keys.Except(toTasks.Keys).ToList();

        var taskDiffs = new List<TaskDiff>();
        foreach (var taskId in allTaskIds)
        {
            var hasFrom = fromTasks.TryGetValue(taskId, out var fromTask);
            var hasTo = toTasks.TryGetValue(taskId, out var toTask);

            if (hasFrom && hasTo
                && (fromTask!.Status != toTask!.Status || fromTask.Output != toTask.Output))
            {
                taskDiffs.Add(new TaskDiff
                {
                    TaskId = taskId,
                    FromStatus = fromTask.Status,
                    ToStatus = toTask.Status,
                    OutputChanged = fromTask.Output != toTask.Output
                });
            }
        }

        return new StateDiff
        {
            FromVersionId = from.VersionId,
            ToVersionId = to.VersionId,
            PhaseChanged = from.State.Phase != to.State.Phase,
            FromPhase = from.State.Phase,
            ToPhase = to.State.Phase,
            TaskDiffs = taskDiffs,
            AddedTaskIds = addedIds,
            RemovedTaskIds = removedIds
        };
    }

    private sealed class VersionManifest
    {
        public int NextVersion { get; set; } = 1;
        public List<ManifestEntry> Entries { get; set; } = [];
    }

    private sealed class ManifestEntry
    {
        public string VersionId { get; set; } = "";
        public int Version { get; set; }
        public string? StepId { get; set; }
        public string? Label { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
