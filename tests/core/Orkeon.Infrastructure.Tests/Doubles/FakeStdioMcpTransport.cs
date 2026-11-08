using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Orkeon.Infrastructure.MCP;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// An <see cref="IMcpTransport"/> that speaks to a <see cref="McpServer"/> through its stdio loop
/// (<see cref="McpServer.RunStdioAsync"/>) in memory: a request is written as one line on the
/// server's stdin, and the answer is the line the server writes back with the same id. Every
/// frame without an id is a notification, delivered on <see cref="Notifications"/>.
/// </summary>
public sealed class FakeStdioMcpTransport : IMcpTransport
{
    private readonly McpServer _server;
    private readonly FakeLineReader _stdin = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonRpcResponse>> _pending = new();
    private readonly Channel<JsonRpcNotification> _notifications = Channel.CreateUnbounded<JsonRpcNotification>();
    private readonly CancellationTokenSource _stop = new();
    private readonly AnswerWriter _stdout;
    private Task _run = Task.CompletedTask;

    /// <summary>A transport over <paramref name="server"/>.</summary>
    public FakeStdioMcpTransport(McpServer server)
    {
        _server = server;
        _stdout = new AnswerWriter(this);
    }

    /// <inheritdoc />
    public bool IsConnected { get; private set; }

    /// <inheritdoc />
    public ChannelReader<JsonRpcNotification> Notifications => _notifications.Reader;

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken ct = default)
    {
        _run = _server.RunStdioAsync(_stdin, _stdout, _stop.Token);
        IsConnected = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken ct = default)
    {
        var key = request.Id!.Value.GetRawText();
        var answer = new TaskCompletionSource<JsonRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[key] = answer;
        _stdin.Send(JsonSerializer.Serialize(request));
        return await answer.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task SendNotificationAsync(JsonRpcNotification notification, CancellationToken ct = default)
    {
        _stdin.Send(JsonSerializer.Serialize(notification));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        IsConnected = false;
        _stdin.Complete();
        try
        {
            await _run.WaitAsync(TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The server ended on the cancellation.
        }

        _stop.Dispose();
        _stdin.Dispose();
        _stdout.Dispose();
        _notifications.Writer.TryComplete();
    }

    private void Deliver(string line)
    {
        var root = JsonElement.Parse(line);
        if (root.TryGetProperty("id", out var id) && _pending.TryRemove(id.GetRawText(), out var answer))
            answer.TrySetResult(JsonSerializer.Deserialize<JsonRpcResponse>(line)!);
        else
            _notifications.Writer.TryWrite(JsonSerializer.Deserialize<JsonRpcNotification>(line)!);
    }

    private sealed class AnswerWriter(FakeStdioMcpTransport owner) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            owner.Deliver(buffer.ToString());
            return Task.CompletedTask;
        }

        public override Task FlushAsync() => Task.CompletedTask;
    }
}
