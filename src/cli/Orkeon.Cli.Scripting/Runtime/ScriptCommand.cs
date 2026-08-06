using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Scripting.Args;
using Orkeon.Cli.Scripting.Bindings;
using Orkeon.Cli.Scripting.Dispatch;
using Orkeon.Cli.Scripting.Loading;
using Orkeon.Cli.Scripting.Telemetry;
using Orkeon.Scripting.Telemetry;

namespace Orkeon.Cli.Scripting.Runtime;

/// <summary>
/// <see cref="IInteractiveCommand"/> implementation backed by a JS closure defined via
/// <c>defineCommand</c> (sync) or <c>defineAsyncCommand</c> (async) in a <c>*.cmd.ts</c> file.
/// </summary>
/// <remarks>
/// <para>
/// Aiguillage (design §8 item 8): a sync command awaits its <c>handler</c> before the prompt
/// returns; an async command runs <c>dispatch</c>, detaches, and replays <c>completed</c> on
/// the next engine pump. Both are framed by the per-command admission gate (§5) and a
/// completion drain (§4.3).
/// </para>
/// <para>
/// The Jint <see cref="Engine"/> is owned by the loader and shared across invocations of the
/// same script; a per-engine <see cref="SemaphoreSlim"/> serialises them (Jint is not
/// thread-safe — §6.3 spec).
/// </para>
/// </remarks>
public sealed partial class ScriptCommand : IInteractiveCommand
{
    private readonly Engine _engine;
    private readonly CommandDescriptor _descriptor;
    private readonly SemaphoreSlim _engineLock;
    private readonly CompletionDrainQueue _drainQueue;
    private readonly ScriptServiceLocator _services;
    private readonly CommandDispatchService? _dispatch;
    private readonly Progress.ProgressBroker? _progress;
    private readonly ILogger _logger;

    // Per-command admission quota (design §5). Non-null only for async commands declaring
    // maxConcurrent; a sync command is naturally serialised by the engine lock.
    private readonly SemaphoreSlim? _admissionGate;

    public ScriptCommand(
        ScriptCommandBinding binding,
        ScriptCommandServices? hostServices = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        _engine = binding.Engine ?? throw new ArgumentNullException(nameof(binding));
        _descriptor = binding.Descriptor ?? throw new ArgumentNullException(nameof(binding));
        _engineLock = binding.EngineLock ?? throw new ArgumentNullException(nameof(binding));
        _drainQueue = binding.DrainQueue ?? throw new ArgumentNullException(nameof(binding));

        var services = hostServices ?? ScriptCommandServices.Empty;
        _services = services.Services ?? ScriptServiceLocator.Empty;
        _dispatch = services.Dispatch;
        _progress = services.Progress;
        _logger = services.Logger ?? NullLogger.Instance;
        _admissionGate = _descriptor.MaxConcurrent is { } max ? new SemaphoreSlim(max, max) : null;
    }

    /// <summary>Descriptor backing this command (exposed for the <c>help-cmd</c> formatter).</summary>
    public CommandDescriptor Descriptor => _descriptor;

    /// <inheritdoc />
    public string Name => _descriptor.Name;

    /// <inheritdoc />
    public IReadOnlyList<string> Aliases => _descriptor.Aliases;

    /// <inheritdoc />
    public string Description => _descriptor.Description;

    /// <summary>Virtual path of the source script — used by telemetry and diagnostics.</summary>
    public string SourceVirtualPath => _descriptor.SourceVirtualPath;

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ExecuteCoreAsync(context, cancellationToken);
    }

    private async Task<CommandResult> ExecuteCoreAsync(CommandContext context, CancellationToken cancellationToken)
    {
        using var activity = ScriptingActivitySource.Instance.StartActivity("cli.command", System.Diagnostics.ActivityKind.Internal);
        activity?.SetTag(CliScriptingTags.CommandSource, $"script:{_descriptor.SourceVirtualPath}");
        activity?.SetTag(CliScriptingTags.CommandName, _descriptor.Name);

        await _engineLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Pump point: replay any async completions that settled since the last invocation
            // on this engine (design §4.3) — under the lock, on the engine thread.
            await DrainCompletionsAsync(context.Console).ConfigureAwait(false);

            // Parse args once; both sync handlers and async dispatch receive the same shape.
            if (!TryBuildArgs(context, out var argsObj, out var argsError))
                return CommandResult.Continue($"Error: {argsError}");

            return _descriptor.Kind == CommandKind.Async
                ? await ExecuteAsyncCommand(context, argsObj!, cancellationToken).ConfigureAwait(false)
                : await ExecuteSyncCommand(context, argsObj!, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _engineLock.Release();
        }
    }

    // ---- sync (defineCommand) -------------------------------------------------------------

    private async Task<CommandResult> ExecuteSyncCommand(CommandContext context, JsValue argsObj, CancellationToken ct)
    {
        var ctxObj = PushContext(context.Console, ct);
        using var scope = _dispatch is not null ? CommandDispatchService.BeginCommand(_descriptor.Name, ct) : null;

        JsValue result;
        try
        {
            result = _engine.Invoke(_descriptor.Handler!, new[] { argsObj, ctxObj });
        }
        catch (Jint.Runtime.JavaScriptException jsEx)
        {
            LogSyncCommandThrew(jsEx, _descriptor.Name, _descriptor.SourceVirtualPath);
            return CommandResult.Continue($"Error: {jsEx.Message}");
        }

        if (result.IsPromise())
        {
            try
            {
                result = await result.UnwrapIfPromiseAsync(ct).ConfigureAwait(false);
            }
            catch (Jint.Runtime.PromiseRejectedException rejected)
            {
                LogSyncCommandRejected(_descriptor.Name, rejected.RejectedValue);
                return CommandResult.Continue($"Error: {rejected.RejectedValue}");
            }
        }

        return MapResult(result);
    }

    // ---- async (defineAsyncCommand) -------------------------------------------------------

    private async Task<CommandResult> ExecuteAsyncCommand(CommandContext context, JsValue argsObj, CancellationToken ct)
    {
        // Admission (design §5): non-blocking acquire; refusal rejects immediately and the
        // dispatch never runs.
        var gateAcquired = false;
        if (_admissionGate is not null)
        {
            if (!_admissionGate.Wait(0, ct))
            {
                var msg = $"quota of {_descriptor.MaxConcurrent} instance(s) of '{_descriptor.Name}' reached";
                _dispatch?.Registry.Register(_descriptor.Name, CommandInstanceKind.Async, "—", "—", Guid.Empty).Reject(msg);
                return CommandResult.Continue($"Rejected: {msg}.");
            }
            gateAcquired = true;
        }

        var releaseGate = gateAcquired;
        var ctxObj = PushContext(context.Console, ct);
        try
        {
            using var scope = _dispatch is not null ? CommandDispatchService.BeginCommand(_descriptor.Name, ct) : null;

            JsValue result;
            try
            {
                result = _engine.Invoke(_descriptor.Dispatch!, new[] { argsObj, ctxObj });

                // An `async dispatch` suspends at its first real await (e.g. a Task-backed
                // service call) and Invoke returns a PENDING promise: the rest of the body —
                // the actual launch — would then only run at the NEXT engine pump, i.e. when
                // the user happens to type another command. Await it here (same pattern as
                // the sync path above) so the launch always completes before the prompt
                // returns. The dispatch contract stays "launch fast and detach": the await
                // covers the launch preamble, never the detached background work.
                if (result.IsPromise())
                    result = await result.UnwrapIfPromiseAsync(ct).ConfigureAwait(false);
            }
            catch (Jint.Runtime.JavaScriptException jsEx)
            {
                LogAsyncDispatchThrew(jsEx, _descriptor.Name);
                return CommandResult.Continue($"Error: {jsEx.Message}");
            }
            catch (Jint.Runtime.PromiseRejectedException rejected)
            {
                LogAsyncDispatchRejected(_descriptor.Name, rejected.RejectedValue);
                return CommandResult.Continue($"Error: {ConciseErrors.Message(rejected)}");
            }

            var captured = scope?.Captured ?? Array.Empty<CommandInstance>();
            if (captured.Count > 0)
            {
                WireCompletions(context.Console, captured, gateAcquired ? _admissionGate : null);
                releaseGate = false; // ownership handed to the completion countdown
            }

            return MapResult(result);
        }
        finally
        {
            if (releaseGate)
                _admissionGate?.Release();
        }
    }

    /// <summary>
    /// Attaches the terminal callback to each instance a <c>dispatch</c> created: enqueue the
    /// <c>completed</c> drain, emit terse host feedback, and release the admission slot once
    /// every instance of this invocation is terminal (design §4.3, §5).
    /// </summary>
    private void WireCompletions(IConsoleAdapter console, IReadOnlyList<CommandInstance> instances, SemaphoreSlim? gate)
    {
        var remaining = new int[] { instances.Count };
        foreach (var instance in instances)
        {
            instance.AttachTerminalCallback(settled =>
            {
                _drainQueue.Enqueue(_descriptor.Completed, settled);

                // Immediate host-side feedback so the operator sees something without waiting
                // for the next pump (design §4.3). Thread-safe vs the engine: console only.
                var view = settled.Snapshot();
                var mark = view.state == "done" ? "✓" : "✗";
                console.WriteLine($"  {mark} [{view.ticket}] {view.name} → {view.targetAgent}: {view.state}");

                if (Interlocked.Decrement(ref remaining[0]) == 0)
                    gate?.Release();

                // Push-render: run the JS completed(result) callback now, instead of deferring
                // it to this command's next invocation. Off the pool thread, serialised on the
                // engine lock. Without this, an async reply (e.g. /assistant) stays invisible
                // until the user happens to re-run the same command — other commands pump only
                // their own queue, and built-ins don't pump at all.
                _ = PushDrainAsync(console);
            });
        }
    }

    /// <summary>
    /// Push variant of the completion pump (design §4.3): acquires the engine lock off the pool
    /// thread the moment async work settles and runs <see cref="DrainCompletionsAsync"/>, so the
    /// <c>completed(result)</c> callback fires immediately rather than waiting for this command's
    /// next invocation. Serialised with command execution via <see cref="_engineLock"/>; the
    /// pull-path drain in <see cref="ExecuteCoreAsync"/> stays as a safety net (the queue's
    /// dequeue is idempotent, so a completion is never rendered twice).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fire-and-forget push-drain fault barrier: a failure running a JS completion callback off the pool thread is logged and must not crash the detached drain task.")]
    private async Task PushDrainAsync(IConsoleAdapter console)
    {
        try
        {
            await _engineLock.WaitAsync().ConfigureAwait(false);
            try { await DrainCompletionsAsync(console).ConfigureAwait(false); }
            finally { _engineLock.Release(); }
        }
        catch (Exception ex)
        {
            LogPushDrainFailed(ex, _descriptor.Name);
        }
    }

    private async Task DrainCompletionsAsync(IConsoleAdapter console)
    {
        var pending = _drainQueue.DrainAll();
        if (pending.Count == 0) return;

        foreach (var item in pending)
        {
            if (item.Completed is null) continue;

            var view = item.Instance.Snapshot();
            var response = view.result ?? new CommandResponse(view.targetAgent, view.intent, success: false, payload: string.Empty, error: view.error);
            var resultVal = JsValue.FromObject(_engine, response);
            var ctxObj = PushContext(console, CancellationToken.None);

            try
            {
                var r = _engine.Invoke(item.Completed, new[] { resultVal, ctxObj });
                // Same pending-promise rule as dispatch: an async completed() must finish
                // its body now, not at some future engine pump.
                if (r.IsPromise()) await r.UnwrapIfPromiseAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Jint.Runtime.JavaScriptException jsEx)
            {
                LogCompletedThrew(jsEx, item.Instance.Name, item.Instance.Ticket);
                console.WriteLine($"Error in completed('{item.Instance.Name}'): {jsEx.Message}");
            }
            catch (Jint.Runtime.PromiseRejectedException rejected)
            {
                LogCompletedThrew(rejected, item.Instance.Name, item.Instance.Ticket);
                console.WriteLine($"Error in completed('{item.Instance.Name}'): {ConciseErrors.Message(rejected)}");
            }
        }
    }

    // ---- shared helpers -------------------------------------------------------------------

    private JsValue PushContext(IConsoleAdapter console, CancellationToken ct)
    {
        var runtimeCtx = new CommandRuntimeContext(
            engine: _engine,
            console: console,
            command: new CommandMeta(_descriptor.Name, _descriptor.Name),
            ct: ct,
            logger: _logger,
            services: _services,
            progressBroker: _progress);
        return ContextBinding.Push(_engine, runtimeCtx);
    }

    private bool TryBuildArgs(CommandContext context, out JsValue? argsObj, out string? error)
    {
        error = null;
        if (_descriptor.HasArgsSchema)
        {
            var parsed = ArgsTokenParser.Parse(context.Args, _descriptor.ArgsSchema);
            var verdict = ArgsValidator.Validate(parsed, _descriptor.ArgsSchema);
            if (!verdict.IsSuccess)
            {
                argsObj = null;
                error = string.Join("; ", verdict.Errors);
                return false;
            }
            argsObj = BuildTypedArgs(_engine, verdict.Values!);
            return true;
        }

        argsObj = JsValue.FromObject(_engine, new { raw = context.Args.ToArray() });
        return true;
    }

    private static JsValue BuildTypedArgs(Engine engine, System.Collections.Immutable.ImmutableDictionary<string, object?> values)
    {
        var bag = new Dictionary<string, object?>(values.Count);
        foreach (var kv in values)
        {
            bag[kv.Key] = kv.Value switch
            {
                System.Collections.Immutable.ImmutableArray<string> arr => arr.ToArray(),
                _ => kv.Value,
            };
        }
        return JsValue.FromObject(engine, bag);
    }

    private static CommandResult MapResult(JsValue result)
    {
        if (result.IsUndefined() || result.IsNull())
            return CommandResult.Continue();

        var clr = result.ToObject();
        if (clr is CommandActionResult action)
            return BuildCommandResult(action.exit, action.message);

        if (result.IsObject())
            return MapObjectResult(result.AsObject());

        return CommandResult.Continue();
    }

    private static CommandResult MapObjectResult(Jint.Native.Object.ObjectInstance obj)
    {
        var exitVal = obj.Get("exit");
        var messageVal = obj.Get("message");
        var exit = !exitVal.IsUndefined() && exitVal.IsBoolean() && exitVal.AsBoolean();
        var message = messageVal.IsString() ? messageVal.AsString() : null;
        return BuildCommandResult(exit, message);
    }

    private static CommandResult BuildCommandResult(bool exit, string? message)
    {
        var hasMessage = !string.IsNullOrEmpty(message);
        if (exit)
        {
            return hasMessage ? CommandResult.Exit(message!) : CommandResult.Exit();
        }

        return hasMessage ? CommandResult.Continue(message!) : CommandResult.Continue();
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Error,
        Message = "Scripted command '{Command}' from {Source} threw")]
    partial void LogSyncCommandThrew(Exception ex, string command, string source);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error,
        Message = "Scripted command '{Command}' rejected: {Reason}")]
    partial void LogSyncCommandRejected(string command, object? reason);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error,
        Message = "Async command '{Command}' dispatch threw")]
    partial void LogAsyncDispatchThrew(Exception ex, string command);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error,
        Message = "Async command '{Command}' dispatch rejected: {Reason}")]
    partial void LogAsyncDispatchRejected(string command, object? reason);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error,
        Message = "Push-drain of completions for '{Command}' failed")]
    partial void LogPushDrainFailed(Exception ex, string command);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error,
        Message = "Async command '{Command}' completed() threw (ticket {Ticket})")]
    partial void LogCompletedThrew(Exception ex, string command, string ticket);
}

/// <summary>
/// The Jint engine binding a <see cref="ScriptCommand"/> runs against: the shared engine, its
/// descriptor, the per-engine serialisation lock, and the completion drain. Grouped to keep the
/// <see cref="ScriptCommand"/> constructor narrow. Prompt handling is provided by the static
/// <see cref="PromptDispatcher"/>, so it is no longer carried here.
/// </summary>
public sealed record ScriptCommandBinding
{
    public required Engine Engine { get; init; }
    public required CommandDescriptor Descriptor { get; init; }
    public required SemaphoreSlim EngineLock { get; init; }
    public required CompletionDrainQueue DrainQueue { get; init; }
}

/// <summary>
/// Optional host services injected into a <see cref="ScriptCommand"/>: the script service
/// locator, the dispatch service for async commands, and a logger. All optional — defaults
/// fall back to empty/null-object instances.
/// </summary>
public sealed record ScriptCommandServices
{
    public static ScriptCommandServices Empty { get; } = new();

    public ScriptServiceLocator? Services { get; init; }
    public CommandDispatchService? Dispatch { get; init; }
    public Progress.ProgressBroker? Progress { get; init; }
    public ILogger? Logger { get; init; }
}
