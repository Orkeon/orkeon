namespace Orkeon.Application.Common.CQRS;

/// <summary>
/// Marker interface for commands.
/// TResponse associates each command with its response type at compile time (MediatR-style CQRS pattern).
/// Used as a type constraint in <see cref="ICommandHandler{TCommand, TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">The type of response this command produces.</typeparam>
public interface ICommand<TResponse>
{
    /// <summary>
    /// Gets the CLR type of the response this command produces.
    /// </summary>
    Type ResponseType => typeof(TResponse);
}
