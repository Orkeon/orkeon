using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.HumanInput;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>An <see cref="IHumanInputProvider"/> that counts availability probes and answers a fixed text.</summary>
internal sealed class StubHumanInputProvider : IHumanInputProvider
{
    public int AvailabilityProbes;

    public System.Threading.Tasks.Task<string> GetInputAsync(HumanInputContext context, CancellationToken cancellationToken = default) =>
        System.Threading.Tasks.Task.FromResult("stub-answer");

    public System.Threading.Tasks.Task<bool> GetConfirmationAsync(HumanInputContext context, CancellationToken cancellationToken = default) =>
        System.Threading.Tasks.Task.FromResult(true);

    public System.Threading.Tasks.Task<string> GetChoiceAsync(HumanInputContext context, CancellationToken cancellationToken = default) =>
        System.Threading.Tasks.Task.FromResult("stub-answer");

    public System.Threading.Tasks.Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref AvailabilityProbes);
        return System.Threading.Tasks.Task.FromResult(true);
    }
}
