using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Commands.Scripting.Progress;
using Orkeon.Domain.HumanInput;

namespace Orkeon.ConsoleApp.Services;

/// <summary>
/// Asks the operator through the REPL read loop, never beside it (RT-08). A question a background task asks names
/// that task on the screen the operator looks at and in the prompt, and takes its answer from the main field or from
/// the task's own thread (#352).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Process-lifetime singleton; the semaphore holds no handle until waited on asynchronously.")]
internal sealed class ReplHumanInputProvider : IHumanInputProvider, IReplLineInterceptor
{
    private const string AnswerPrompt = "answer> ";

    private readonly Lazy<IConsoleAdapter> _console;
    private readonly Func<(string Ticket, string Name)?> _origin;
    private readonly SemaphoreSlim _one = new(1, 1);
    private readonly object _gate = new();
    private Pending? _pending;

    /// <param name="console">The REPL's console, resolved at the first question.</param>
    /// <param name="origin">
    /// The background task of the asking flow, ticket and name; null on the main thread. Defaults to the flow's
    /// console thread (<see cref="ConsoleThread.Current"/>) named by the dispatch's ambient instance
    /// (<see cref="ProgressAmbient.CurrentInstance"/>).
    /// </param>
    public ReplHumanInputProvider(Func<IConsoleAdapter> console, Func<(string Ticket, string Name)?>? origin = null)
    {
        _console = new(console);
        _origin = origin ?? AmbientTask;
    }

    /// <summary>Raised when a question starts waiting, and when it is answered or withdrawn.</summary>
    public event EventHandler? PendingChanged;

    public string? PromptOverride
    {
        get
        {
            lock (_gate)
                return _pending is null ? null : PromptFor(_pending.Origin);
        }
    }

    // The thread the flow writes to names the task (null on main); the ambient instance gives its name.
    private static (string Ticket, string Name)? AmbientTask()
    {
        if (ConsoleThread.Current is not { } ticket)
            return null;
        var name = ProgressAmbient.CurrentInstance is { } instance && instance.Ticket == ticket ? instance.Name : ticket;
        return (ticket, name);
    }

    private static string PromptFor((string Ticket, string Name)? origin)
        => origin is { } task ? $"answer [{task.Ticket}]> " : AnswerPrompt;

    public bool TryConsume(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var answer = line.Trim();
        if (answer.StartsWith('/'))
            return false;
        return Answer(null, answer);
    }

    /// <summary>
    /// Answers the question the task <paramref name="ticket"/> asked, typed in that task's thread. False when that
    /// task waits for no answer: nothing was taken.
    /// </summary>
    public bool TryAnswer(string ticket, string line)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(line);
        return Answer(ticket, line.Trim());
    }

    // ticket null: the main field, which answers whatever question waits.
    private bool Answer(string? ticket, string answer)
    {
        string? problem;
        Pending pending;
        lock (_gate)
        {
            if (_pending is null || (ticket is not null && _pending.Origin?.Ticket != ticket))
                return false;

            pending = _pending;
            problem = pending.Accept(answer);
            if (problem is null)
                _pending = null;
        }

        if (problem is not null)
        {
            Say(pending.Origin, problem);
            return true;
        }

        if (pending.Origin is { } task)
            Say(task, $"[{task.Ticket}] answer taken: {(answer.Length == 0 ? "(default)" : answer)}");
        PendingChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public Task<string> GetInputAsync(HumanInputContext context, CancellationToken cancellationToken = default)
        => AskAsync(
            context,
            static (_, a) => (a, null),
            static c => c.DefaultValue ?? string.Empty,
            options: false,
            cancellationToken);

    public Task<bool> GetConfirmationAsync(HumanInputContext context, CancellationToken cancellationToken = default)
        => AskAsync(
            context,
            static (_, a) => a.ToUpperInvariant() switch
            {
                "Y" or "YES" or "O" or "OUI" => (true, null),
                "N" or "NO" or "NON" => (false, null),
                _ => (false, "Answer yes or no."),
            },
            static c => bool.TryParse(c.DefaultValue, out var d) && d,
            options: false,
            cancellationToken);

    public Task<string> GetChoiceAsync(HumanInputContext context, CancellationToken cancellationToken = default)
        => AskAsync(
            context,
            static (c, a) => Resolve(c, a) is { } v ? (v, null) : (string.Empty, "Pick a number or a value from the list."),
            static c => Recommended(c) ?? c.DefaultValue ?? string.Empty,
            options: true,
            cancellationToken);

    public Task<IReadOnlyList<string>> GetMultiChoiceAsync(HumanInputContext context, CancellationToken cancellationToken = default)
        => AskAsync<IReadOnlyList<string>>(
            context,
            static (c, a) =>
            {
                var picked = new List<string>();
                foreach (var part in a.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (Resolve(c, part) is not { } v)
                        return ([], $"'{part}' is not in the list: pick numbers or values, comma-separated.");
                    picked.Add(v);
                }

                return (picked, null);
            },
            static c => Recommended(c) is { } r ? [r] : [],
            options: true,
            cancellationToken);

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    private static string? Recommended(HumanInputContext c)
        => c.Recommended is { } i && i >= 0 && i < c.Options.Count ? c.Options[i] : null;

    private static string? Resolve(HumanInputContext c, string token)
    {
        if (int.TryParse(token, out var n))
            return n >= 1 && n <= c.Options.Count ? c.Options[n - 1] : null;
        return c.Options.FirstOrDefault(o => string.Equals(o, token, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<T> AskAsync<T>(
        HumanInputContext context,
        Func<HumanInputContext, string, (T Value, string? Problem)> parse,
        Func<HumanInputContext, T> whenEmpty,
        bool options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var origin = _origin();
        if (_one.CurrentCount == 0)
            Say(origin, origin is { } queued
                ? $"[{queued.Ticket}] {queued.Name} has a question: asked once the one waiting is answered."
                : "A question waits its turn: asked once the one waiting is answered.");

        try
        {
            await _one.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (origin is { } announced && cancellationToken.IsCancellationRequested)
        {
            // Announced while queued, withdrawn before its turn: the announcement gets its end.
            Say(origin, $"[{announced.Ticket}] question withdrawn");
            throw;
        }

        try
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = new Pending(origin, a =>
            {
                if (a.Length == 0)
                {
                    completion.TrySetResult(whenEmpty(context));
                    return null;
                }

                var (value, problem) = parse(context, a);
                if (problem is null)
                    completion.TrySetResult(value);
                return problem;
            });

            // Registered before the question is written: an answer typed right after it shows is never lost.
            lock (_gate)
                _pending = pending;
            PendingChanged?.Invoke(this, EventArgs.Empty);
            Write(pending.Origin, context, options);

            await using var registration = cancellationToken.Register(() =>
            {
                bool withdrawn;
                lock (_gate)
                {
                    withdrawn = ReferenceEquals(_pending, pending);
                    if (withdrawn)
                        _pending = null;
                }

                completion.TrySetCanceled(cancellationToken);
                if (!withdrawn)
                    return;

                if (origin is { } task)
                    Say(origin, $"[{task.Ticket}] question withdrawn");
                PendingChanged?.Invoke(this, EventArgs.Empty);
            });
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _one.Release();
        }
    }

    private void Write((string Ticket, string Name)? origin, HumanInputContext context, bool options)
    {
        Say(origin, origin is { } task ? $"[{task.Ticket}] {task.Name} asks: {context.Prompt}" : context.Prompt);
        if (!options)
            return;

        for (var i = 0; i < context.Options.Count; i++)
        {
            var marker = i == context.Recommended ? " (recommended)" : string.Empty;
            Say(origin, $"  {i + 1}. {context.Options[i]}{marker}");
        }
    }

    // A task's line goes to its thread as an attention line, which the screen the operator looks at shows too; a
    // console that does not separate threads gets a plain line, the task tag inline.
    private void Say((string Ticket, string Name)? origin, string text)
    {
        var console = _console.Value;
        if (origin is { } task && console is IThreadedConsole { SeparatesThreads: true } threaded)
            threaded.WriteAttention(task.Ticket, text);
        else
            console.WriteLine(text);
    }

    private sealed class Pending((string Ticket, string Name)? origin, Func<string, string?> accept)
    {
        public (string Ticket, string Name)? Origin { get; } = origin;

        public string? Accept(string answer) => accept(answer);
    }
}
