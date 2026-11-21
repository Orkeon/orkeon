using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Runs;

namespace Orkeon.Hosting.Runs;

/// <summary>
/// The four observers of the agent loops (<c>agent.compacted</c>, <c>agent.skills</c>, <c>agent.route</c> with
/// <c>agent.effort</c>, <c>agent.pruned</c>, #432 #434 #439), registered over one sink the same way by every host that records a run:
/// <c>orkeon run</c>, <c>orkeon-host</c> and the MCP host.
/// </summary>
internal static class AgentRunEventObservers
{
    /// <summary>Registers the four observers with <paramref name="lifetime"/>, each writing to the sink <paramref name="sink"/> gives.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="lifetime">Singleton when the process is one run, scoped when each run has its own scope.</param>
    /// <param name="sink">The run's sink, read from the provider that resolves the observer.</param>
    public static IServiceCollection AddAgentRunEventObservers(
        this IServiceCollection services, ServiceLifetime lifetime, Func<IServiceProvider, IRunEventSink> sink)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(sink);
        services.Add(new ServiceDescriptor(typeof(IAgentCompactionObserver), sp => new RunEventsCompactionObserver(sink(sp)), lifetime));
        services.Add(new ServiceDescriptor(typeof(IAgentSkillsObserver), sp => new RunEventsSkillsObserver(sink(sp)), lifetime));
        services.Add(new ServiceDescriptor(typeof(IAgentRouteObserver), sp => new RunEventsRouteObserver(sink(sp)), lifetime));
        services.Add(new ServiceDescriptor(typeof(IAgentPruneObserver), sp => new RunEventsPruneObserver(sink(sp)), lifetime));
        return services;
    }
}

/// <summary>
/// A sink that writes to the run's publisher as it is at the time of the write: a host builds the publisher when its run
/// begins, after the observers may have been resolved. An event written while there is none (before the run began, or
/// with the history off) is dropped, never thrown.
/// </summary>
internal sealed class DeferredRunEventSink : IRunEventSink
{
    private readonly Func<IRunEventSink?> _target;

    /// <summary>Writes to what <paramref name="target"/> answers at each write; null drops the event.</summary>
    public DeferredRunEventSink(Func<IRunEventSink?> target) =>
        _target = target ?? throw new ArgumentNullException(nameof(target));

    /// <inheritdoc />
    public System.Threading.Tasks.Task WriteAsync(RunEvent runEvent, CancellationToken ct = default) =>
        _target() is { } sink ? sink.WriteAsync(runEvent, ct) : System.Threading.Tasks.Task.CompletedTask;
}
