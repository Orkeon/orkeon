using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Orkeon.Application.Interfaces.Knowledge;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Infrastructure.Knowledge.Loaders;

/// <summary>
/// Loads HTML files and extracts clean text content via the virtual file system.
/// </summary>
public partial class HtmlDocumentLoader : IDocumentLoader
{
    private readonly IFileSystemService _fs;

    /// <summary>
    /// Initializes a new instance of <see cref="HtmlDocumentLoader"/>.
    /// </summary>
    /// <param name="fs">Virtual file system service used to read files.</param>
    public HtmlDocumentLoader(IFileSystemService fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        _fs = fs;
    }

    /// <inheritdoc />
    public string SupportedType => "html";

    /// <inheritdoc />
    public async Task<LoadedDocument> LoadAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var entry = await _fs.TryGetEntryAsync(source, cancellationToken).ConfigureAwait(false);
        if (entry is null || entry.Kind != VirtualEntryKind.File)
            throw new FileNotFoundException($"File not found: {source}", source);

        var htmlContent = await _fs.TryReadAllTextAsync(source, cancellationToken).ConfigureAwait(false)
            ?? string.Empty;

        var fileName = source.TrimEnd('/').Split('/').LastOrDefault() ?? source;
#pragma warning disable CA1308 // lowercase is the required stored metadata form for the file extension, not a comparison normalization
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
#pragma warning restore CA1308

        var text = ExtractText(htmlContent);
        var title = ExtractTitle(htmlContent);

        var metadata = new Dictionary<string, object>
        {
            ["file_name"] = fileName,
            ["file_path"] = source,
            ["file_size"] = entry.SizeBytes,
            ["extension"] = extension,
            ["last_modified"] = entry.LastModified.UtcDateTime
        };

        if (!string.IsNullOrEmpty(title))
            metadata["title"] = title;

        return new LoadedDocument(
            Content: text,
            SourceId: source,
            SourceType: SupportedType,
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
            return string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".htm", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Extracts clean text from HTML by removing scripts, styles, and tags.
    /// </summary>
    internal static string ExtractText(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Remove script and style nodes
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

        // Decode HTML entities
        text = System.Net.WebUtility.HtmlDecode(text);

        // Normalize whitespace: collapse multiple whitespace chars into single spaces
        text = WhitespaceRegex().Replace(text, " ").Trim();

        return text;
    }

    /// <summary>
    /// Extracts the title from HTML content.
    /// </summary>
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
