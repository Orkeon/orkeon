using System.Globalization;
using System.Text;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Models;
using UglyToad.PdfPig;

namespace Orkeon.Rag.Loaders;

/// <summary>
/// Loads PDF files and extracts their text content using PdfPig via the virtual
/// file system. Port of <c>Orkeon.Infrastructure.Knowledge.Loaders.PdfDocumentLoader</c>
/// onto the <c>Orkeon.Rag.Abstractions</c> contract (RAG-02/C3).
/// </summary>
public sealed class PdfDocumentLoader : FileDocumentLoaderBase
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf"
    };

    /// <summary>Initializes a new instance of <see cref="PdfDocumentLoader"/>.</summary>
    /// <param name="fileSystem">Virtual file system service used to read files.</param>
    public PdfDocumentLoader(IFileSystemService fileSystem)
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
        var bytes = await FileSystem.TryReadAllBytesAsync(source.Location, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"File not found: {source.Location}", source.Location);

        using var document = PdfDocument.Open(bytes);

        var textBuilder = new StringBuilder();
        foreach (var pageText in document.GetPages().Select(page => page.Text))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(pageText))
            {
                if (textBuilder.Length > 0)
                    textBuilder.AppendLine();

                textBuilder.Append(pageText);
            }
        }

        var metadata = BuildFileMetadata(source, entry);
        metadata["page_count"] = document.NumberOfPages.ToString(CultureInfo.InvariantCulture);
        metadata["title"] = document.Information.Title ?? string.Empty;
        metadata["author"] = document.Information.Author ?? string.Empty;
        metadata["creator"] = document.Information.Creator ?? string.Empty;

        var sourceId = ResolveSourceId(source);
        var pdfTitle = document.Information.Title;
        return new RagDocument
        {
            Id = sourceId,
            SourceId = sourceId,
            Content = textBuilder.ToString(),
            Title = string.IsNullOrWhiteSpace(pdfTitle) ? GetFileName(source.Location) : pdfTitle,
            Location = source.Location,
            Metadata = metadata.ToImmutable(),
        };
    }
}
