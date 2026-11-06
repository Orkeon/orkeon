using Jint;
using Jint.Native;
using Jint.Native.Object;
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
/// A console with panels (<see cref="IChoiceConsole"/>) asks <c>select</c> and <c>confirm</c> with one; Esc answers
/// <c>null</c> to a select and the default to a confirm. The others keep the numbered lines, which is also what a
/// replay reads.
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

    /// <summary>The console's panels, when it can ask one right now.</summary>
    public static IChoiceConsole? PanelsOf(IConsoleAdapter console)
        => console is IChoiceConsole { CanChoose: true } panels ? panels : null;

    private static async Task<JsValue> RunCoreAsync(Engine engine, IConsoleAdapter console, JsValue spec, CancellationToken ct)
    {
        var obj = spec.AsObject();
        var typeVal = obj.Get("type");
        var type = typeVal.IsString() ? typeVal.AsString() : "text";

        var messageVal = obj.Get("message");
        var message = messageVal.IsString() ? messageVal.AsString() : string.Empty;

        return type switch
        {
            "confirm" when PanelsOf(console) is { } panels => await AskConfirmAsync(engine, panels, message, GetBool(obj, "default"), ct).ConfigureAwait(false),
            "confirm" => await ReadConfirmAsync(engine, console, message, GetBool(obj, "default"), ct).ConfigureAwait(false),
            "select" => await ReadSelectAsync(engine, console, message, obj, ct).ConfigureAwait(false),
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
        ObjectInstance spec,
        CancellationToken ct)
    {
        var choices = ReadChoices(spec);
        if (choices.Count == 0)
            throw new ArgumentException("ctx.prompt({type:'select'}): 'choices' must be a non-empty array.");

        return PanelsOf(console) is { } panels
            ? AskSelectAsync(engine, panels, message, choices, spec, ct)
            : ReadSelectCoreAsync(engine, console, message, choices, spec, ct);
    }

    private static async Task<JsValue> AskSelectAsync(
        Engine engine,
        IChoiceConsole panels,
        string message,
        List<ConsoleChoice> choices,
        ObjectInstance spec,
        CancellationToken ct)
    {
        var request = new ConsoleChoiceRequest
        {
            Title = GetString(spec, "title"),
            Description = GetString(spec, "description"),
            Question = string.IsNullOrEmpty(message) ? null : message,
            Keys = GetString(spec, "keys"),
            Control = ReadControl(spec),
            Cancellable = GetBool(spec, "cancellable") ?? true,
            DefaultValue = GetString(spec, "default"),
            Choices = choices,
        };
        var answer = await panels.ChooseAsync(request, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (answer is null) return JsValue.Null;
        return Selected(engine, spec, answer.Value, answer.ControlValue);
    }

    private static async Task<JsValue> AskConfirmAsync(Engine engine, IChoiceConsole panels, string message, bool? defaultValue, CancellationToken ct)
    {
        var request = new ConsoleChoiceRequest
        {
            Question = message,
            // No unless the script says Yes: Enter answers what a plain console answers for an empty line.
            DefaultValue = defaultValue == true ? "yes" : "no",
            Keys = $"Enter to confirm, Esc answers {(defaultValue == true ? "Yes" : "No")}",
            Choices =
            [
                new ConsoleChoice("yes", "Yes") { Shortcut = "y" },
                new ConsoleChoice("no", "No") { Shortcut = "n" },
            ],
        };
        var answer = await panels.ChooseAsync(request, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return JsValue.FromObject(engine, answer is null ? defaultValue ?? false : answer.Value == "yes");
    }

    private static async Task<JsValue> ReadSelectCoreAsync(
        Engine engine,
        IConsoleAdapter console,
        string message,
        List<ConsoleChoice> choices,
        ObjectInstance spec,
        CancellationToken ct)
    {
        console.WriteLine(message);
        for (var i = 0; i < choices.Count; i++)
        {
            var description = string.IsNullOrEmpty(choices[i].Description) ? string.Empty : $"  {choices[i].Description}";
            console.WriteLine($"  [{i}] {choices[i].Label}{description}");
        }

        var defaultValue = GetString(spec, "default");
        var defaultIdx = -1;
        if (defaultValue is not null)
        {
            for (var i = 0; i < choices.Count; i++)
            {
                if (Names(choices[i], defaultValue))
                {
                    defaultIdx = i;
                    break;
                }
            }
        }

        var hint = defaultIdx >= 0 ? $"[{defaultIdx}]" : string.Empty;
        console.Write($"? {hint} > ");

        // Nothing adjusts a control on typed lines: it answers the value it opened on.
        var control = ReadControl(spec)?.Value;
        var line = await ReadLineAsync(console, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(line))
        {
            if (defaultIdx >= 0) return Selected(engine, spec, choices[defaultIdx].Value, control);
            throw new InvalidOperationException("ctx.prompt({type:'select'}): no selection and no default.");
        }

        var trimmed = line.Trim();
        if (int.TryParse(trimmed, out var idx) && idx >= 0 && idx < choices.Count)
            return Selected(engine, spec, choices[idx].Value, control);

        var match = choices.FirstOrDefault(c => Names(c, trimmed));
        if (match is not null)
            return Selected(engine, spec, match.Value, control);

        throw new InvalidOperationException($"ctx.prompt({{type:'select'}}): '{trimmed}' is not a valid choice.");
    }

    private static bool Names(ConsoleChoice choice, string text)
        => string.Equals(choice.Value, text, StringComparison.OrdinalIgnoreCase)
           || string.Equals(choice.Label, text, StringComparison.OrdinalIgnoreCase);

    // The value alone unless the script asked for { detailed: true }, the only way to read a control.
    private static JsValue Selected(Engine engine, ObjectInstance spec, string value, string? control)
    {
        if (GetBool(spec, "detailed") != true) return JsValue.FromObject(engine, value);
        var result = new JsObject(engine);
        result.Set("value", value);
        result.Set("control", control is null ? JsValue.Null : new JsString(control));
        return result;
    }

    private static async Task<string?> ReadLineAsync(IConsoleAdapter console, CancellationToken ct)
    {
        // A console with panels has a read that cancels: nothing stays open behind a cancelled prompt.
        if (PanelsOf(console) is { } panels)
        {
            var line = await panels.ReadAnswerAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return line;
        }

        // Task.Run frees the calling thread; the underlying adapter's ReadLine remains
        // blocked until either the user submits or the adapter closes its input.
        var readTask = Task.Run(console.ReadLine, ct);
        return await readTask.WaitAsync(ct).ConfigureAwait(false);
    }

    private static string BuildTextPrompt(string message, string? defaultValue)
        => string.IsNullOrEmpty(defaultValue)
            ? $"{message} > "
            : $"{message} [{defaultValue}] > ";

    private static string? GetString(ObjectInstance obj, string key)
    {
        var v = obj.Get(key);
        return v.IsString() ? v.AsString() : null;
    }

    private static bool? GetBool(ObjectInstance obj, string key)
    {
        var v = obj.Get(key);
        return v.IsBoolean() ? v.AsBoolean() : null;
    }

    private static List<ConsoleChoice> ReadChoices(ObjectInstance spec)
    {
        var v = spec.Get("choices");
        if (!v.IsArray()) return [];
        var arr = v.AsArray();
        var list = new List<ConsoleChoice>((int)arr.Length);
        for (uint i = 0; i < arr.Length; i++)
            list.Add(ReadChoice(arr.Get(i)));
        return list;
    }

    private static ConsoleChoice ReadChoice(JsValue element)
    {
        if (!element.IsObject() || element.IsArray())
        {
            var text = element.IsString() ? element.AsString() : element.ToString();
            return new ConsoleChoice(text, text);
        }
        var obj = element.AsObject();
        var value = GetString(obj, "value")
            ?? throw new ArgumentException("ctx.prompt({type:'select'}): a choice object needs a string 'value'.");
        return new ConsoleChoice(value, GetString(obj, "label") ?? value)
        {
            Description = GetString(obj, "description"),
            Hint = GetString(obj, "hint"),
            IsCurrent = GetBool(obj, "current") == true,
        };
    }

    private static ConsoleChoiceControl? ReadControl(ObjectInstance spec)
    {
        var v = spec.Get("control");
        if (!v.IsObject()) return null;
        var obj = v.AsObject();
        var values = obj.Get("values");
        if (!values.IsArray() || values.AsArray().Length == 0)
            throw new ArgumentException("ctx.prompt({type:'select'}): 'control.values' must be a non-empty array.");
        var array = values.AsArray();
        var list = new List<string>((int)array.Length);
        for (uint i = 0; i < array.Length; i++)
            list.Add(array.Get(i).ToString());
        var value = GetString(obj, "value");
        return new ConsoleChoiceControl(GetString(obj, "label") ?? string.Empty, list, value is not null && list.Contains(value) ? value : list[0]);
    }
}
