using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Data.Tests;

public sealed class XmlParserToolTests : IDisposable
{
    private readonly XmlParserTool _tool;
    private readonly string _testDir;

    public XmlParserToolTests()
    {
        _tool = new XmlParserTool(new PassThroughFileSystemService(), new StubPathValidator().AllowAll());
        _testDir = Path.Combine(Path.GetTempPath(), $"xml_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public async Task ShouldReturnStructure_WhenParsingXmlString()
    {
        var xml = "<root><item id=\"1\">First</item><item id=\"2\">Second</item></root>";
        var request = new ToolCallRequest(
            ToolName: "xml_parser",
            Parameters: new Dictionary<string, object?>
            {
                ["input"] = xml,
                ["operation"] = "parse"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal("root", dict["root_element"]);
        Assert.Equal(2, (int)dict["child_count"]!);
    }

    [Fact]
    public async Task ShouldReturnStructure_WhenParsingXmlFile()
    {
        var xmlPath = Path.Combine(_testDir, "test.xml");
        await File.WriteAllTextAsync(xmlPath, "<config><setting name=\"key\">value</setting></config>", TestContext.Current.CancellationToken);

        var request = new ToolCallRequest(
            ToolName: "xml_parser",
            Parameters: new Dictionary<string, object?>
            {
                ["input"] = xmlPath,
                ["operation"] = "parse"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal("config", dict["root_element"]);
        Assert.Equal("file", dict["source"]);
    }

    [Fact]
    public async Task ShouldReturnMatches_WhenQueryingWithXPath()
    {
        var xml = "<root><items><item>A</item><item>B</item><item>C</item></items></root>";
        var request = new ToolCallRequest(
            ToolName: "xml_parser",
            Parameters: new Dictionary<string, object?>
            {
                ["input"] = xml,
                ["operation"] = ParamQuery,
                ["xpath"] = "//item"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(3, (int)dict["match_count"]!);
    }

    [Fact]
    public async Task ShouldReturnError_WhenXmlIsInvalid()
    {
        var request = new ToolCallRequest(
            ToolName: "xml_parser",
            Parameters: new Dictionary<string, object?>
            {
                ["input"] = "<invalid><unclosed",
                ["operation"] = "parse"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenQueryingWithoutXPath()
    {
        var xml = "<root><item>test</item></root>";
        var request = new ToolCallRequest(
            ToolName: "xml_parser",
            Parameters: new Dictionary<string, object?>
            {
                ["input"] = xml,
                ["operation"] = ParamQuery
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("XPath", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldHaveCorrectConfiguration_WhenAccessingSchema()
    {
        Assert.Equal("xml_parser", _tool.Name);
        Assert.True(_tool.Schema.Parameters["input"].Required);
        Assert.False(_tool.Schema.Parameters["xpath"].Required);
        Assert.False(_tool.Schema.Parameters["operation"].Required);
    }

    [Fact]
    public async Task XmlParser_PathTraversal_ValidatorDenied_FailsFastWithoutReading()
    {
        var deniedValidator = new StubPathValidator()
            .RespondWith((_, _) => PathValidationResult.Denied("path traversal detected"));

        using var tool = new XmlParserTool(new PassThroughFileSystemService(), deniedValidator);

        var traversalPath = Path.Combine(_testDir, "..", "..", "etc", "shadow.xml");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "shadow.xml"), "<root/>", TestContext.Current.CancellationToken);

        var request = new ToolCallRequest(
            ToolName: "xml_parser",
            Parameters: new Dictionary<string, object?> { ["input"] = traversalPath, ["operation"] = "parse" }
        );

        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("traversal", result.Error, StringComparison.OrdinalIgnoreCase);
        // The defense-in-depth validator is consulted with the resolved physical path
        // (the VFS collapses the traversal segments before delegating), so match on the
        // file name rather than the raw requested path.
        Assert.Contains(deniedValidator.Calls, c => c.Path.EndsWith("shadow.xml", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_testDir, true); } catch { }
        _tool.Dispose();
    }
}
