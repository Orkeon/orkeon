namespace Orkeon.Application.Common.CQRS;

/// <summary>
/// Interface for query handlers.
/// </summary>
/// <typeparam name="TQuery">The type of query to handle.</typeparam>
/// <typeparam name="TResponse">The type of response returned by the handler.</typeparam>
public interface IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    /// <summary>
    /// Handles the query asynchronously.
    /// </summary>
    /// <param name="query">The query to handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response from handling the query.</returns>
    System.Threading.Tasks.Task<TResponse> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}
