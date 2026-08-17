using Orkeon.Cli.Abstractions.Commands;

namespace Orkeon.Cli.Commands.Scripting.Dispatch.Commands;

/// <summary>
/// Factory for the runner-integrated introspection / control commands (design §6, §8 item 10):
/// <c>ps</c>, <c>inspect</c>, <c>result</c>, <c>cancel</c>. They read (and, for <c>cancel</c>,
/// mutate) the shared <see cref="CommandInstanceRegistry"/> and are fast sync commands that
/// stay responsive while detached async commands run.
/// </summary>
public static class DispatchBuiltinCommands
{
    /// <summary>Builds the four built-ins over <paramref name="dispatch"/>.</summary>
    public static IReadOnlyList<IInteractiveCommand> Create(CommandDispatchService dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        return new IInteractiveCommand[]
        {
            new PsCommand(dispatch.Registry),
            new InspectCommand(dispatch.Registry),
            new ResultCommand(dispatch.Registry),
            new CancelCommand(dispatch),
        };
    }

    /// <summary>Reads the leading <c>--flag=value</c> / <c>--flag value</c> / bare token for a flag.</summary>
    internal static string? ReadOption(IReadOnlyList<string> args, string flag)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];
            if (token.StartsWith("--" + flag + "=", StringComparison.OrdinalIgnoreCase))
                return token[(flag.Length + 3)..];
            if (string.Equals(token, "--" + flag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                return args[i + 1];
        }
        // Bare first positional token (e.g. `inspect t3`).
        return args.FirstOrDefault(a => !a.StartsWith('-'));
    }
}

/// <summary>Lists running (or filtered) command instances.</summary>
internal sealed class PsCommand(CommandInstanceRegistry registry) : IInteractiveCommand
{
    public string Name => "ps";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "List in-flight (or filtered) dispatched commands.";

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var stateArg = DispatchBuiltinCommands.ReadOption(context.Args, "state") ?? "running";
        var filter = stateArg.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? CommandInstanceFilter.All
            : new CommandInstanceFilter(State: ParseState(stateArg));

        var rows = registry.List(filter);
        if (rows.Count == 0)
        {
            context.Console.WriteLine("(no commands)");
            return Task.FromResult(CommandResult.Continue());
        }

        context.Console.WriteLine($"{"TICKET",-8}{"CMD",-16}{"AGENT",-16}{"STATE",-12}ELAPSED");
        foreach (var r in rows)
            context.Console.WriteLine($"{r.ticket,-8}{Trim(r.name, 15),-16}{Trim(r.targetAgent, 15),-16}{r.state,-12}{r.elapsedMs}ms");
        return Task.FromResult(CommandResult.Continue());
    }

    private static CommandInstanceState? ParseState(string token) =>
#pragma warning disable CA1308 // normalized key for a switch; lowercase is the required form, not a comparison normalization
        token.ToLowerInvariant() switch
    {
        "running" => CommandInstanceState.Running,
        "done" => CommandInstanceState.Done,
        "failed" => CommandInstanceState.Failed,
        "cancelled" or "canceled" => CommandInstanceState.Cancelled,
        "rejected" => CommandInstanceState.Rejected,
        _ => null,
    };
#pragma warning restore CA1308

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}

/// <summary>Shows the detail of one command instance by ticket.</summary>
internal sealed class InspectCommand(CommandInstanceRegistry registry) : IInteractiveCommand
{
    public string Name => "inspect";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "Show details of a dispatched command by its ticket.";

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var ticket = DispatchBuiltinCommands.ReadOption(context.Args, "ticket");
        if (string.IsNullOrWhiteSpace(ticket))
            return Task.FromResult(CommandResult.Continue("Usage: inspect --ticket=<ticket>"));

        var view = registry.Get(ticket)?.Snapshot();
        if (view is null)
            return Task.FromResult(CommandResult.Continue($"Unknown ticket '{ticket}'."));

        var c = context.Console;
        c.WriteLine($"ticket    : {view.ticket}");
        c.WriteLine($"command   : {view.name} ({view.kind})");
        c.WriteLine($"agent     : {view.targetAgent}");
        c.WriteLine($"intent    : {view.intent}");
        c.WriteLine($"state     : {view.state}");
        c.WriteLine($"elapsed   : {view.elapsedMs}ms");
        c.WriteLine($"corrId    : {view.correlationId}");
        if (view.tokens > 0) c.WriteLine($"tokens    : {view.tokens}");
        if (view.progress?.message is { } pm) c.WriteLine($"progress  : {pm}");
        if (view.result is { } res) c.WriteLine($"result    : {res.payload}");
        if (view.error is { } err) c.WriteLine($"error     : {err}");
        return Task.FromResult(CommandResult.Continue());
    }
}

/// <summary>Prints the result payload of a ticket (poll ⊂ get).</summary>
internal sealed class ResultCommand(CommandInstanceRegistry registry) : IInteractiveCommand
{
    public string Name => "result";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "Print the result payload of a dispatched command by ticket.";

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var ticket = DispatchBuiltinCommands.ReadOption(context.Args, "ticket");
        if (string.IsNullOrWhiteSpace(ticket))
            return Task.FromResult(CommandResult.Continue("Usage: result --ticket=<ticket>"));

        var view = registry.Get(ticket)?.Snapshot();
        if (view is null)
            return Task.FromResult(CommandResult.Continue($"Unknown ticket '{ticket}'."));

        return Task.FromResult(view.state switch
        {
            "running" => CommandResult.Continue($"[{ticket}] still running ({view.elapsedMs}ms)."),
            "done" => CommandResult.Continue($"[{ticket}] {view.result?.payload}"),
            _ => CommandResult.Continue($"[{ticket}] {view.state}: {view.error ?? "(no detail)"}"),
        });
    }
}

/// <summary>Requests cancellation of an in-flight ticket.</summary>
internal sealed class CancelCommand(CommandDispatchService dispatch) : IInteractiveCommand
{
    public string Name => "cancel";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "Cancel an in-flight dispatched command by ticket.";

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var ticket = DispatchBuiltinCommands.ReadOption(context.Args, "ticket");
        if (string.IsNullOrWhiteSpace(ticket))
            return Task.FromResult(CommandResult.Continue("Usage: cancel --ticket=<ticket>"));

        var ok = dispatch.cancel(ticket);
        return Task.FromResult(CommandResult.Continue(
            ok ? $"[{ticket}] cancellation requested." : $"[{ticket}] not cancellable (unknown or already terminal)."));
    }
}
