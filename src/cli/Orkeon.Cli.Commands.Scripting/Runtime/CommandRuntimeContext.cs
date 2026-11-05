using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Scripting;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Commands.Scripting.Progress;

namespace Orkeon.Cli.Commands.Scripting.Runtime;

#pragma warning disable IDE1006 // intentional camelCase: this CLR type is exposed to JS via Jint as `ctx`
/// <summary>
/// Rich runtime surface exposed to scripted command handlers as <c>ctx</c>. Mirrors
/// spec §5.1 (TypeScript shape) at the CLR level — Jint reflects properties and methods
/// to JS directly, so the camelCase API surface here is what scripts see.
/// </summary>
/// <remarks>
/// Created per invocation by the <c>ScriptCommand</c> handler. Owns no engine state
/// beyond what the handler explicitly captures via <c>ctx.services</c> (whitelisted
/// access lands in Phase 4 — see <see cref="ScriptServiceLocator"/>).
/// </remarks>
public sealed class CommandRuntimeContext
{
    private readonly Engine _engine;
    private readonly IConsoleAdapter _console;
    private readonly CancellationToken _ct;
    private readonly ProgressBroker? _progressBroker;

    public CommandRuntimeContext(
        Engine engine,
        IConsoleAdapter console,
        CommandMeta command,
        CancellationToken ct,
        ILogger? logger = null,
        ScriptServiceLocator? services = null,
        ProgressBroker? progressBroker = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        ArgumentNullException.ThrowIfNull(command);
        this.command = command;
        _ct = ct;
        log = new JsLogShim(logger ?? NullLogger.Instance);
        this.services = services ?? ScriptServiceLocator.Empty;
        _progressBroker = progressBroker;
    }

    /// <summary>Metadata of the command being invoked (name + raw input line).</summary>
    public CommandMeta command { get; }

    /// <summary>Structured logger surface — debug/info/warn/error.</summary>
    public JsLogShim log { get; }

    /// <summary>Whitelisted service locator (Phase 2 stub; Phase 4 wires the whitelist).</summary>
    public ScriptServiceLocator services { get; }

    /// <summary>
    /// Cooperative cancellation token, exposed directly to JS (PascalCase via Jint
    /// reflection — see spec §5.2). The TS alias <c>CommandSignal</c> with
    /// <c>aborted</c>/<c>throwIfAborted</c> is documented in <c>.d.ts</c> as a TypeScript
    /// convention for <c>IsCancellationRequested</c>/<c>ThrowIfCancellationRequested</c>;
    /// there is no CLR wrapper.
    /// </summary>
    public CancellationToken signal => _ct;

    /// <summary>Write text to the console (no trailing newline).</summary>
    public void write(string text) => _console.Write(text ?? string.Empty);

    /// <summary>Write a line to the console.</summary>
    public void writeLine(string text) => _console.WriteLine(text ?? string.Empty);

    /// <summary>
    /// Write a typed block, <c>{ kind: "markdown" | "diff", text }</c>, on lines of its own: styled by a
    /// console able to, as plain lines by any other.
    /// </summary>
    public void writeBlock(JsValue spec)
    {
        if (spec is null || !spec.IsObject())
            throw new ArgumentException("ctx.writeBlock(spec): spec must be an object with { kind, text }.");
        var obj = spec.AsObject();
        var textValue = obj.Get("text");
        if (!textValue.IsString())
            throw new ArgumentException("ctx.writeBlock(spec): 'text' must be a string.");
        var kindValue = obj.Get("kind");
        var kind = (kindValue.IsString() ? kindValue.AsString() : null) switch
        {
            "markdown" => ConsoleBlockKind.Markdown,
            "diff" => ConsoleBlockKind.Diff,
            _ => throw new ArgumentException("ctx.writeBlock(spec): 'kind' must be \"markdown\" or \"diff\"."),
        };
        var text = textValue.AsString();
        if (_console is IBlockConsole blocks) blocks.WriteBlock(kind, text);
        else _console.WriteLine(text.EndsWith('\n') ? text[..^1] : text);
    }

    /// <summary>The palette of the console, <c>"dark"</c>, <c>"light"</c> or <c>"none"</c>; null on a console without palettes.</summary>
    public string? theme => _console is IThemeConsole themed ? ThemeName(themed.Theme) : null;

    /// <summary>
    /// Paints the console in the palette named <c>"dark"</c>, <c>"light"</c> or <c>"none"</c>, what is on screen
    /// included. Returns false on a console without palettes, which stays as it is.
    /// </summary>
    public bool setTheme(string name)
    {
        var palette = name switch
        {
            "dark" => ConsoleTheme.Dark,
            "light" => ConsoleTheme.Light,
            "none" => ConsoleTheme.None,
            _ => throw new ArgumentException("ctx.setTheme(name): name must be \"dark\", \"light\" or \"none\"."),
        };
        if (_console is not IThemeConsole themed) return false;
        themed.SetTheme(palette);
        return true;
    }

    private static string ThemeName(ConsoleTheme palette) => palette switch
    {
        ConsoleTheme.Light => "light",
        ConsoleTheme.None => "none",
        _ => "dark",
    };

    /// <summary>Clear the console / logs pane.</summary>
    public void clear() => _console.Clear();

    /// <summary>Whether <see cref="prompt"/> asks a choice with a panel, so a command may open one without an argument.</summary>
    public bool interactive => PromptDispatcher.PanelsOf(_console) is not null;

    /// <summary>Block until the user answers a prompt described by <paramref name="spec"/>.</summary>
    public async Task<JsValue> prompt(JsValue spec)
    {
        // The operator's time is not the script's: an answer may take longer than ExecutionTimeout.
        using var paused = JsEngineFactory.PauseExecutionClock(_engine);
        return await PromptDispatcher.RunAsync(_engine, _console, spec, _ct).ConfigureAwait(false);
    }

    /// <summary>Open a progress reporter with an optional total step count.</summary>
    public ProgressHandle progress(JsValue spec)
    {
        if (spec is null || !spec.IsObject())
            throw new ArgumentException("ctx.progress(spec): spec must be an object with at least { label }.");
        var obj = spec.AsObject();
        var labelVal = obj.Get("label");
        if (!labelVal.IsString())
            throw new ArgumentException("ctx.progress(spec): 'label' must be a string.");
        var totalVal = obj.Get("total");
        int? total = totalVal.IsNumber() ? (int)totalVal.AsNumber() : null;
        return new ProgressHandle(_console, labelVal.AsString(), total, _progressBroker);
    }

    /// <summary>
    /// Render a tabular view of <paramref name="rows"/> using every column of the first row.
    /// </summary>
    /// <remarks>
    /// Single-argument overload for the TS signature <c>table(rows, columns?)</c> where
    /// <c>columns</c> is optional. Without it, a <c>ctx.table(rows)</c> call (one argument) finds
    /// no matching CLR method and Jint throws "No public methods with the specified arguments
    /// were found." — which is exactly how every table-rendering command (doctor, ps, stats…)
    /// failed in the REPL.
    /// </remarks>
    public void table(JsValue rows) => table(rows, JsValue.Undefined);

    /// <summary>Render a tabular view of <paramref name="rows"/> with optional column projection.</summary>
    public void table(JsValue rows, JsValue columns)
    {
        if (rows is null || !rows.IsArray())
        {
            _console.WriteLine("(no rows)");
            return;
        }

        var rowsArr = rows.AsArray();
        if (rowsArr.Length == 0)
        {
            _console.WriteLine("(no rows)");
            return;
        }

        var cols = ResolveColumns(rows, columns);
        if (cols.Count == 0)
        {
            for (uint i = 0; i < rowsArr.Length; i++)
                _console.WriteLine(rowsArr.Get(i).ToString() ?? string.Empty);
            return;
        }

        var widths = cols.Select(c => c.Length).ToArray();
        var rowsRendered = RenderRows(rows, cols, widths);

        // Header.
        _console.WriteLine(string.Join("  ", cols.Select((h, idx) => h.PadRight(widths[idx]))));
        _console.WriteLine(string.Join("  ", widths.Select(w => new string('-', w))));
        foreach (var row in rowsRendered)
            _console.WriteLine(string.Join("  ", row.Select((cell, idx) => cell.PadRight(widths[idx]))));
    }

    /// <summary>
    /// Resolves column ordering: an explicit <paramref name="columns"/> array wins, otherwise
    /// the keys of the first row (insertion-ish order).
    /// </summary>
    private static IReadOnlyList<string> ResolveColumns(JsValue rows, JsValue columns)
    {
        if (columns is not null && columns.IsArray())
        {
            var colArr = columns.AsArray();
            var list = new List<string>((int)colArr.Length);
            for (uint i = 0; i < colArr.Length; i++)
            {
                var elem = colArr.Get(i);
                list.Add(elem.IsString() ? elem.AsString() : elem.ToString() ?? string.Empty);
            }
            return list;
        }

        var first = rows.AsArray().Get(0);
        return first.IsObject()
            ? first.AsObject().GetOwnProperties().Select(p => p.Key.ToString() ?? string.Empty).ToArray()
            : Array.Empty<string>();
    }

    /// <summary>Renders each row to a string[] of cells and widens <paramref name="widths"/> in place.</summary>
    private static List<string[]> RenderRows(JsValue rows, IReadOnlyList<string> cols, int[] widths)
    {
        var rowsArr = rows.AsArray();
        var rowsRendered = new List<string[]>((int)rowsArr.Length);
        for (uint i = 0; i < rowsArr.Length; i++)
        {
            var row = rowsArr.Get(i);
            var cells = new string[cols.Count];
            for (var c = 0; c < cols.Count; c++)
            {
                var cellVal = row.IsObject() ? row.AsObject().Get(cols[c]) : JsValue.Undefined;
                var s = cellVal.IsUndefined() || cellVal.IsNull() ? string.Empty : cellVal.ToString() ?? string.Empty;
                cells[c] = s;
                if (s.Length > widths[c]) widths[c] = s.Length;
            }
            rowsRendered.Add(cells);
        }
        return rowsRendered;
    }

    /// <summary>Continue the runner loop without a message.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "instance member required by the Jint JS binding surface — reflected onto the `ctx` object scripts call as ctx.continue()/ctx.exit().")]
    public CommandActionResult @continue() => new(false, null);

    /// <summary>Continue the runner loop and display <paramref name="message"/>.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "instance member required by the Jint JS binding surface — reflected onto the `ctx` object scripts call as ctx.continue()/ctx.exit().")]
    public CommandActionResult @continue(string? message) => new(false, message);

    /// <summary>Exit the runner loop without a farewell.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "instance member required by the Jint JS binding surface — reflected onto the `ctx` object scripts call as ctx.continue()/ctx.exit().")]
    public CommandActionResult exit() => new(true, null);

    /// <summary>Exit the runner loop and display <paramref name="farewell"/>.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "instance member required by the Jint JS binding surface — reflected onto the `ctx` object scripts call as ctx.continue()/ctx.exit().")]
    public CommandActionResult exit(string? farewell) => new(true, farewell);
}
#pragma warning restore IDE1006
