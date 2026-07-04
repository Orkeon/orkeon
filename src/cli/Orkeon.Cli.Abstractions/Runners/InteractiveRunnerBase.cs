using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Registry;

namespace Orkeon.Cli.Abstractions.Runners;

/// <summary>
/// Generic interactive REPL loop. Derive and provide a <see cref="Banner"/>,
/// a <see cref="Prompt"/>, and a specific <see cref="IInteractiveCommandRegistry"/>.
/// </summary>
public abstract partial class InteractiveRunnerBase : IInteractiveRunner
{
    protected IInteractiveCommandRegistry Defaults { get; }
    protected IInteractiveCommandRegistry Specific { get; }
    protected IConsoleAdapter Console { get; }
    protected ILogger Logger { get; }
    protected IServiceProvider Services { get; }

    /// <summary>
    /// Per-command CTS, linked to the parent runner ct. Non-null while a command's
    /// <c>ExecuteAsync</c> is in flight; reset to null in the finally. Read by
    /// <see cref="IsCommandRunning"/> and cancelled by <see cref="RequestCommandCancellation"/>.
    /// </summary>
    private volatile CancellationTokenSource? _currentCommandCts;

    private volatile string? _currentCommandName;
    private long _currentCommandStartedAtUtcTicks;

    /// <inheritdoc />
    public bool IsCommandRunning => _currentCommandCts is not null;

    /// <inheritdoc />
    public string? CurrentCommandName => _currentCommandName;

    /// <inheritdoc />
    public DateTime? CurrentCommandStartedAtUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _currentCommandStartedAtUtcTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    /// <inheritdoc />
    public void RequestCommandCancellation()
    {
        // Snapshot to avoid the field being nulled between the null-check and Cancel.
        var cts = _currentCommandCts;
        if (cts is not null && !cts.IsCancellationRequested)
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { /* command finished between snapshot and Cancel — fine */ }
        }
    }

    protected InteractiveRunnerBase(
        IInteractiveCommandRegistry defaults,
        IInteractiveCommandRegistry specific,
        IConsoleAdapter console,
        ILogger logger,
        IServiceProvider services)
    {
        Defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));
        Specific = specific ?? throw new ArgumentNullException(nameof(specific));
        Console = console ?? throw new ArgumentNullException(nameof(console));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    protected abstract string Banner { get; }
    protected abstract string Prompt { get; }

    /// <summary>
    /// When <see langword="true"/>, only a line whose <em>first element</em> is a <c>/</c>-prefixed
    /// token is treated as a command (e.g. <c>/help</c>); every other line is routed verbatim to
    /// <see cref="ResolveFallback"/> (the free-text → LLM path). When <see langword="false"/>
    /// (default), the legacy behaviour applies: commands are matched bare and only unrecognised
    /// input falls through to the fallback. Overridden by the scripted coding-agent REPL.
    /// </summary>
    protected virtual bool SlashCommandsOnly => false;

    /// <summary>
    /// The command invoked for input that matches no command. Defaults to the specific registry's
    /// <see cref="IInteractiveCommandRegistry.Fallback"/>; a runner may override to resolve a
    /// fallback command by another rule (e.g. the scripted REPL's <c>assistant</c>).
    /// </summary>
    protected virtual IInteractiveCommand? ResolveFallback() => Specific.Fallback;

    protected virtual Task OnStartAsync(CancellationToken ct) => Task.CompletedTask;
    protected virtual Task OnExitAsync(CommandResult? lastResult, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Default 2-token hierarchical parser. Returns (CommandName, Args).
    /// Sees §5.2 of the spec.
    /// </summary>
    protected virtual (string Name, IReadOnlyList<string> Args) ParseCommandName(string trimmedInput)
    {
        ArgumentNullException.ThrowIfNull(trimmedInput);
        var parts = trimmedInput.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 2)
        {
            var candidate2 = parts[0] + " " + parts[1];
            if (KnowsCommand(candidate2))
            {
                IReadOnlyList<string> args2 = parts.Length == 3
                    ? parts[2].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    : Array.Empty<string>();
                return (candidate2, args2);
            }
        }

        IReadOnlyList<string> args = parts.Length >= 2
            ? trimmedInput[parts[0].Length..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();
        return (parts[0], args);
    }

    private bool KnowsCommand(string name)
        => MatchCommand(Specific, name) is not null
        || MatchCommand(Defaults, name) is not null;

    private static IInteractiveCommand? MatchCommand(IInteractiveCommandRegistry registry, string name)
    {
        foreach (var cmd in registry.Commands)
        {
            if (string.Equals(cmd.Name, name, StringComparison.OrdinalIgnoreCase))
                return cmd;

            if (cmd.Aliases.Any(alias => string.Equals(alias, name, StringComparison.OrdinalIgnoreCase)))
                return cmd;
        }
        return null;
    }

    /// <summary>Default behavior for unknown commands. Override to e.g. suggest similar commands.</summary>
    protected virtual Task OnUnknownCommandAsync(string input, CancellationToken ct)
    {
        Console.WriteLine($"Unknown command: '{input}'. Type 'help' for available commands.");
        return Task.CompletedTask;
    }

    /// <summary>Run the REPL loop. Pushes the specific registry into <see cref="RunnerContext"/>.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort exit hook: a failing OnExitAsync in the finally must not mask the loop result or crash teardown; it is logged.")]
    public async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine(Banner);

        using var _ = RunnerContext.Push(Specific);

        await OnStartAsync(ct).ConfigureAwait(false);

        CommandResult? lastResult = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var (action, result) = await RunIterationAsync(ct).ConfigureAwait(false);
                if (result is not null)
                    lastResult = result;

                if (action == IterationAction.Break)
                    break;
            }
        }
        finally
        {
            try
            {
                await OnExitAsync(lastResult, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogOnExitFailed(Logger, ex);
            }
        }
    }

    /// <summary>Outcome of a single REPL iteration: whether to continue the loop and the produced result (if any).</summary>
    private enum IterationAction
    {
        Continue,
        Break
    }

    /// <summary>
    /// Executes a single REPL iteration: read input, resolve the command, run it.
    /// Returns the loop action and the command result (when one was produced).
    /// </summary>
    private async Task<(IterationAction Action, CommandResult? Result)> RunIterationAsync(CancellationToken ct)
    {
        using var scope = Services.CreateScope();

        Console.Write(Prompt);
        string? line;
        try
        {
            line = await Console.ReadLineAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return (IterationAction.Break, null);
        }

        if (line is null)
            return (IterationAction.Break, null);

        var trimmed = line.Trim();
        if (trimmed.Length == 0)
            return (IterationAction.Continue, null);

        var resolved = ResolveCommand(line, trimmed);
        if (resolved is null)
        {
            await OnUnknownCommandAsync(trimmed, ct).ConfigureAwait(false);
            return (IterationAction.Continue, null);
        }

        var ctx = new CommandContext
        {
            RawInput = resolved.RawInput,
            Args = resolved.Args,
            Console = Console,
            Scope = scope.ServiceProvider
        };

        return await ExecuteCommandAsync(resolved.Command, ctx, ct).ConfigureAwait(false);
    }

    /// <summary>Resolves the input to a command (matched or fallback). Returns null when no command applies.</summary>
    private ResolvedCommand? ResolveCommand(string rawLine, string trimmed)
    {
        if (SlashCommandsOnly)
            return ResolveSlashOrFreeText(rawLine, trimmed);

        var (name, parsedArgs) = ParseCommandName(trimmed);
        var command = MatchCommand(Specific, name) ?? MatchCommand(Defaults, name);

        if (command is not null)
            return new ResolvedCommand(command, parsedArgs, rawLine);

        var fallback = ResolveFallback();
        if (fallback is not null)
        {
            var args = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return new ResolvedCommand(fallback, args, rawLine);
        }

        return null;
    }

    /// <summary>
    /// Slash-only resolution (<see cref="SlashCommandsOnly"/>): a leading <c>/</c> selects a command
    /// by its first token; anything else — including a line that opens with <c>@</c> (a file
    /// reference) — is free text handed to <see cref="ResolveFallback"/>. A <c>/</c>-prefixed token
    /// that matches nothing returns <see langword="null"/> (→ unknown-command), so a mistyped command
    /// is never silently sent to the LLM.
    /// </summary>
    private ResolvedCommand? ResolveSlashOrFreeText(string rawLine, string trimmed)
    {
        if (trimmed.StartsWith('/'))
        {
            var body = trimmed[1..].TrimStart();
            if (body.Length == 0)
                return null;

            var (name, parsedArgs) = ParseCommandName(body);
            var command = MatchCommand(Specific, name) ?? MatchCommand(Defaults, name);
            return command is not null ? new ResolvedCommand(command, parsedArgs, rawLine) : null;
        }

        var fallback = ResolveFallback();
        if (fallback is null)
            return null;

        var args = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new ResolvedCommand(fallback, args, rawLine);
    }

    /// <summary>Runs a resolved command under a per-command CTS, handling cancellation and errors.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "REPL fault barrier: a single failing command is logged and reported to the console so it cannot crash the interactive loop; cancellation is rethrown via the specific OperationCanceledException catches.")]
    private async Task<(IterationAction Action, CommandResult? Result)> ExecuteCommandAsync(
        IInteractiveCommand command, CommandContext ctx, CancellationToken ct)
    {
        CommandResult result;
        using var commandCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _currentCommandName = command.Name;
        Interlocked.Exchange(ref _currentCommandStartedAtUtcTicks, DateTime.UtcNow.Ticks);
        _currentCommandCts = commandCts;
        try
        {
            result = await command.ExecuteAsync(ctx, commandCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return (IterationAction.Break, null);
        }
        catch (OperationCanceledException)
        {
            LogCommandCancelled(Logger, command.Name);
            Console.WriteLine($"  Command '{command.Name}' cancelled.");
            return (IterationAction.Continue, null);
        }
        catch (Exception ex)
        {
            LogCommandFailed(Logger, ex, command.Name);
            Console.WriteLine($"Error: {ex.Message}");
            return (IterationAction.Continue, null);
        }
        finally
        {
            _currentCommandCts = null;
            _currentCommandName = null;
            Interlocked.Exchange(ref _currentCommandStartedAtUtcTicks, 0);
        }

        if (!string.IsNullOrEmpty(result.Message))
            Console.WriteLine(result.Message);

        return (result.ShouldExit ? IterationAction.Break : IterationAction.Continue, result);
    }

    /// <summary>A command resolved from user input, with its parsed arguments and the raw input line.</summary>
    private sealed record ResolvedCommand(IInteractiveCommand Command, IReadOnlyList<string> Args, string RawInput);

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "OnExitAsync threw an exception")]
    static partial void LogOnExitFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Command '{Command}' was cancelled by the user")]
    static partial void LogCommandCancelled(ILogger logger, string command);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Command '{Command}' threw an exception")]
    static partial void LogCommandFailed(ILogger logger, Exception ex, string command);
}
