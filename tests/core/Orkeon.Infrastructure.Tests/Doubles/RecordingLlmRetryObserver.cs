using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>Records every retry the policy schedules and every settlement.</summary>
public sealed class RecordingLlmRetryObserver : ILlmRetryObserver
{
    public List<LlmRetryEvent> Retries { get; } = [];
    public int Settled;

    public void OnRetryScheduled(LlmRetryEvent retry) => Retries.Add(retry);

    public void OnCallSettled() => Settled++;
}
