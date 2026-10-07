using System.Text;
using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// The console of a verb a program drives: standard output read line by line while the verb
/// still runs, and a standard input the test writes to and closes. <see cref="TestConsole"/>
/// plays a verb that has all its input before it starts; a sign-in answers what the verb said
/// first. <see cref="Console"/> is process-global: callers hold <see cref="CliCollection"/>.
/// </summary>
internal sealed class ConversationConsole : IDisposable
{
    private readonly TextWriter _originalOut;
    private readonly TextWriter _originalError;
    private readonly TextReader _originalIn;
    private readonly LineWriter _out = new();
    private readonly StringWriter _error = new();

    public ConversationConsole()
    {
        _originalOut = Console.Out;
        _originalError = Console.Error;
        _originalIn = Console.In;
        Console.SetOut(_out);
        Console.SetError(_error);
        Console.SetIn(Input);
    }

    /// <summary>The verb's standard input.</summary>
    public ScriptedStandardInput Input { get; } = new();

    /// <summary>Everything written to standard output so far.</summary>
    public string Stdout => _out.Text;

    /// <summary>Everything written to standard error so far.</summary>
    public string Stderr
    {
        get
        {
            lock (_error)
                return _error.ToString();
        }
    }

    /// <summary>The standard-output line at <paramref name="index"/>, once the verb has written it.</summary>
    public Task<string> LineAsync(int index) => _out.LineAsync(index);

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
        Console.SetIn(_originalIn);
        // Ends a reader thread still parked on the input.
        Input.Dispose();
        _out.Dispose();
        _error.Dispose();
    }

    /// <summary>Standard output as complete lines, each one awaitable by its index.</summary>
    private sealed class LineWriter : TextWriter
    {
        private readonly Lock _gate = new();
        private readonly StringBuilder _text = new();
        private readonly StringBuilder _pending = new();
        private readonly List<string> _lines = [];
        private readonly Dictionary<int, TaskCompletionSource<string>> _waiters = [];

        public override Encoding Encoding => Encoding.UTF8;

        public string Text
        {
            get
            {
                lock (_gate)
                    return _text.ToString();
            }
        }

        public Task<string> LineAsync(int index)
        {
            lock (_gate)
            {
                if (index < _lines.Count)
                    return Task.FromResult(_lines[index]);

                if (!_waiters.TryGetValue(index, out var waiter))
                    _waiters[index] = waiter = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

                return waiter.Task;
            }
        }

        public override void Write(char value)
        {
            TaskCompletionSource<string>? reached = null;
            string? line = null;
            lock (_gate)
            {
                _text.Append(value);
                if (value != '\n')
                {
                    _pending.Append(value);
                    return;
                }

                line = _pending.ToString().TrimEnd('\r');
                _pending.Clear();
                _lines.Add(line);
                _waiters.Remove(_lines.Count - 1, out reached);
            }

            reached?.TrySetResult(line);
        }
    }
}
