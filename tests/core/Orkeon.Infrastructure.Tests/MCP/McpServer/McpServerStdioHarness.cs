using System.Text.Json;
using Orkeon.Application.Services.Security;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>
/// A server run over a fed reader and a recording writer, as <c>orkeon mcp serve</c> runs it. Every
/// wait is a hang guard: far above what a loaded machine needs, never a bound the server is held
/// to, and the verdict comes from the assertion that follows.
/// </summary>
internal sealed class McpServerStdioHarness : IAsyncDisposable
{
    private static readonly TimeSpan HangGuard = TimeSpan.FromMinutes(2);
    private readonly CancellationTokenSource _stop = new();

    public McpServerStdioHarness(McpServerOptions options, params MockGateTool[] tools)
    {
        var registry = new MockToolRegistry();
        foreach (var tool in tools)
            registry.AddTool(tool.Name, tool);
        Server = new McpServer(registry, ToolInvocationPipeline.Unguarded, new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService(), options);
        Run = Server.RunStdioAsync(Reader, Writer, _stop.Token);
    }

    public McpServer Server { get; }
    public FakeLineReader Reader { get; } = new();
    public FakeLineWriter Writer { get; } = new();
    public Task Run { get; }

    public static string Call(object id, string tool) =>
        JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method = "tools/call", @params = new { name = tool, arguments = new { } } });

    public static string Ping(object id) =>
        JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method = "ping" });

    public static string Cancel(object requestId) =>
        JsonSerializer.Serialize(new { jsonrpc = "2.0", method = "notifications/cancelled", @params = new { requestId, reason = "test" } });

    /// <summary>The ids of the answers written so far, in writing order; every line must be a JSON-RPC object.</summary>
    public List<string> AnswerIds() =>
        Writer.Lines.Select(line => JsonElement.Parse(line).GetProperty("id").ToString()).ToList();

    /// <summary>Waits for <paramref name="condition"/>, then asserts it.</summary>
    public Task EventuallyAsync(Func<bool> condition, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string? expression = null) =>
        WaitAsync(condition, () => string.Join(" | ", Writer.Lines), expression);

    public static async Task WaitAsync(
        Func<bool> condition,
        Func<string> context,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        try
        {
            await Polling.WaitUntilAsync(condition, HangGuard);
        }
        catch (TimeoutException)
        {
            // Reported by the assertion below, with the condition.
        }

        Assert.True(condition(), $"The condition never held: {expression}; written so far: {context()}");
    }

    /// <summary>Closes stdin and waits for the server to finish.</summary>
    public async Task FinishAsync()
    {
        Reader.Complete();
        await EventuallyAsync(() => Run.IsCompleted, "the server ended after stdin closed");
        await Run;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        try
        {
            await Run.WaitAsync(HangGuard);
        }
        catch (OperationCanceledException)
        {
            // The server ended on the cancellation.
        }

        _stop.Dispose();
    }
}
