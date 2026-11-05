using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Commands.Scripting.Progress;

/// <summary>
/// Tells the console about each call of the host's invocation point, the one every model-driven call goes through.
/// The arguments stay here and on the screen: the run's stream and the logs keep to the tool's name.
/// </summary>
public sealed class ToolActivityPipeline : IToolInvocationPipeline
{
    private readonly IToolInvocationPipeline _inner;
    private readonly ToolActivityFeed _feed;
    private long _lastId;

    /// <summary>Wraps <paramref name="inner"/>.</summary>
    public ToolActivityPipeline(IToolInvocationPipeline inner, ToolActivityFeed feed)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
    }

    /// <summary>Puts the feed in front of the invocation point registered so far.</summary>
    public static IServiceCollection Decorate(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ToolActivityFeed>();
        var inner = services.LastOrDefault(d => d.ServiceType == typeof(IToolInvocationPipeline));
        if (inner?.ImplementationFactory is not { } factory) return services;
        services.Remove(inner);
        services.AddSingleton<IToolInvocationPipeline>(sp => new ToolActivityPipeline(
            (IToolInvocationPipeline)factory(sp), sp.GetRequiredService<ToolActivityFeed>()));
        return services;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<ToolInvocationResult> InvokeAsync(
        ToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return _feed.Observer is { } observer
            ? ObservedAsync(observer, invocation, cancellationToken)
            : _inner.InvokeAsync(invocation, cancellationToken);
    }

    private async System.Threading.Tasks.Task<ToolInvocationResult> ObservedAsync(
        IToolActivityObserver observer, ToolInvocation invocation, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _lastId);
        var thread = ConsoleThread.Current;
        var tool = invocation.Tool.Name;
        observer.Started(new ToolCallStarted(id, tool, thread) { Subject = ToolCallSubject.Of(tool, invocation.Arguments) });
        var succeeded = false;
        try
        {
            var result = await _inner.InvokeAsync(invocation, cancellationToken).ConfigureAwait(false);
            succeeded = result.Success;
            return result;
        }
        finally
        {
            observer.Finished(new ToolCallFinished(id, thread, succeeded));
        }
    }
}
