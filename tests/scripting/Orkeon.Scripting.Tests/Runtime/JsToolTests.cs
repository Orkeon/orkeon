using Jint;
using Orkeon.Scripting.Runtime;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;
using ParameterSchema = Orkeon.Domain.Tools.Protocol.ParameterSchema;

namespace Orkeon.Scripting.Tests.Runtime;

public sealed class JsToolTests
{
    private static Engine NewEngine() => new JsEngineFactory().Create();

    private static JsTool BuildTool(Engine engine, string jsCallback, bool explicitSchema = false)
    {
        var callback = engine.Evaluate(jsCallback);
        var schema = new ToolSchema("echo", "Echo tool", new Dictionary<string, ParameterSchema>());
        return new JsTool("echo", "Echo tool", schema, explicitSchema, engine, callback);
    }

    [Fact]
    public void Constructor_sets_properties()
    {
        var engine = NewEngine();
        var tool = BuildTool(engine, "(input) => input", explicitSchema: true);

        Assert.Equal("echo", tool.Name);
        Assert.Equal("Echo tool", tool.Description);
        Assert.True(tool.HasExplicitSchema);
        Assert.NotNull(tool.Schema);
    }

    [Fact]
    public async Task CallAsync_synchronous_callback_returns_success()
    {
        var engine = NewEngine();
        var tool = BuildTool(engine, "(input) => ({ doubled: input.n * 2 })");

        var response = await tool.CallAsync(new ToolCallRequest("echo",
            new Dictionary<string, object?> { ["n"] = 21 }), TestContext.Current.CancellationToken);

        Assert.True(response.Success);
        var dict = (IDictionary<string, object?>)response.Result!;
        Assert.Equal(42d, dict["doubled"]);
    }

    [Fact]
    public async Task CallAsync_promise_callback_is_unwrapped()
    {
        var engine = NewEngine();
        var tool = BuildTool(engine, "async (input) => 'async:' + input.x");

        var response = await tool.CallAsync(new ToolCallRequest("echo",
            new Dictionary<string, object?> { ["x"] = "v" }), TestContext.Current.CancellationToken);

        Assert.True(response.Success);
        Assert.Equal("async:v", response.Result);
    }

    [Fact]
    public async Task CallAsync_callback_throwing_returns_failure_with_message()
    {
        var engine = NewEngine();
        var tool = BuildTool(engine, "() => { throw new Error('kaboom'); }");

        var response = await tool.CallAsync(new ToolCallRequest("echo",
            new Dictionary<string, object?>()), TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Contains("kaboom", response.Error);
    }

    [Fact]
    public async Task CallAsync_null_request_throws()
    {
        var engine = NewEngine();
        var tool = BuildTool(engine, "(i) => i");

        await Assert.ThrowsAsync<ArgumentNullException>(() => tool.CallAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_with_valid_json_input_succeeds()
    {
        var engine = NewEngine();
        var tool = BuildTool(engine, "(input) => 'got:' + input.name");

        var result = await tool.ExecuteAsync("{\"name\":\"bob\"}", TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("got:bob", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_with_empty_input_uses_empty_parameters()
    {
        var engine = NewEngine();
        var tool = BuildTool(engine, "(input) => 'keys:' + Object.keys(input).length");

        var result = await tool.ExecuteAsync("", TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("keys:0", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_with_invalid_json_returns_error()
    {
        var engine = NewEngine();
        var tool = BuildTool(engine, "(i) => i");

        var result = await tool.ExecuteAsync("{not json", TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Invalid JSON", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_callback_failure_returns_error()
    {
        var engine = NewEngine();
        var tool = BuildTool(engine, "() => { throw new Error('nope'); }");

        var result = await tool.ExecuteAsync("{}", TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("nope", result.Error);
    }

    [Fact]
    public void ValidateInput_empty_is_valid()
    {
        var engine = NewEngine();
        var tool = BuildTool(engine, "(i) => i");

        Assert.True(tool.ValidateInput(""));
    }

    [Fact]
    public void ValidateInput_valid_json_is_valid()
    {
        var engine = NewEngine();
        var tool = BuildTool(engine, "(i) => i");

        Assert.True(tool.ValidateInput("{\"a\":1}"));
    }

    [Fact]
    public void ValidateInput_invalid_json_is_invalid()
    {
        var engine = NewEngine();
        var tool = BuildTool(engine, "(i) => i");

        Assert.False(tool.ValidateInput("{bad"));
    }
}
