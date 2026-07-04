using Orkeon.Application.Interfaces.Knowledge;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Infrastructure.Knowledge.Loaders;

/// <summary>
/// Loads CSV files and converts them to a text representation via the virtual file system.
/// </summary>
public class CsvDocumentLoader : IDocumentLoader
{
    private readonly IFileSystemService _fs;

    /// <summary>
    /// Initializes a new instance of <see cref="CsvDocumentLoader"/>.
    /// </summary>
    /// <param name="fs">Virtual file system service used to read files.</param>
    public CsvDocumentLoader(IFileSystemService fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        _fs = fs;
    }

    /// <inheritdoc />
    public string SupportedType => "csv";

    /// <inheritdoc />
    public async Task<LoadedDocument> LoadAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var entry = await _fs.TryGetEntryAsync(source, cancellationToken).ConfigureAwait(false);
        if (entry is null || entry.Kind != VirtualEntryKind.File)
            throw new FileNotFoundException($"File not found: {source}", source);

        var rawText = await _fs.TryReadAllTextAsync(source, cancellationToken).ConfigureAwait(false)
            ?? string.Empty;

        var fileName = source.TrimEnd('/').Split('/').LastOrDefault() ?? source;
#pragma warning disable CA1308 // lowercase is the normalized extension stored in document metadata (wire/storage form), not a comparison normalization
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
#pragma warning restore CA1308

        var lines = rawText.Split('\n', StringSplitOptions.None)
            .Select(l => l.TrimEnd('\r'))
            .ToArray();

        if (lines.Length == 0 || (lines.Length == 1 && string.IsNullOrWhiteSpace(lines[0])))
        {
            return new LoadedDocument(
                Content: string.Empty,
                SourceId: source,
                SourceType: SupportedType,
                Metadata: BuildMetadata(fileName, source, extension, entry.SizeBytes, entry.LastModified, 0, 0));
        }

        // Parse header
        var headers = ParseCsvLine(lines[0]);
        var textBuilder = new System.Text.StringBuilder();
        var rowCount = 0;

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            var values = ParseCsvLine(lines[i]);
            var rowParts = new List<string>();

            for (int j = 0; j < headers.Length && j < values.Length; j++)
            {
                rowParts.Add($"{headers[j]}: {values[j]}");
            }

            textBuilder.AppendLine(string.Join(", ", rowParts));
            rowCount++;
        }

        var metadata = BuildMetadata(fileName, source, extension, entry.SizeBytes, entry.LastModified, rowCount, headers.Length);

        return new LoadedDocument(
            Content: textBuilder.ToString().TrimEnd(),
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
            return string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static Dictionary<string, object> BuildMetadata(
        string fileName, string filePath, string extension,
        long sizeBytes, DateTimeOffset lastModified, int rowCount, int columnCount)
    {
        return new Dictionary<string, object>
        {
            ["file_name"] = fileName,
            ["file_path"] = filePath,
            ["file_size"] = sizeBytes,
            ["extension"] = extension,
            ["last_modified"] = lastModified.UtcDateTime,
            ["row_count"] = rowCount,
            ["column_count"] = columnCount
        };
    }

    /// <summary>
    /// Simple CSV line parser that handles quoted fields.
    /// </summary>
    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (inQuotes)
            {
                i = ProcessQuotedChar(c, line, i, current, ref inQuotes);
            }
            else
            {
                ProcessUnquotedChar(c, current, fields, ref inQuotes);
            }
        }

        fields.Add(current.ToString().Trim());
        return fields.ToArray();
    }

    private static int ProcessQuotedChar(char c, string line, int i, System.Text.StringBuilder current, ref bool inQuotes)
    {
        if (c == '"')
        {
            if (i + 1 < line.Length && line[i + 1] == '"')
            {
                current.Append('"');
                return i + 1; // skip escaped quote
            }
            inQuotes = false;
        }
        else
        {
            current.Append(c);
        }
        return i;
    }

    private static void ProcessUnquotedChar(char c, System.Text.StringBuilder current, List<string> fields, ref bool inQuotes)
    {
        if (c == '"')
        {
            inQuotes = true;
        }
        else if (c == ',')
        {
            fields.Add(current.ToString().Trim());
            current.Clear();
        }
        else
        {
            current.Append(c);
        }
    }
}
