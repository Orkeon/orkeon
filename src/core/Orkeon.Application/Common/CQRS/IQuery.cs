namespace Orkeon.Application.Common.CQRS;

/// <summary>
/// Marker interface for queries.
/// TResponse associates each query with its response type at compile time (MediatR-style CQRS pattern).
/// Used as a type constraint in <see cref="IQueryHandler{TQuery, TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">The type of response this query produces.</typeparam>
public interface IQuery<TResponse>
{
    /// <summary>
    /// Gets the CLR type of the response this query produces.
    /// </summary>
    Type ResponseType => typeof(TResponse);
}
