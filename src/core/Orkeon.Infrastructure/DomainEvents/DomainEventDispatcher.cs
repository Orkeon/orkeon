using Orkeon.Domain.SharedKernel.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Orkeon.Infrastructure.DomainEvents;

/// <summary>
/// Dispatches domain events to their registered handlers using the service provider.
/// </summary>
public sealed partial class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DomainEventDispatcher> _logger;

    /// <inheritdoc cref="IDomainEventDispatcher"/>
    public DomainEventDispatcher(IServiceProvider serviceProvider, ILogger<DomainEventDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task DispatchAsync(DomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return DispatchCoreAsync(domainEvent, cancellationToken);
    }

    private async Task DispatchCoreAsync(DomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var eventType = domainEvent.GetType();
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);

        var handlers = _serviceProvider.GetServices(handlerType);

        var handlerFound = false;
        foreach (var handler in handlers)
        {
            if (handler is null) continue;
            handlerFound = true;

            var method = handlerType.GetMethod(nameof(IDomainEventHandler<DomainEvent>.HandleAsync));
            if (method is null) continue;

            LogDispatchingEvent(eventType.Name, handler.GetType().Name);

            await ((Task)method.Invoke(handler, [domainEvent, cancellationToken])!).ConfigureAwait(false);
        }

        if (!handlerFound)
        {
            LogNoHandlersRegistered(eventType.Name);
        }
    }

    /// <inheritdoc/>
    public Task DispatchManyAsync(IEnumerable<DomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);
        return DispatchManyCoreAsync(domainEvents, cancellationToken);
    }

    private async Task DispatchManyCoreAsync(IEnumerable<DomainEvent> domainEvents, CancellationToken cancellationToken)
    {
        foreach (var domainEvent in domainEvents)
        {
            await DispatchAsync(domainEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dispatching {EventName} to {HandlerType}")]
    private partial void LogDispatchingEvent(string eventName, string handlerType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No handlers registered for {EventName}")]
    private partial void LogNoHandlersRegistered(string eventName);
}
