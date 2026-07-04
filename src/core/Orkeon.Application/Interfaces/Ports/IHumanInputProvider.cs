using Orkeon.Domain.HumanInput;

namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Interface for providing human input to agents.
/// </summary>
public interface IHumanInputProvider
{
    /// <summary>
    /// Requests input from a human user.
    /// </summary>
    System.Threading.Tasks.Task<string> GetInputAsync(HumanInputContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests confirmation from a human user.
    /// </summary>
    System.Threading.Tasks.Task<bool> GetConfirmationAsync(HumanInputContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests a choice from a human user.
    /// </summary>
    System.Threading.Tasks.Task<string> GetChoiceAsync(HumanInputContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if human input is available.
    /// </summary>
    System.Threading.Tasks.Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}
