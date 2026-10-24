using Orkeon.ConsoleApp.Services;
using Orkeon.Domain.Tools;
using Orkeon.Scripting;
using System.Text.Json;
using Orkeon.Tests.Shared.FileSystem;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;
using ParameterSchema = Orkeon.Domain.Tools.Protocol.ParameterSchema;

namespace Orkeon.ConsoleApp.Tests.Services;

public sealed class ScriptHostStartScriptRunnerTests
{
    private static async Task<StartScriptOutput> RunAsync(string source)
    {
        const string path = "/workspace/start.ork.ts";
        var fs = new FakeFileSystemService().AddMount("/workspace").AddFile(path, source);
        var runner = new ScriptHostStartScriptRunner(new ScriptHost(fs, new JsEngineFactory()), fs, Array.Empty<IBaseTool>());
        return await runner.RunAsync(path, TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("BR", "RT-05")]
    public async Task ShouldStoreWhatTheScriptPrinted_WhenOrkTsScriptLogs()
    {
        var output = await RunAsync("console.log(\"open work: X\");");

        Assert.Contains("open work: X", output.Stdout);
    }

    [Fact]
    [Trait("BR", "RT-05")]
    public async Task ShouldNotStoreTypeName_WhenScriptReturnsAnObject()
    {
        var output = await RunAsync("({ open: 3 });");

        Assert.DoesNotContain("ExpandoObject", output.Stdout);
    }

    private sealed class ShellStub(string name, ToolResult result) : IBaseTool
    {
        public string Name { get; } = name;
        public string Description => "stub";
        public ToolSchema Schema { get; } = new(name, "stub", new Dictionary<string, ParameterSchema>());
        public List<string> Inputs { get; } = new();

        public Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ToolCallResponse(true, null, null));

        public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        {
            Inputs.Add(input);
            return Task.FromResult(result);
        }

        public bool ValidateInput(string input) => true;
    }

    private static async Task<StartScriptOutput> RunShAsync(params IBaseTool[] tools)
    {
        const string path = "/workspace/start.sh";
        var fs = new FakeFileSystemService().AddMount("/workspace").AddFile(path, "echo hi");
        var runner = new ScriptHostStartScriptRunner(new ScriptHost(fs, new JsEngineFactory()), fs, tools);
        return await runner.RunAsync(path, TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("BR", "R11")]
    public async Task ShouldRunShScript_WhenStartScriptIsNotOrkTs()
    {
        var shell = new ShellStub("shell_command", ToolResult.CreateSuccess("{\"stdout\":\"hello\"}"));

        var output = await RunShAsync(shell);

        var input = Assert.Single(shell.Inputs);
        using var doc = JsonDocument.Parse(input);
        var command = doc.RootElement.EnumerateObject().Single(p => string.Equals(p.Name, "command", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("sh /workspace/start.sh", command.Value.GetString());
        Assert.Equal("hello", output.Stdout);
    }

    [Fact]
    [Trait("BR", "R11")]
    public async Task ShouldReturnPlainOutput_WhenShellOutputIsNotJson()
    {
        var shell = new ShellStub("shell_command", ToolResult.CreateSuccess("plain text"));

        var output = await RunShAsync(shell);

        Assert.Equal("plain text", output.Stdout);
    }

    [Fact]
    [Trait("BR", "R11")]
    public async Task ShouldThrow_WhenShellToolFails()
    {
        var shell = new ShellStub("shell_command", ToolResult.CreateError("boom"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RunShAsync(shell));

        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    [Trait("BR", "R11")]
    public async Task ShouldThrow_WhenShellToolIsNotRegistered()
    {
        var other = new ShellStub("other_tool", ToolResult.CreateSuccess("x"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RunShAsync(other));

        Assert.Equal("The shell_command tool is not registered.", ex.Message);
    }
}
