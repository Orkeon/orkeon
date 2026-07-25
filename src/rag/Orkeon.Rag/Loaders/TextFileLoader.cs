using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Loaders;

/// <summary>
/// Loads plain text and markdown files via the virtual file system.
/// Port of legacy Infrastructure <c>TextFileLoader</c> onto the
/// <c>Orkeon.Rag.Abstractions</c> contract (RAG-02/C3).
/// </summary>
public sealed class TextFileLoader : FileDocumentLoaderBase
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".text", ".log"
    };

    /// <summary>Initializes a new instance of <see cref="TextFileLoader"/>.</summary>
    /// <param name="fileSystem">Virtual file system service used to read files.</param>
    public TextFileLoader(IFileSystemService fileSystem)
        : base(fileSystem)
    {
    }

    /// <inheritdoc />
    protected override IReadOnlySet<string> SupportedExtensions => Extensions;

    /// <inheritdoc />
    protected override async Task<RagDocument> LoadDocumentAsync(
        SourceDescriptor source,
        VirtualFileEntry entry,
        CancellationToken cancellationToken)
    {
        var content = await FileSystem.TryReadAllTextAsync(source.Location, cancellationToken).ConfigureAwait(false)
            ?? string.Empty;

        var sourceId = ResolveSourceId(source);
        return new RagDocument
        {
            Id = sourceId,
            SourceId = sourceId,
            Content = content,
            Title = GetFileName(source.Location),
            Location = source.Location,
            Metadata = BuildFileMetadata(source, entry).ToImmutable(),
        };
    }
}
