using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew;
using Orkeon.Application.EventHub;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Tools;
using Orkeon.Scripting.Cli.Events;

namespace Orkeon.Scripting.Cli.Commands.Run;

/// <summary>
/// Everything an observed run wires, in one place — because there are two hosts to wire it
/// into. The YAML/directory path goes through the shared runner and the <c>.ork.ts</c> path
/// builds its own host; the first version of this wiring lived inside the first path only,
/// and <c>--events</c> on a script was silently a no-op: Studio launched, added the flag for
/// both dialects, and watched "nothing reported yet" forever.
/// </summary>
internal sealed class ObservedRunContext : IAsyncDisposable
{
    private readonly OrkeonEventWriter _events;
    private readonly bool _stream;
    private readonly string _clientName;
    private RunEventObserver? _observer;

    // Written once by the IEventHub singleton factory (whatever thread first resolves the
    // hub), read by the command worker: volatile so a command cannot observe a stale null
    // after the bridge exists. A command arriving BEFORE the host has built the hub is
    // dropped by design — there is no hub to project it onto yet.
    private volatile JsonLinesEventHubBridge? _bridge;
    private bool _finished;

    /// <summary>Builds the context and starts the single stdin reader immediately.</summary>
    /// <param name="events">The outbound stream.</param>
    /// <param name="stream">Whether <c>llm.delta</c> events were asked for.</param>
    /// <param name="clientName">The peer's name on the hub (<c>client://{name}</c>).</param>
    public ObservedRunContext(OrkeonEventWriter events, bool stream, string clientName)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _stream = stream;
        _clientName = clientName;

        // One reader on stdin, routed by kind. Two would race, and BUS-04's channel dropped
        // every line that was not a human answer — including the hub commands.
        Inbound = new InboundCommandPump(
            Console.In,
            (line, ct) => _bridge?.HandleCommandAsync(line, ct) ?? System.Threading.Tasks.Task.CompletedTask);

        // Started unconditionally, not on the first human question: the peer's post/send/
        // subscribe lines arrive whenever the peer pleases, and a pump that only wakes up for
        // humanInput leaves every hub command unread on runs that never ask one — which is
        // most of them, and exactly how BUS-05 shipped dead.
        Inbound.EnsureRunning();
    }

    /// <summary>The single stdin reader; the human-input provider waits on it.</summary>
    public InboundCommandPump Inbound { get; }

    /// <summary>
    /// Registers the observed seams on <paramref name="services"/>: every tool decorated
    /// (the DI-registered ones here, the per-agent delegation pair through
    /// <see cref="IToolDecorator"/>), the execution hook composed rather than replaced, the
    /// human-input provider that never answers on the user's behalf, the hub bridge, and the
    /// console logs pushed to stderr — stdout carries the protocol, and a log line between
    /// two JSONL documents is a parser error on the client.
    /// </summary>
    public void WireServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Protocol purity: everything the loggers write goes to stderr.
        services.Configure<Microsoft.Extensions.Logging.Console.ConsoleLoggerOptions>(
            o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        // BUS-03: wrap every registered tool so its calls become events. Doing it here —
        // where tools enter the process — covers the agent loops and the scripting facade at
        // once. The decorator implements ITool, not just IBaseTool, because CrewFactory
        // assigns with `tool is ITool`: a base-only decorator would leave agents toolless.
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IBaseTool) && !d.IsKeyedService).ToList())
        {
            services.Remove(descriptor);
            services.Add(new ServiceDescriptor(
                typeof(IBaseTool),
                sp => new ObservedTool((IBaseTool)Materialize(sp, descriptor)!, _events),
                descriptor.Lifetime));
        }

        // The delegation pair is built per agent with `new`, never through DI — this port is
        // how it gets the same decorator, and delegation.started its only producer.
        services.AddSingleton<IToolDecorator>(new ObservedToolDecorator(_events));

        // ICrewExecutionHook is a single service and the runner may already have registered
        // AutoSummaryWriter on it. Take that registration over rather than past it: observing
        // a run must not cost it its AUTO_SUMMARY.md. One observer instance behind the three
        // sink interfaces — the CLI process is one run. The first version registered the hook
        // scoped and the sinks singleton: a captive dependency that silently built a second
        // observer, and the closing event reported the wrong instance's token tally.
        var existingHook = services.LastOrDefault(d => d.ServiceType == typeof(ICrewExecutionHook));
        if (existingHook is not null)
            services.Remove(existingHook);

        services.AddSingleton(sp =>
        {
            var inner = existingHook is null ? null : (ICrewExecutionHook?)Materialize(sp, existingHook);
            _observer = new RunEventObserver(_events, inner, _stream);
            return _observer;
        });
        services.AddSingleton<ICrewExecutionHook>(sp => sp.GetRequiredService<RunEventObserver>());
        services.AddSingleton<ILlmUsageSink>(sp => sp.GetRequiredService<RunEventObserver>());
        services.AddSingleton<ILlmDeltaSink>(sp => sp.GetRequiredService<RunEventObserver>());

        // D6: an observed run never approves on the user's behalf. The runner's AutoApprove
        // fallback is registered by TryAdd, so an explicit singleton here wins without
        // removing anything.
        services.AddSingleton<IHumanInputProvider>(
            new JsonLinesHumanInputProvider(_events, Inbound));

        // BUS-05: give the observing process a seat at the hub. A decorator, so local traffic
        // keeps going through the in-memory hub untouched — and only when the host registered
        // a hub at all.
        var hubDescriptor = services.LastOrDefault(d => d.ServiceType == typeof(IEventHub));
        if (hubDescriptor is not null)
        {
            services.Remove(hubDescriptor);
            services.AddSingleton<IEventHub>(sp =>
            {
                var inner = (IEventHub)Materialize(sp, hubDescriptor)!;
                _bridge = new JsonLinesEventHubBridge(
                    inner, _events, _clientName, sp.GetService<IEventHubCallerContext>());
                return _bridge;
            });
        }
    }

    /// <summary>
    /// Closes the stream: disposes the bridge and the pump, then emits <c>run.finished</c>
    /// mirroring <paramref name="exitCode"/>. Idempotent — the caller's fault barrier may
    /// reach it twice.
    /// </summary>
    public async Task<int> FinishAsync(int exitCode)
    {
        if (_finished)
            return exitCode;

        _finished = true;
        await DisposeCoreAsync().ConfigureAwait(false);

        _events.Emit(RunEventKinds.RunFinished, new
        {
            success = exitCode == 0,
            exitCode,
            tokens = _observer?.TokensUsed ?? 0,
        });

        return exitCode;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!_finished)
            await DisposeCoreAsync().ConfigureAwait(false);
    }

    private async Task DisposeCoreAsync()
    {
        // The pump goes first, deliberately: disposing the bridge while the command worker
        // still runs left a window where a concurrent `subscribe` re-armed a relay AFTER the
        // bridge had snapshotted its subscriptions — a relay nobody would ever cancel,
        // writing hub.message lines after run.finished. Closing the pump completes the
        // command channel and awaits the worker, so nothing can touch the bridge afterwards.
        await Inbound.DisposeAsync().ConfigureAwait(false);

        if (_bridge is not null)
        {
            await _bridge.DisposeAsync().ConfigureAwait(false);
            _bridge = null;
        }
    }

    /// <summary>
    /// Materialises a captured service descriptor — the runner registers its hook by
    /// factory, so the descriptor is the only handle on the instance it would have built.
    /// Keyed descriptors are skipped rather than touched: reading their implementation
    /// members throws, and nothing observed here registers keyed services.
    /// </summary>
    private static object? Materialize(IServiceProvider sp, ServiceDescriptor descriptor)
    {
        if (descriptor.IsKeyedService)
            return null;
        if (descriptor.ImplementationInstance is { } instance)
            return instance;
        if (descriptor.ImplementationFactory is { } factory)
            return factory(sp);
        return descriptor.ImplementationType is { } type
            ? ActivatorUtilities.CreateInstance(sp, type)
            : null;
    }
}
