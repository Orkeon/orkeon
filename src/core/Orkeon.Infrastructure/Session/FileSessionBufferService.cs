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
        if (json is null || Parse(json) is not { Session: { } saved }) return;
        _ = _inner.Restore(saved.SessionId!, saved.Title, saved.Messages!, saved.State!);
    }

    /// <summary>
    /// The project's file the session is saved to - <c>&lt;SessionsDirectory&gt;/&lt;project slug&gt;/last.json</c>,
    /// the file <see cref="SaveAsync"/> writes - or null before <see cref="LoadAsync"/> named the project.
    /// </summary>
    public string? SessionFilePath => _projectKey is null ? null : FilePath(_projectKey);

    /// <summary>
    /// Swaps the conversation buffer, the state bag, the id and the title for those of a file this service saved,
    /// read at the virtual <paramref name="path"/>. The file is judged whole before anything changes: a refused file
    /// leaves the session as it was. The session keeps saving to <see cref="SessionFilePath"/>, so the loaded
    /// session overwrites that file at the next save: archive the current one with <see cref="SaveToAsync"/> first.
    /// A load while another command's turn runs is not supported: the last writer of the buffer wins.
    /// </summary>
    /// <returns>The number of messages loaded, counted under the same lock as the swap.</returns>
    /// <exception cref="FileNotFoundException">No file at <paramref name="path"/>.</exception>
    /// <exception cref="InvalidDataException">The file is not a session this service saved.</exception>
    /// <exception cref="FileAccessDeniedException">The path is outside any mount or lacks read rights.</exception>
    public async Task<int> LoadFromAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var json = await _fileSystem.TryReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"No session file at '{path}'; the session is unchanged.");
        var (saved, problem) = Parse(json);
        if (saved is null)
            throw new InvalidDataException($"'{path}' is not a session the REPL saved ({problem}); the session is unchanged.");
        return _inner.Restore(saved.SessionId!, saved.Title, saved.Messages!, saved.State!);
    }

    /// <summary>The session a file holds when it has the shape <see cref="SaveAsync"/> writes, else what is wrong with it.</summary>
    private static (PersistedSession? Session, string? Problem) Parse(string json)
    {
        PersistedSession? saved;
        try
        {
            saved = JsonSerializer.Deserialize<PersistedSession>(json);
        }
        catch (JsonException ex)
        {
            return (null, $"unreadable JSON: {ex.Message}");
        }

        if (saved is null) return (null, "empty document");
        if (string.IsNullOrWhiteSpace(saved.SessionId)) return (null, "no SessionId");
        if (saved.Messages is null) return (null, "no Messages");
        if (saved.State is null) return (null, "no State");
        if (saved.Messages.Any(m => m is null || m.Role is null || m.Content is null))
            return (null, "a message without Role or Content");
        return (saved, null);
    }

    /// <summary>Writes the buffer, state bag, id and title to the project's file.</summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (_projectKey is null) return;
        await _fileSystem.WriteAllTextAsync(FilePath(_projectKey), Serialize(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the buffer, state bag, id and title to the virtual <paramref name="path"/>, in the shape
    /// <see cref="SaveAsync"/> writes, so <see cref="LoadFromAsync"/> reads it back. The project's file is left as it
    /// is, and the session goes on saving there.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty or blank.</exception>
    /// <exception cref="FileAccessDeniedException">The path is outside any mount or lacks write rights.</exception>
    public async Task SaveToAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await _fileSystem.WriteAllTextAsync(path, Serialize(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The session as one JSON document of the shape <see cref="Parse"/> accepts.</summary>
    private string Serialize()
    {
        var snapshot = _inner.Snapshot();
        return JsonSerializer.Serialize(new PersistedSession
        {
            SessionId = snapshot.SessionId,
            Title = snapshot.Title,
            Messages = snapshot.Messages,
            State = snapshot.State,
        });
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
