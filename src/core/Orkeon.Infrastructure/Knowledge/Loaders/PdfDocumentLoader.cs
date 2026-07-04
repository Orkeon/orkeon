using System.Text;
using Orkeon.Application.Interfaces.Knowledge;
using Orkeon.Domain.FileSystem;
using UglyToad.PdfPig;

namespace Orkeon.Infrastructure.Knowledge.Loaders;

/// <summary>
/// Loads PDF files and extracts their text content using PdfPig via the virtual file system.
/// </summary>
public class PdfDocumentLoader : IDocumentLoader
{
    private readonly IFileSystemService _fs;

    /// <summary>
    /// Initializes a new instance of <see cref="PdfDocumentLoader"/>.
    /// </summary>
    /// <param name="fs">Virtual file system service used to read files.</param>
    public PdfDocumentLoader(IFileSystemService fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        _fs = fs;
    }

    /// <inheritdoc />
    public string SupportedType => "pdf";

    /// <inheritdoc />
    public async Task<LoadedDocument> LoadAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var entry = await _fs.TryGetEntryAsync(source, cancellationToken).ConfigureAwait(false);
        if (entry is null || entry.Kind != VirtualEntryKind.File)
            throw new FileNotFoundException($"File not found: {source}", source);

        var bytes = await _fs.TryReadAllBytesAsync(source, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
            throw new FileNotFoundException($"File not found: {source}", source);

        var fileName = source.TrimEnd('/').Split('/').LastOrDefault() ?? source;
#pragma warning disable CA1308 // lowercase extension is stored as metadata (wire/storage form), not a comparison normalization
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
#pragma warning restore CA1308

        using var document = PdfDocument.Open(bytes);

        var textBuilder = new StringBuilder();

        var pageTexts = document.GetPages().Select(page => page.Text);
        foreach (var pageText in pageTexts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(pageText))
            {
                if (textBuilder.Length > 0)
                    textBuilder.AppendLine();

                textBuilder.Append(pageText);
            }
        }

        var metadata = new Dictionary<string, object>
        {
            ["file_name"] = fileName,
            ["file_path"] = source,
            ["file_size"] = entry.SizeBytes,
            ["extension"] = extension,
            ["last_modified"] = entry.LastModified.UtcDateTime,
            ["pageCount"] = document.NumberOfPages,
            ["title"] = document.Information.Title ?? string.Empty,
            ["author"] = document.Information.Author ?? string.Empty,
            ["creator"] = document.Information.Creator ?? string.Empty
        };

        return new LoadedDocument(
            Content: textBuilder.ToString(),
            SourceId: source,
            SourceType: "pdf",
            Metadata: metadata);
    }

    /// <inheritdoc />
    public bool CanLoad(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        try
        {
            var extension = Path.GetExtension(source);
            return string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
