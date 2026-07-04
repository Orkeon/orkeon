using System.Text;
using Orkeon.Cli.TerminalGui.Layout;

namespace Orkeon.Cli.TerminalGui.Logging;

/// <summary>
/// <see cref="TextWriter"/> that captures <c>System.Console.Out</c> / <c>Console.Error</c>
/// writes from third-party code (child <c>IHost</c>s,
/// libraries that bypass <c>IConsoleAdapter</c>, <c>AddSimpleConsole</c> from inner
/// hosts, etc.) and forwards complete lines to the <see cref="LogsPaneView"/>.
/// </summary>
/// <remarks>
/// Required because <see cref="Microsoft.Extensions.Logging.ILoggerProvider"/> swap is
/// only effective for the host that owns the LoggerFactory we configured. Inner hosts
/// (e.g. spawned by <c>RunOneShotAsync</c>) have their own LoggerFactory with
/// <c>AddSimpleConsole</c>; their writes go to <c>System.Console.Out</c>. By substituting
/// <c>Console.Out</c> at the process level once the TUI is up, every such write is funnelled
/// into the logs pane regardless of which DI container produced it.
/// <para>
/// Terminal.Gui's DOTNET driver writes directly to fd 1 (not via <c>Console.Out</c>), so
/// substituting <c>Console.Out</c> does NOT break the TUI rendering pipeline.
/// </para>
/// </remarks>
internal sealed class LogsPaneTextWriter : TextWriter
{
    private readonly LogsPaneView _logs;
    private readonly StringBuilder _pending = new();
    private readonly Lock _gate = new();

    public LogsPaneTextWriter(LogsPaneView logs)
    {
        _logs = logs ?? throw new ArgumentNullException(nameof(logs));
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        FlushIfNewline(value);
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        foreach (var c in value) FlushIfNewline(c);
    }

    public override void WriteLine() => FlushPending(forceLine: true);

    public override void WriteLine(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            foreach (var c in value) FlushIfNewline(c);
        FlushPending(forceLine: true);
    }

    private void FlushIfNewline(char c)
    {
        // Treat both \n and \r\n as line terminators. Strip CR.
        if (c == '\r') return;
        lock (_gate)
        {
            if (c == '\n')
            {
                _logs.Append(_pending.ToString());
                _pending.Clear();
            }
            else
            {
                _pending.Append(c);
            }
        }
    }

    private void FlushPending(bool forceLine)
    {
        lock (_gate)
        {
            if (_pending.Length > 0 || forceLine)
            {
                _logs.Append(_pending.ToString());
                _pending.Clear();
            }
        }
    }
}
