using System.Text.Json;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Commands.Scripting.Progress;

namespace Orkeon.Cli.Commands.Scripting.Runtime;

/// <summary>
/// The REPL's <see cref="IToolApprovalChannel"/>. A tool call the permission gate asks about, from a crew running in
/// the background, is written to the console, and the operator's next line answers it: the read loop hands that line
/// over through <see cref="IReplLineInterceptor"/>, so the loop stays the console's only reader. One question is shown
/// at a time, the others wait their turn. <c>y</c> allows the call, <c>s</c> allows the tool for the rest of the
/// session, <c>n</c> refuses; a line starting with <c>/</c> is left to the loop (a command runs, the question waits),
/// any other line asks the whole question again. A question names the background task that asks it, and
/// <see cref="Pending"/> lets the status line and the tasks list show it wherever the operator looks (#194).
/// Cancelling the crew withdraws its question; disposing the channel, at the REPL's end,
/// refuses every question still waiting. While a command holds the loop waiting for a crew (<c>runCrew</c>), that
/// command reads the answers itself (<see cref="AnswerFromConsole"/>): the loop is not reading, so the console still
/// has one reader.
/// </summary>
internal sealed class ConsoleToolApprovalChannel : IToolApprovalChannel, IReplLineInterceptor, IToolApprovalWatch, IDisposable
{
    private const int MaxArgumentsLength = 300;

    private readonly Func<IConsoleAdapter> _console;
    private readonly Func<(string Ticket, string Name)?> _origin;
    private readonly Lock _gate = new();
    private readonly LinkedList<PendingApproval> _queue = new();
    private readonly HashSet<string> _allowedForSession = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    // Whether the head question's prompt is on the console. The read loop writes its own prompt from
    // PromptOverride; AnswerFromConsole, which has no loop, writes it when the question came in without one.
    private bool _promptShown;

    /// <summary>Creates the channel.</summary>
    /// <param name="console">
    /// The REPL's console, resolved at the first question: the TUI's adapter initialises the terminal when it is resolved.
    /// </param>
    /// <param name="origin">
    /// The background task of the asking flow, ticket and name; null outside a task. Defaults to the dispatch's
    /// ambient instance (<see cref="ProgressAmbient.CurrentInstance"/>).
    /// </param>
    public ConsoleToolApprovalChannel(Func<IConsoleAdapter> console, Func<(string Ticket, string Name)?>? origin = null)
    {
        ArgumentNullException.ThrowIfNull(console);
        _console = console;
        _origin = origin ?? AmbientTask;
    }

    private static (string Ticket, string Name)? AmbientTask()
        => ProgressAmbient.CurrentInstance is { } instance ? (instance.Ticket, instance.Name) : null;

    /// <inheritdoc />
    public string? PromptOverride
    {
        get
        {
            lock (_gate)
                return _queue.First is { } head ? PromptFor(head.Value) : null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<PendingToolApproval> Pending
    {
        get
        {
            lock (_gate)
                return [.. _queue.Select(p => new PendingToolApproval(p.Request.ToolName, p.Origin?.Ticket, p.Origin?.Name))];
        }
    }

    /// <inheritdoc />
    public Task<ToolApprovalDecision> RequestAsync(ToolApprovalRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<ToolApprovalDecision>(cancellationToken);

        var pending = new PendingApproval(request, _origin());
        lock (_gate)
        {
            if (_disposed)
                return Task.FromResult(ToolApprovalDecision.Deny);
            if (_allowedForSession.Contains(request.ToolName))
                return Task.FromResult(ToolApprovalDecision.AllowForSession);

            pending.Node = _queue.AddLast(pending);
            if (_queue.Count == 1)
                Ask(pending, withPrompt: true);
        }

        pending.Registration = cancellationToken.Register(() => Withdraw(pending, cancellationToken));
        return pending.Completion.Task;
    }

    /// <inheritdoc />
    public bool TryConsume(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var answered = new List<PendingApproval>();
        ToolApprovalDecision decision;
        lock (_gate)
        {
            if (_queue.First is not { } head)
                return false;

            var answer = line.Trim();
            if (answer.StartsWith('/'))
                return false;

            var parsed = ParseAnswer(answer);
            if (parsed is null)
            {
                // Asked again whole: the operator may never have seen it, written under another task's lines.
                Console.WriteLine("  answer y (allow once), s (allow for this session) or n (deny)");
                Ask(head.Value, withPrompt: false);
                return true;
            }

            decision = parsed.Value;
            var toolName = head.Value.Request.ToolName;
            _queue.RemoveFirst();
            answered.Add(head.Value);
            if (decision == ToolApprovalDecision.AllowForSession)
            {
                // The other questions about the same tool are answered by the same approval.
                _allowedForSession.Add(toolName);
                for (var node = _queue.First; node is not null;)
                {
                    var next = node.Next;
                    if (string.Equals(node.Value.Request.ToolName, toolName, StringComparison.OrdinalIgnoreCase))
                    {
                        _queue.Remove(node);
                        answered.Add(node.Value);
                    }

                    node = next;
                }
            }

            Console.WriteLine(Outcome(toolName, decision));
            if (_queue.First is { } nextQuestion)
                Ask(nextQuestion.Value, withPrompt: false);
        }

        foreach (var pending in answered)
            pending.Settle(decision);
        return true;
    }

    /// <summary>Whether a question waits for the operator.</summary>
    public bool HasQuestion
    {
        get
        {
            lock (_gate)
                return _queue.First is not null;
        }
    }

    /// <summary>
    /// Reads one line from the console and answers the question waiting with it, for a command that holds the read
    /// loop while a crew runs (<c>runCrew</c>). Nothing happens when no question waits. A line starting with <c>/</c>
    /// cannot run as a command here: the question is asked again. The end of the input refuses the question.
    /// </summary>
    /// <returns>True when a line was read.</returns>
    public bool AnswerFromConsole()
    {
        lock (_gate)
        {
            if (_queue.First is not { } head)
                return false;
            if (!_promptShown)
            {
                Console.Write(PromptFor(head.Value));
                _promptShown = true;
            }
        }

        // Outside the lock: the read blocks until the operator answers, and a crew may ask meanwhile.
        var line = Console.ReadLine() ?? "n";
        if (!TryConsume(line))
        {
            lock (_gate)
            {
                if (_queue.First is null)
                    return true;
                Console.WriteLine("  a command cannot run while this one waits for its crew: answer y, s or n");
                Ask(_queue.First.Value, withPrompt: false);
            }
        }

        return true;
    }

    /// <summary>Refuses every question still waiting; later requests are refused at once.</summary>
    public void Dispose()
    {
        List<PendingApproval> waiting;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            waiting = [.. _queue];
            _queue.Clear();
        }

        foreach (var pending in waiting)
            pending.Settle(ToolApprovalDecision.Deny);
    }

    private IConsoleAdapter Console => _console();

    private void Withdraw(PendingApproval pending, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (pending.Node?.List is null)
                return;

            var wasShown = _queue.First == pending.Node;
            _queue.Remove(pending.Node);
            Console.WriteLine($"  approval for {pending.Request.ToolName} withdrawn: its run was cancelled");
            if (wasShown && _queue.First is { } next)
                Ask(next.Value, withPrompt: true);
        }

        pending.Completion.TrySetCanceled(cancellationToken);
    }

    // Called under the lock: the question and its prompt reach the console in one piece.
    private void Ask(PendingApproval pending, bool withPrompt)
    {
        var request = pending.Request;
        Console.WriteLine("");
        Console.WriteLine($"  ? {OriginOf(pending)}{request.Reason}");
        Console.WriteLine($"    {request.ToolName} {FormatArguments(request)}");
        if (withPrompt)
            Console.Write(PromptFor(pending));
        _promptShown = withPrompt;
    }

    private static string OriginOf(PendingApproval pending)
        => pending.Origin is { } task ? $"[{task.Ticket}] {task.Name}: " : "";

    private static string PromptFor(PendingApproval pending)
        => $"approve {pending.Request.ToolName}{(pending.Origin is { } task ? $" for [{task.Ticket}]" : "")}? [y] once / [s] session / [n] no > ";

    private static string Outcome(string toolName, ToolApprovalDecision decision) => decision switch
    {
        ToolApprovalDecision.AllowOnce => $"  ✓ {toolName} allowed once",
        ToolApprovalDecision.AllowForSession => $"  ✓ {toolName} allowed for this session",
        _ => $"  ✗ {toolName} denied",
    };

    private static ToolApprovalDecision? ParseAnswer(string answer) => answer.ToUpperInvariant() switch
    {
        "Y" or "YES" or "O" or "OUI" => ToolApprovalDecision.AllowOnce,
        "S" or "SESSION" => ToolApprovalDecision.AllowForSession,
        "N" or "NO" or "NON" => ToolApprovalDecision.Deny,
        _ => null,
    };

    private static string FormatArguments(ToolApprovalRequest request)
    {
        string text;
        try
        {
            text = JsonSerializer.Serialize(request.Arguments);
        }
        catch (Exception ex) when (ex is NotSupportedException or JsonException or InvalidOperationException)
        {
            text = "{" + string.Join(", ", request.Arguments.Select(a => $"{a.Key}: {a.Value}")) + "}";
        }

        return text.Length <= MaxArgumentsLength ? text : string.Concat(text.AsSpan(0, MaxArgumentsLength), "…");
    }

    private sealed class PendingApproval
    {
        public PendingApproval(ToolApprovalRequest request, (string Ticket, string Name)? origin)
        {
            Request = request;
            Origin = origin;
            Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public ToolApprovalRequest Request { get; }

        public (string Ticket, string Name)? Origin { get; }

        public TaskCompletionSource<ToolApprovalDecision> Completion { get; }

        public LinkedListNode<PendingApproval>? Node { get; set; }

        public CancellationTokenRegistration Registration { get; set; }

        public void Settle(ToolApprovalDecision decision)
        {
            Registration.Dispose();
            Completion.TrySetResult(decision);
        }
    }
}
