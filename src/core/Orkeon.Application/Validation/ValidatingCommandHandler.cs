using Orkeon.Application.Common.CQRS;

namespace Orkeon.Application.Validation;

/// <summary>
/// Decorator that validates a command before delegating to the inner handler.
/// If validation fails, a <see cref="CommandValidationException"/> is thrown.
/// </summary>
/// <typeparam name="TCommand">The type of command to handle.</typeparam>
/// <typeparam name="TResponse">The type of response returned by the handler.</typeparam>
public sealed class ValidatingCommandHandler<TCommand, TResponse> : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    private readonly ICommandHandler<TCommand, TResponse> _inner;
    private readonly ICommandValidator<TCommand> _validator;

    /// <summary>
    /// Initializes a new instance of <see cref="ValidatingCommandHandler{TCommand, TResponse}"/>.
    /// </summary>
    public ValidatingCommandHandler(
        ICommandHandler<TCommand, TResponse> inner,
        ICommandValidator<TCommand> validator)
    {
        _inner = inner;
        _validator = validator;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<TResponse> HandleAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        var result = _validator.Validate(command);
        if (!result.IsValid)
        {
            throw new CommandValidationException(result.Errors);
        }

        return _inner.HandleAsync(command, cancellationToken);
    }
}
