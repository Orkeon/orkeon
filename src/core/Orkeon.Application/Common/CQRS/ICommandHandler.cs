namespace Orkeon.Application.Common.CQRS;

/// <summary>
/// Interface for command handlers.
/// </summary>
/// <typeparam name="TCommand">The type of command to handle.</typeparam>
/// <typeparam name="TResponse">The type of response returned by the handler.</typeparam>
public interface ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    /// <summary>
    /// Handles the command asynchronously.
    /// </summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response from handling the command.</returns>
    System.Threading.Tasks.Task<TResponse> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}
