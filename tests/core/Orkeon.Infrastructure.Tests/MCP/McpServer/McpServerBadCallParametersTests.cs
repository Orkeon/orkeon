using System.Text.Json;
using Orkeon.Application.Services.Security;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>
/// #413: what <c>tools/call</c> and <c>prompts/get</c> refuse. Parameters that fail the request's schema are a
/// protocol error, <c>-32602</c> - never <c>-32603</c>, never a call that runs as if nothing had been given. A
/// number no double holds is an input validation error: a tool result with <c>isError: true</c> that names the
/// argument, never the serializer's own message.
/// </summary>
public sealed class McpServerBadCallParametersTests
{
    private readonly InlineMockBaseTool _tool = new("list_mounts", "Lists the mounts");
    private readonly FakeMcpServerExtension _extension = new();

    private McpServer Server()
    {
        var registry = new MockToolRegistry();
        registry.AddTool(_tool.Name, _tool);
        return new McpServer(registry, ToolInvocationPipeline.Unguarded, new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService(), new McpServerOptions(), null, _extension);
    }

    private Task<JsonRpcResponse?> SendAsync(string method, string paramsJson) =>
        Server().ProcessRequestAsync(
            JsonSerializer.Deserialize<JsonRpcRequest>($$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}","params":{{paramsJson}}}""")!,
            TestContext.Current.CancellationToken);

    private static void AssertInvalidParams(JsonRpcResponse? response, string expectedInMessage)
    {
        Assert.NotNull(response);
        Assert.NotNull(response.Error);
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, response.Error.Code);
        Assert.Contains(expectedInMessage, response.Error.Message, StringComparison.Ordinal);
        Assert.Null(response.Result);
    }

    // --- 1. arguments that are not an object ---

    [Theory]
    [InlineData("""["a"]""")]
    [InlineData("\"{}\"")]
    [InlineData("5")]
    [InlineData("true")]
    public async Task ShouldRefuseTheCall_WhenTheArgumentsOfARegistryToolAreNotAnObject(string arguments)
    {
        var response = await SendAsync("tools/call", $$"""{"name":"list_mounts","arguments":{{arguments}}}""");

        AssertInvalidParams(response, "arguments must be an object");
        Assert.Equal(0, _tool.CallAsyncCallCount);
    }

    [Fact]
    public async Task ShouldRefuseTheCall_WhenTheArgumentsOfACrewToolAreNotAnObject()
    {
        var response = await SendAsync("tools/call", """{"name":"run_crew_demo","arguments":["a"]}""");

        AssertInvalidParams(response, "arguments must be an object");
        Assert.Null(_extension.LastArguments);
    }

    [Fact]
    public async Task ShouldRefuseThePrompt_WhenItsArgumentsAreNotAnObject()
    {
        var response = await SendAsync("prompts/get", """{"name":"greet","arguments":["a"]}""");

        AssertInvalidParams(response, "arguments must be an object");
    }

    [Theory]
    [InlineData("""{"name":"list_mounts"}""")]
    [InlineData("""{"name":"list_mounts","arguments":null}""")]
    [InlineData("""{"name":"list_mounts","arguments":{}}""")]
    public async Task ShouldRunTheTool_WhenTheArgumentsAreAbsentNullOrEmpty(string paramsJson)
    {
        var response = await SendAsync("tools/call", paramsJson);

        Assert.Null(response!.Error);
        Assert.Equal(1, _tool.CallAsyncCallCount);
    }

    [Fact]
    public async Task ShouldServeThePrompt_WhenItsArgumentsAreNull()
    {
        var response = await SendAsync("prompts/get", """{"name":"greet","arguments":null}""");

        Assert.Null(response!.Error);
    }

    // --- 2. a number beyond the range of a double ---

    [Theory]
    [InlineData("""{"x":1e400}""", "x")]
    [InlineData("""{"x":-1e400}""", "x")]
    [InlineData("""{"x":[1,1e400]}""", "x[1]")]
    [InlineData("""{"x":{"y":-1e400}}""", "x.y")]
    [InlineData("""{"a":1,"x":[{"y":[1e400]}]}""", "x[0].y[0]")]
    // A name with a dot is not a nesting: it is quoted.
    [InlineData("""{"a.b":[0,{"c":1e400}]}""", "[\"a.b\"][1].c")]
    [InlineData("""{"a":{"b.c":1e400}}""", "a[\"b.c\"]")]
    public async Task ShouldFailTheCallNamingTheArgument_WhenANumberIsNotFinite(string arguments, string expectedPath)
    {
        var response = await SendAsync("tools/call", $$"""{"name":"list_mounts","arguments":{{arguments}}}""");

        Assert.Null(response!.Error);
        var result = response.Result!.Value;
        Assert.True(result.GetProperty("isError").GetBoolean());
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        Assert.Equal($"Argument '{expectedPath}' is not a finite number.", text);
        Assert.Equal(0, _tool.CallAsyncCallCount);
    }

    [Fact]
    public async Task ShouldRunTheTool_WhenEveryNumberIsFinite()
    {
        var response = await SendAsync("tools/call", """{"name":"list_mounts","arguments":{"x":1e300,"y":[9223372036854775807,-0.5],"z":{"w":18446744073709551615}}}""");

        Assert.Null(response!.Error);
        Assert.False(response.Result!.Value.TryGetProperty("isError", out var isError) && isError.GetBoolean());
        Assert.Equal(1, _tool.CallAsyncCallCount);
    }

    // --- 3. params that is not an object, a name that is not a string ---

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"name":5}""")]
    [InlineData("""{"name":["list_mounts"]}""")]
    [InlineData("""{"name":{"a":1},"arguments":{}}""")]
    public async Task ShouldAnswerInvalidParams_WhenTheParamsAreNoObjectWithAStringName(string paramsJson)
    {
        var response = await SendAsync("tools/call", paramsJson);

        AssertInvalidParams(response, "params must be an object with a string name");
        Assert.Equal(0, _tool.CallAsyncCallCount);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"name":5}""")]
    public async Task ShouldRefuseThePromptInTheSameWords_WhenItsParamsAreNoObjectWithAStringName(string paramsJson)
    {
        var response = await SendAsync("prompts/get", paramsJson);

        AssertInvalidParams(response, "params must be an object with a string name");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"name":null}""")]
    [InlineData("""{"name":""}""")]
    public async Task ShouldAnswerInvalidParams_WhenTheToolNameIsMissing(string paramsJson)
    {
        var response = await SendAsync("tools/call", paramsJson);

        AssertInvalidParams(response, "Missing tool name");
    }
}
