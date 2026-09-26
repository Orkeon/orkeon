using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Orkeon.Tools.Email.Tests.Doubles;

/// <summary>
/// Base of the scripted mail servers: listens on 127.0.0.1 on an ephemeral port, serves every
/// connection on its own task, records every client line in a transcript and every command it
/// does not implement, and keeps any exception its own code throws so a test can surface a bug
/// of the double instead of a mysterious client timeout.
/// </summary>
internal abstract class FakeProtocolServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _connections = [];
    private readonly List<string> _transcript = [];
    private readonly List<string> _unknown = [];
    private readonly List<Exception> _errors = [];
    private Task? _acceptLoop;
    private int _connectionCount;

    /// <summary>Guards the model of the derived server and the recorded lists.</summary>
    protected Lock Gate { get; } = new();

    /// <summary>The port the server listens on.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Every line the clients sent, literals summarized, in order.</summary>
    public IReadOnlyList<string> Transcript
    {
        get
        {
            lock (Gate)
                return [.. _transcript];
        }
    }

    /// <summary>The commands the server answered as unknown.</summary>
    public IReadOnlyList<string> UnknownCommands
    {
        get
        {
            lock (Gate)
                return [.. _unknown];
        }
    }

    /// <summary>Exceptions thrown by the server's own code (bugs of the double).</summary>
    public IReadOnlyList<Exception> Errors
    {
        get
        {
            lock (Gate)
                return [.. _errors];
        }
    }

    /// <summary>How many connections were accepted.</summary>
    public int ConnectionCount => Volatile.Read(ref _connectionCount);

    /// <summary>Starts accepting; derived constructors call it last.</summary>
    protected void Start()
    {
        _listener.Start();
        _acceptLoop = AcceptLoopAsync();
    }

    /// <summary>Fails the test with the transcript when the server met an unknown command or broke.</summary>
    public void AssertHealthy()
    {
        var unknown = UnknownCommands;
        var errors = Errors;
        if (unknown.Count == 0 && errors.Count == 0)
            return;

        Assert.Fail(
            $"{GetType().Name} was not healthy.\nUnknown commands:\n  {string.Join("\n  ", unknown)}\n" +
            $"Errors:\n  {string.Join("\n  ", errors.Select(e => e.ToString()))}\nTranscript:\n  {string.Join("\n  ", Transcript)}");
    }

    /// <summary>Serves one connection until the client leaves.</summary>
    protected abstract Task ServeAsync(MailConnection connection, CancellationToken cancellationToken);

    /// <summary>Records a client line.</summary>
    protected void Record(string line)
    {
        lock (Gate)
            _transcript.Add(line);
    }

    /// <summary>Records a command the server does not implement.</summary>
    protected void RecordUnknown(string line)
    {
        lock (Gate)
            _unknown.Add(line);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        if (_acceptLoop is not null)
            await _acceptLoop;

        Task[] connections;
        lock (Gate)
            connections = [.. _connections];
        await Task.WhenAll(connections);

        _listener.Dispose();
        _stop.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            Interlocked.Increment(ref _connectionCount);
            var connection = HandleAsync(client);
            lock (Gate)
                _connections.Add(connection);
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            await using var connection = new MailConnection(client.GetStream());
            try
            {
                await ServeAsync(connection, _stop.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // The client went away, or the server is shutting down.
            }
            catch (Exception ex)
            {
                lock (Gate)
                    _errors.Add(ex);
            }
        }
    }
}

/// <summary>Buffered line and literal I/O over one client connection.</summary>
internal sealed class MailConnection : IAsyncDisposable
{
    private readonly NetworkStream _stream;
    private readonly byte[] _buffer = new byte[16 * 1024];
    private int _start;
    private int _end;

    /// <summary>Wraps <paramref name="stream"/>.</summary>
    public MailConnection(NetworkStream stream) => _stream = stream;

    /// <summary>Reads one line without its CRLF, as raw bytes; null at end of stream.</summary>
    public async Task<byte[]?> ReadLineBytesAsync(CancellationToken cancellationToken)
    {
        var line = new List<byte>(128);
        while (true)
        {
            if (_start == _end && !await FillAsync(cancellationToken))
                return line.Count == 0 ? null : [.. line];

            var newline = Array.IndexOf(_buffer, (byte)'\n', _start, _end - _start);
            if (newline < 0)
            {
                line.AddRange(_buffer.AsSpan(_start, _end - _start));
                _start = _end;
                continue;
            }

            line.AddRange(_buffer.AsSpan(_start, newline - _start));
            _start = newline + 1;
            if (line.Count > 0 && line[^1] == '\r')
                line.RemoveAt(line.Count - 1);
            return [.. line];
        }
    }

    /// <summary>Reads one line as UTF-8 text; null at end of stream.</summary>
    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken) =>
        await ReadLineBytesAsync(cancellationToken) is { } bytes ? Encoding.UTF8.GetString(bytes) : null;

    /// <summary>Reads exactly <paramref name="count"/> bytes.</summary>
    public async Task<byte[]> ReadExactAsync(int count, CancellationToken cancellationToken)
    {
        var result = new byte[count];
        var filled = 0;
        while (filled < count)
        {
            if (_start == _end && !await FillAsync(cancellationToken))
                throw new IOException("The client closed the connection inside a literal.");

            var take = Math.Min(count - filled, _end - _start);
            Array.Copy(_buffer, _start, result, filled, take);
            _start += take;
            filled += take;
        }

        return result;
    }

    /// <summary>Writes <paramref name="line"/> followed by CRLF.</summary>
    public Task WriteLineAsync(string line, CancellationToken cancellationToken) =>
        WriteAsync(Encoding.UTF8.GetBytes(line + "\r\n"), cancellationToken);

    /// <summary>Writes raw bytes.</summary>
    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken)
    {
        await _stream.WriteAsync(data, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _stream.DisposeAsync();

    private async Task<bool> FillAsync(CancellationToken cancellationToken)
    {
        _start = 0;
        _end = await _stream.ReadAsync(_buffer.AsMemory(), cancellationToken);
        return _end > 0;
    }
}
