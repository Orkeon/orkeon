using System.Collections.Concurrent;
using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;

namespace Orkeon.Cli.Scripting.Dispatch;

#pragma warning disable IDE1006 // camelCase methods: this CLR type is exposed to scripts as ctx.services.get("commands")
/// <summary>
/// Host-side façade exposed to scripts as <c>commands</c> (design §4, §8 item 1). Turns the
/// name-addressed <c>request</c>/<c>post</c>/<c>list</c>/<c>get</c>/<c>cancel</c> verbs into
/// correlated <see cref="IAgentChannel"/> traffic and bookkeeping in the
/// <see cref="CommandInstanceRegistry"/>.
/// </summary>
/// <remarks>
/// <para>
/// A singleton: the agent that registers (<c>onCommand</c>) and the command that dispatches
/// must share the same channel, directory, and registry even across different Jint engines
/// (design §3 — completion lives host-side, keyed by correlation id).
/// </para>
/// <para>
/// <c>request</c> returns a <see cref="Task{TResult}"/> Jint awaits as a Promise; <c>post</c>
/// fires the request on a pool thread and returns a ticket immediately. JS is never called
/// back from the pool thread — the agent's response only mutates host-side state (§2, §4.3).
/// </para>
/// </remarks>
public sealed partial class CommandDispatchService
{
    private readonly IAgentChannel _channel;
    private readonly AgentCommandDirectory _directory;
    private readonly CommandInstanceRegistry _registry;
    private readonly ILogger _logger;
    private readonly AgentId _cliAgentId = AgentId.Create();

    // Per-instance CTS for detached async work, so cancel(ticket) can interrupt an in-flight
    // post without affecting the (already-returned) REPL command's token.
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _asyncCts = new(StringComparer.Ordinal);

    private static readonly AsyncLocal<CommandDispatchScope?> _ambient = new();

    public CommandDispatchService(
        IAgentChannel channel,
        AgentCommandDirectory directory,
        CommandInstanceRegistry registry,
        ILogger<CommandDispatchService>? logger = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <summary>The instance registry (used by built-in ps/inspect/result/cancel commands).</summary>
    public CommandInstanceRegistry Registry => _registry;

    /// <summary>The name→id directory (used by the agent-side onCommand seam).</summary>
    public AgentCommandDirectory Directory => _directory;

    /// <summary>The shared transport — the same channel agents must register on (design §3).</summary>
    public IAgentChannel Channel => _channel;

    // ---- Ambient command scope ------------------------------------------------------------

    /// <summary>
    /// Pushes the ambient command scope. The runner wraps each handler / <c>dispatch</c> call
    /// in this so <c>request</c>/<c>post</c> can read the command name + token and record the
    /// async instances created during <c>dispatch</c>.
    /// </summary>
    public static CommandDispatchScope BeginCommand(string commandName, CancellationToken ct, TimeSpan? timeout = null)
    {
        var previous = _ambient.Value;
        var scope = new CommandDispatchScope(commandName ?? string.Empty, timeout, () => _ambient.Value = previous, ct);
        _ambient.Value = scope;
        return scope;
    }

    // ---- JS surface -----------------------------------------------------------------------

    /// <summary>
    /// Dispatches to an agent and returns its response (design §4.1). Returns the value directly
    /// (not a Task): a sync command blocks on the engine thread, and JS <c>await</c> on a plain
    /// value resolves to it — this avoids depending on Jint's CLR-Task→Promise conversion, which
    /// the rest of the DSL doesn't rely on either.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Service-boundary fault barrier: any agent/dispatch failure is recorded on the instance and returned as a failure CommandResponse so one request cannot crash the script engine; cancellation is rethrown.")]
    public CommandResponse request(string agent, string intent, string payload)
    {
        var scope = _ambient.Value;
        var agentId = ResolveAgent(agent);
        var correlationId = Guid.NewGuid();
        var instance = _registry.Register(scope?.CommandName ?? "request", CommandInstanceKind.Sync, agent, intent, correlationId);
        var ct = scope?.CancellationToken ?? CancellationToken.None; // Ctrl-C interrupts the wait.
        try
        {
            var response = SendAsync(agentId, agent, intent, payload, correlationId, scope?.Timeout, ct).GetAwaiter().GetResult();
            if (response.success) instance.Complete(response);
            else instance.Fail(response.error ?? "agent returned a failure response");
            return response;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            instance.Cancel();
            throw;
        }
        catch (Exception ex)
        {
            LogRequestFailed(ex, instance.Name, agent);
            instance.Fail(ex.Message);
            return new CommandResponse(agent, intent, success: false, payload: string.Empty, error: ex.Message);
        }
    }

    /// <summary>Dispatches to an agent in the background and returns a ticket immediately. Design §4.2.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fire-and-forget dispatch fault barrier: a background agent failure is logged and recorded on the instance so it cannot crash the detached task; cancellation marks the instance cancelled.")]
    public string post(string agent, string intent, string payload)
    {
        var scope = _ambient.Value;
        var agentId = ResolveAgent(agent);
        var correlationId = Guid.NewGuid();
        var instance = _registry.Register(scope?.CommandName ?? "post", CommandInstanceKind.Async, agent, intent, correlationId);
        scope?.Capture(instance);

        // Detached: a fresh CTS unlinked from the REPL command token (which is disposed the
        // instant the fast dispatch returns). cancel(ticket) cancels this one.
        var cts = scope?.Timeout is { } t ? new CancellationTokenSource(t) : new CancellationTokenSource();
        _asyncCts[instance.Ticket] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                var response = await SendAsync(agentId, agent, intent, payload, correlationId, scope?.Timeout, cts.Token).ConfigureAwait(false);
                if (response.success) instance.Complete(response);
                else instance.Fail(response.error ?? "agent returned a failure response");
            }
            catch (OperationCanceledException)
            {
                instance.Cancel();
            }
            catch (Exception ex)
            {
                LogAsyncCommandFailed(ex, instance.Name, instance.Ticket, agent);
                instance.Fail(ex.Message);
            }
            finally
            {
                if (_asyncCts.TryRemove(instance.Ticket, out var owned)) owned.Dispose();
            }
        }, CancellationToken.None);

        return instance.Ticket;
    }

    /// <summary>
    /// Runs arbitrary host-side work in the background and returns a ticket immediately, reusing
    /// the exact ticket → terminal → <c>completed</c> drain cycle as <see cref="post"/>. Unlike
    /// <see cref="post"/> (which dispatches to a named agent over the channel), the work is an
    /// opaque <see cref="Func{T, TResult}"/> — used by <c>script-host.runCrewAsync</c> to launch a
    /// crew (exp 07 SPEC §6). The instance is captured in the ambient scope so the runner wires its
    /// completion onto the engine's drain queue. JS is never called back from the pool thread.
    /// </summary>
    /// <param name="label">Logical name for the instance (e.g. the crew name). Used as the intent.</param>
    /// <param name="target">Target label (e.g. "crew:project-init"). Cosmetic — for ps/inspect.</param>
    /// <param name="work">The host work; receives the per-ticket cancellation token.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fire-and-forget host-work fault barrier: a background work failure is logged and recorded on the instance so it cannot crash the detached task; cancellation marks the instance cancelled.")]
    public string postWork(string label, string target, Func<CancellationToken, Task<CommandResponse>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var scope = _ambient.Value;
        var correlationId = Guid.NewGuid();
        var instance = _registry.Register(
            scope?.CommandName ?? label ?? "postWork", CommandInstanceKind.Async, target ?? string.Empty, label ?? string.Empty, correlationId);
        scope?.Capture(instance);

        var cts = scope?.Timeout is { } t ? new CancellationTokenSource(t) : new CancellationTokenSource();
        _asyncCts[instance.Ticket] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                var response = await work(cts.Token).ConfigureAwait(false);
                if (response.success) instance.Complete(response);
                else instance.Fail(response.error ?? "host work returned a failure response");
            }
            catch (OperationCanceledException)
            {
                instance.Cancel();
            }
            catch (Exception ex)
            {
                LogAsyncHostWorkFailed(ex, label, instance.Ticket);
                instance.Fail(ex.Message);
            }
            finally
            {
                if (_asyncCts.TryRemove(instance.Ticket, out var owned)) owned.Dispose();
            }
        }, CancellationToken.None);

        return instance.Ticket;
    }

    /// <summary>Lists in-flight / recent instances matching an optional filter. Design §6.</summary>
    public IReadOnlyList<CommandInstanceView> list(JsValue? filter)
        => _registry.List(ParseFilter(filter));

    /// <summary>Lists all in-flight / recent instances.</summary>
    public IReadOnlyList<CommandInstanceView> list() => _registry.List(CommandInstanceFilter.All);

    /// <summary>Returns the full view for a ticket, or <see langword="null"/>. (<c>poll ⊂ get</c>, design §6.)</summary>
    public CommandInstanceView? get(string ticket) => _registry.Get(ticket)?.Snapshot();

    /// <summary>Requests cancellation of an in-flight ticket. Returns false when the ticket is unknown or already terminal.</summary>
    public bool cancel(string ticket)
    {
        var instance = _registry.Get(ticket);
        if (instance is null || instance.IsTerminal) return false;
        if (_asyncCts.TryGetValue(ticket, out var cts))
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { /* completed concurrently */ }
        }
        instance.Cancel("cancelled via cancel()");
        return true;
    }

    // ---- internals ------------------------------------------------------------------------

    private async Task<CommandResponse> SendAsync(
        AgentId agentId, string agent, string intent, string payload, Guid correlationId, TimeSpan? timeout, CancellationToken ct)
    {
        var request = new AgentChannelRequest(correlationId, _cliAgentId, agentId, intent ?? string.Empty, payload ?? string.Empty);
        var response = await _channel.RequestAsync(request, timeout, ct).ConfigureAwait(false);
        return new CommandResponse(agent, intent ?? string.Empty, response.Success, response.Payload, response.Error);
    }

    private AgentId ResolveAgent(string agent)
    {
        if (string.IsNullOrWhiteSpace(agent))
            throw new InvalidOperationException("commands: an agent name is required.");
        if (_directory.Resolve(agent) is { } resolved)
            return resolved;

        var names = _directory.GetNames();
        throw new InvalidOperationException(
            $"commands: unknown agent '{agent}'. Registered: {(names.Count == 0 ? "(none)" : string.Join(", ", names))}.");
    }

    private static CommandInstanceFilter ParseFilter(JsValue? filter)
    {
        if (filter is null || filter.IsUndefined() || filter.IsNull())
            return CommandInstanceFilter.All;

        // Allow a bare state token: commands.list("running").
        if (filter.IsString())
            return new CommandInstanceFilter(State: ParseState(filter.AsString()));

        if (!filter.IsObject())
            return CommandInstanceFilter.All;

        var obj = filter.AsObject();
        var stateVal = obj.Get("state");
        var nameVal = obj.Get("name");
        var agentVal = obj.Get("agent");
        return new CommandInstanceFilter(
            State: stateVal.IsString() ? ParseState(stateVal.AsString()) : null,
            Name: nameVal.IsString() ? nameVal.AsString() : null,
            Agent: agentVal.IsString() ? agentVal.AsString() : null);
    }

    private static CommandInstanceState? ParseState(string token) =>
#pragma warning disable CA1308 // normalized key for a switch; lowercase is the required form, not a comparison normalization
        token?.ToLowerInvariant() switch
    {
        "running" => CommandInstanceState.Running,
        "done" => CommandInstanceState.Done,
        "failed" => CommandInstanceState.Failed,
        "cancelled" or "canceled" => CommandInstanceState.Cancelled,
        "rejected" => CommandInstanceState.Rejected,
        "all" or "" or null => null,
        _ => null,
    };
#pragma warning restore CA1308

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Error,
        Message = "Command '{Command}' request to agent '{Agent}' failed")]
    partial void LogRequestFailed(Exception ex, string command, string agent);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error,
        Message = "Async command '{Command}' (ticket {Ticket}) to agent '{Agent}' failed")]
    partial void LogAsyncCommandFailed(Exception ex, string command, string ticket, string agent);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error,
        Message = "Async host work '{Label}' (ticket {Ticket}) failed")]
    partial void LogAsyncHostWorkFailed(Exception ex, string? label, string ticket);
}
#pragma warning restore IDE1006
