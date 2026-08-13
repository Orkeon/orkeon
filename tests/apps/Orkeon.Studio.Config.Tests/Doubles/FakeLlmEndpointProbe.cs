using Orkeon.Studio.Core.Llm;

namespace Orkeon.Studio.Config.Tests.Doubles;

/// <summary>
/// An <see cref="ILlmEndpointProbe"/> that answers from a script instead of reaching an
/// endpoint, recording what it was asked to probe. <see cref="Gate"/> holds the answer back
/// so a test can observe the UI while a probe is still in flight.
/// </summary>
public sealed class FakeLlmEndpointProbe : ILlmEndpointProbe
{
    /// <summary>Every request the probe was given, in call order.</summary>
    public List<LlmProbeRequest> Requests { get; } = [];

    /// <summary>The scripted answer.</summary>
    public LlmProbeResult Result { get; set; } = LlmProbeResult.Reachable(2);

    /// <summary>When set, thrown instead of answering.</summary>
    public Exception? FailWith { get; set; }

    /// <summary>When set, the probe does not answer until this is completed.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>The single request, when exactly one was made.</summary>
    public LlmProbeRequest LastRequest => Requests[^1];

    /// <inheritdoc />
    public async Task<LlmProbeResult> ProbeAsync(
        LlmProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Requests.Add(request);

        if (Gate is { } gate)
            await gate.Task.WaitAsync(cancellationToken);

        return FailWith is null ? Result : throw FailWith;
    }
}
