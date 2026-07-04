using Orkeon.Application.Interfaces.Knowledge;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Infrastructure.Knowledge.Loaders;

/// <summary>
/// Loads plain text and markdown files via the virtual file system.
/// </summary>
public class TextFileLoader : IDocumentLoader
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".text", ".log"
    };

    private readonly IFileSystemService _fs;

    /// <summary>
    /// Initializes a new instance of <see cref="TextFileLoader"/>.
    /// </summary>
    /// <param name="fs">Virtual file system service used to read files.</param>
    public TextFileLoader(IFileSystemService fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        _fs = fs;
    }

    /// <inheritdoc />
    public string SupportedType => "text";

    /// <inheritdoc />
    public async Task<LoadedDocument> LoadAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var entry = await _fs.TryGetEntryAsync(source, cancellationToken).ConfigureAwait(false);
        if (entry is null || entry.Kind != VirtualEntryKind.File)
            throw new FileNotFoundException($"File not found: {source}", source);

        var content = await _fs.TryReadAllTextAsync(source, cancellationToken).ConfigureAwait(false)
            ?? string.Empty;

        var fileName = source.TrimEnd('/').Split('/').LastOrDefault() ?? source;
#pragma warning disable CA1308 // lowercase extension is the stored metadata/wire form, not a comparison normalization
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
#pragma warning restore CA1308

        var metadata = new Dictionary<string, object>
        {
            ["file_name"] = fileName,
            ["file_path"] = source,
            ["file_size"] = entry.SizeBytes,
            ["extension"] = extension,
            ["last_modified"] = entry.LastModified.UtcDateTime
        };

        return new LoadedDocument(
            Content: content,
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
            return SupportedExtensions.Contains(extension);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
