using Orkeon.Studio.Core.Llm;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>
/// An <see cref="ILlmEndpointProbe"/> that replays a scripted verdict instead of reaching an
/// endpoint, recording what it was asked to probe.
/// </summary>
public sealed class FakeLlmEndpointProbe : ILlmEndpointProbe
{
    public List<LlmProbeRequest> Requests { get; } = [];

    public LlmProbeResult Result { get; set; } = LlmProbeResult.Reachable(2);

    public Exception? FailWith { get; set; }

    /// <summary>When set, the probe does not answer until this is completed.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public LlmProbeRequest LastRequest => Requests[^1];

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
