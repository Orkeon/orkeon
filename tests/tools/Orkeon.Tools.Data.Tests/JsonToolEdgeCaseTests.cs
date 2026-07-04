using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Data.Tests;

/// <summary>
/// Additional edge-case coverage for <see cref="JsonTool"/> exercising array parsing,
/// every query value-type branch, and navigation error paths.
/// </summary>
public sealed class JsonToolEdgeCaseTests : IDisposable
{
    private readonly JsonTool _tool = new(new PassThroughFileSystemService());

    private static Dictionary<string, object?> Dict(ToolCallResponse r)
    {
        var d = r.Result as Dictionary<string, object?>;
        Assert.NotNull(d);
        return d;
    }

    private async Task<ToolCallResponse> CallAsync(Dictionary<string, object?> parameters)
        => await _tool.CallAsync(new ToolCallRequest(ToolName: "json_tool", Parameters: parameters));

    [Fact]
    public async Task ShouldReportElementCount_WhenParsingArray()
    {
        var result = await CallAsync(new Dictionary<string, object?>
        {
            ["input"] = "[10,20,30,40]",
            ["operation"] = "parse"
        });

        Assert.True(result.Success, result.Error);
        var dict = Dict(result);
        var info = dict["info"] as Dictionary<string, object?>;
        Assert.NotNull(info);
        Assert.Equal("Array", info["type"]);
        Assert.Equal(4, Convert.ToInt32(info["element_count"]));
    }

    [Fact]
    public async Task ShouldReportScalarType_WhenParsingScalar()
    {
        var result = await CallAsync(new Dictionary<string, object?>
        {
            ["input"] = "\"just a string\"",
            ["operation"] = "parse"
        });

        Assert.True(result.Success, result.Error);
        var dict = Dict(result);
        var info = dict["info"] as Dictionary<string, object?>;
        Assert.NotNull(info);
        Assert.Equal("String", info["type"]);
    }

    [Fact]
    public async Task ShouldReturnNumberValue_WhenQueryingInteger()
    {
        var result = await CallAsync(new Dictionary<string, object?>
        {
            ["input"] = "{\"count\":99}",
            ["operation"] = ParamQuery,
            [ParamQuery] = "count"
        });

        Assert.True(result.Success, result.Error);
        var dict = Dict(result);
        Assert.Equal("Number", dict["type"]?.ToString());
        Assert.Equal(99L, Convert.ToInt64(dict["value"]));
    }

    [Fact]
    public async Task ShouldReturnDoubleValue_WhenQueryingFloat()
    {
        var result = await CallAsync(new Dictionary<string, object?>
        {
            ["input"] = "{\"ratio\":3.5}",
            ["operation"] = ParamQuery,
            [ParamQuery] = "ratio"
        });

        Assert.True(result.Success, result.Error);
        var dict = Dict(result);
        Assert.Equal(3.5, Convert.ToDouble(dict["value"]), 3);
    }

    [Fact]
    public async Task ShouldReturnBooleanValue_WhenQueryingTrue()
    {
        var result = await CallAsync(new Dictionary<string, object?>
        {
            ["input"] = "{\"flag\":true}",
            ["operation"] = ParamQuery,
            [ParamQuery] = "flag"
        });

        Assert.True(result.Success, result.Error);
        var dict = Dict(result);
        Assert.Equal("True", dict["type"]?.ToString());
        Assert.Equal(true, dict["value"]);
    }

    [Fact]
    public async Task ShouldReturnBooleanValue_WhenQueryingFalse()
    {
        var result = await CallAsync(new Dictionary<string, object?>
        {
            ["input"] = "{\"flag\":false}",
            ["operation"] = ParamQuery,
            [ParamQuery] = "flag"
        });

        Assert.True(result.Success, result.Error);
        var dict = Dict(result);
        Assert.Equal("False", dict["type"]?.ToString());
        Assert.Equal(false, dict["value"]);
    }

    [Fact]
    public async Task ShouldReturnNullSentinel_WhenQueryingNull()
    {
        var result = await CallAsync(new Dictionary<string, object?>
        {
            ["input"] = "{\"nothing\":null}",
            ["operation"] = ParamQuery,
            [ParamQuery] = "nothing"
        });

        Assert.True(result.Success, result.Error);
        var dict = Dict(result);
        Assert.Equal("Null", dict["type"]?.ToString());
        Assert.Equal("null", dict["value"]?.ToString());
    }

    [Fact]
    public async Task ShouldReturnRawObject_WhenQueryingNestedObject()
    {
        var result = await CallAsync(new Dictionary<string, object?>
        {
            ["input"] = "{\"obj\":{\"a\":1}}",
            ["operation"] = ParamQuery,
            [ParamQuery] = "obj"
        });

        Assert.True(result.Success, result.Error);
        var dict = Dict(result);
        Assert.Equal("Object", dict["type"]?.ToString());
        Assert.Contains("\"a\"", dict["value"]?.ToString());
    }

    [Fact]
    public async Task ShouldError_WhenArrayIndexOutOfRange()
    {
        var result = await CallAsync(new Dictionary<string, object?>
        {
            ["input"] = "{\"items\":[1,2]}",
            ["operation"] = ParamQuery,
            [ParamQuery] = "items[5]"
        });

        Assert.False(result.Success);
        Assert.Contains("out of range", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldError_WhenIndexingNonArray()
    {
        var result = await CallAsync(new Dictionary<string, object?>
        {
            ["input"] = "{\"value\":42}",
            ["operation"] = ParamQuery,
            [ParamQuery] = "value[0]"
        });

        Assert.False(result.Success);
        Assert.Contains("non-array", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldError_WhenAccessingPropertyOnNonObject()
    {
        var result = await CallAsync(new Dictionary<string, object?>
        {
            ["input"] = "{\"value\":42}",
            ["operation"] = ParamQuery,
            [ParamQuery] = "value.deeper"
        });

        Assert.False(result.Success);
        Assert.Contains("non-object", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldError_WhenQueryIsMissingForQueryOperation()
    {
        var result = await CallAsync(new Dictionary<string, object?>
        {
            ["input"] = "{\"a\":1}",
            ["operation"] = ParamQuery
        });

        Assert.False(result.Success);
        Assert.Contains("required", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldFormatArray_AndReportLength()
    {
        var result = await CallAsync(new Dictionary<string, object?>
        {
            ["input"] = "[1,2,3]",
            ["operation"] = "format"
        });

        Assert.True(result.Success, result.Error);
        var dict = Dict(result);
        var formatted = dict["formatted"]?.ToString() ?? "";
        Assert.Contains("\n", formatted);
        Assert.Equal(formatted.Length, Convert.ToInt32(dict["length"]));
    }

    [Fact]
    public async Task ShouldError_WhenInputIsWhitespaceOnly()
    {
        var result = await CallAsync(new Dictionary<string, object?>
        {
            ["input"] = "   ",
            ["operation"] = "parse"
        });

        Assert.False(result.Success);
        Assert.Contains("empty", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _tool.Dispose();
    }
}
