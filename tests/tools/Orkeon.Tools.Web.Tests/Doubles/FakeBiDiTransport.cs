using System.Text.Json.Nodes;
using System.Threading.Channels;
using Orkeon.Tools.Web.Firefox.BiDi;

namespace Orkeon.Tools.Web.Tests.Doubles;

/// <summary>
/// A scripted browser end of a WebDriver BiDi channel. Every command sent is recorded in
/// <see cref="Commands"/>; with <see cref="AutoAnswer"/> it is answered at once by the handler of its
/// method (an empty result when none is set). A handler that throws <see cref="FakeBiDiError"/> answers
/// an error. <see cref="Push"/> and <see cref="Close"/> drive the channel by hand.
/// </summary>
internal sealed class FakeBiDiTransport : IBiDiTransport
{
    private readonly Channel<string?> _incoming = Channel.CreateUnbounded<string?>();
    private readonly List<(string Method, JsonObject Params)> _commands = [];
    private readonly Lock _sync = new();

    public Dictionary<string, Func<JsonObject, JsonObject>> Handlers { get; } = new(StringComparer.Ordinal);

    public bool AutoAnswer { get; set; } = true;

    /// <summary>Methods recorded and never answered, as a browser that went deaf would.</summary>
    public HashSet<string> Unanswered { get; } = new(StringComparer.Ordinal);

    public Exception? SendFailure { get; set; }

    public bool Disposed { get; private set; }

    public IReadOnlyList<(string Method, JsonObject Params)> Commands
    {
        get
        {
            lock (_sync) return [.. _commands];
        }
    }

    public IReadOnlyList<string> Methods => [.. Commands.Select(c => c.Method)];

    public IReadOnlyList<JsonObject> ParamsOf(string method) => [.. Commands.Where(c => c.Method == method).Select(c => c.Params)];

    public Task SendAsync(string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SendFailure is not null) throw SendFailure;

        var envelope = JsonNode.Parse(message)!.AsObject();
        var method = envelope["method"]!.GetValue<string>();
        var parameters = envelope["params"]!.DeepClone().AsObject();
        lock (_sync) _commands.Add((method, parameters));
        if (!AutoAnswer || Unanswered.Contains(method)) return Task.CompletedTask;

        var id = envelope["id"]!.GetValue<long>();
        try
        {
            var result = Handlers.TryGetValue(method, out var handler) ? handler(parameters) : [];
            Push(new JsonObject { ["type"] = "success", ["id"] = id, ["result"] = result }.ToJsonString());
        }
        catch (FakeBiDiError error)
        {
            Push(new JsonObject { ["type"] = "error", ["id"] = id, ["error"] = error.Code, ["message"] = error.Message }.ToJsonString());
        }

        return Task.CompletedTask;
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _incoming.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    /// <summary>Delivers <paramref name="message"/> to the connection as the browser would.</summary>
    public void Push(string message) => _incoming.Writer.TryWrite(message);

    /// <summary>The browser closes the channel.</summary>
    public void Close() => _incoming.Writer.TryComplete();

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        Close();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Thrown by a <see cref="FakeBiDiTransport"/> handler to answer a WebDriver error.</summary>
internal sealed class FakeBiDiError(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
