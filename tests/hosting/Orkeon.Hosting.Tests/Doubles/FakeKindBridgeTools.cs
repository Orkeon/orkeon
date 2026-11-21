using System.Threading.Channels;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Execution;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.MCP;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>A tool of the caller's own run (#445): its contract says it stays with the caller.</summary>
[ToolContract("caller_tool", StaysWithCaller = true)]
public sealed class FakeCallerBridgeTool : FakeBridgeTool
{
    /// <summary>Builds the tool under <paramref name="name"/>.</summary>
    public FakeCallerBridgeTool(string name = "caller_tool") : base(name)
    {
    }
}

/// <summary>
/// A tool whose class declares no contract (#445), as a plugin's or a script's tool: it answers as a
/// <see cref="FakeBridgeTool"/> it keeps, without being a wrapper of it.
/// </summary>
public sealed class FakeUnmarkedBridgeTool : IBaseTool
{
    private readonly FakeBridgeTool _tool;

    /// <summary>Builds the tool under <paramref name="name"/>.</summary>
    public FakeUnmarkedBridgeTool(string name) => _tool = new FakeBridgeTool(name);

    /// <inheritdoc />
    public string Name => _tool.Name;

    /// <inheritdoc />
    public string Description => _tool.Description;

    /// <inheritdoc />
    public ToolSchema Schema => _tool.Schema;

    /// <inheritdoc />
    public ToolAccess Access => _tool.Access;

    /// <inheritdoc />
    public ToolAccess AccessFor(IReadOnlyDictionary<string, object?> arguments) => _tool.AccessFor(arguments);

    /// <inheritdoc />
    public Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default) =>
        _tool.CallAsync(request, cancellationToken);

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default) =>
        _tool.ExecuteAsync(input, cancellationToken);

    /// <inheritdoc />
    public bool ValidateInput(string input) => true;
}

/// <summary>A transport of an MCP server that never answers: enough to build an <see cref="McpToolAdapter"/>.</summary>
public sealed class StubMcpTransport : IMcpTransport
{
    private readonly Channel<JsonRpcNotification> _notifications = Channel.CreateUnbounded<JsonRpcNotification>();

    /// <inheritdoc />
    public bool IsConnected => false;

    /// <inheritdoc />
    public ChannelReader<JsonRpcNotification> Notifications => _notifications.Reader;

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException("no MCP server behind this transport");

    /// <inheritdoc />
    public Task SendNotificationAsync(JsonRpcNotification notification, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>An MCP tool named <paramref name="name"/> over a transport of this kind.</summary>
    /// <param name="name">The tool's name.</param>
    /// <returns>The adapter.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "A transport that holds nothing, kept by the adapter for the test's life.")]
    public static McpToolAdapter Tool(string name) => new(new McpToolDefinition { Name = name }, new McpClient(new StubMcpTransport()));
}
