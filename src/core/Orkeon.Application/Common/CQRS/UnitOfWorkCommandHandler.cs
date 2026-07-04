using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Common.CQRS;

/// <summary>
/// Decorator that calls <see cref="IUnitOfWork.SaveChangesAsync"/> after the inner handler completes,
/// ensuring domain events are dispatched after persistence.
/// </summary>
/// <typeparam name="TCommand">The type of command to handle.</typeparam>
/// <typeparam name="TResponse">The type of response returned by the handler.</typeparam>
public sealed class UnitOfWorkCommandHandler<TCommand, TResponse> : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    private readonly ICommandHandler<TCommand, TResponse> _inner;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>
    /// Initializes a new instance of <see cref="UnitOfWorkCommandHandler{TCommand, TResponse}"/>.
    /// </summary>
    public UnitOfWorkCommandHandler(
        ICommandHandler<TCommand, TResponse> inner,
        IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<TResponse> HandleAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        var result = await _inner.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }
}
