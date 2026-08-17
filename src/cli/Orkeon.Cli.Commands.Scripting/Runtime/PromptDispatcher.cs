using Jint;
using Jint.Native;
using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Commands.Scripting.Runtime;

/// <summary>
/// Routes a <c>ctx.prompt(spec)</c> call to <see cref="IConsoleAdapter.ReadLine"/> with
/// the correct rendering for each <c>spec.type</c> (text / confirm / select / password).
/// </summary>
/// <remarks>
/// <para>
/// The prompt line is always rendered with a trailing <c>"> "</c> so it matches
/// <c>TerminalGuiConsoleAdapter.LooksLikePrompt</c> and is routed to the REPL pane's
/// prompt label (cf. plan §4 R3).
/// </para>
/// <para>
/// Cancellation: <see cref="IConsoleAdapter.ReadLine"/> is sync and can't be cancelled
/// portably. We wrap the call in a <see cref="Task"/> and await with the caller's
/// <see cref="CancellationToken"/> — on cancellation the awaiting handler observes
/// <see cref="OperationCanceledException"/>. The blocked thread eventually
/// returns when the host adapter completes its input channel (test adapters expose
/// <c>CompleteInput()</c>; the runner cancels the linked CTS on Ctrl+C).
/// </para>
/// </remarks>
public static class PromptDispatcher
{
    /// <summary>
    /// Dispatches based on the JS-side <c>spec.type</c> string. Returns the answer as a
    /// <see cref="JsValue"/> (string for text/select/password, boolean for confirm).
    /// </summary>
    public static Task<JsValue> RunAsync(Engine engine, IConsoleAdapter console, JsValue spec, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(spec);

        if (!spec.IsObject())
            throw new ArgumentException("ctx.prompt(spec): spec must be an object with at least { type, message }.");

        return RunCoreAsync(engine, console, spec, ct);
    }

    private static async Task<JsValue> RunCoreAsync(Engine engine, IConsoleAdapter console, JsValue spec, CancellationToken ct)
    {
        var obj = spec.AsObject();
        var typeVal = obj.Get("type");
        var type = typeVal.IsString() ? typeVal.AsString() : "text";

        var messageVal = obj.Get("message");
        var message = messageVal.IsString() ? messageVal.AsString() : string.Empty;

        return type switch
        {
            "confirm" => await ReadConfirmAsync(engine, console, message, GetBool(obj, "default"), ct).ConfigureAwait(false),
            "select" => await ReadSelectAsync(engine, console, message, ReadStringArray(obj, "choices"), GetString(obj, "default"), ct).ConfigureAwait(false),
            "password" => await ReadTextAsync(engine, console, $"{message} > ", ct).ConfigureAwait(false),
            _ => await ReadTextAsync(engine, console, BuildTextPrompt(message, GetString(obj, "default")), ct).ConfigureAwait(false),
        };
    }

    private static async Task<JsValue> ReadTextAsync(Engine engine, IConsoleAdapter console, string prompt, CancellationToken ct)
    {
        console.Write(prompt);
        var line = await ReadLineAsync(console, ct).ConfigureAwait(false);
        return line is null ? JsValue.Null : JsValue.FromObject(engine, line);
    }

    private static async Task<JsValue> ReadConfirmAsync(Engine engine, IConsoleAdapter console, string message, bool? defaultValue, CancellationToken ct)
    {
        var hint = defaultValue switch
        {
            true => "(Y/n)",
            false => "(y/N)",
            _ => "(y/n)",
        };
        console.Write($"{message} {hint} > ");
        var line = await ReadLineAsync(console, ct).ConfigureAwait(false);
        var answer = ParseConfirmAnswer(line, defaultValue);
        return JsValue.FromObject(engine, answer);
    }

    private static bool ParseConfirmAnswer(string? line, bool? defaultValue)
    {
        if (string.IsNullOrEmpty(line)) return defaultValue ?? false;
        var trimmed = line.Trim();
        if (trimmed.Equals("y", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase))
            return true;
        if (trimmed.Equals("n", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("no", StringComparison.OrdinalIgnoreCase))
            return false;
        return defaultValue ?? false;
    }

    private static Task<JsValue> ReadSelectAsync(
        Engine engine,
        IConsoleAdapter console,
        string message,
        IReadOnlyList<string> choices,
        string? defaultValue,
        CancellationToken ct)
    {
        if (choices.Count == 0)
            throw new ArgumentException("ctx.prompt({type:'select'}): 'choices' must be a non-empty string array.");

        return ReadSelectCoreAsync(engine, console, message, choices, defaultValue, ct);
    }

    private static async Task<JsValue> ReadSelectCoreAsync(
        Engine engine,
        IConsoleAdapter console,
        string message,
        IReadOnlyList<string> choices,
        string? defaultValue,
        CancellationToken ct)
    {
        console.WriteLine(message);
        for (var i = 0; i < choices.Count; i++)
            console.WriteLine($"  [{i}] {choices[i]}");

        var defaultIdx = -1;
        if (defaultValue is not null)
        {
            for (var i = 0; i < choices.Count; i++)
            {
                if (string.Equals(choices[i], defaultValue, StringComparison.OrdinalIgnoreCase))
                {
                    defaultIdx = i;
                    break;
                }
            }
        }

        var hint = defaultIdx >= 0 ? $"[{defaultIdx}]" : string.Empty;
        console.Write($"? {hint} > ");

        var line = await ReadLineAsync(console, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(line))
        {
            if (defaultIdx >= 0) return JsValue.FromObject(engine, choices[defaultIdx]);
            throw new InvalidOperationException("ctx.prompt({type:'select'}): no selection and no default.");
        }

        var trimmed = line.Trim();
        if (int.TryParse(trimmed, out var idx) && idx >= 0 && idx < choices.Count)
            return JsValue.FromObject(engine, choices[idx]);

        var match = choices.FirstOrDefault(c => string.Equals(c, trimmed, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            return JsValue.FromObject(engine, match);

        throw new InvalidOperationException($"ctx.prompt({{type:'select'}}): '{trimmed}' is not a valid choice.");
    }

    private static async Task<string?> ReadLineAsync(IConsoleAdapter console, CancellationToken ct)
    {
        // Task.Run frees the calling thread; the underlying adapter's ReadLine remains
        // blocked until either the user submits or the adapter closes its input.
        var readTask = Task.Run(console.ReadLine, ct);
        return await readTask.WaitAsync(ct).ConfigureAwait(false);
    }

    private static string BuildTextPrompt(string message, string? defaultValue)
        => string.IsNullOrEmpty(defaultValue)
            ? $"{message} > "
            : $"{message} [{defaultValue}] > ";

    private static string? GetString(Jint.Native.Object.ObjectInstance obj, string key)
    {
        var v = obj.Get(key);
        return v.IsString() ? v.AsString() : null;
    }

    private static bool? GetBool(Jint.Native.Object.ObjectInstance obj, string key)
    {
        var v = obj.Get(key);
        return v.IsBoolean() ? v.AsBoolean() : null;
    }

    private static IReadOnlyList<string> ReadStringArray(Jint.Native.Object.ObjectInstance obj, string key)
    {
        var v = obj.Get(key);
        if (!v.IsArray()) return Array.Empty<string>();
        var arr = v.AsArray();
        var list = new List<string>((int)arr.Length);
        for (uint i = 0; i < arr.Length; i++)
        {
            var elem = arr.Get(i);
            list.Add(elem.IsString() ? elem.AsString() : elem.ToString() ?? string.Empty);
        }
        return list;
    }
}
