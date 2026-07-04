using System.Text;
using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Abstractions.Tests.Console;

/// <summary>
/// Synchronous scripted console adapter for unit-testing services that call
/// <see cref="IConsoleAdapter"/> in a blocking loop. Lines and keys are pre-queued;
/// reads dequeue them in order. Output is captured for assertions.
/// </summary>
public sealed class ScriptedConsoleAdapter : IConsoleAdapter
{
    private readonly Queue<string?> _lines = new();
    private readonly Queue<ConsoleKeyInfo> _keys = new();
    private readonly StringBuilder _output = new();

    public int ClearCount { get; private set; }
    public int ReadKeyCount { get; private set; }
    public bool? LastReadKeyIntercept { get; private set; }

    public ScriptedConsoleAdapter QueueLine(string? line)
    {
        _lines.Enqueue(line);
        return this;
    }

    public ScriptedConsoleAdapter QueueLines(params string?[] lines)
    {
        foreach (var line in lines)
            _lines.Enqueue(line);
        return this;
    }

    public ScriptedConsoleAdapter QueueKey(ConsoleKey key, char keyChar = '\0')
    {
        _keys.Enqueue(new ConsoleKeyInfo(keyChar, key, false, false, false));
        return this;
    }

    public void Write(string text) => _output.Append(text);

    public void WriteLine(string text) => _output.AppendLine(text);

    public string? ReadLine()
    {
        if (_lines.Count == 0)
            throw new InvalidOperationException("ScriptedConsoleAdapter: no more queued lines.");
        return _lines.Dequeue();
    }

    public ConsoleKeyInfo ReadKey(bool intercept = false)
    {
        ReadKeyCount++;
        LastReadKeyIntercept = intercept;
        if (_keys.Count == 0)
            throw new InvalidOperationException("ScriptedConsoleAdapter: no more queued keys.");
        return _keys.Dequeue();
    }

    public void Clear()
    {
        ClearCount++;
        _output.Clear();
    }

    public string Output => _output.ToString();
}
