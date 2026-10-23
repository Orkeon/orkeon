using System.Text.Json;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Infrastructure.Session;

/// <summary>Persists the REPL session of one project through <see cref="IFileSystemService"/>.</summary>
public sealed class FileSessionBufferService : ISessionBufferService
{
    private readonly IFileSystemService _fileSystem;
    private readonly CliSessionOptions _options;
    private readonly InMemorySessionBufferService _inner;
    private string? _projectKey;

    /// <summary>Creates the service over a file system and the session options.</summary>
    public FileSessionBufferService(IFileSystemService fileSystem, CliSessionOptions options)
        : this(fileSystem, options, new InMemorySessionBufferService())
    {
    }

    internal FileSessionBufferService(IFileSystemService fileSystem, CliSessionOptions options, InMemorySessionBufferService inner)
    {
        _fileSystem = fileSystem;
        _options = options;
        _inner = inner;
    }

    /// <summary>Rehydrates the last session saved for <paramref name="projectKey"/>; empty when none or unreadable.</summary>
    public async Task LoadAsync(string projectKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectKey);
        _projectKey = projectKey;
        var json = await _fileSystem.TryReadAllTextAsync(FilePath(projectKey), cancellationToken).ConfigureAwait(false);
        if (json is null) return;

        PersistedSession? saved;
        try
        {
            saved = JsonSerializer.Deserialize<PersistedSession>(json);
        }
        catch (JsonException)
        {
            return;
        }

        if (saved?.SessionId is null || saved.Messages is null || saved.State is null) return;
        _inner.Restore(saved.SessionId, saved.Title, saved.Messages, saved.State);
    }

    /// <summary>Writes the buffer, state bag, id and title to the project's file.</summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (_projectKey is null) return;
        var snapshot = _inner.Snapshot();
        var json = JsonSerializer.Serialize(new PersistedSession
        {
            SessionId = snapshot.SessionId,
            Title = snapshot.Title,
            Messages = snapshot.Messages,
            State = snapshot.State,
        });
        await _fileSystem.WriteAllTextAsync(FilePath(_projectKey), json, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IReadOnlyList<SessionMessage> GetMessages() => _inner.GetMessages();
    /// <inheritdoc />
    public void ReplaceMessages(IReadOnlyList<SessionMessage> messages) => _inner.ReplaceMessages(messages);
    /// <inheritdoc />
    public bool AppendNote(string note) => _inner.AppendNote(note);
    /// <inheritdoc />
    public SessionMetadata GetMetadata() => _inner.GetMetadata();
    /// <inheritdoc />
    public bool SetTitle(string title) => _inner.SetTitle(title);
    /// <inheritdoc />
    public int Truncate(int retainCount) => _inner.Truncate(retainCount);
    /// <inheritdoc />
    public int Reset() => _inner.Reset();
    /// <inheritdoc />
    public int EstimateTokenCount() => _inner.EstimateTokenCount();
    /// <inheritdoc />
    public int MessageCount => _inner.MessageCount;
    /// <inheritdoc />
    public string? GetState(string key) => _inner.GetState(key);
    /// <inheritdoc />
    public void SetState(string key, string? value) => _inner.SetState(key, value);

    /// <summary><c>&lt;SessionsDirectory&gt;/&lt;project slug&gt;/last.json</c>.</summary>
    private string FilePath(string projectKey)
        => $"{_options.SessionsDirectory.TrimEnd('/')}/{ProjectKey.Slug(projectKey)}/last.json";

    private sealed record PersistedSession
    {
        public string? SessionId { get; init; }
        public string? Title { get; init; }
        public SessionMessage[]? Messages { get; init; }
        public Dictionary<string, string>? State { get; init; }
    }
}
