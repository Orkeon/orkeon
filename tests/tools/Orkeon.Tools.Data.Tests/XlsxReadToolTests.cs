using ClosedXML.Excel;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.Data.Tests;

public sealed class XlsxReadToolTests : IDisposable
{
    private readonly XlsxReadTool _tool;
    private readonly string _testDir;

    public XlsxReadToolTests()
    {
        _tool = new XlsxReadTool(new PassThroughFileSystemService(), new StubPathValidator().AllowAll());
        _testDir = Path.Combine(Path.GetTempPath(), $"xlsx_read_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public async Task ShouldExtractData_WhenXlsxIsValid()
    {
        var xlsxPath = CreateXlsxFile("Sheet1", ["Name", "Age"], [["Alice", "30"], ["Bob", "25"]]);

        var request = new ToolCallRequest(
            ToolName: "xlsx_reader",
            Parameters: new Dictionary<string, object?> { ["file_path"] = xlsxPath }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);

        var sheets = dict["sheets"] as List<object>;
        Assert.NotNull(sheets);
        Assert.Single(sheets);

        var sheet = sheets[0] as Dictionary<string, object?>;
        Assert.NotNull(sheet);
        Assert.Equal("Sheet1", sheet["name"]?.ToString());

        var headers = sheet["headers"] as List<object>;
        Assert.NotNull(headers);
        Assert.Equal(2, headers.Count);
        Assert.Equal("Name", headers[0]?.ToString());
        Assert.Equal("Age", headers[1]?.ToString());

        var rows = sheet["rows"] as List<object>;
        Assert.NotNull(rows);
        Assert.Equal(2, rows.Count);

        Assert.Equal(2, (int)sheet["row_count"]!);
        Assert.Equal(2, (int)sheet["column_count"]!);
    }

    [Fact]
    public async Task ShouldReturnError_WhenFileDoesNotExist()
    {
        var request = new ToolCallRequest(
            ToolName: "xlsx_reader",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = "/nonexistent/file.xlsx"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("File not found", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenFileIsNotXlsx()
    {
        var txtPath = Path.Combine(_testDir, $"{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(txtPath, "This is a text file", TestContext.Current.CancellationToken);

        var request = new ToolCallRequest(
            ToolName: "xlsx_reader",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = txtPath
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(".xlsx", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenFilePathIsEmpty()
    {
        var request = new ToolCallRequest(
            ToolName: "xlsx_reader",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = ""
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("empty", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReadSpecificSheet_WhenSheetNameProvided()
    {
        var xlsxPath = CreateMultiSheetXlsxFile();

        var request = new ToolCallRequest(
            ToolName: "xlsx_reader",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = xlsxPath,
                ["sheet_name"] = "Products"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);

        var sheets = dict["sheets"] as List<object>;
        Assert.NotNull(sheets);
        Assert.Single(sheets);

        var sheet = sheets[0] as Dictionary<string, object?>;
        Assert.NotNull(sheet);
        Assert.Equal("Products", sheet["name"]?.ToString());

        // SheetNames should still list all sheets in the workbook
        var sheetNames = dict["sheet_names"] as List<object>;
        Assert.NotNull(sheetNames);
        Assert.Equal(2, sheetNames.Count);
    }

    [Fact]
    public async Task ShouldReadAllSheets_WhenNoSheetNameProvided()
    {
        var xlsxPath = CreateMultiSheetXlsxFile();

        var request = new ToolCallRequest(
            ToolName: "xlsx_reader",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = xlsxPath
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);

        var sheets = dict["sheets"] as List<object>;
        Assert.NotNull(sheets);
        Assert.Equal(2, sheets.Count);

        var sheetNames = dict["sheet_names"] as List<object>;
        Assert.NotNull(sheetNames);
        Assert.Contains("Employees", sheetNames.Select(s => s?.ToString()));
        Assert.Contains("Products", sheetNames.Select(s => s?.ToString()));
    }

    [Fact]
    public async Task ShouldExtractHeaders_WhenHasHeaderRowIsTrue()
    {
        var xlsxPath = CreateXlsxFile("Data", ["Col1", "Col2", "Col3"], [["A", "B", "C"]]);

        var request = new ToolCallRequest(
            ToolName: "xlsx_reader",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = xlsxPath,
                ["has_header_row"] = true
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);

        var sheets = dict["sheets"] as List<object>;
        Assert.NotNull(sheets);
        var sheet = sheets[0] as Dictionary<string, object?>;
        Assert.NotNull(sheet);

        var headers = sheet["headers"] as List<object>;
        Assert.NotNull(headers);
        Assert.Equal(3, headers.Count);
        Assert.Equal("Col1", headers[0]?.ToString());
        Assert.Equal("Col2", headers[1]?.ToString());
        Assert.Equal("Col3", headers[2]?.ToString());

        var rows = sheet["rows"] as List<object>;
        Assert.NotNull(rows);
        Assert.Single(rows); // Only data rows, header excluded
    }

    [Fact]
    public async Task ShouldNotExtractHeaders_WhenHasHeaderRowIsFalse()
    {
        var xlsxPath = CreateXlsxFile("Data", ["Col1", "Col2"], [["A", "B"], ["C", "D"]]);

        var request = new ToolCallRequest(
            ToolName: "xlsx_reader",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = xlsxPath,
                ["has_header_row"] = false
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);

        var sheets = dict["sheets"] as List<object>;
        Assert.NotNull(sheets);
        var sheet = sheets[0] as Dictionary<string, object?>;
        Assert.NotNull(sheet);

        var headers = sheet["headers"] as List<object>;
        Assert.NotNull(headers);
        Assert.Empty(headers); // No headers extracted

        var rows = sheet["rows"] as List<object>;
        Assert.NotNull(rows);
        Assert.Equal(3, rows.Count); // All rows including the "header" row treated as data
    }

    [Fact]
    public async Task ShouldExtractMetadata_WhenIncludeMetadataIsTrue()
    {
        var xlsxPath = CreateMultiSheetXlsxFile();

        var request = new ToolCallRequest(
            ToolName: "xlsx_reader",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = xlsxPath,
                ["include_metadata"] = true
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);

        var metadata = dict["metadata"] as Dictionary<string, object?>;
        Assert.NotNull(metadata);
        Assert.Equal("2", metadata["sheet_count"]?.ToString());
        Assert.Contains("Employees", metadata["sheet_names"]?.ToString());
        Assert.Contains("Products", metadata["sheet_names"]?.ToString());

        Assert.Equal(2, (int)dict["sheet_count"]!);

        var sheetNames = dict["sheet_names"] as List<object>;
        Assert.NotNull(sheetNames);
        Assert.Equal(2, sheetNames.Count);
    }

    [Fact]
    public void ShouldHaveCorrectConfiguration_WhenAccessingSchema()
    {
        Assert.Equal("xlsx_reader", _tool.Name);
        Assert.Equal("File Operations", _tool.Category);
        Assert.True(_tool.Schema.Parameters["file_path"].Required);
        Assert.False(_tool.Schema.Parameters["sheet_name"].Required);
        Assert.False(_tool.Schema.Parameters["include_metadata"].Required);
        Assert.False(_tool.Schema.Parameters["has_header_row"].Required);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private string CreateXlsxFile(string sheetName, List<string> headers, List<List<string>> rows)
    {
        var path = Path.Combine(_testDir, $"{Guid.NewGuid():N}.xlsx");
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(sheetName);

        // Write headers
        for (var col = 0; col < headers.Count; col++)
            worksheet.Cell(1, col + 1).Value = headers[col];

        // Write data rows
        for (var row = 0; row < rows.Count; row++)
            for (var col = 0; col < rows[row].Count; col++)
                worksheet.Cell(row + 2, col + 1).Value = rows[row][col];

        workbook.SaveAs(path);
        return path;
    }

    private string CreateMultiSheetXlsxFile()
    {
        var path = Path.Combine(_testDir, $"{Guid.NewGuid():N}.xlsx");
        using var workbook = new XLWorkbook();

        // Sheet 1: Employees
        var ws1 = workbook.Worksheets.Add("Employees");
        ws1.Cell(1, 1).Value = "Name";
        ws1.Cell(1, 2).Value = "Department";
        ws1.Cell(2, 1).Value = "Alice";
        ws1.Cell(2, 2).Value = "Engineering";
        ws1.Cell(3, 1).Value = "Bob";
        ws1.Cell(3, 2).Value = "Marketing";

        // Sheet 2: Products
        var ws2 = workbook.Worksheets.Add("Products");
        ws2.Cell(1, 1).Value = "Product";
        ws2.Cell(1, 2).Value = "Price";
        ws2.Cell(2, 1).Value = "Widget";
        ws2.Cell(2, 2).Value = "9.99";

        workbook.SaveAs(path);
        return path;
    }

    [Fact]
    public async Task XlsxRead_PathTraversal_ValidatorDenied_FailsFastWithoutReading()
    {
        var deniedValidator = new StubPathValidator()
            .RespondWith((_, _) => PathValidationResult.Denied("path traversal detected"));

        using var tool = new XlsxReadTool(new PassThroughFileSystemService(), deniedValidator);

        var request = new ToolCallRequest(
            ToolName: "xlsx_reader",
            Parameters: new Dictionary<string, object?> { ["file_path"] = "../../etc/passwd.xlsx" }
        );

        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("traversal", result.Error, StringComparison.OrdinalIgnoreCase);
        // The defense-in-depth validator is consulted with the resolved physical path
        // (the VFS collapses the traversal segments before delegating), so match on the
        // file name rather than the raw requested path.
        Assert.Contains(deniedValidator.Calls, c => c.Path.EndsWith("passwd.xlsx", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_testDir, true); } catch { }
        _tool.Dispose();
    }
}
