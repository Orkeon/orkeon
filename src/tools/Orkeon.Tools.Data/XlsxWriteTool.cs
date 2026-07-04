using ClosedXML.Excel;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Base;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Data;

// ── Request / Response records ────────────────────────────────────────

/// <summary>
/// Represents a single sheet to write into an Excel workbook.
/// </summary>
public record XlsxSheetInput
{
    /// <summary>Gets the sheet name.</summary>
    [FieldSchema(Description = "Sheet name", IsRequired = true, Example = "Sheet1")]
    public string Name { get; init; } = "";

    /// <summary>Gets the column headers.</summary>
    [FieldSchema(Description = "Column headers", IsRequired = false)]
    public IReadOnlyList<string> Headers { get; init; } = [];

    /// <summary>Gets the data rows.</summary>
    [FieldSchema(Description = "Data rows (list of lists)", IsRequired = false)]
    public IReadOnlyList<List<string>> Rows { get; init; } = [];
}

/// <summary>
/// Request parameters for creating or updating an XLSX file.
/// </summary>
public record XlsxWriteRequest
{
    /// <summary>Gets the output file path for the XLSX file.</summary>
    [FieldSchema(Description = "Output file path for the .xlsx", IsRequired = true, Example = "/data/output.xlsx")]
    public string FilePath { get; init; } = "";

    /// <summary>Gets the sheets to write.</summary>
    [FieldSchema(Description = "Sheets to write (each with name, headers, rows)", IsRequired = true)]
    public IReadOnlyList<XlsxSheetInput> Sheets { get; init; } = [];

    /// <summary>Gets whether to append to an existing file.</summary>
    [FieldSchema(Description = "Whether to append sheets to an existing file", IsRequired = false, Default = false)]
    public bool AppendToExisting { get; init; }
}

/// <summary>
/// Response from creating or updating an XLSX file.
/// </summary>
public record XlsxWriteResponse
{
    /// <summary>Gets whether the file was created/updated successfully.</summary>
    [ReturnSchema(Description = "Whether the file was created/updated successfully", Example = true)]
    public bool Success { get; init; }

    /// <summary>Gets the path of the created file.</summary>
    [ReturnSchema(Description = "Path of the created file")]
    public string FilePath { get; init; } = "";

    /// <summary>Gets the file size in bytes.</summary>
    [ReturnSchema(Description = "File size in bytes", Example = 12345)]
    public long FileSize { get; init; }

    /// <summary>Gets the number of sheets written.</summary>
    [ReturnSchema(Description = "Number of sheets written", Example = 1)]
    public int SheetCount { get; init; }
}

/// <summary>
/// Tool for creating or updating Excel (.xlsx) files with structured data
/// including multiple sheets, headers, and rows. Uses ClosedXML for document generation.
/// </summary>
[ToolContract("xlsx_writer",
    Name = "xlsx_writer",
    Description = "Create or update Excel (.xlsx) files with structured data including multiple sheets, headers, and rows.")]
public partial class XlsxWriteTool : FileToolBase<XlsxWriteRequest, XlsxWriteResponse>
{
    /// <summary>Initializes a new instance of <see cref="XlsxWriteTool"/> with VFS support.</summary>
    /// <param name="fileSystemService">Virtual file system service.</param>
    /// <param name="pathValidator">Path validator for traversal/SSRF protection (defense in depth).</param>
    /// <param name="logger">Optional logger instance.</param>
    public XlsxWriteTool(
        IFileSystemService fileSystemService,
        IPathValidator pathValidator,
        ILogger<XlsxWriteTool>? logger = null)
        : base(fileSystemService, pathValidator, logger)
    {
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(XlsxWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FilePath))
            return "FilePath cannot be empty";

        var pathResult = ResolveVirtualPath(request.FilePath, FileAccessRights.Write);
        if (!pathResult.IsAllowed)
            return pathResult.DenialReason ?? "Invalid or unsafe file path";

        if (!request.FilePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return "File must have a .xlsx extension";

        if (request.Sheets.Count == 0)
            return "At least one sheet must be provided";

        return null;
    }

    /// <inheritdoc />
    protected override Task<XlsxWriteResponse> ExecuteTypedAsync(
        XlsxWriteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ExecuteTypedCoreAsync();

        async Task<XlsxWriteResponse> ExecuteTypedCoreAsync()
        {
            int totalSheetCount;

            // Build the workbook in memory, then commit the bytes through the
            // virtual file system so the physical mount location is resolved by the VFS.
            var existingBytes = request.AppendToExisting
                ? await _fileSystemService.TryReadAllBytesAsync(request.FilePath, cancellationToken).ConfigureAwait(false)
                : null;

            byte[] bytes;
            using (var workbook = existingBytes is { Length: > 0 }
                ? new XLWorkbook(new MemoryStream(existingBytes))
                : new XLWorkbook())
            {
                foreach (var sheet in request.Sheets)
                {
                    WriteSheet(workbook, sheet);
                }

                totalSheetCount = workbook.Worksheets.Count;

                using var outputStream = new MemoryStream();
                workbook.SaveAs(outputStream);
                bytes = outputStream.ToArray();
            }

            var fileSize = await _fileSystemService.WriteAllBytesAsync(request.FilePath, bytes, cancellationToken).ConfigureAwait(false);

            LogWriteXlsx(request.FilePath, totalSheetCount);

            return new XlsxWriteResponse
            {
                Success = true,
                FilePath = request.FilePath,
                FileSize = fileSize,
                SheetCount = totalSheetCount
            };
        }
    }

    private static void WriteSheet(XLWorkbook workbook, XlsxSheetInput sheet)
    {
        if (workbook.Worksheets.TryGetWorksheet(sheet.Name, out var existing))
        {
            workbook.Worksheets.Delete(existing.Name);
        }

        var worksheet = workbook.Worksheets.Add(sheet.Name);

        var startRow = 1;
        if (sheet.Headers.Count > 0)
        {
            for (var col = 0; col < sheet.Headers.Count; col++)
            {
                var cell = worksheet.Cell(1, col + 1);
                cell.Value = sheet.Headers[col];
                cell.Style.Font.Bold = true;
            }
            startRow = 2;
        }

        for (var row = 0; row < sheet.Rows.Count; row++)
        {
            for (var col = 0; col < sheet.Rows[row].Count; col++)
            {
                worksheet.Cell(row + startRow, col + 1).Value = sheet.Rows[row][col];
            }
        }

        worksheet.Columns().AdjustToContents();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully wrote XLSX: {Path} ({SheetCount} sheets)")]
    private partial void LogWriteXlsx(string path, int sheetCount);
}
