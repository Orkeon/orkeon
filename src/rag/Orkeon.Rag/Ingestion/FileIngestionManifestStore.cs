using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Pipeline;

namespace Orkeon.Rag.Ingestion;

/// <summary>
/// <see cref="IIngestionManifestStore"/> writing one JSON file per collection at
/// <c>{RagIngestionOptions.ManifestDirectory}/{collection}.json</c> through the
/// VFS (<see cref="IFileSystemService"/>, virtual paths — same discipline as the
/// RaggableTree cache).
/// </summary>
/// <remarks>
/// <para>Collection names containing characters unsafe for a file name are
/// sanitized and suffixed with a short SHA-256 discriminator of the original
/// name, so distinct collections never share a manifest file.</para>
/// <para>Failure posture: a missing, unreadable, or corrupt manifest loads as
/// <c>null</c> (full ingestion, never a crash); a manifest directory outside any
/// mount degrades to a warning — ingestion still works, just never incrementally.
/// A well-formed manifest is required to carry its embedding profile; one without
/// it is treated as corrupt.</para>
/// </remarks>
public sealed partial class FileIngestionManifestStore : IIngestionManifestStore
{
    private const int NameHashLength = 8;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly IFileSystemService _fileSystem;
    private readonly string _manifestDirectory;
    private readonly ILogger<FileIngestionManifestStore> _logger;

    /// <summary>Initializes the store.</summary>
    /// <param name="fileSystem">VFS service used for all reads and writes.</param>
    /// <param name="options">Ingestion options carrying <see cref="RagIngestionOptions.ManifestDirectory"/>; <c>null</c> selects the defaults.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public FileIngestionManifestStore(
        IFileSystemService fileSystem,
        RagIngestionOptions? options = null,
        ILogger<FileIngestionManifestStore>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        _fileSystem = fileSystem;
        _manifestDirectory = (options ?? new RagIngestionOptions()).ManifestDirectory.TrimEnd('/');
        _logger = logger ?? NullLogger<FileIngestionManifestStore>.Instance;
    }

    /// <summary>Virtual path of the manifest file for <paramref name="collection"/>.</summary>
    public string GetManifestPath(string collection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        return $"{_manifestDirectory}/{SafeFileName(collection)}.json";
    }

    /// <inheritdoc />
    public async Task<IngestionManifest?> LoadAsync(
        string collection,
        CancellationToken cancellationToken = default)
    {
        var path = GetManifestPath(collection);

        string? json;
        try
        {
            json = await _fileSystem.TryReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (FileAccessDeniedException ex)
        {
            LogManifestUnreadable(path, ex.Message);
            return null;
        }
        catch (IOException ex)
        {
            LogManifestUnreadable(path, ex.Message);
            return null;
        }

        if (json is null)
            return null; // No manifest yet — first ingestion of the collection.

        IngestionManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<IngestionManifest>(json, s_jsonOptions);
        }
        catch (JsonException ex)
        {
            LogManifestCorrupt(path, ex.Message);
            return null;
        }

        // A manifest without its identity fields is unusable for incremental
        // decisions — treat it as corrupt (full ingestion), never crash.
        if (manifest is null
            || string.IsNullOrWhiteSpace(manifest.Collection)
            || manifest.Embedding is null
            || manifest.Sources is null)
        {
            LogManifestCorrupt(path, "missing collection, embedding profile, or sources.");
            return null;
        }

        return manifest;
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        IngestionManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var path = GetManifestPath(manifest.Collection);
        var json = JsonSerializer.Serialize(manifest, s_jsonOptions);

        try
        {
            await _fileSystem.CreateDirectoryAsync(_manifestDirectory, cancellationToken).ConfigureAwait(false);
            await _fileSystem.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        }
        catch (FileAccessDeniedException ex)
        {
            // Degraded mode, surfaced but non-fatal: the ingestion itself
            // succeeded — only the incremental state is lost, so the next run
            // will be a full one.
            LogManifestNotPersisted(path, ex.Message);
        }
    }

    private static string SafeFileName(string collection)
    {
        var sanitized = new StringBuilder(collection.Length);
        var changed = false;

        foreach (var c in collection)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
            {
                sanitized.Append(c);
            }
            else
            {
                sanitized.Append('_');
                changed = true;
            }
        }

        if (!changed)
            return sanitized.ToString();

        // Disambiguate sanitized names: 'a/b' and 'a_b' must not collide.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(collection));
        return $"{sanitized}-{Convert.ToHexStringLower(hash)[..NameHashLength]}";
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RAG ingestion manifest '{Path}' is unreadable ({Reason}) — falling back to full ingestion.")]
    private partial void LogManifestUnreadable(string path, string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RAG ingestion manifest '{Path}' is corrupt ({Reason}) — falling back to full ingestion.")]
    private partial void LogManifestCorrupt(string path, string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RAG ingestion manifest '{Path}' could not be persisted ({Reason}) — the next ingestion will not be incremental.")]
    private partial void LogManifestNotPersisted(string path, string reason);
}
