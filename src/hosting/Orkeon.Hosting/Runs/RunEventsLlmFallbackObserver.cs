using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;

namespace Orkeon.Hosting.Runs;

/// <summary>Writes a profile's switch to its fallback on the run protocol (llm.fallback).</summary>
internal sealed class RunEventsLlmFallbackObserver : ILlmFallbackObserver
{
    private readonly IRunEventSink _events;

    public RunEventsLlmFallbackObserver(IRunEventSink events) => _events = events;

    /// <inheritdoc />
    public void FallbackTaken(string profile, string fromProfile, string toProfile, string reason) =>
        _events.Emit(RunEventKinds.LlmFallback, new { profile, from = fromProfile, to = toProfile, reason });
}
