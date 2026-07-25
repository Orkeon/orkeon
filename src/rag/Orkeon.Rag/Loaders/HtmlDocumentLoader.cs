using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Loaders;

/// <summary>
/// Loads HTML files and extracts clean text content via the virtual file system.
/// Port of legacy Infrastructure <c>HtmlDocumentLoader</c> onto
/// the <c>Orkeon.Rag.Abstractions</c> contract (RAG-02/C3).
/// </summary>
public sealed partial class HtmlDocumentLoader : FileDocumentLoaderBase
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm"
    };

    /// <summary>Initializes a new instance of <see cref="HtmlDocumentLoader"/>.</summary>
    /// <param name="fileSystem">Virtual file system service used to read files.</param>
    public HtmlDocumentLoader(IFileSystemService fileSystem)
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
        var htmlContent = await FileSystem.TryReadAllTextAsync(source.Location, cancellationToken).ConfigureAwait(false)
            ?? string.Empty;

        var text = ExtractText(htmlContent);
        var title = ExtractTitle(htmlContent);

        var metadata = BuildFileMetadata(source, entry);
        if (!string.IsNullOrEmpty(title))
            metadata["title"] = title;

        var sourceId = ResolveSourceId(source);
        return new RagDocument
        {
            Id = sourceId,
            SourceId = sourceId,
            Content = text,
            Title = string.IsNullOrEmpty(title) ? GetFileName(source.Location) : title,
            Location = source.Location,
            Metadata = metadata.ToImmutable(),
        };
    }

    /// <summary>Extracts clean text from HTML by removing scripts, styles, and tags.</summary>
    internal static string ExtractText(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var nodesToRemove = doc.DocumentNode
            .SelectNodes("//script|//style|//comment()")
            ?.ToList();

        if (nodesToRemove != null)
        {
            foreach (var node in nodesToRemove)
            {
                node.Remove();
            }
        }

        var text = doc.DocumentNode.InnerText;
        text = System.Net.WebUtility.HtmlDecode(text);
        text = WhitespaceRegex().Replace(text, " ").Trim();

        return text;
    }

    /// <summary>Extracts the title from HTML content.</summary>
    internal static string ExtractTitle(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        if (titleNode != null)
            return System.Net.WebUtility.HtmlDecode(titleNode.InnerText.Trim());

        return string.Empty;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
