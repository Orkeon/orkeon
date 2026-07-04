using System.Globalization;
using System.Text;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Data.Constants.Csv;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Data;

// ── Request / Response records ────────────────────────────────────────

/// <summary>
/// Request parameters for reading a CSV file.
/// </summary>
public record CsvReaderRequest
{
    /// <summary>Gets the file path of the CSV file to read.</summary>
    [FieldSchema(Description = "The file path of the CSV file to read", Example = "/data/sales_report.csv")]
    public string Path { get; init; } = "";

    /// <summary>Gets whether the CSV file has a header row.</summary>
    [FieldSchema(Description = "Whether the CSV file has a header row (default: true)", IsRequired = false, Example = true)]
    public bool HasHeader { get; init; } = true;

    /// <summary>Gets the column delimiter character.</summary>
    [FieldSchema(Description = "Column delimiter character (default: ',')", IsRequired = false, Example = ",")]
    public string Delimiter { get; init; } = CsvDefaults.DefaultDelimiter;

    /// <summary>Gets the maximum number of rows to read.</summary>
    [FieldSchema(Description = "Maximum number of rows to read (default: no limit)", Example = 100)]
    public int? MaxRows { get; init; }
}

/// <summary>
/// Response from reading a CSV file, containing parsed rows and metadata.
/// </summary>
public record CsvReaderResponse
{
    /// <summary>Gets the column header names.</summary>
    [ReturnSchema(Description = "Column header names")]
    public IReadOnlyList<string> Headers { get; init; } = [];

    /// <summary>Gets the data rows as key-value pairs mapping header name to cell value.</summary>
    [ReturnSchema(Description = "Data rows as key-value pairs (header → value)")]
    public IReadOnlyList<Dictionary<string, object>> Rows { get; init; } = [];

    /// <summary>Gets the number of data rows read.</summary>
    [ReturnSchema(Description = "Number of data rows read", Example = 150)]
    public int RowCount { get; init; }

    /// <summary>Gets the number of columns in the CSV.</summary>
    [ReturnSchema(Description = "Number of columns", Example = 5)]
    public int ColumnCount { get; init; }

    /// <summary>Gets whether rows were truncated due to the max_rows limit.</summary>
    [ReturnSchema(Description = "Whether rows were truncated due to max_rows limit", Example = false)]
    public bool Truncated { get; init; }
}

/// <summary>
/// Tool for reading CSV files and returning structured data.
/// Uses CsvHelper for robust CSV parsing.
/// </summary>
[ToolContract("csv_reader",
    Name = "csv_reader",
    Description = "Read CSV files and return structured data. Supports custom delimiters and header detection.")]
public partial class CsvReaderTool : FileToolBase<CsvReaderRequest, CsvReaderResponse>
{
    /// <summary>Initializes a new instance of <see cref="CsvReaderTool"/> with VFS support.</summary>
    /// <param name="fileSystemService">Virtual file system service.</param>
    /// <param name="logger">Optional logger instance.</param>
    public CsvReaderTool(
        IFileSystemService fileSystemService,
        ILogger<CsvReaderTool>? logger = null)
        : base(fileSystemService, logger)
    {
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(CsvReaderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Path))
            return "Path cannot be empty";

        var fsCheck = ResolveVirtualPath(request.Path, FileAccessRights.Read);
        if (!fsCheck.IsAllowed)
        {
            var reason = fsCheck.DenialReason ?? "rejected by file system policy";
            return $"Path rejected by file system policy (path traversal or outside allowed mounts): {reason}";
        }

        return null;
    }

    /// <inheritdoc />
    protected override Task<CsvReaderResponse> ExecuteTypedAsync(
        CsvReaderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<CsvReaderResponse> ExecuteTypedCoreAsync()
        {
            if (!await _fileSystemService.ExistsAsync(request.Path, cancellationToken).ConfigureAwait(false))
                throw new FileNotFoundException($"File not found: {request.Path}");

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = request.HasHeader,
                Delimiter = request.Delimiter,
                MissingFieldFound = null,
                BadDataFound = null
            };

            var (rows, headers, rowCount) = await ReadCsvDataVfsAsync(
                _fileSystemService, request.Path, config, request.HasHeader, request.MaxRows, cancellationToken).ConfigureAwait(false);

            LogReadCsv(request.Path, rows.Count, headers.Count);

            return new CsvReaderResponse
            {
                Headers = headers,
                Rows = rows,
                RowCount = rows.Count,
                ColumnCount = headers.Count,
                Truncated = request.MaxRows.HasValue && rowCount >= request.MaxRows.Value
            };
        }
    }

    private static async Task<(List<Dictionary<string, object>> rows, List<string> headers, int rowCount)> ReadCsvDataVfsAsync(
        IFileSystemService fs, string vPath, CsvConfiguration config, bool hasHeader, int? maxRows, CancellationToken cancellationToken)
    {
        var rows = new List<Dictionary<string, object>>();
        var headers = new List<string>();

        var stream = await fs.OpenReadStreamAsync(vPath, cancellationToken).ConfigureAwait(false);
        await using var __stream = stream.ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var csv = new CsvReader(reader, config);

        return await ReadCsvCoreAsync(csv, hasHeader, maxRows, headers, rows, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(List<Dictionary<string, object>> rows, List<string> headers, int rowCount)> ReadCsvCoreAsync(
        CsvReader csv, bool hasHeader, int? maxRows, List<string> headers, List<Dictionary<string, object>> rows, CancellationToken cancellationToken)
    {

        if (hasHeader)
        {
            await csv.ReadAsync().ConfigureAwait(false);
            csv.ReadHeader();
            headers.AddRange(csv.HeaderRecord ?? []);
        }

        var rowCount = 0;
        while (await csv.ReadAsync().ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (maxRows.HasValue && rowCount >= maxRows.Value)
                break;

            var row = hasHeader && csv.HeaderRecord != null
                ? ReadHeaderRow(csv)
                : ReadIndexedRow(csv, headers);

            rows.Add(row);
            rowCount++;
        }

        return (rows, headers, rowCount);
    }

    private static Dictionary<string, object> ReadHeaderRow(CsvReader csv)
    {
        var row = new Dictionary<string, object>();
        for (int i = 0; i < csv.HeaderRecord!.Length; i++)
        {
            var value = csv.TryGetField<string>(i, out var field) ? field ?? "" : "";
            row[csv.HeaderRecord[i]] = value;
        }
        return row;
    }

    private static Dictionary<string, object> ReadIndexedRow(CsvReader csv, List<string> headers)
    {
        // Bound the loop by the parser's actual field count for the current
        // record. Relying on TryGetField(index) to return false past the end is
        // unsafe: on malformed input (e.g. a JSON file fed as headerless CSV)
        // CsvHelper keeps returning true for ever-growing indices, which spins
        // the CPU forever and never reaches the cancellation check in the outer
        // read loop.
        var row = new Dictionary<string, object>();
        var fieldCount = csv.Parser.Count;
        for (var fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            var colName = $"{CsvDefaults.UnnamedColumnPrefix}{fieldIndex}";
            if (!headers.Contains(colName))
                headers.Add(colName);
            row[colName] = csv.TryGetField<string>(fieldIndex, out var field) ? field ?? "" : "";
        }
        return row;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully read CSV: {Path} ({Rows} rows, {Cols} columns)")]
    private partial void LogReadCsv(string path, int rows, int cols);
}
