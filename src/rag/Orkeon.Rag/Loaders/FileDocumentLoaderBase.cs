using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Loaders;

/// <summary>
/// Base class for file-backed <see cref="IDocumentLoader"/>s: resolves the
/// <see cref="SourceDescriptor"/> against the virtual file system, validates the
/// entry, and yields a single <see cref="RagDocument"/> built by the derived
/// loader. All I/O goes through <see cref="IFileSystemService"/> (VFS rule).
/// </summary>
public abstract class FileDocumentLoaderBase : IDocumentLoader
{
    /// <summary><see cref="SourceDescriptor.Kind"/> hint accepted by file loaders.</summary>
    public const string FileKind = "file";

    /// <summary>Initializes the loader with the virtual file system service.</summary>
    /// <param name="fileSystem">Virtual file system service used to read files.</param>
    protected FileDocumentLoaderBase(IFileSystemService fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        FileSystem = fileSystem;
    }

    /// <summary>Virtual file system service used for all reads.</summary>
    protected IFileSystemService FileSystem { get; }

    /// <summary>File extensions (with leading dot) this loader accepts.</summary>
    protected abstract IReadOnlySet<string> SupportedExtensions { get; }

    /// <inheritdoc />
    public bool CanLoad(SourceDescriptor source)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.Location))
            return false;

        // Honor the loader hint: file loaders only claim "file"-kind (or unhinted) sources.
        if (source.Kind is not null
            && !string.Equals(source.Kind, FileKind, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var extension = Path.GetExtension(source.Location);
            return SupportedExtensions.Contains(extension);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RagDocument> LoadAsync(
        SourceDescriptor source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Location);

        var entry = await FileSystem.TryGetEntryAsync(source.Location, cancellationToken).ConfigureAwait(false);
        if (entry is null || entry.Kind != VirtualEntryKind.File)
            throw new FileNotFoundException($"File not found: {source.Location}", source.Location);

        yield return await LoadDocumentAsync(source, entry, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds the single <see cref="RagDocument"/> for a validated file entry.</summary>
    /// <param name="source">The source descriptor being loaded.</param>
    /// <param name="entry">The resolved virtual file entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected abstract Task<RagDocument> LoadDocumentAsync(
        SourceDescriptor source,
        VirtualFileEntry entry,
        CancellationToken cancellationToken);

    /// <summary>Stable identity of the source: <see cref="SourceDescriptor.SourceId"/> or the location.</summary>
    private protected static string ResolveSourceId(SourceDescriptor source) =>
        string.IsNullOrWhiteSpace(source.SourceId) ? source.Location : source.SourceId;

    /// <summary>File name segment of a virtual path.</summary>
    private protected static string GetFileName(string virtualPath) =>
        virtualPath.TrimEnd('/').Split('/').LastOrDefault() ?? virtualPath;

    /// <summary>
    /// Builds the common provenance metadata of a file-backed document
    /// (<c>file_name</c>, <c>file_path</c>, <c>file_size</c>, <c>extension</c>,
    /// <c>last_modified</c> as an ISO 8601 round-trip UTC timestamp).
    /// </summary>
    private protected static ImmutableDictionary<string, string>.Builder BuildFileMetadata(
        SourceDescriptor source,
        VirtualFileEntry entry)
    {
        var fileName = GetFileName(source.Location);
#pragma warning disable CA1308 // lowercase extension is the stored metadata/wire form, not a comparison normalization
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
#pragma warning restore CA1308

        var builder = ImmutableDictionary.CreateBuilder<string, string>();
        builder["file_name"] = fileName;
        builder["file_path"] = source.Location;
        builder["file_size"] = entry.SizeBytes.ToString(CultureInfo.InvariantCulture);
        builder["extension"] = extension;
        builder["last_modified"] = entry.LastModified.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        return builder;
    }
}
