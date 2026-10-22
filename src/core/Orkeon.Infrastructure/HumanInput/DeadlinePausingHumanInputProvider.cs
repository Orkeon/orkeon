using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.HumanInput;

namespace Orkeon.Infrastructure.HumanInput;

/// <summary>Decorator: the wait for a human does not count against the running task's deadline (CH-01).</summary>
public sealed class DeadlinePausingHumanInputProvider : IHumanInputProvider
{
    private readonly IHumanInputProvider _inner;

    /// <summary>Wraps <paramref name="inner"/>.</summary>
    public DeadlinePausingHumanInputProvider(IHumanInputProvider inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<string> GetInputAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        using (TaskDeadline.PauseCurrent())
        {
            return await _inner.GetInputAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<bool> GetConfirmationAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        using (TaskDeadline.PauseCurrent())
        {
            return await _inner.GetConfirmationAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<string> GetChoiceAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        using (TaskDeadline.PauseCurrent())
        {
            return await _inner.GetChoiceAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<IReadOnlyList<string>> GetMultiChoiceAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        using (TaskDeadline.PauseCurrent())
        {
            return await _inner.GetMultiChoiceAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        _inner.IsAvailableAsync(cancellationToken);
}
