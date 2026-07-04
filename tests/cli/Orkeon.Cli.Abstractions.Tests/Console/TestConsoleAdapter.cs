using System.Text;
using System.Threading.Channels;
using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Abstractions.Tests.Console;

/// <summary>
/// Test adapter that replaces System.Console with async channels.
/// The app writes output and blocks on input — the test driver feeds input
/// and reads output, creating a conversational interaction loop.
/// </summary>
public sealed class TestConsoleAdapter : IConsoleAdapter, IDisposable
{
    private readonly Channel<string> _lineInput = Channel.CreateUnbounded<string>();
    private readonly Channel<ConsoleKeyInfo> _keyInput = Channel.CreateUnbounded<ConsoleKeyInfo>();
    private readonly StringBuilder _output = new();
    private readonly SemaphoreSlim _blocked = new(0);
    private readonly object _lock = new();
    private int _clearCount;

    public int ClearCount
    {
        get { lock (_lock) return _clearCount; }
    }

    // ─── App-side (called by the services) ───

    public void Write(string text)
    {
        lock (_lock) _output.Append(text);
    }

    public void WriteLine(string text)
    {
        lock (_lock) _output.AppendLine(text);
    }

    public string? ReadLine()
    {
        _blocked.Release();
        try
        {
            return _lineInput.Reader.ReadAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public ConsoleKeyInfo ReadKey(bool intercept = false)
    {
        _blocked.Release();
        try
        {
            return _keyInput.Reader.ReadAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (ChannelClosedException)
        {
            return default;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _output.Clear();
            _clearCount++;
        }
    }

    // ─── Driver-side (called by the test) ───

    /// <summary>Wait until the app is blocked on ReadLine or ReadKey.</summary>
    public async Task WaitUntilBlockedAsync(TimeSpan? timeout = null)
    {
        if (!await _blocked.WaitAsync(timeout ?? TimeSpan.FromSeconds(10)))
            throw new TimeoutException("App did not block on input within timeout.");
    }

    /// <summary>Send a line of text (unblocks a pending ReadLine).</summary>
    public async Task SendLineAsync(string text)
    {
        await _lineInput.Writer.WriteAsync(text);
    }

    /// <summary>Send a key press (unblocks a pending ReadKey).</summary>
    public async Task SendKeyAsync(ConsoleKey key, char keyChar = '\0')
    {
        var info = new ConsoleKeyInfo(keyChar, key, false, false, false);
        await _keyInput.Writer.WriteAsync(info);
    }

    /// <summary>Get all output accumulated so far.</summary>
    public string GetOutput()
    {
        lock (_lock) return _output.ToString();
    }

    /// <summary>Get all output and clear the buffer (without incrementing ClearCount).</summary>
    public string TakeOutput()
    {
        lock (_lock)
        {
            var text = _output.ToString();
            _output.Clear();
            return text;
        }
    }

    /// <summary>Wait until the output contains the expected text.</summary>
    public async Task<string> WaitForOutputAsync(string containsText, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            var output = GetOutput();
            if (output.Contains(containsText, StringComparison.Ordinal))
                return output;
            await Task.Delay(25);
        }
        throw new TimeoutException(
            $"Timed out waiting for output containing '{containsText}'.\nActual output:\n{GetOutput()}");
    }

    /// <summary>Signal that the app should terminate (complete the input channels).</summary>
    public void Complete()
    {
        _lineInput.Writer.TryComplete();
        _keyInput.Writer.TryComplete();
    }

    public void Dispose() => _blocked.Dispose();
}
