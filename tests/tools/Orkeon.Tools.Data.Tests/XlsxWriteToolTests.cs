using ClosedXML.Excel;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.Data.Tests;

public sealed class XlsxWriteToolTests : IDisposable
{
    private readonly XlsxWriteTool _tool;
    private readonly string _testDir;

    public XlsxWriteToolTests()
    {
        _tool = new XlsxWriteTool(new PassThroughFileSystemService(), new StubPathValidator().AllowAll());
        _testDir = Path.Combine(Path.GetTempPath(), $"xlsx_write_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public async Task ShouldCreateBasicXlsxFile()
    {
        var filePath = Path.Combine(_testDir, "basic.xlsx");

        var request = new ToolCallRequest(
            ToolName: "xlsx_writer",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = filePath,
                ["sheets"] = new List<object>
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = "Sheet1",
                        ["headers"] = new List<object> { "Name", "Age" },
                        ["rows"] = new List<object>
                        {
                            new List<object> { "Alice", "30" }
                        }
                    }
                }
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.True((bool)dict["success"]!);
        Assert.Equal(filePath, dict["file_path"]?.ToString());
        Assert.True(Convert.ToInt64(dict["file_size"]!) > 0);
        Assert.True(File.Exists(filePath));

        // Verify with ClosedXML
        using var wb = new XLWorkbook(filePath);
        Assert.Equal(1, wb.Worksheets.Count);
    }

    [Fact]
    public async Task ShouldCreateXlsxWithHeadersAndRows()
    {
        var filePath = Path.Combine(_testDir, "data.xlsx");

        var request = new ToolCallRequest(
            ToolName: "xlsx_writer",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = filePath,
                ["sheets"] = new List<object>
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = "People",
                        ["headers"] = new List<object> { "Name", "Age", "City" },
                        ["rows"] = new List<object>
                        {
                            new List<object> { "Alice", "30", "Paris" },
                            new List<object> { "Bob", "25", "London" }
                        }
                    }
                }
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.True(File.Exists(filePath));

        // Verify content with ClosedXML
        using var wb = new XLWorkbook(filePath);
        var ws = wb.Worksheet("People");
        Assert.NotNull(ws);

        // Headers
        Assert.Equal("Name", ws.Cell(1, 1).GetString());
        Assert.Equal("Age", ws.Cell(1, 2).GetString());
        Assert.Equal("City", ws.Cell(1, 3).GetString());
        Assert.True(ws.Cell(1, 1).Style.Font.Bold);

        // Data rows
        Assert.Equal("Alice", ws.Cell(2, 1).GetString());
        Assert.Equal("30", ws.Cell(2, 2).GetString());
        Assert.Equal("Paris", ws.Cell(2, 3).GetString());
        Assert.Equal("Bob", ws.Cell(3, 1).GetString());
        Assert.Equal("25", ws.Cell(3, 2).GetString());
        Assert.Equal("London", ws.Cell(3, 3).GetString());
    }

    [Fact]
    public async Task ShouldCreateMultiSheetXlsx()
    {
        var filePath = Path.Combine(_testDir, "multi.xlsx");

        var request = new ToolCallRequest(
            ToolName: "xlsx_writer",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = filePath,
                ["sheets"] = new List<object>
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = "Employees",
                        ["headers"] = new List<object> { "Name", "Role" },
                        ["rows"] = new List<object>
                        {
                            new List<object> { "Alice", "Engineer" }
                        }
                    },
                    new Dictionary<string, object?>
                    {
                        ["name"] = "Departments",
                        ["headers"] = new List<object> { "Department", "Budget" },
                        ["rows"] = new List<object>
                        {
                            new List<object> { "Engineering", "100000" }
                        }
                    }
                }
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(2, Convert.ToInt32(dict["sheet_count"]!));

        // Verify with ClosedXML
        using var wb = new XLWorkbook(filePath);
        Assert.Equal(2, wb.Worksheets.Count);

        var ws1 = wb.Worksheet("Employees");
        Assert.NotNull(ws1);
        Assert.Equal("Alice", ws1.Cell(2, 1).GetString());

        var ws2 = wb.Worksheet("Departments");
        Assert.NotNull(ws2);
        Assert.Equal("Engineering", ws2.Cell(2, 1).GetString());
    }

    [Fact]
    public async Task ShouldReturnError_WhenFilePathIsEmpty()
    {
        var request = new ToolCallRequest(
            ToolName: "xlsx_writer",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = "",
                ["sheets"] = new List<object>
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = "Sheet1",
                        ["headers"] = new List<object> { "A" },
                        ["rows"] = new List<object>
                        {
                            new List<object> { "1" }
                        }
                    }
                }
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("empty", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnError_WhenFileExtensionIsNotXlsx()
    {
        var filePath = Path.Combine(_testDir, "output.csv");

        var request = new ToolCallRequest(
            ToolName: "xlsx_writer",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = filePath,
                ["sheets"] = new List<object>
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = "Sheet1",
                        ["headers"] = new List<object> { "A" },
                        ["rows"] = new List<object>
                        {
                            new List<object> { "1" }
                        }
                    }
                }
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(".xlsx", result.Error);
    }

    [Fact]
    public async Task ShouldCreateDirectoryIfNotExists()
    {
        var subDir = Path.Combine(_testDir, "subdir", "nested");
        var filePath = Path.Combine(subDir, "output.xlsx");

        var request = new ToolCallRequest(
            ToolName: "xlsx_writer",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = filePath,
                ["sheets"] = new List<object>
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = "Sheet1",
                        ["headers"] = new List<object> { "Col1" },
                        ["rows"] = new List<object>
                        {
                            new List<object> { "Data in nested dir" }
                        }
                    }
                }
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task ShouldAppendToExistingFile()
    {
        var filePath = Path.Combine(_testDir, "append.xlsx");

        // First: create a file with one sheet
        var createRequest = new ToolCallRequest(
            ToolName: "xlsx_writer",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = filePath,
                ["sheets"] = new List<object>
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = "Original",
                        ["headers"] = new List<object> { "Name" },
                        ["rows"] = new List<object>
                        {
                            new List<object> { "Alice" }
                        }
                    }
                }
            }
        );

        var createResult = await _tool.CallAsync(createRequest, TestContext.Current.CancellationToken);
        Assert.True(createResult.Success);

        // Second: append a new sheet to the existing file
        var appendRequest = new ToolCallRequest(
            ToolName: "xlsx_writer",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = filePath,
                ["append_to_existing"] = true,
                ["sheets"] = new List<object>
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = "Appended",
                        ["headers"] = new List<object> { "City" },
                        ["rows"] = new List<object>
                        {
                            new List<object> { "Paris" }
                        }
                    }
                }
            }
        );

        var appendResult = await _tool.CallAsync(appendRequest, TestContext.Current.CancellationToken);
        Assert.True(appendResult.Success);

        var appendDict = appendResult.Result as Dictionary<string, object?>;
        Assert.NotNull(appendDict);
        Assert.Equal(2, Convert.ToInt32(appendDict["sheet_count"]!));

        // Verify with ClosedXML — both sheets should be present
        using var wb = new XLWorkbook(filePath);
        Assert.Equal(2, wb.Worksheets.Count);

        var wsOriginal = wb.Worksheet("Original");
        Assert.NotNull(wsOriginal);
        Assert.Equal("Alice", wsOriginal.Cell(2, 1).GetString());

        var wsAppended = wb.Worksheet("Appended");
        Assert.NotNull(wsAppended);
        Assert.Equal("Paris", wsAppended.Cell(2, 1).GetString());
    }

    [Fact]
    public async Task ShouldReturnError_WhenNoSheetsProvided()
    {
        var filePath = Path.Combine(_testDir, "empty_sheets.xlsx");

        var request = new ToolCallRequest(
            ToolName: "xlsx_writer",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = filePath,
                ["sheets"] = new List<object>()
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("sheet", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldHaveCorrectConfiguration_WhenAccessingSchema()
    {
        Assert.Equal("xlsx_writer", _tool.Name);
        Assert.Equal("File Operations", _tool.Category);
        Assert.True(_tool.Schema.Parameters["file_path"].Required);
        Assert.True(_tool.Schema.Parameters["sheets"].Required);
        Assert.False(_tool.Schema.Parameters["append_to_existing"].Required);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_testDir, true); } catch { }
        _tool.Dispose();
    }
}
