using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Data.Tests;

public sealed class CsvReaderToolTests : IDisposable
{
    private readonly CsvReaderTool _tool;
    private readonly string _testDir;

    public CsvReaderToolTests()
    {
        _tool = new CsvReaderTool(new PassThroughFileSystemService());
        _testDir = Path.Combine(Path.GetTempPath(), $"csv_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public async Task ShouldReturnStructuredData_WhenCsvIsValid()
    {
        var csvPath = CreateCsvFile("name,age,city\nAlice,30,Paris\nBob,25,London");

        var request = new ToolCallRequest(
            ToolName: "csv_reader",
            Parameters: new Dictionary<string, object?> { [ParamPath] = csvPath }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(2, (int)dict["row_count"]!);
        Assert.Equal(3, (int)dict["column_count"]!);

        var headers = dict["headers"] as List<object>;
        Assert.NotNull(headers);
        Assert.Contains("name", headers.Select(h => h?.ToString()));
        Assert.Contains("age", headers.Select(h => h?.ToString()));
        Assert.Contains("city", headers.Select(h => h?.ToString()));
    }

    [Fact]
    public async Task ShouldParseCorrectly_WhenUsingCustomDelimiter()
    {
        var csvPath = CreateCsvFile("name;age;city\nAlice;30;Paris");

        var request = new ToolCallRequest(
            ToolName: "csv_reader",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = csvPath,
                ["delimiter"] = ";"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(1, (int)dict["row_count"]!);
        Assert.Equal(3, (int)dict["column_count"]!);
    }

    [Fact]
    public async Task ShouldTruncateResults_WhenMaxRowsIsSpecified()
    {
        var csvPath = CreateCsvFile("id,value\n1,a\n2,b\n3,c\n4,d\n5,e");

        var request = new ToolCallRequest(
            ToolName: "csv_reader",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = csvPath,
                ["max_rows"] = 2
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(2, (int)dict["row_count"]!);
        Assert.True((bool)dict["truncated"]!);
    }

    [Fact]
    public async Task ShouldReturnError_WhenFileDoesNotExist()
    {
        var request = new ToolCallRequest(
            ToolName: "csv_reader",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = "/nonexistent/file.csv"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("File not found", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenPathIsMissing()
    {
        var request = new ToolCallRequest(
            ToolName: "csv_reader",
            Parameters: []
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(ParamPath, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldHaveCorrectConfiguration_WhenAccessingSchema()
    {
        Assert.Equal("csv_reader", _tool.Name);
        Assert.True(_tool.Schema.Parameters[ParamPath].Required);
        Assert.False(_tool.Schema.Parameters["has_header"].Required);
        Assert.False(_tool.Schema.Parameters["delimiter"].Required);
    }

    [Fact]
    public async Task ShouldRejectUnsafePath_WhenPathValidatorDenies()
    {
        // Arrange — kept to document the legacy expectation.
        var fs = new PassThroughFileSystemService((path, _) =>
            path == "../../../etc/passwd"
                ? PathValidationResult.Denied("Directory traversal detected")
                : PathValidationResult.Allowed(Path.GetFullPath(path)));

        using var tool = new CsvReaderTool(fs);

        var request = new ToolCallRequest(
            ToolName: "csv_reader",
            Parameters: new Dictionary<string, object?> { [ParamPath] = "../../../etc/passwd" }
        );

        // Act
        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("traversal", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldRejectMissingFile_WhenFileDoesNotExist()
    {
        // Arrange — the file genuinely does not exist on disk, so PassThroughFileSystemService
        // ExistsAsync returns false naturally.
        var nonExistentPath = Path.Combine(_testDir, "nonexistent.csv");
        using var tool = new CsvReaderTool(new PassThroughFileSystemService());

        var request = new ToolCallRequest(
            ToolName: "csv_reader",
            Parameters: new Dictionary<string, object?> { [ParamPath] = nonExistentPath }
        );

        // Act
        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldSucceed_WhenPathValidatorAllowsValidCsv()
    {
        // Arrange
        var csvPath = CreateCsvFile("Name,Age\nAlice,30\nBob,25");
        using var tool = new CsvReaderTool(new PassThroughFileSystemService());

        var request = new ToolCallRequest(
            ToolName: "csv_reader",
            Parameters: new Dictionary<string, object?> { [ParamPath] = csvPath }
        );

        // Act
        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(2, (int)dict["row_count"]!);
    }

    [Fact]
    public async Task ShouldRejectEmptyPath_WithoutCallingPathValidator()
    {
        // Arrange — strict stub: any IFileSystemService call would throw.
        var fs = new ThrowingFileSystemService();
        using var tool = new CsvReaderTool(fs);

        var request = new ToolCallRequest(
            ToolName: "csv_reader",
            Parameters: new Dictionary<string, object?> { [ParamPath] = "" }
        );

        // Act
        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("empty", result.Error, StringComparison.OrdinalIgnoreCase);

        // The file system layer was never touched.
        Assert.Equal(0, fs.CallCount);
    }

    [Fact]
    public async Task ShouldTerminate_WhenJsonFedAsHeaderlessCsv()
    {
        // Regression: a JSON file read as headerless CSV used to spin forever in
        // ReadIndexedRow because TryGetField(index) never returned false on the
        // malformed record. The inner loop is now bounded by Parser.Count.
        var jsonPath = Path.Combine(_testDir, $"{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            jsonPath,
            "[\n  {\n    \"nom\": \"Dubois\",\n    \"emails\": [\"a@b.fr\", \"c@d.fr\"],\n    \"notes\": \"devis 4500 EUR\"\n  }\n]",
            TestContext.Current.CancellationToken);

        // Hard timeout: if the loop regresses, the test fails (cancelled) instead
        // of wedging the whole suite.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var request = new ToolCallRequest(
            ToolName: "csv_reader",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = jsonPath,
                ["has_header"] = false
            }
        );

        var result = await _tool.CallAsync(request, cts.Token);

        Assert.False(cts.IsCancellationRequested, "csv_reader hung on JSON input fed as headerless CSV");
        Assert.True(result.Success);
    }

    private string CreateCsvFile(string content)
    {
        var path = Path.Combine(_testDir, $"{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_testDir, true); } catch { }
        _tool.Dispose();
    }
}
