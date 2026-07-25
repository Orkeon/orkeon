using System.Globalization;
using System.Text;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Loaders;

/// <summary>
/// Loads CSV files and converts them to a <c>header: value</c> text representation
/// via the virtual file system. Port of
/// <c>Orkeon.Infrastructure.Knowledge.Loaders.CsvDocumentLoader</c> onto the
/// <c>Orkeon.Rag.Abstractions</c> contract (RAG-02/C3).
/// </summary>
public sealed class CsvDocumentLoader : FileDocumentLoaderBase
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csv"
    };

    /// <summary>Initializes a new instance of <see cref="CsvDocumentLoader"/>.</summary>
    /// <param name="fileSystem">Virtual file system service used to read files.</param>
    public CsvDocumentLoader(IFileSystemService fileSystem)
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
        var rawText = await FileSystem.TryReadAllTextAsync(source.Location, cancellationToken).ConfigureAwait(false)
            ?? string.Empty;

        var lines = rawText.Split('\n', StringSplitOptions.None)
            .Select(l => l.TrimEnd('\r'))
            .ToArray();

        var metadata = BuildFileMetadata(source, entry);
        var sourceId = ResolveSourceId(source);

        if (lines.Length == 0 || (lines.Length == 1 && string.IsNullOrWhiteSpace(lines[0])))
        {
            metadata["row_count"] = "0";
            metadata["column_count"] = "0";
            return new RagDocument
            {
                Id = sourceId,
                SourceId = sourceId,
                Content = string.Empty,
                Title = GetFileName(source.Location),
                Location = source.Location,
                Metadata = metadata.ToImmutable(),
            };
        }

        var headers = ParseCsvLine(lines[0]);
        var textBuilder = new StringBuilder();
        var rowCount = 0;

        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            var values = ParseCsvLine(lines[i]);
            var rowParts = new List<string>();

            for (var j = 0; j < headers.Length && j < values.Length; j++)
            {
                rowParts.Add($"{headers[j]}: {values[j]}");
            }

            textBuilder.AppendLine(string.Join(", ", rowParts));
            rowCount++;
        }

        metadata["row_count"] = rowCount.ToString(CultureInfo.InvariantCulture);
        metadata["column_count"] = headers.Length.ToString(CultureInfo.InvariantCulture);

        return new RagDocument
        {
            Id = sourceId,
            SourceId = sourceId,
            Content = textBuilder.ToString().TrimEnd(),
            Title = GetFileName(source.Location),
            Location = source.Location,
            Metadata = metadata.ToImmutable(),
        };
    }

    /// <summary>Simple CSV line parser that handles quoted fields.</summary>
    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

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

    private static int ProcessQuotedChar(char c, string line, int i, StringBuilder current, ref bool inQuotes)
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

    private static void ProcessUnquotedChar(char c, StringBuilder current, List<string> fields, ref bool inQuotes)
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
