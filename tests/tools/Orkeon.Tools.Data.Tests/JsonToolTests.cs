using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Data.Tests;

public sealed class JsonToolTests : IDisposable
{
    private readonly JsonTool _tool;

    public JsonToolTests()
    {
        _tool = new JsonTool(new PassThroughFileSystemService());
    }

    [Fact]
    public async Task ShouldReturnStructureInfo_WhenParsingJson()
    {
        var json = "{\"name\":\"test\",\"value\":42,\"items\":[1,2,3]}";
        var request = new ToolCallRequest(
            ToolName: "json_tool",
            Parameters: new Dictionary<string, object?>
            {
                ["input"] = json,
                ["operation"] = "parse"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.True((bool)dict["parsed"]!);
        var info = dict["info"] as Dictionary<string, object?>;
        Assert.NotNull(info);
        Assert.Equal("Object", info["type"]);
        Assert.Equal(3, (int)info["property_count"]!);
    }

    [Fact]
    public async Task ShouldExtractValue_WhenQueryingJson()
    {
        var json = "{\"data\":{\"items\":[{\"name\":\"first\"},{\"name\":\"second\"}]}}";
        var request = new ToolCallRequest(
            ToolName: "json_tool",
            Parameters: new Dictionary<string, object?>
            {
                ["input"] = json,
                ["operation"] = ParamQuery,
                [ParamQuery] = "data.items[1].name"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal("second", dict["value"]);
    }

    [Fact]
    public async Task ShouldReturnIndentedJson_WhenFormattingJson()
    {
        var json = "{\"a\":1,\"b\":2}";
        var request = new ToolCallRequest(
            ToolName: "json_tool",
            Parameters: new Dictionary<string, object?>
            {
                ["input"] = json,
                ["operation"] = "format"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        var formatted = dict["formatted"]!.ToString()!;
        Assert.Contains("\n", formatted);
        Assert.Contains("  ", formatted);
    }

    [Fact]
    public async Task ShouldReturnError_WhenJsonIsInvalid()
    {
        var request = new ToolCallRequest(
            ToolName: "json_tool",
            Parameters: new Dictionary<string, object?>
            {
                ["input"] = "not valid json {",
                ["operation"] = "parse"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Invalid JSON", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenQueryPropertyIsMissing()
    {
        var json = "{\"a\":1}";
        var request = new ToolCallRequest(
            ToolName: "json_tool",
            Parameters: new Dictionary<string, object?>
            {
                ["input"] = json,
                ["operation"] = ParamQuery,
                [ParamQuery] = "b.c"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public void ShouldHaveCorrectConfiguration_WhenAccessingSchema()
    {
        Assert.Equal("json_tool", _tool.Name);
        Assert.Equal("Data Operations", _tool.Category);
        Assert.True(_tool.Schema.Parameters["input"].Required);
        Assert.True(_tool.Schema.Parameters["operation"].Required);
        Assert.False(_tool.Schema.Parameters[ParamQuery].Required);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _tool.Dispose();
    }
}
