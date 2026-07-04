using ClosedXML.Excel;
using System.Globalization;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Base;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Data;

// ── Request / Response / Helper records ──────────────────────────────

/// <summary>
/// Data for a single worksheet extracted from an Excel workbook.
/// </summary>
public record XlsxSheetData
{
    /// <summary>Gets the sheet name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Gets the header row values (if HasHeaderRow was true).</summary>
    public IReadOnlyList<string> Headers { get; init; } = [];

    /// <summary>Gets the data rows (each row is a list of string cell values).</summary>
    public IReadOnlyList<List<string>> Rows { get; init; } = [];

    /// <summary>Gets the number of data rows.</summary>
    public int RowCount { get; init; }

    /// <summary>Gets the number of columns.</summary>
    public int ColumnCount { get; init; }
}

/// <summary>
/// Request parameters for reading an Excel (.xlsx) file.
/// </summary>
public record XlsxReadRequest
{
    /// <summary>Gets the file path of the XLSX file to read.</summary>
    [FieldSchema(Description = "Path to the .xlsx file to read", IsRequired = true, Example = "/data/report.xlsx")]
    public string FilePath { get; init; } = "";

    /// <summary>Gets the specific sheet name to read (empty = all sheets).</summary>
    [FieldSchema(Description = "Specific sheet to read (empty = all sheets)", IsRequired = false, Example = "Sheet1")]
    public string SheetName { get; init; } = "";

    /// <summary>Gets whether to include workbook metadata (sheet names, dimensions).</summary>
    [FieldSchema(Description = "Whether to include workbook metadata", IsRequired = false, Example = true)]
    public bool IncludeMetadata { get; init; } = true;

    /// <summary>Gets whether to treat the first row as a header row.</summary>
    [FieldSchema(Description = "Whether to treat first row as header", IsRequired = false, Example = true)]
    public bool HasHeaderRow { get; init; } = true;
}

/// <summary>
/// Response from reading an Excel (.xlsx) file, containing structured sheet data and metadata.
/// </summary>
public record XlsxReadResponse
{
    /// <summary>Gets the data from each sheet.</summary>
    [ReturnSchema(Description = "Data from each sheet")]
    public IReadOnlyList<XlsxSheetData> Sheets { get; init; } = [];

    /// <summary>Gets the list of all sheet names in the workbook.</summary>
    [ReturnSchema(Description = "List of all sheet names in the workbook")]
    public IReadOnlyList<string> SheetNames { get; init; } = [];

    /// <summary>Gets the number of sheets in the workbook.</summary>
    [ReturnSchema(Description = "Number of sheets", Example = 3)]
    public int SheetCount { get; init; }

    /// <summary>Gets the workbook metadata (if requested).</summary>
    [ReturnSchema(Description = "Workbook metadata")]
    public Dictionary<string, string> Metadata { get; init; } = [];
}

/// <summary>
/// Tool for reading Excel (.xlsx) files and returning structured data.
/// Uses ClosedXML for robust spreadsheet parsing.
/// </summary>
[ToolContract("xlsx_reader",
    Name = "xlsx_reader",
    Description = "Read Excel (.xlsx) files and return structured data including sheets, rows, columns, and metadata.")]
public partial class XlsxReadTool : FileToolBase<XlsxReadRequest, XlsxReadResponse>
{
    /// <summary>Initializes a new instance of <see cref="XlsxReadTool"/> with virtual file system support.</summary>
    /// <param name="fileSystemService">Virtual file system service for mount-aware path resolution.</param>
    /// <param name="pathValidator">Path validator for traversal/SSRF protection (defense in depth).</param>
    /// <param name="logger">Optional logger instance.</param>
    public XlsxReadTool(
        IFileSystemService fileSystemService,
        IPathValidator pathValidator,
        ILogger<XlsxReadTool>? logger = null)
        : base(fileSystemService, pathValidator, logger)
    {
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(XlsxReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FilePath))
            return "FilePath cannot be empty";

        if (!request.FilePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return "File must have a .xlsx extension";

        // VFS path: validate mount + Read rights. Existence is verified during execution.
        var vfsResult = ResolveVirtualPath(request.FilePath, FileAccessRights.Read);
        return vfsResult.IsAllowed ? null : vfsResult.DenialReason ?? "path validation failed";
    }

    /// <inheritdoc />
    protected override Task<XlsxReadResponse> ExecuteTypedAsync(
        XlsxReadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<XlsxReadResponse> ExecuteTypedCoreAsync()
        {
            // VFS path: read the bytes through the virtual file system, which resolves the
            // virtual path (e.g. /data/x.xlsx) to its physical mount location.
            var vfsResult = ResolveVirtualPath(request.FilePath, FileAccessRights.Read);
            if (!vfsResult.IsAllowed)
                throw new InvalidOperationException(vfsResult.DenialReason ?? "path validation failed");

            var bytes = await _fileSystemService.TryReadAllBytesAsync(request.FilePath, cancellationToken).ConfigureAwait(false)
                ?? throw new FileNotFoundException($"File not found: {request.FilePath}");
            var workbook = new XLWorkbook(new MemoryStream(bytes));

            using (workbook)
            {
                var allSheetNames = workbook.Worksheets.Select(ws => ws.Name).ToList();
                var sheetsData = new List<XlsxSheetData>();
                var totalRows = 0;

                IEnumerable<IXLWorksheet> worksheetsToRead = string.IsNullOrWhiteSpace(request.SheetName)
                    ? workbook.Worksheets
                    : [workbook.Worksheet(request.SheetName)];

                foreach (var worksheet in worksheetsToRead)
                {
                    var sheetData = ReadSheet(worksheet, request.HasHeaderRow);
                    sheetsData.Add(sheetData);
                    totalRows += sheetData.RowCount;
                }

                var metadata = request.IncludeMetadata
                    ? ExtractMetadata(workbook, allSheetNames)
                    : [];

                LogReadXlsx(request.FilePath, allSheetNames.Count, totalRows);

                return new XlsxReadResponse
                {
                    Sheets = sheetsData,
                    SheetNames = allSheetNames,
                    SheetCount = allSheetNames.Count,
                    Metadata = metadata
                };
            }
        }
    }

    private static XlsxSheetData ReadSheet(IXLWorksheet worksheet, bool hasHeaderRow)
    {
        var range = worksheet.RangeUsed();
        if (range is null)
        {
            return new XlsxSheetData
            {
                Name = worksheet.Name,
                Headers = [],
                Rows = [],
                RowCount = 0,
                ColumnCount = 0
            };
        }

        var allRows = range.RowsUsed().ToList();
        var columnCount = range.ColumnCount();
        var headers = new List<string>();
        var dataRows = new List<List<string>>();

        var startIndex = 0;

        if (hasHeaderRow && allRows.Count > 0)
        {
            var headerRow = allRows[0];
            for (var col = 1; col <= columnCount; col++)
            {
                headers.Add(headerRow.Cell(col).GetFormattedString());
            }
            startIndex = 1;
        }

        for (var i = startIndex; i < allRows.Count; i++)
        {
            var row = allRows[i];
            var rowData = new List<string>();
            for (var col = 1; col <= columnCount; col++)
            {
                rowData.Add(row.Cell(col).GetFormattedString());
            }
            dataRows.Add(rowData);
        }

        return new XlsxSheetData
        {
            Name = worksheet.Name,
            Headers = headers,
            Rows = dataRows,
            RowCount = dataRows.Count,
            ColumnCount = columnCount
        };
    }

    private static Dictionary<string, string> ExtractMetadata(XLWorkbook workbook, List<string> sheetNames)
    {
        var metadata = new Dictionary<string, string>
        {
            ["sheet_count"] = sheetNames.Count.ToString(CultureInfo.InvariantCulture),
            ["sheet_names"] = string.Join(", ", sheetNames)
        };

        var props = workbook.Properties;

        if (!string.IsNullOrEmpty(props.Title))
            metadata["title"] = props.Title;

        if (!string.IsNullOrEmpty(props.Author))
            metadata["author"] = props.Author;

        if (!string.IsNullOrEmpty(props.Subject))
            metadata["subject"] = props.Subject;

        if (!string.IsNullOrEmpty(props.Company))
            metadata["company"] = props.Company;

        if (!string.IsNullOrEmpty(props.Manager))
            metadata["manager"] = props.Manager;

        if (!string.IsNullOrEmpty(props.Category))
            metadata["category"] = props.Category;

        return metadata;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully read XLSX: {Path} ({SheetCount} sheets, {TotalRows} rows)")]
    private partial void LogReadXlsx(string path, int sheetCount, int totalRows);
}
